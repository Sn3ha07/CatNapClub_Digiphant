import unittest
from types import SimpleNamespace
from bridge import PerformerTracker, object_slots, nearest_body, eye_closure, hands_by_face, hands_in_top, SleepTracker, extract_features, requested_count, thumbs_up_slots, to_frame, zone_bounds, zone_person


class TrackingTests(unittest.TestCase):
    def test_roles_survive_detection_reordering_and_dropout(self):
        t = PerformerTracker(3)
        self.assertEqual(t.assign([(.8, .5), (.2, .5)]), {})
        self.assertEqual(t.assign([(.8, .5), (.2, .5), (.5, .5)]), {1: 1, 2: 2, 3: 0})
        self.assertEqual(t.assign([(.51, .5), (.81, .5), (.21, .5)]), {1: 2, 2: 0, 3: 1})
        self.assertEqual(t.assign([(.52, .5), (.22, .5)]), {1: 1, 2: 0})
        self.assertEqual(t.assign([(.82, .5)]), {3: 0})

    def test_ambiguous_assignment_is_rejected(self):
        t = PerformerTracker(3)
        t.assign([(.2, .5), (.4, .5), (.8, .5)])
        self.assertEqual(t.assign([(.3, .5), (.8, .5)]), {})
        t.reset()
        self.assertIsNone(t.positions)

    def test_one_and_two_people_with_extra_bystanders(self):
        for count in (1, 2):
            t = PerformerTracker(count)
            assigned = t.assign([(.8, .5), (.2, .5), (.5, .5)])
            self.assertEqual(len(assigned), count)
            self.assertEqual(assigned[1], 1)
            self.assertEqual(t.assign([(.21, .5)]), {1: 0})

    def test_count_request_validation(self):
        for count in (1, 2, 3, 4):
            self.assertEqual(requested_count('{"version":1,"performerCount":%d}' % count), count)
        for data in ('null', '[]', 'bad', '{"version":1,"performerCount":0}',
                     '{"version":1,"performerCount":5}', '{"version":1,"performerCount":true}',
                     '{"version":1,"performerCount":"2"}', '{"version":2,"performerCount":2}'):
            self.assertIsNone(requested_count(data))

    def test_four_people(self):
        t = PerformerTracker(4)
        self.assertEqual(len(t.assign([(.1, .5), (.35, .5), (.6, .5), (.85, .5)])), 4)
        self.assertEqual(t.assign([]), {})

    def test_upper_body_works_with_no_visible_lower_body(self):
        points = [SimpleNamespace(x=.5, y=.5, presence=0., visibility=0.) for _ in range(33)]
        for i, xy in {11:(.4,.3), 12:(.6,.3), 15:(.3,.5), 16:(.7,.5)}.items():
            points[i] = SimpleNamespace(x=xy[0], y=xy[1], presence=1., visibility=1.)
        center, baseline, confidence = extract_features(points, True)
        self.assertEqual(center, (.5, .3))
        self.assertEqual(confidence, [1.] * 6)
        self.assertEqual(extract_features(points)[2][4], 0.)
        points[15].y -= .1
        _, raised, _ = extract_features(points, True)
        self.assertGreater(raised[0], baseline[0])
        self.assertEqual(raised[0], raised[2])
        self.assertEqual(raised[1], raised[3])
        points[11].y -= .05
        self.assertLess(extract_features(points, True)[1][4], baseline[4])
        points[15].visibility = 0.
        _, _, confidence = extract_features(points, True)
        self.assertEqual(confidence, [0., 1., 0., 1., 1., 0.])

    def test_features_are_translation_and_scale_invariant(self):
        points = [SimpleNamespace(x=.5, y=.5, presence=1., visibility=1.) for _ in range(33)]
        for i, xy in {11:(.4,.3),12:(.6,.3),23:(.4,.6),24:(.6,.6),15:(.2,.1),16:(.8,.2),27:(.4,.9),28:(.6,.9)}.items():
            points[i].x, points[i].y = xy
        _, first, _ = extract_features(points)
        for p in points:
            p.x = p.x * .5 + .1
            p.y = p.y * .5 + .2
        _, second, _ = extract_features(points)
        for a, b in zip(first, second):
            self.assertAlmostEqual(a, b)
        points[15].visibility = .1
        _, _, confidence = extract_features(points)
        self.assertEqual(confidence[0], .1)
        self.assertEqual(confidence[1], 1.)

    def test_zones_cover_frame_and_overlap_neighbours(self):
        self.assertEqual(zone_bounds(1, 1280), [(0, 1280)])
        bounds = zone_bounds(3, 1200)
        self.assertEqual(bounds, [(0, 500), (300, 900), (700, 1200)])
        for (_, right), (left, _) in zip(bounds, bounds[1:]):
            self.assertLess(left, right)

    def test_zone_landmarks_map_back_to_full_frame(self):
        point = SimpleNamespace(x=.5, y=.25, presence=.9, visibility=.8)
        mapped = to_frame([point], 300, 900, 1200)[0]
        self.assertAlmostEqual(mapped.x, .5)
        self.assertEqual((mapped.y, mapped.visibility, mapped.presence), (.25, .8, .9))
        self.assertAlmostEqual(to_frame([SimpleNamespace(x=0., y=0., presence=1., visibility=1.)], 700, 1200, 1200)[0].x, 700 / 1200)

    def test_each_zone_claims_only_its_own_centred_body(self):
        left, middle, edge = ((.15, .5),), ((.5, .5),), ((.36, .5),)
        self.assertIs(zone_person([left, edge], 1, 3), left)
        self.assertIs(zone_person([middle, edge], 2, 3), middle)
        self.assertIs(zone_person([edge], 2, 3), edge)
        self.assertIsNone(zone_person([left], 3, 3))

    def test_thumbs_up_belongs_to_nearest_wrist(self):
        def body(left_wrist, right_wrist):
            points = [SimpleNamespace(x=.5, y=.5) for _ in range(33)]
            points[15] = SimpleNamespace(x=left_wrist[0], y=left_wrist[1])
            points[16] = SimpleNamespace(x=right_wrist[0], y=right_wrist[1])
            return points
        bodies = {1: body((.1, .4), (.25, .4)), 3: body((.75, .3), (.9, .4))}
        self.assertEqual(thumbs_up_slots([((.76, .31), True)], bodies), {3})
        self.assertEqual(thumbs_up_slots([((.24, .41), True), ((.9, .4), False)], bodies), {1})
        self.assertEqual(thumbs_up_slots([((.5, .9), True)], bodies), set())
        self.assertEqual(thumbs_up_slots([((.5, .5), True)], {}), set())

    def test_banana_belongs_to_its_zone_or_nearest_wrist(self):
        def body(wrist):
            points = [SimpleNamespace(x=.5, y=.5) for _ in range(33)]
            points[15] = points[16] = SimpleNamespace(x=wrist[0], y=wrist[1])
            return points
        bodies = {1: body((.15, .4)), 3: body((.85, .4))}
        self.assertEqual(object_slots([(.8, .2)], bodies, 3), {3})
        self.assertEqual(object_slots([(.5, .5)], bodies, 3), set())  # P2's zone, but P2 is not tracked
        self.assertEqual(object_slots([], bodies, 3), set())
        self.assertEqual(object_slots([(.2, .45)], bodies, 1), {1})
        self.assertEqual(object_slots([(.5, .9)], bodies, 1), set())

    def test_eye_closure_averages_both_eyes(self):
        def blinks(left, right):
            return [SimpleNamespace(category_name='eyeBlinkLeft', score=left),
                    SimpleNamespace(category_name='eyeBlinkRight', score=right)]
        self.assertAlmostEqual(eye_closure(blinks(.8, .6)), .7)
        self.assertAlmostEqual(eye_closure(blinks(.9, .2)), .55)  # one eye partly hidden by a hand
        self.assertIsNone(eye_closure([]))

    def test_praying_hands_any_angle_without_bending_the_head(self):
        face, width = (500, 300), 100  # nose and face width in pixels
        self.assertTrue(hands_by_face((480, 410), (530, 420), face, width))   # under the chin, like the photo
        self.assertTrue(hands_by_face((600, 300), (640, 310), face, width))   # beside the cheek
        self.assertTrue(hands_by_face((470, 520), (540, 515), face, width))   # at the chest, head upright
        self.assertFalse(hands_by_face((350, 420), (650, 420), face, width))  # hands apart
        self.assertFalse(hands_by_face((480, 700), (520, 700), face, width))  # resting in the lap
        self.assertTrue(hands_by_face(None, (520, 420), face, width))         # one wrist hidden, the other by the face
        self.assertFalse(hands_by_face(None, (520, 560), face, width))        # one wrist only, down at the chest
        self.assertFalse(hands_by_face(None, (700, 400), face, width))        # one wrist only, off to the side
        self.assertFalse(hands_by_face(None, None, face, width))              # no wrists visible

    def test_sleep_latches_until_eyes_open_and_learns_each_persons_eyes(self):
        tracker = SleepTracker()
        for _ in range(30):
            tracker.update(.3, False)  # this person's open eyes read high
        self.assertGreater(tracker.closed_level(), .5)
        self.assertFalse(tracker.update(.45, True))  # a squint is not closed for them
        self.assertFalse(tracker.update(.8, True))    # the pose must be held a moment...
        self.assertFalse(tracker.update(.8, True))
        self.assertTrue(tracker.update(.8, True))     # ...then the elephant sleeps
        self.assertTrue(tracker.update(.8, False))    # hands lowered: still asleep
        self.assertTrue(tracker.update(None, False))  # face hidden: still asleep
        self.assertTrue(tracker.update(.2, False))    # one open reading (a blink) is not enough
        self.assertFalse(tracker.update(.2, False))   # eyes seen open again: wakes
        for _ in range(5):
            self.assertFalse(tracker.update(.8, False))  # eyes closed without the hands does not sleep

    def test_no_sleep_at_start_before_eyes_are_learned(self):
        tracker = SleepTracker()
        # Looking down at a laptop with hands resting together reads like the pose from the first check.
        self.assertFalse(any(tracker.update(.45, True) for _ in range(SleepTracker.WARMUP_CHECKS)))
        # Meanwhile it learned that .45 is this person's normal reading, so it still does not sleep.
        self.assertFalse(any(tracker.update(.45, True) for _ in range(20)))
        self.assertTrue([tracker.update(.9, True) for _ in range(3)][-1])  # a real closed-eye pose still works

    def test_one_dropped_check_does_not_reset_the_sleep_pose(self):
        tracker = SleepTracker()
        for _ in range(SleepTracker.WARMUP_CHECKS):
            tracker.update(.1, False)
        # Pose, a frame where a wrist is lost, then the pose again: 3 of the last 5 checks is enough.
        self.assertFalse(tracker.update(.9, True))
        self.assertFalse(tracker.update(.9, False))
        self.assertFalse(tracker.update(.9, True))
        self.assertTrue(tracker.update(.9, True))

    def test_failed_attempts_do_not_make_closed_eyes_harder(self):
        tracker = SleepTracker()
        for _ in range(SleepTracker.WARMUP_CHECKS):
            tracker.update(.1, False)
        level = tracker.closed_level()
        # Several attempts with closed eyes where the hands were not picked up.
        for _ in range(40):
            tracker.update(.9, False)
        self.assertAlmostEqual(tracker.closed_level(), level)  # closed eyes were not learned as 'normal'
        self.assertFalse(tracker.asleep)
        self.assertTrue([tracker.update(.9, True) for _ in range(3)][-1])  # a proper attempt still works

    def test_hands_up_needs_both_hands_in_the_top_30_percent(self):
        def body(left_y, right_y, visibility=1.):
            points = [SimpleNamespace(x=.5, y=.6, visibility=1.) for _ in range(33)]
            points[15] = SimpleNamespace(x=.4, y=left_y, visibility=visibility)       # left wrist
            points[19] = SimpleNamespace(x=.4, y=left_y - .03, visibility=visibility)  # left index finger
            points[16] = SimpleNamespace(x=.6, y=right_y, visibility=visibility)
            points[20] = SimpleNamespace(x=.6, y=right_y - .03, visibility=visibility)
            return points
        self.assertTrue(hands_in_top(body(.15, .2)))            # both thrown overhead
        self.assertTrue(hands_in_top(body(.32, .32)))           # fingertips reach the top 30%
        self.assertFalse(hands_in_top(body(.15, .6)))           # one hand only
        self.assertFalse(hands_in_top(body(.5, .5)))            # both hands up at chest / face height
        self.assertFalse(hands_in_top(body(.1, .1, visibility=.05)))  # hands not actually tracked

    def test_face_belongs_to_nearest_nose(self):
        bodies = {1: [SimpleNamespace(x=.2, y=.3)], 2: [SimpleNamespace(x=.7, y=.3)]}
        self.assertEqual(nearest_body((.68, .31), bodies), 2)
        self.assertIsNone(nearest_body((.45, .8), bodies))
        self.assertIsNone(nearest_body((.5, .5), {}))


if __name__ == '__main__':
    unittest.main()
