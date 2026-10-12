# DigiPhant: student controls and interface guide

This standalone guide describes the DigiPhant starter's standard controls for
one to four performers. It does not depend on a particular computer, operating
system, project name, or folder layout.

Before using it, complete your project's setup and identify the scene and
tracking-launch method provided by your instructor or coding agent. The scene
must have the DigiPhant controls, elephant rig, and camera tracking configured.
The standard recording controls also need the recording component and encoder.
If your group has changed the mappings or interface, use your project's
documented controls for those changes.

## Start a session

1. Open your group's DigiPhant scene in Unity.
2. Press Unity's **Play** button and select the **Game** tab. In the standard
   Editor integration, camera tracking starts automatically, including in Test
   sliders mode. If your setup uses a separately launched tracker, start it as
   instructed for your project; avoid launching a second copy.
3. Select the number of people: **1**, **2**, **3**, or **4**.
4. Select **Seated / upper body** or **Full body** (standing).
5. Select **Camera** to use body tracking. Wait for the preview and performer
   labels. The initial assignment needs the selected number of people visible.
6. Click **Set neutral pose (10 seconds)**. Get into a comfortable resting pose
   and hold it through the countdown. Wait for **Neutral pose saved**.
7. Test a small movement before combining gestures. Scroll inside the left
   control panel if some controls are below the visible area.

For a first solo test, explicitly select **1** and **Seated / upper body**.
Do not assume that your scene already has those options selected.

Keep shoulders and both hands visible in seated mode. Standing/full-body mode
also needs hips and feet in view. Keep the Game view visible and focused during
the session, especially while recording.

## Choose a useful neutral pose

All camera controls measure a change from the saved neutral pose. Left and right
refer to **your own body**, not the side of the screen. The standard preview is
unmirrored.

Hold your left hand around waist height, within the camera frame, so you have
room to raise it for forward travel and lower it for backward travel. Keep your
shoulders relaxed and leave room to spread your hands. Your neutral pose does
not need to be perfectly symmetrical.

Calibration requires fresh, confident tracking of all assigned movements. If it
fails, improve framing or bring obscured hands/feet into view, then try again.
Changing group size or movement mode clears calibration; wait for assignment
and calibrate again. Each new Play session also needs calibration.

## One-person gesture controls

| Elephant action | Seated / upper body | Standing / Full body |
| --- | --- | --- |
| Move forward | Raise your left hand above neutral | Raise your left hand above neutral |
| Run | Raise your left hand farther; stronger forward input changes walking to running | Same |
| Move backward | Lower your left hand below neutral | Same |
| Stop travel | Return your left hand to its neutral height | Same |
| Turn left/right | Tilt your shoulder line, lowering one shoulder relative to the other | Lean your shoulders sideways relative to your hips |
| Stop turning | Return your shoulder tilt to neutral | Return your torso lean to neutral |
| Front and rear left legs | Raise/lower your left hand | Lift/lower your left foot |
| Front and rear right legs | Raise/lower your right hand | Lift/lower your right foot |
| Trunk curl | Raise/lower your right hand | Same |
| Head turn | Tilt your shoulders | Lean your torso sideways |
| Tail sway | Tilt your shoulders | Lean your torso sideways |
| Both ears | Spread your hands apart or bring them closer together | Same |

These mappings share gestures. In seated mode, your left hand controls both left
legs and travel; your right hand controls both right legs and the trunk.
Shoulder tilt controls steering, head turn, and tail sway together. In standing
mode, your feet control the legs independently of your hands, while torso lean
still controls steering, head, and tail together.

Travel and steering require **Enable locomotion**. You can steer while moving
or turn in place with your left hand at neutral. Turning changes the elephant's
heading; it does not necessarily move it toward the corresponding screen edge.
Small changes near neutral are ignored for travel and steering to reduce drift.

Returning to neutral removes your gesture offsets; an enabled locomotion system
can still play its idle animation. Switching locomotion off gives a stationary
puppet for practicing body-part gestures.

## Two-, three-, and four-person roles

Changing the group-size button applies these default body-part assignments:

