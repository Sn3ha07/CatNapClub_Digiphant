"""DigiPhant camera -> local Unity UDP bridge. Run with --help for setup options."""
import argparse
from collections import namedtuple
import errno
import itertools
import json
import math
from pathlib import Path
import socket
import time

FEATURES = ('LeftHandHeight', 'RightHandHeight', 'LeftFootLift', 'RightFootLift', 'Lean', 'ArmSpread', 'ThumbsUp', 'Banana', 'SleepPose', 'Bottle',
            'HandsUp')
HANDS_UP_TOP = .3  # HandsUp: both hands within this top fraction of the camera image
MODEL_URL = 'https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_full/float16/latest/pose_landmarker_full.task'
GESTURE_MODEL_URL = 'https://storage.googleapis.com/mediapipe-models/gesture_recognizer/gesture_recognizer/float16/latest/gesture_recognizer.task'
THUMBS_UP_HOLD = .4  # Seconds a thumbs-up stays on after the last sighting, bridging missed frames.
OBJECT_MODEL_URL = 'https://storage.googleapis.com/mediapipe-models/object_detector/efficientdet_lite0/float16/latest/efficientdet_lite0.tflite'
OBJECT_HOLD = .8  # Zones take turns for object detection, so an object is rechecked only every few frames.
OBJECTS = {'banana': (0, 220, 255), 'bottle': (255, 170, 40)}  # COCO class -> preview colour (BGR)
# Minimum detection score per object. Clear plastic bottles score low, so bottles get a lower bar.
OBJECT_SCORE = {'banana': .4, 'bottle': .3}
FACE_MODEL_URL = 'https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/latest/face_landmarker.task'
HEAD_TILT_SLEEP = 18  # degrees: a head resting sideways on the hands (reference photos: 38 and 25; upright: 1-10)


def extract_features(landmarks, upper_body_only=False):
    """Body-relative image measurements; Unity subtracts the calibrated neutral pose."""
    def xy(i):
        p = landmarks[i]
        return p.x, p.y
    def quality(indices):
        return min(min(landmarks[i].visibility, landmarks[i].presence) for i in indices)
    if upper_body_only:
        ls, rs = xy(11), xy(12)
        shoulder = ((ls[0] + rs[0]) / 2, (ls[1] + rs[1]) / 2)
        scale = max(math.dist(ls, rs), .03)
        left = (shoulder[1] - xy(15)[1]) / scale
        right = (shoulder[1] - xy(16)[1]) / scale
        # Shoulder tilt replaces hip-relative lean; leg channels use hand heights.
        raw = [left, right, left, right, (ls[1] - rs[1]) / scale,
               abs(xy(15)[0] - xy(16)[0]) / (2 * scale)]
        groups = [[11, 12, 15], [11, 12, 16], [11, 12, 15],
                  [11, 12, 16], [11, 12], [11, 12, 15, 16]]
        return shoulder, [max(-3, min(3, v)) for v in raw], [quality(g) for g in groups]
    ls, rs, lh, rh = (xy(i) for i in (11, 12, 23, 24))
    shoulder = ((ls[0] + rs[0]) / 2, (ls[1] + rs[1]) / 2)
    hip = ((lh[0] + rh[0]) / 2, (lh[1] + rh[1]) / 2)
    scale = max(math.dist(shoulder, hip), .03)
    base = [11, 12, 23, 24]
    groups = [base + [15], base + [16], base + [27], base + [28], base, base + [15, 16]]
    raw = [(shoulder[1] - xy(15)[1]) / scale,
           (shoulder[1] - xy(16)[1]) / scale,
           (hip[1] - xy(27)[1]) / scale,
           (hip[1] - xy(28)[1]) / scale,
           (shoulder[0] - hip[0]) / scale,
           abs(xy(15)[0] - xy(16)[0]) / (2 * scale)]
    return hip, [max(-3, min(3, v)) for v in raw], [quality(g) for g in groups]


class PerformerTracker:
    """Small-group nearest-position assignment with ambiguity rejection.

    This is not biometric identity. Crossings/long occlusions may need manual reset.
    """
    def __init__(self, count, max_distance=.20):
        if count not in (1, 2, 3, 4):
            raise ValueError("Choose 1, 2, 3, or 4 people")
        self.count = count
        self.max_distance = max_distance
        self.positions = None

    def reset(self):
        self.positions = None

    def assign(self, centers):
        if self.positions is None:
            if len(centers) < self.count:
                return {}
            order = sorted(range(len(centers)), key=lambda i: centers[i][0])[:self.count]
            self.positions = [centers[i] for i in order]
            return {slot + 1: index for slot, index in enumerate(order)}
        candidates = []
        for assignment in itertools.product(range(-1, len(centers)), repeat=self.count):
            used = [i for i in assignment if i >= 0]
            if len(set(used)) != len(used):
                continue
            distances = [math.dist(self.positions[s], centers[i]) for s, i in enumerate(assignment) if i >= 0]
            if any(d > self.max_distance for d in distances):
                continue
            candidates.append((-len(used), sum(distances), assignment))
        candidates.sort()
        best = candidates[0]
        # Avoid arbitrary role swaps when two assignments are almost equally plausible.
        if len(candidates) > 1 and candidates[1][0] == best[0] and candidates[1][1] - best[1] < .015:
            return {}
        result = {s + 1: i for s, i in enumerate(best[2]) if i >= 0}
        for slot, index in result.items():
            self.positions[slot - 1] = centers[index]
        return result


