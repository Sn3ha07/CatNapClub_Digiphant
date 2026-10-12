"""The sleep pose must accept the reference photos in reference_poses/ (P3 resting their head on pressed
hands, and pressed hands held to one side of a tilted head). The photos are screenshots with the demo's
drawing over the face and hands, so the face is located from the pose (nose and ears) here."""
import math
import unittest
from pathlib import Path
from types import SimpleNamespace

from bridge import HEAD_TILT_SLEEP, SleepTracker, hands_by_face, head_tilt, palms_near_face, pose_face

HERE = Path(__file__).parent
REFERENCES = ('sleep_head_on_hands', 'sleep_hands_to_side')


class ReferencePoseTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        import cv2
        import mediapipe as mp
        from mediapipe.tasks import python
        from mediapipe.tasks.python import vision
        options = vision.PoseLandmarkerOptions(base_options=python.BaseOptions(model_asset_path=str(HERE / 'pose_landmarker_full.task')),
                                               running_mode=vision.RunningMode.IMAGE)
        cls.bodies = {}
        missing = [name for name in REFERENCES if not (HERE / 'reference_poses' / (name + '.png')).exists()]
        if missing:
            # The photos are kept out of the shared repository; only the computer that has them runs these checks.
            raise unittest.SkipTest('reference photos not present: ' + ', '.join(missing))
        with vision.PoseLandmarker.create_from_options(options) as pose:
            for name in REFERENCES:
                bgr = cv2.imread(str(HERE / 'reference_poses' / (name + '.png')))
                height, width = bgr.shape[:2]
                found = pose.detect(mp.Image(image_format=mp.ImageFormat.SRGB, data=cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB))).pose_landmarks
                cls.bodies[name] = (found[0] if found else None, width, height)

    def test_reference_photos_count_as_hands_by_the_face(self):
        for name in REFERENCES:
            with self.subTest(photo=name):
                body, width, height = self.bodies[name]
                self.assertIsNotNone(body, 'pose not found')
                face_centre, face_width = pose_face(body, width, height)
                def wrist(i):
                    p = body[i]
                    return (p.x * width, p.y * height) if p.visibility >= .1 else None
                self.assertTrue(hands_by_face(wrist(15), wrist(16), face_centre, face_width))

    def test_reference_photos_show_a_resting_head(self):
        for name in REFERENCES:
            with self.subTest(photo=name):
                body, width, height = self.bodies[name]
                self.assertGreaterEqual(head_tilt(body, width, height), HEAD_TILT_SLEEP)

    def test_upright_people_are_not_resting(self):
        import cv2
        import mediapipe as mp
        from mediapipe.tasks import python
        from mediapipe.tasks.python import vision
        photos = sorted((HERE / 'reference_poses').glob('upright_*'))
        if not photos:
            self.skipTest('no upright sample photos')
        options = vision.PoseLandmarkerOptions(base_options=python.BaseOptions(model_asset_path=str(HERE / 'pose_landmarker_full.task')),
                                               running_mode=vision.RunningMode.IMAGE)
        with vision.PoseLandmarker.create_from_options(options) as pose:
            for photo in photos:
                with self.subTest(photo=photo.name):
                    bgr = cv2.imread(str(photo))
                    height, width = bgr.shape[:2]
                    found = pose.detect(mp.Image(image_format=mp.ImageFormat.SRGB, data=cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB))).pose_landmarks
                    self.assertTrue(found)
                    self.assertLess(head_tilt(found[0], width, height), HEAD_TILT_SLEEP)


class HandHelperTests(unittest.TestCase):
    def test_palms_by_the_face(self):
        face, width = (500, 300), 100
        self.assertTrue(palms_near_face([(620, 350)], face, width))   # hands to the side of the face (photo 2)
        self.assertTrue(palms_near_face([(500, 420)], face, width))   # under the chin (photo 1)
        self.assertFalse(palms_near_face([(500, 600)], face, width))  # in the lap
        self.assertFalse(palms_near_face([], face, width))

    def test_pose_face_uses_nose_and_ears(self):
        points = [SimpleNamespace(x=.5, y=.5) for _ in range(33)]
        points[0] = SimpleNamespace(x=.5, y=.3)                                   # nose
        points[7], points[8] = SimpleNamespace(x=.45, y=.3), SimpleNamespace(x=.55, y=.3)  # ears
        points[11], points[12] = SimpleNamespace(x=.4, y=.5), SimpleNamespace(x=.6, y=.5)  # shoulders
        centre, width = pose_face(points, 1000, 1000)
        self.assertEqual(centre, (500, 300))
        self.assertAlmostEqual(width, 110)


class HeadTiltTests(unittest.TestCase):
    def points(self, left_ear, right_ear):
        points = [SimpleNamespace(x=.5, y=.5, visibility=1) for _ in range(33)]
        points[7], points[8] = SimpleNamespace(x=left_ear[0], y=left_ear[1], visibility=1), SimpleNamespace(x=right_ear[0], y=right_ear[1], visibility=1)
        return points

    def test_level_and_tilted_heads(self):
        self.assertAlmostEqual(head_tilt(self.points((.4, .3), (.6, .3)), 1000, 1000), 0)
        self.assertAlmostEqual(head_tilt(self.points((.4, .3), (.6, .5)), 1000, 1000), 45)
        self.assertAlmostEqual(head_tilt(self.points((.6, .3), (.4, .5)), 1000, 1000), 45)  # mirrored either way

    def test_hidden_ears_fall_back_to_eyes(self):
        points = self.points((.4, .3), (.6, .5))
        points[7].visibility = 0
        points[2], points[5] = SimpleNamespace(x=.45, y=.3, visibility=1), SimpleNamespace(x=.55, y=.3, visibility=1)
        self.assertAlmostEqual(head_tilt(points, 1000, 1000), 0)


class PoseOnlySleepTests(unittest.TestCase):
    def test_resting_head_sleeps_without_seeing_the_eyes(self):
        sleeper = SleepTracker()
        for _ in range(2):
            self.assertFalse(sleeper.update(None, True, True))
        self.assertTrue(sleeper.update(None, True, True))
        self.assertTrue(sleeper.update(None, False, False))  # still asleep with the face hidden
        sleeper.update(.05, False)
        self.assertFalse(sleeper.update(.05, False))          # eyes seen open twice: awake

    def test_hidden_face_alone_is_not_sleep(self):
        sleeper = SleepTracker()
        for _ in range(10):
            self.assertFalse(sleeper.update(None, True, False))


if __name__ == '__main__':
    unittest.main()