| Elephant part | 1 person | 2 people | 3 people | 4 people |
| --- | --- | --- | --- | --- |
| Front legs | P1 | P1 | P1 | P1 |
| Rear legs | P1 | P2 | P2 | P2 |
| Tail | P1 | P2 | P2 | P2 |
| Head and ears | P1 | P2 | P3 | P3 |
| Trunk | P1 | P2 | P3 | P4 |

With the standard movement mappings, **P1 controls travel and steering at every
group size**. Each assigned person uses the same gesture for their part as in
the table above: hands or feet for legs, right hand for trunk, hand spread for
ears, and shoulder tilt or torso lean for head/tail.

The camera preview is split into one zone per person, labelled **P1 zone**,
**P2 zone** and so on from left to right in the **unmirrored camera image**.
Each person stands in their own zone; the bridge searches each zone separately,
so everyone is easier to detect and roles cannot swap. Your role belongs to the
zone, so stay inside it: the body's centre (hips, or shoulders when seated) must
be in the zone, while arms may reach across the line. The preview text lists any
empty zone. A grey skeleton is a second body in the same zone and is ignored.

To search the whole frame instead and assign people left-to-right by position,
start the bridge manually with `--no-zones`. In that mode, crossing or hiding one
another can confuse the roles; use **Reassign people** and recalibrate.

## Interface reference

The camera preview and recording controls sit above a scrollable control panel.
Some controls appear only in Camera mode, Test sliders mode, or during recording.

| Control or display | How to use it |
| --- | --- |
| **Camera preview** | Shows the unmirrored webcam image, skeletons, assignment labels, and connection status. |
| **Retry** | Reconnects the preview and, with automatic startup configured, retries tracker startup. If Unity started the tracker, retry stops that process first. |
| **1 / 2 / 3 / 4** | Sets the required performer count, applies default roles, resets body sliders, and clears neutral calibration. |
| **Full body** | Uses feet for leg gestures and hip-relative torso lean for steering/head/tail. |
| **Seated / upper body** | Uses hands for leg gestures and shoulder tilt for steering/head/tail. Feet and hips need not be visible. |
| **Test sliders** | Uses on-screen sliders to drive the elephant. Calibration is unnecessary; the camera preview can stay on. |
| **Camera** | Uses tracked body movements. Set neutral before controlling the elephant. |
| **Status message** | Reports calibration progress, tracking/configuration issues, or readiness. |
| **Performer N · visible / waiting / lost** | Reports whether that assigned performer has recent usable tracking. Visibility alone does not guarantee every mapped hand/foot is sufficiently visible for calibration. |
| **Set neutral pose (10 seconds)** | Clears the old calibration, starts a countdown, then saves a new neutral pose if the required inputs are visible. |
| **Cancel countdown** | Cancels the pending calibration. Start a new countdown before continuing camera control. |
| **Reassign people** | Clears calibration and asks the bridge to reset identities. Wait for labels, then set neutral again. |
| **Enable locomotion** | Enables travel, turning, and gait animation. Disable it for a stationary puppet. |
| **Action / speed display** | Shows the current movement action and speed in scene units per second. |
| **Return to starting position** | Restores position and heading, stops movement, and clears calibration. Recalibrate in Camera mode. Appears while locomotion is enabled. |
| **Record performance** | Starts recording the Game view. Requires Camera mode, a live preview, and successful calibration. |
| **Stop recording and save** | Ends capture and starts MP4 export. Wait for the Saved message. |
| Unity **Play/Stop** button | Starts or ends the session. Stopping Play closes the bridge Unity launched; a manually started bridge must be stopped separately. |

### Test sliders

Select **Test sliders** to inspect each part independently:

- **Front left leg**, **Front right leg**, **Rear left leg**, **Rear right leg**:
  rotate the respective leg.
- **Head turn**, **Trunk curl**, **Left ear**, **Right ear**, **Tail sway**:
  move that body part.
- **Reset body sliders**: centers all body-part sliders and restores gesture
  offsets. It does not reset world position or the travel/turn sliders.
- **Travel: backward / stop / forward**: left moves backward, center stops
  travel, right moves forward; stronger forward input selects running.