Landmark = namedtuple('Landmark', 'x y visibility presence')


def zone_bounds(count, width, overlap=.25):
    """Pixel column ranges, one zone per performer left-to-right in the unmirrored image.

    Zones overlap their neighbours so spread arms stay inside the crop.
    """
    step = width / count
    pad = step * overlap if count > 1 else 0
    return [(max(0, round(i * step - pad)), min(width, round((i + 1) * step + pad))) for i in range(count)]


def to_frame(landmarks, x0, x1, width):
    """Map landmarks detected in a zone crop back to full-frame normalised coordinates."""
    offset, span = x0 / width, (x1 - x0) / width
    return [Landmark(offset + p.x * span, p.y, p.visibility, p.presence) for p in landmarks]


def in_zone(x, slot, count):
    """Whether a body centre lies in this zone's own column; overlap detections belong to neighbours."""
    return (slot - 1) / count <= x <= slot / count


def zone_person(observations, slot, count):
    """The detection nearest the middle of this zone's own column."""
    middle = (slot - .5) / count
    inside = [o for o in observations if in_zone(o[0][0], slot, count)]
    return min(inside, key=lambda o: abs(o[0][0] - middle), default=None)


def thumbs_up_slots(hands, bodies, max_distance=.12):
    """Slots showing a thumbs-up: each such hand belongs to the body with the nearest pose wrist.

    hands: [(wrist_xy, is_thumbs_up)] in full-frame coordinates; bodies: {slot: pose landmarks}.
    """
    slots = set()
    for wrist, thumbs_up in hands:
        if not thumbs_up:
            continue
        distance, slot = min(((math.dist(wrist, (body[i].x, body[i].y)), slot)
                              for slot, body in bodies.items() for i in (15, 16)), default=(math.inf, None))
        if distance <= max_distance:
            slots.add(slot)
    return slots


def object_slots(centers, bodies, zone_count):
    """Slots holding an object (banana, bottle). With zones, the object belongs to the zone it is in;
    without zones, to the body with the nearest pose wrist.

    centers: object box centres in full-frame coordinates; bodies: {slot: pose landmarks}.
    """
    if zone_count > 1:
        return {slot for x, _ in centers for slot in bodies if in_zone(x, slot, zone_count)}
    return thumbs_up_slots([(c, True) for c in centers], bodies, max_distance=.25)


def eye_closure(blendshapes):
    """Average blink score of both eyes, 0 (open) to 1 (closed), or None without face scores.

    Averaging copes with a hand or head tilt hiding part of one eye.
    """
    scores = {c.category_name: c.score for c in blendshapes or []}
    if 'eyeBlinkLeft' not in scores or 'eyeBlinkRight' not in scores:
        return None
    return (scores['eyeBlinkLeft'] + scores['eyeBlinkRight']) / 2


def hands_by_face(left_wrist, right_wrist, face_center, face_width):
    """Praying hands near the face, from the pose wrists in pixels.

    Either wrist right by the face counts on its own: pressed palms often hide one wrist, and crossed
    arms can make the pose model put the other one far away (reference photo 'sleep_hands_to_side').
    Otherwise both wrists close together anywhere from eye level down to the chest, roughly in line
    with the face. Hand angle and head tilt do not matter.
    """
    for wrist in (left_wrist, right_wrist):
        if wrist is not None and (abs(wrist[0] - face_center[0]) < 1.1 * face_width
                                  and -.8 * face_width < wrist[1] - face_center[1] < 1.8 * face_width):
            return True
    if left_wrist is None or right_wrist is None:
        return False
    together = math.dist(left_wrist, right_wrist) < 1.1 * face_width
    middle = ((left_wrist[0] + right_wrist[0]) / 2, (left_wrist[1] + right_wrist[1]) / 2)
    beside = abs(middle[0] - face_center[0]) < 1.6 * face_width
    height = -.8 * face_width < middle[1] - face_center[1] < 2.6 * face_width
    return together and beside and height


def palms_near_face(palms, face_center, face_width):
    """Any hand (palm centre in pixels, from the gesture recognizer's hand model) by the face: beside or under it."""
    return any(abs(x - face_center[0]) < 1.6 * face_width and -.8 * face_width < y - face_center[1] < 2.0 * face_width
               for x, y in palms)


def pose_face(body, width, height):
    """Face centre and width in pixels from the pose (nose and ears), for when no face mesh is found,
    e.g. a head tilted onto the hands."""
    def px(i):
        return body[i].x * width, body[i].y * height
    ears = math.dist(px(7), px(8))
    shoulders = math.dist(px(11), px(12))
    return px(0), max(ears * 1.1, shoulders * .45, 1)


def palm_centre(hand):
    """Mean of the wrist and the four finger bases, in the landmarks' normalised coordinates."""
    points = [hand[i] for i in (0, 5, 9, 13, 17)]
    return sum(p.x for p in points) / 5, sum(p.y for p in points) / 5


def head_tilt(body, width, height):
    """Degrees the head is tipped sideways, from the line between the ears (or the eyes when an ear
    is hidden). Upright is about 0; a head resting on the hands is 25 or more."""
    for left, right in ((7, 8), (2, 5)):
        a, b = body[left], body[right]
        if getattr(a, 'visibility', 1) >= .1 and getattr(b, 'visibility', 1) >= .1:
            dx, dy = (b.x - a.x) * width, (b.y - a.y) * height
            if abs(dx) + abs(dy) > 1:
                angle = abs(math.degrees(math.atan2(dy, dx)))
                return min(angle, 180 - angle)
    return None


class SleepTracker:
    """One performer's sleep state, calibrated to their own eyes.

    It first watches the performer's eyes for a few checks (about three seconds) to learn their normal
    reading. Then it falls asleep once the eyes are clearly closed with the hands together by the face
    in most of the recent checks (3 of the last 5, about 1.4 seconds), so one missed frame doesn't reset
    it. It stays asleep, even with the hands lowered, until the eyes are seen clearly open twice in a row.

    When the face model can't see the face (a head resting sideways on the hands hides it), the body pose
    stands in: hands by the face with the head clearly tilted counts as the sleep pose, without the eyes.
    """
    WARMUP_CHECKS = 12  # face checks needed to learn the performer's eyes before sleep can start
    WINDOW = 5          # recent face checks considered for falling asleep
    NEEDED = 3          # of which this many must show the pose

    def __init__(self):
        self.baseline = .15
        self.asleep = False
        self.checks = 0
        self.recent = []
        self.open_checks = 0

    def closed_level(self):
        return min(.75, max(.35, self.baseline + .25))

    def open_level(self):
        return self.closed_level() - .12

    def update(self, closure, hands_ok, head_resting=False):
        """closure: eye_closure() for this check, or None if the face was not seen.
        head_resting: the body pose shows the head tipped onto the hands (used only when closure is None)."""
        if closure is None:
            if head_resting and not self.asleep:
                # The pose alone, no eye calibration needed: still 3 of the last 5 checks.
                self.recent = (self.recent + [True])[-self.WINDOW:]
                if sum(self.recent) >= self.NEEDED:
                    self.asleep, self.recent = True, []
            return self.asleep  # otherwise no evidence either way
        if self.asleep:
            self.open_checks = self.open_checks + 1 if closure < self.open_level() else 0
            if self.open_checks >= 2:
                self.asleep, self.open_checks = False, 0
            return self.asleep
        self.checks += 1
        if self.checks <= self.WARMUP_CHECKS:
            # Nobody is asked to sleep in the first moments, so every reading is this person's normal eyes.
            self.baseline = min(.5, self.baseline + (closure - self.baseline) * .25)
            return False
        closed = closure >= self.closed_level()
        self.recent = (self.recent + [closed and hands_ok])[-self.WINDOW:]
        if sum(self.recent) >= self.NEEDED:
            self.asleep, self.recent = True, []
        elif not closed:
            # Only open-looking eyes teach the baseline. Learning closed eyes (e.g. during a sleep pose with a
            # wrist briefly hidden) would push 'closed' out of reach and make the next attempts fail.
            self.baseline = min(.5, self.baseline + (closure - self.baseline) * .05)
        return self.asleep


def hands_in_top(landmarks, top=HANDS_UP_TOP, min_visibility=.2):
    """Both hands in the top part of the camera image (full-frame landmarks; y = 0 at the top).

    Each hand counts by its highest visible point: wrist or index finger (pose 15/19 left, 16/20 right).
    Raised hands are often near the frame edge, so the visibility bar is low.
    """
    def hand_top(points):
        ys = [landmarks[i].y for i in points if (getattr(landmarks[i], 'visibility', 1) or 0) >= min_visibility]
        return min(ys) if ys else None
    left, right = hand_top((15, 19)), hand_top((16, 20))
    return left is not None and right is not None and left <= top and right <= top


def nearest_body(point, bodies, max_distance=.12):
    """Slot whose pose nose is nearest a full-frame point, or None."""
    distance, slot = min(((math.dist(point, (body[0].x, body[0].y)), slot) for slot, body in bodies.items()),
                         default=(math.inf, None))
    return slot if distance <= max_distance else None