- **Turn: left / straight / right**: left or right steers; center stops turning.
- **Stop moving**: centers Travel and Turn without resetting body-part sliders
  or world position.

Travel, Turn, and Stop moving appear only when locomotion is enabled. Sliders
test rig motion; they do not simulate performer tracking or calibration.

## Record a performance

1. Choose Camera mode, assign the performers, and calibrate. Confirm that gestures
   move the intended parts and that the preview and elephant are both visible.
2. Keep the Game view visible and focused. A view around 1280×720 or larger is a
   useful starting point. Avoid switching tabs, minimizing, pausing, or resizing
   the Game view during the take.
3. Click **Record performance**. The camera and elephant are captured together,
   including the visible interface. No microphone audio is recorded.
4. Click **Stop recording and save**, then wait for **Saved: ...**. The Record
   button is unavailable while the export is running.
5. Open the MP4 at the location shown in the **Saved** message. The standard
   recorder writes `Recordings/<session>/performance.mp4` under the Unity project
   folder; an adapted project may use a different location. Check the start,
   end, and the movements you intended to demonstrate.
6. Upload the checked MP4 to the sharing location specified by your instructor
   (for example, your team's Drive folder). Check that the intended viewers can
   access it. Upload is separate; the standard interface saves locally only.

The recorder targets 10 fps at up to 1280×720. If capture is slower, the exporter
holds frames to preserve elapsed time. Stopping Play also ends capture and
starts export, but using the explicit Stop button makes the result easier to
check. Keep the session's images, `frames.jsonl`, and `session.json` until you
have verified the MP4. If export fails, retain these files and use your project's
recording-recovery instructions or ask your coding agent to retry the export.
The standard recorder and automatic tracker startup are Unity Editor features;
a standalone application needs a separately configured workflow.

## Recover from common problems

| Symptom | What to do |
| --- | --- |
| No preview | Wait for startup, check camera permission, close competing camera apps, then Retry. If your setup uses a separate tracker, confirm it is running. Ask your instructor or coding agent to check camera selection if needed. |
| Waiting for performers | Select the actual group size and bring everyone into view together. |
| Calibration fails | Show all assigned hands/feet clearly. Use seated mode if feet cannot be seen. Hold the neutral pose through the countdown. |
| Preview works but elephant does not respond | Select Camera, check performer assignment, and set neutral. Enable locomotion for travel/steering. |
| Elephant drifts | Recalibrate from a comfortable resting pose. Keep your left hand and shoulder tilt/torso lean close to neutral when you want to stop. |
| Wrong person controls a part | Reassign people, wait for correct labels, then recalibrate. |
| Tracking disappears | Improve visibility. Affected gestures fade toward rest and missing required movement inputs stop travel/turning. Tracking can resume when visible; reassign and recalibrate if identities are wrong. |
| Need to stop immediately | Turn off Enable locomotion to stop travel, or stop Unity Play mode to end the session. |
| Recording does not start | Select Camera, wait for a live preview, and complete calibration. Read the recorder status for missing setup or export errors. |

## Change a mapping

Stop Play before making persistent changes. Select the object containing the
DigiPhant controller (normally **DigiPhant Controls**) in the Hierarchy and edit
**Controls** in the Inspector: performer, movement source,
sensitivity, smoothing, and rotation degrees. Expand the locomotion component's
**Forward** and **Steering** sources to change who controls travel/turning.
A negative source weight reverses the movement direction. Save your group's
scene, enter Play, and recalibrate to test one change at a time.

Keep an unchanged copy of the reference scene. Document any changed gestures,
performer assignments, or interface labels for your group. Choose the group size
before customizing roles: its buttons reapply the standard body-part assignments.

## Check your group's setup before a performance

- Test each assigned gesture individually, then combine them.
- Confirm that returning to neutral stops travel and turning.
- Check what happens when a required hand, foot, or performer leaves the frame,
  and practice recovering tracking or reassigning people.
- Record a short take and play the exported video before recording a full
  performance. Check that both the people and elephant are visible.

These instructions describe the standard implementation. Camera framing,
tracking quality, and any custom mappings still need to be checked in your own
project; this guide does not certify a particular machine or performance setup.