def requested_count(data):
    """Validate Unity's local count request without changing tracking on bad input."""
    try:
        value = json.loads(data)
        if isinstance(value, dict) and value.get('version') == 1:
            count = value.get('performerCount')
            if type(count) is int and 1 <= count <= 4:
                return count
    except (ValueError, UnicodeError):
        pass
    return None


def encode_preview(frame, cv2):
    """Fit one low-latency JPEG into a local UDP datagram, preserving aspect ratio."""
    height, width = frame.shape[:2]
    scale = min(320 / width, 240 / height)
    preview = cv2.resize(frame, (max(1, round(width * scale)), max(1, round(height * scale))))
    # Stay below macOS's commonly configured 9216-byte UDP send limit.
    # JPEG size depends on scene detail, so reduce quality and then dimensions.
    for _ in range(4):
        for quality in (65, 45, 25):
            ok, encoded = cv2.imencode('.jpg', preview, [cv2.IMWRITE_JPEG_QUALITY, quality])
            if ok and len(encoded) <= 8000:
                return encoded.tobytes()
        h, w = preview.shape[:2]
        preview = cv2.resize(preview, (max(1, w // 2), max(1, h // 2)))
    return None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--people', type=int, choices=(1, 2, 3, 4), default=3)
    parser.add_argument('--no-window', action='store_true', help='Preview is displayed inside Unity instead')
    parser.add_argument('--upper-body-only', action='store_true', help='Use shoulders and hands; feet and hips may be out of frame')
    parser.add_argument('--no-zones', action='store_true',
                        help='Search the whole frame for everyone instead of one zone per performer')
    parser.add_argument('--camera', type=int, default=0)
    parser.add_argument('--port', type=int, default=5055)
    parser.add_argument('--model', type=Path, default=Path(__file__).parent / 'pose_landmarker_full.task')
    parser.add_argument('--gesture-model', type=Path, default=Path(__file__).parent / 'gesture_recognizer.task')
    parser.add_argument('--object-model', type=Path, default=Path(__file__).parent / 'efficientdet_lite0.tflite')
    parser.add_argument('--face-model', type=Path, default=Path(__file__).parent / 'face_landmarker.task')
    parser.add_argument('--download-model', action='store_true', help='Download the official models if missing')
    args = parser.parse_args()
    if not 1024 <= args.port <= 65533:
        parser.error('--port must be between 1024 and 65533')
    if not args.model.exists() and not args.download_model:
        parser.error('Model missing. Run again with --download-model (internet required).')
    for path, url, name in ((args.model, MODEL_URL, 'pose'), (args.gesture_model, GESTURE_MODEL_URL, 'gesture'),
                            (args.object_model, OBJECT_MODEL_URL, 'object detection'),
                            (args.face_model, FACE_MODEL_URL, 'face')):
        if path.exists() or not args.download_model:
            continue
        import urllib.request
        path.parent.mkdir(parents=True, exist_ok=True)
        temporary = path.with_suffix('.download')
        print(f'Downloading the official MediaPipe {name} model...')
        urllib.request.urlretrieve(url, temporary)
        temporary.replace(path)
    if not args.gesture_model.exists():
        print('Gesture model missing: ThumbsUp is unavailable. Run once with --download-model to add it.')
    if not args.object_model.exists():
        print('Object detection model missing: Banana and Bottle are unavailable. Run once with --download-model to add it.')
    if not args.face_model.exists():
        print('Face model missing: SleepPose is unavailable. Run once with --download-model to add it.')
    import cv2
    import mediapipe as mp
    from mediapipe.tasks import python
    from mediapipe.tasks.python import vision
    import numpy as np

    def open_detectors():
        # Zone mode runs one detector per performer on its own crop, so each body fills more of the image.
        zones = 1 if args.no_zones else args.people
        options = vision.PoseLandmarkerOptions(
            base_options=python.BaseOptions(model_asset_path=str(args.model)),
            running_mode=vision.RunningMode.VIDEO, num_poses=4 if args.no_zones else 2,
            min_pose_detection_confidence=.5, min_pose_presence_confidence=.5,
            min_tracking_confidence=.5)
        return [vision.PoseLandmarker.create_from_options(options) for _ in range(zones)]

    def open_recognizer():
        # IMAGE mode keeps no hand-tracking state between frames; VIDEO mode intermittently
        # failed inside MediaPipe ("Packet isn't the sole owner of the holder") on live camera input.
        return vision.GestureRecognizer.create_from_options(vision.GestureRecognizerOptions(
            base_options=python.BaseOptions(model_asset_path=str(args.gesture_model)),
            running_mode=vision.RunningMode.IMAGE, num_hands=4 if args.no_zones else 2))

    def open_recognizers():
        # One gesture recognizer per pose detector, run on the same image or zone crop.
        if not args.gesture_model.exists():
            return []
        return [open_recognizer() for _ in range(1 if args.no_zones else args.people)]

    def open_object_detector():
        # EfficientDet-Lite0 recognises the 80 COCO object classes; bananas and bottles matter here.
        if not args.object_model.exists():
            return None
        return vision.ObjectDetector.create_from_options(vision.ObjectDetectorOptions(
            base_options=python.BaseOptions(model_asset_path=str(args.object_model)),
            running_mode=vision.RunningMode.IMAGE, score_threshold=min(OBJECT_SCORE.values()), category_allowlist=list(OBJECTS), max_results=4))

    def find_objects(image, x0):
        # (category, (left, top, right, bottom)) for bananas and bottles, in full-frame pixels.
        nonlocal object_detector
        try:
            detections = object_detector.detect(image).detections
        except RuntimeError as error:
            print('Object detector restarted after a MediaPipe error:', str(error).strip().splitlines()[-1][-120:])
            close_quietly(object_detector)
            object_detector = open_object_detector()
            return []
        return [(d.categories[0].category_name,
                 (x0 + d.bounding_box.origin_x, d.bounding_box.origin_y,
                  x0 + d.bounding_box.origin_x + d.bounding_box.width, d.bounding_box.origin_y + d.bounding_box.height))
                for d in detections if d.categories[0].score >= OBJECT_SCORE[d.categories[0].category_name]]

    def open_face_landmarker():
        # Face blendshapes give eye blinks.
        if not args.face_model.exists():
            return None
        return vision.FaceLandmarker.create_from_options(vision.FaceLandmarkerOptions(
            base_options=python.BaseOptions(model_asset_path=str(args.face_model)),
            running_mode=vision.RunningMode.IMAGE, output_face_blendshapes=True, num_faces=1))

    def find_sleep_pose(image, x0, x1):
        # (eye closure, face centre and width in full-frame pixels, nose in normalised full-frame
        # coordinates); all None when no face mesh is found (the body pose fills in afterwards).
        # Returns None only if the model failed.
        nonlocal face_landmarker
        try:
            result = face_landmarker.detect(image)
        except RuntimeError as error:
            print('Face landmarker restarted after a MediaPipe error:', str(error).strip().splitlines()[-1][-120:])
            close_quietly(face_landmarker)
            face_landmarker = open_face_landmarker()
            return None
        if not result.face_landmarks:
            return None, None, None, None  # e.g. a head tilted onto the hands
        face = result.face_landmarks[0]
        def px(p):
            return x0 + p.x * (x1 - x0), p.y * h
        closure = eye_closure(result.face_blendshapes[0] if result.face_blendshapes else [])
        face_width = max(math.dist(px(face[234]), px(face[454])), 1)
        return closure, px(face[1]), face_width, tuple(to_frame(face[1:2], x0, x1, w)[0][:2])

    def close_quietly(task):
        # MediaPipe re-raises an earlier graph failure on close; cleanup must still continue.
        try:
            task.close()
        except Exception:
            pass

    def find_hands(index, image, x0, x1, width):
        # Thumb_Up is one of the recognizer's built-in gestures. A gesture failure costs one
        # frame of ThumbsUp for that zone; body tracking carries on.
        try:
            result = recognizers[index].recognize(image)
        except RuntimeError as error:
            print('Gesture recognizer restarted after a MediaPipe error:', str(error).strip().splitlines()[-1][-120:])
            close_quietly(recognizers[index])
            recognizers[index] = open_recognizer()
            return []
        # Palm centres in full-frame pixels, for the sleep check (hands pressed together by the face).
        zone_palms[index] = [((x0 + x * (x1 - x0)), y * h) for x, y in (palm_centre(hand) for hand in result.hand_landmarks)]
        return [(tuple(to_frame(landmarks[:1], x0, x1, width)[0][:2]),
                 bool(gestures) and gestures[0].category_name == 'Thumb_Up' and gestures[0].score >= .5)
                for landmarks, gestures in zip(result.hand_landmarks, result.gestures)]

    tracker = PerformerTracker(args.people)
    detectors = []
    recognizers = []
    object_detector = None
    face_landmarker = None
    sleep_trackers = {}  # slot -> SleepTracker
    zone_palms = {}      # zone index -> palm centres (pixels) from the gesture recognizer
    sleep_log = {'file': None, 'on': True}

    def log_sleep(clock, slot, closure, sleeper, hands_ok, body, tilt):
        # One line per face check in the project's Temp folder, to tune the sleep pose with real readings.
        if not sleep_log['on']:
            return
        try:
            if sleep_log['file'] is None:
                path = Path(__file__).resolve().parents[2] / 'Temp' / 'digiphant-sleep-log.csv'
                path.parent.mkdir(exist_ok=True)
                sleep_log['file'] = open(path, 'w', buffering=1)
                sleep_log['file'].write('time,slot,eyeClosure,closedLevel,baseline,handsByFace,headTilt,leftWristVis,rightWristVis,recentPoses,asleep\n')
            vis = lambda i: getattr(body[i], 'visibility', 1) or 0
            sleep_log['file'].write(f'{clock:.3f},{slot},{"" if closure is None else f"{closure:.3f}"},{sleeper.closed_level():.3f},'
                                    f'{sleeper.baseline:.3f},{int(hands_ok)},{"" if tilt is None else f"{tilt:.0f}"},{vis(15):.2f},{vis(16):.2f},{sum(sleeper.recent)},{int(sleeper.asleep)}\n')
        except OSError:
            sleep_log['on'] = False
    pose_status = {}  # slot -> (time, eyes_closed, hands_by_face), for the preview label
    thumbs_until = {}
    object_until = {name: {} for name in OBJECTS}
    frame_index = 0
    camera = cv2.VideoCapture(args.camera)
    if not camera.isOpened():
        raise SystemExit('Camera could not open. Check permissions or try --camera 1.')
    sender = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        sender.bind(('127.0.0.1', args.port + 1))
        sender.setblocking(False)
    except OSError:
        camera.release()
        sender.close()
        raise SystemExit('Another camera bridge is already running. Close its preview with Q first.')
    edges = [(11, 12), (11, 23), (12, 24), (23, 24), (11, 13), (13, 15),
             (12, 14), (14, 16), (23, 25), (25, 27), (24, 26), (26, 28),
             (27, 29), (29, 31), (27, 31), (28, 30), (30, 32), (28, 32),
             (0, 7), (0, 8), (7, 11), (8, 12)]
    colors = [(100, 230, 100), (255, 180, 80), (100, 160, 255), (220, 100, 230)]
    previous_timestamp = -1
    last_preview = 0.0
    if args.no_zones:
        print('Stand side by side, all visible. IDs start left-to-right in the unmirrored preview.')
    else:
        print('Stand one person per zone: P1 is the left zone of the unmirrored preview.')
    print('R: reset identities (then recalibrate in Unity). Q: quit.')
    try:
        detectors = open_detectors()
        recognizers = open_recognizers()
        object_detector = open_object_detector()
        face_landmarker = open_face_landmarker()
        while True:
            for _ in range(32):
                try:
                    data, address = sender.recvfrom(1024)
                except BlockingIOError:
                    break
                if address == ('127.0.0.1', args.port):
                    try:
                        command = json.loads(data)
                        if isinstance(command, dict) and command.get('version') == 1:
                            upper = command.get('upperBodyOnly')
                            if type(upper) is bool and upper != args.upper_body_only:
                                args.upper_body_only = upper
                                tracker.reset()
                                print('Movement mode changed. Set neutral pose in Unity.')
                            if command.get('reset') is True:
                                tracker.reset()
                    except (ValueError, UnicodeError):
                        pass
                count = requested_count(data) if address == ('127.0.0.1', args.port) else None
                if count is not None and count != args.people:
                    args.people = count
                    tracker = PerformerTracker(count)
                    thumbs_until.clear()
                    for until in object_until.values():
                        until.clear()
                    sleep_trackers.clear()
                    zone_palms.clear()
                    if not args.no_zones:
                        for task in detectors + recognizers:
                            close_quietly(task)
                        detectors = open_detectors()
                        recognizers = open_recognizers()
                    print(f'Unity selected {count} people. Reassigning; set neutral pose in Unity.')
            ok, frame = camera.read()
            if not ok:
                raise RuntimeError('Camera stopped delivering frames.')
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            timestamp = max(previous_timestamp + 1, time.monotonic_ns() // 1_000_000)
            previous_timestamp = timestamp
            h, w = frame.shape[:2]
            observations = []
            bodies = []
            hands = []
            objects = []
            pose_check = None  # (zone slot or None, eye closure, face centre, face width, nose)
            frame_index += 1
            if args.no_zones:
                image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
                result = detectors[0].detect_for_video(image, timestamp)
                if recognizers:
                    hands = find_hands(0, image, 0, w, w)
                if object_detector:
                    objects = find_objects(image, 0)
                if face_landmarker:
                    found_pose = find_sleep_pose(image, 0, w)
                    pose_check = (None,) + found_pose if found_pose else None
                bodies = result.pose_landmarks
                for landmarks in bodies:
                    center, values, confidence = extract_features(landmarks, args.upper_body_only)
                    if confidence[4] >= .5:
                        observations.append((center, values, confidence, landmarks))
                assignments = tracker.assign([o[0] for o in observations])
            else:
                # Each zone owns one slot, so identity follows where people stand.
                assignments = {}
                for slot, (detector, (x0, x1)) in enumerate(zip(detectors, zone_bounds(len(detectors), w)), 1):
                    image = mp.Image(image_format=mp.ImageFormat.SRGB, data=np.ascontiguousarray(rgb[:, x0:x1]))
                    result = detector.detect_for_video(image, timestamp)
                    if recognizers:
                        hands += find_hands(slot - 1, image, x0, x1, w)
                    # Object detection costs ~11 ms per zone, so zones take turns.
                    if object_detector and frame_index % len(detectors) == slot - 1:
                        objects = find_objects(image, x0)
                    if face_landmarker and frame_index % len(detectors) == slot - 1:
                        found_pose = find_sleep_pose(image, x0, x1)
                        pose_check = (slot,) + found_pose if found_pose else None
                    found = []
                    for landmarks in result.pose_landmarks:
                        landmarks = to_frame(landmarks, x0, x1, w)
                        center, values, confidence = extract_features(landmarks, args.upper_body_only)
                        if confidence[4] >= .5 and in_zone(center[0], slot, len(detectors)):
                            found.append((center, values, confidence, landmarks))
                    chosen = zone_person(found, slot, len(detectors))
                    for observation in found:
                        if observation is chosen:
                            assignments[slot] = len(observations)
                        observations.append(observation)
                        bodies.append(observation[3])
            clock = time.monotonic()
            for slot in thumbs_up_slots(hands, {slot: observations[i][3] for slot, i in assignments.items()}):
                thumbs_until[slot] = clock + THUMBS_UP_HOLD
            thumbs = {slot for slot, until in thumbs_until.items() if clock < until}
            showing = {}  # object name -> slots currently showing it
            for name, until in object_until.items():
                centers = [((left + right) / 2 / w, (top + bottom) / 2 / h) for found, (left, top, right, bottom) in objects if found == name]
                for slot in object_slots(centers, {slot: observations[i][3] for slot, i in assignments.items()},
                                         1 if args.no_zones else len(detectors)):
                    until[slot] = clock + OBJECT_HOLD
                showing[name] = {slot for slot, end in until.items() if clock < end}
            if pose_check:
                zone, closure, face_center, face_width, nose = pose_check
                tracked = {slot: observations[i][3] for slot, i in assignments.items()}
                if zone:
                    owner = zone if zone in tracked and (nose is None or in_zone(nose[0], zone, len(detectors))) else None
                else:
                    owner = nearest_body(nose, tracked) if nose else None
                if owner:
                    body = tracked[owner]
                    # Face position: the face model, else the pose's nose and ears.
                    if face_center is None:
                        face_center, face_width = pose_face(body, w, h)
                    def wrist(index):
                        # Hands pressed to the face are partly hidden, so accept low pose visibility here.
                        point = body[index]
                        return (point.x * w, point.y * h) if (getattr(point, 'visibility', 1) or 0) >= .1 else None
                    # Hands: pose wrists, or any hand the gesture recognizer sees by the face.
                    palms = zone_palms.get((zone or 1) - 1, [])
                    hands_ok = (hands_by_face(wrist(15), wrist(16), face_center, face_width)
                                or palms_near_face(palms, face_center, face_width))
                    # Named sleeper, not tracker: `tracker` is the PerformerTracker that keeps P1-P3 apart.
                    sleeper = sleep_trackers.setdefault(owner, SleepTracker())
                    # No face mesh: the pose decides. A head tipped onto the hands hides the face from the face model.
                    tilt = head_tilt(body, w, h) if closure is None else None
                    head_resting = hands_ok and tilt is not None and tilt >= HEAD_TILT_SLEEP
                    sleeper.update(closure, hands_ok, head_resting)
                    pose_status[owner] = (clock, head_resting or (closure is not None and closure >= sleeper.closed_level()), hands_ok)
                    log_sleep(clock, owner, closure, sleeper, hands_ok, body, tilt)
            sleeping = {slot for slot, sleeper in sleep_trackers.items() if sleeper.asleep}
            # Gesture and object channels are known (on or off) whenever the body is tracked and the model is loaded.
            packet = {'version': 1, 'performerCount': args.people, 'upperBodyOnly': args.upper_body_only, 'people': [
                dict(slot=slot, values=observations[i][1] + [1. if slot in thumbs else 0., 1. if slot in showing['banana'] else 0.,
                                                             1. if slot in sleeping else 0., 1. if slot in showing['bottle'] else 0.,
                                                             1. if hands_in_top(observations[i][3]) else 0.],
                     confidence=observations[i][2] + [observations[i][2][4] if recognizers else 0.,
                                                      observations[i][2][4] if object_detector else 0.,
                                                      observations[i][2][4] if face_landmarker else 0.,
                                                      observations[i][2][4] if object_detector else 0.,
                                                      observations[i][2][4]])
                for slot, i in assignments.items()]}
            sender.sendto(json.dumps(packet, allow_nan=False).encode(), ('127.0.0.1', args.port))
            # Draw every detection, even while waiting for the full group or
            # rejecting an ambiguous identity. Only assigned bodies drive Unity.
            slots = {id(observations[i][3]): slot for slot, i in assignments.items()}
            thickness = max(2, round(max(w / 320, h / 240) * 2))
            radius = max(3, round(thickness * 1.5))
            if not args.no_zones and len(detectors) > 1:
                for i in range(len(detectors)):
                    x = round(i * w / len(detectors))
                    if i:
                        cv2.line(frame, (x, 0), (x, h - 1), (255, 255, 255), max(1, thickness // 2), cv2.LINE_AA)
                    cv2.putText(frame, f'P{i + 1} zone', (x + 10, h - 14), cv2.FONT_HERSHEY_SIMPLEX,
                                max(.6, thickness * .3), colors[i], thickness, cv2.LINE_AA)
            for name, (left, top, right, bottom) in objects:
                color = OBJECTS[name]
                cv2.rectangle(frame, (int(left), int(top)), (int(right), int(bottom)), color, thickness, cv2.LINE_AA)
                cv2.putText(frame, name, (int(left), max(14, int(top) - 6)), cv2.FONT_HERSHEY_SIMPLEX,
                            max(.6, thickness * .3), color, thickness, cv2.LINE_AA)
            for landmarks in bodies:
                slot = slots.get(id(landmarks))
                color = colors[slot - 1] if slot is not None else (180, 180, 180)
                points = {}
                for index, landmark in enumerate(landmarks):
                    if (min(landmark.visibility, landmark.presence) >= .5
                            and math.isfinite(landmark.x) and math.isfinite(landmark.y)
                            and 0 <= landmark.x <= 1 and 0 <= landmark.y <= 1):
                        points[index] = (min(w - 1, int(landmark.x * w)),
                                         min(h - 1, int(landmark.y * h)))
                for a, b in edges:
                    if a in points and b in points:
                        cv2.line(frame, points[a], points[b], (20, 20, 20), thickness + 2, cv2.LINE_AA)
                        cv2.line(frame, points[a], points[b], color, thickness, cv2.LINE_AA)
                for point in points.values():
                    cv2.circle(frame, point, radius + 1, (20, 20, 20), -1, cv2.LINE_AA)
                    cv2.circle(frame, point, radius, color, -1, cv2.LINE_AA)
                anchor = points.get(11, next(iter(points.values()), None))
                if anchor is not None:
                    label = f'P{slot}' if slot is not None else 'Unassigned'
                    if slot in thumbs:
                        label += ' thumbs up'
                    for name in OBJECTS:
                        if slot in showing[name]:
                            label += ' ' + name
                    if slot is not None and hands_in_top(landmarks):
                        label += ' hands up'
                    # Show each half of the sleep pose so performers can see which part is missing.
                    seen, eyes_shut, hands_near = pose_status.get(slot, (-1, False, False))
                    if slot in sleeping:
                        label += ' asleep (open eyes to wake)'
                    elif clock - seen < 1:
                        label += (' eyes closed' if eyes_shut else '') + (' hands by face' if hands_near else '')
                    font_scale = max(.6, thickness * .3)
                    cv2.putText(frame, label, anchor, cv2.FONT_HERSHEY_SIMPLEX, font_scale,
                                (20, 20, 20), thickness + 2, cv2.LINE_AA)
                    cv2.putText(frame, label, anchor, cv2.FONT_HERSHEY_SIMPLEX, font_scale,
                                color, thickness, cv2.LINE_AA)
            message = f'{len(assignments)}/{args.people} assigned' if args.no_window else f'{len(assignments)}/{args.people} assigned | R: reassign | Q: quit'
            if not args.no_zones:
                empty = [f'P{s}' for s in range(1, args.people + 1) if s not in assignments]
                message = 'All zones filled' if not empty else 'Empty zone: ' + ', '.join(empty)
                if not args.no_window:
                    message += ' | Q: quit'
            elif tracker.positions is None:
                message = f'Waiting for {args.people} visible people' if args.no_window else f'Stand side by side: waiting for {args.people} visible people | Q: quit'
            cv2.putText(frame, message, (12, 26), cv2.FONT_HERSHEY_SIMPLEX, .55, (255, 255, 255), 2)
            now = time.monotonic()
            if now - last_preview >= .1:
                jpeg = encode_preview(frame, cv2)
                if jpeg is not None:
                    try:
                        sender.sendto(jpeg, ('127.0.0.1', args.port + 2))
                    except OSError as error:
                        # A dropped preview must not stop performer tracking.
                        if error.errno not in (errno.EMSGSIZE, errno.EAGAIN, errno.EWOULDBLOCK, errno.ENOBUFS):
                            raise
                last_preview = now
            if not args.no_window:
                cv2.imshow('DigiPhant tracking (unmirrored)', frame)
            key = (cv2.waitKey(1) & 0xff) if not args.no_window else -1
            if key == ord('q'):
                break
            if key == ord('r'):
                tracker.reset()
    finally:
        for task in detectors + recognizers + [task for task in (object_detector, face_landmarker) if task]:
            close_quietly(task)
        sender.sendto(b'{"version":1,"people":[]}', ('127.0.0.1', args.port))
        sender.close()
        camera.release()
        cv2.destroyAllWindows()


if __name__ == '__main__':
    main()
