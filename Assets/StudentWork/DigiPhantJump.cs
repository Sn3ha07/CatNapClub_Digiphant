using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using DigiPhant;
using UnityEngine;

namespace StudentWork
{
    // P3 throws both hands up quickly -> the elephant hops. Runs after DigiPhantController so the
    // hop is added on top of that frame's pose and travel instead of being overwritten by them.
    //
    // Hand heights are judged against the performer's own shoulders (the bridge's raw measurement),
    // not the neutral pose, so it works whatever hand position was used for calibration.
    // Full-body units are torso lengths; seated units are shoulder widths, scaled by Seated Scale.
    //
    // Self-contained: it reads the controller's received measurements directly, so it works with the
    // unmodified DigiPhant starter scripts as well as adapted copies. Add it with Add Component.
    //
    // CatNapClub changes from the DigiPhantJump.md original: P3 by default, no jumps outside a game
    // round (Locomotion > Hold Activities), and the front camera is left to follow on its own.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(DigiPhantController))]
    public class DigiPhantJump : MonoBehaviour
    {
        [Tooltip("Whose hands trigger the jump. Clamped to the group size, so solo testing uses P1.")]
        [Range(1, 4)] public int performer = 3;
        [Tooltip("Both wrists must end at least this far above the shoulders (torso lengths; overhead is about 0.6).")]
        public float upLevel = .2f;
        [Tooltip("How far the lower of the two hands must rise within Rise Window (torso lengths; sides to overhead is about 1.6).")]
        [Min(.1f)] public float riseAmount = .7f;
        [Min(.05f)] public float riseWindow = .6f;
        [Tooltip("CatNapClub: also require both hands in the top 30% of the camera image (the bridge's HandsUp input), so hands raised only to chest or face height never jump.")]
        public bool requireHandsAtTop = true;
        [Tooltip("Both wrists must drop below this height (0 = shoulder level) before the next jump can fire.")]
        public float rearmLevel = 0;
        [Tooltip("Wrist confidence accepted for the jump. Lower than the controls' threshold because raised hands often leave the top of the frame.")]
        [Range(0, 1)] public float minHandConfidence = .2f;
        [Tooltip("Seated mode measures in shoulder widths, which are smaller than a torso. Thresholds are multiplied by this.")]
        [Min(.1f)] public float seatedScale = 1.4f;
        [Min(.2f)] public float duration = 1.1f;
        [Tooltip("Peak height in Unity units. 1.5 lets tucked feet clear a 0.8-unit log.")]
        [Min(0)] public float height = 1.5f;
        [Tooltip("How far the jump carries the elephant forward, in walking steps. 4 steps (about 7.6 units) clears a 0.8-unit log with room to spare; the legs are about 2.3 units apart.")]
        [Min(0)] public float forwardSteps = 4;
        [Tooltip("Length of one step in Unity units. 0 = work it out from the walk animation and Walk Speed (about 1.9).")]
        [Min(0)] public float stepLength = 0;
        [Tooltip("Seconds after landing before another jump can start.")]
        [Min(0)] public float cooldown = .4f;
        [Tooltip("Extra rotation at the top of the jump, as a fraction of each control's Degrees. Applied to controls labelled '... leg'.")]
        [Range(0, 1)] public float legTuck = .8f;
        [Tooltip("Applied to the 'Head nod' control.")]
        [Range(-1, 1)] public float headLift = .6f;
        [Tooltip("Applied to the 'Trunk curl' control.")]
        [Range(-1, 1)] public float trunkLift = .5f;
        [Tooltip("Show the jump status text (ready / re-arm / JUMP!) on the stage. Off keeps the stage clean for performances.")]
        public bool showStatus = false;
        [Tooltip("Show live hand heights on the stage, to help tune the thresholds.")]
        public bool showDiagnostics = false;
        [Tooltip("Show a Jump button on the stage in Test sliders mode.")]
        public bool showTestButton = true;
        [Tooltip("In Play mode, write the performer's hand readings to Temp/digiphant-jump-log.csv.")]
        public bool writeDebugLog = true;

        public bool IsJumping => jumpStart >= 0;
        public int Jumps { get; private set; }
        public float CurrentHeight { get; private set; }
        public string Status { get; private set; } = "";
        public string Diagnostics { get; private set; } = "";
        public float ForwardDistance
        {
            get
            {
                if (locomotion == null) locomotion = GetComponent<DigiPhantLocomotion>();
                float step = stepLength > 0 ? stepLength
                    : locomotion != null && locomotion.walk != null ? locomotion.walk.length * locomotion.walkSpeed / 2 : 1.9f;
                return forwardSteps * step;
            }
        }

        // Scenes saved with older versions are upgraded once: version 1 (0.9 s, 1 unit, no forward travel)
        // and version 2 (2 forward steps) move to the current defaults unless those values were changed.
        [SerializeField, HideInInspector] int settingsVersion;
        void UpgradeSettings()
        {
            if (settingsVersion >= 3) return;
            if (settingsVersion < 2)
            {
                if (Mathf.Approximately(duration, .9f)) duration = 1.1f;
                if (Mathf.Approximately(height, 1)) height = 1.5f;
            }
            if (Mathf.Approximately(forwardSteps, 2)) forwardSteps = 4;
            settingsVersion = 3;
        }
        void OnValidate() => UpgradeSettings();
        void Awake() => UpgradeSettings();

        DigiPhantController controller;
        DigiPhantLocomotion locomotion;
        readonly Queue<(float time, float low)> history = new Queue<(float, float)>();
        readonly Queue<(float time, float rise)> recentRises = new Queue<(float, float)>();
        bool armed = true;
        float jumpStart = -1, lastLanding = -1000, groundY, travelled;
        Vector3 jumpDirection;
        StreamWriter log;

        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly FieldInfo ValuesField = typeof(DigiPhantController).GetField("values", Private);
        static readonly FieldInfo ConfidenceField = typeof(DigiPhantController).GetField("confidence", Private);
        static readonly FieldInfo DisabledField = typeof(BoneControl).GetField("disabled");

        Transform Root => locomotion != null ? locomotion.travelRoot : null;
        int Performer => Mathf.Clamp(performer, 1, Mathf.Max(1, controller.performerCount));

        void OnEnable()
        {
            controller = GetComponent<DigiPhantController>();
            locomotion = GetComponent<DigiPhantLocomotion>();
            history.Clear();
            recentRises.Clear();
            armed = true;
        }
        void OnDisable()
        {
            Land(-1000);
            log?.Dispose();
            log = null;
        }
        void LateUpdate() => Step(Time.realtimeSinceStartup);

        public void Step(float now)
        {
            if (controller == null) OnEnable();
            Detect(now);
            if (!IsJumping) return;
            float t = Mathf.Clamp01((now - jumpStart) / duration);
            MoveForward(t);
            if (t >= 1) { Land(now); return; }
            float arc = Mathf.Sin(t * Mathf.PI);
            CurrentHeight = height * arc;
            var root = Root;
            if (root) { var p = root.position; p.y = groundY + CurrentHeight; root.position = p; }
            ApplyPose(arc);
        }

        public bool TriggerJump(float now)
        {
            if (controller == null) OnEnable();
            if (IsJumping || now - lastLanding < cooldown) return false;
            jumpStart = now;
            groundY = Root ? Root.position.y : 0;
            travelled = 0;
            jumpDirection = Root ? Vector3.ProjectOnPlane(Root.forward, Vector3.up).normalized : Vector3.zero;
            Jumps++;
            return true;
        }

        // Adds this frame's share of the forward distance on top of any walking. The follow camera moves
        // with it so the view doesn't lag; locomotion's stage-radius limit still applies next frame.
        void MoveForward(float t)
        {
            var root = Root;
            if (!root) return;
            float target = ForwardDistance * t, delta = target - travelled;
            travelled = target;
            if (Mathf.Abs(delta) < 1e-6f) return;
            root.position += jumpDirection * delta;
            // CatNapClub: the front camera follows the elephant by itself, so only move a plain follow camera.
            var front = locomotion.followCamera ? locomotion.followCamera.GetComponent<DigiPhantFrontCamera>() : null;
            if (locomotion.followElephant && locomotion.followCamera && !(front && front.isActiveAndEnabled))
                locomotion.followCamera.transform.position += jumpDirection * delta;
        }

        void Land(float now)
        {
            if (!IsJumping) return;
            var root = Root;
            if (root) { var p = root.position; p.y = groundY; root.position = p; }
            jumpStart = -1;
            CurrentHeight = 0;
            lastLanding = now;
        }

        void Detect(float now)
        {
            int p = Performer;
            if (controller.inputMode != InputMode.Camera)
            { history.Clear(); Status = "Jump: press the Jump button"; Diagnostics = ""; return; }
            // CatNapClub: the game holds activities outside a round (title, Get Ready), so no jumps then either.
            if (locomotion != null && locomotion.holdActivities)
            { history.Clear(); armed = true; Status = "Jump: waiting for the round to start"; Diagnostics = ""; return; }

            float scale = controller.upperBodyOnly ? seatedScale : 1;
            bool hasLeft = ReadRaw(p, Movement.LeftHandHeight, now, out float left, out float leftQuality);
            bool hasRight = ReadRaw(p, Movement.RightHandHeight, now, out float right, out float rightQuality);
            bool visible = hasLeft && hasRight && leftQuality >= minHandConfidence && rightQuality >= minHandConfidence;
            while (history.Count > 0 && now - history.Peek().time > riseWindow) history.Dequeue();
            while (recentRises.Count > 0 && now - recentRises.Peek().time > 3) recentRises.Dequeue();

            float low = Mathf.Min(left, right), high = Mathf.Max(left, right), rise = 0;
            bool jumped = false;
            // Both hands must also be in the top 30% of the camera picture (sent by the bridge as HandsUp).
            bool atTop = !requireHandsAtTop || (ReadRaw(p, Movement.HandsUp, now, out float handsUp, out _) && handsUp >= .5f);
            if (!visible)
            {
                // Keep the history: a hand briefly lost mid-throw shouldn't erase where the throw started.
                Status = "Jump: P" + p + (hasLeft || hasRight ? " hands not visible" : " not visible");
            }
            else
            {
                float lowest = low;
                foreach (var sample in history) lowest = Mathf.Min(lowest, sample.low);
                history.Enqueue((now, low));
                rise = low - lowest;
                recentRises.Enqueue((now, rise));
                // Both hands must come down again before the next jump, so holding them up can't repeat it.
                if (!armed && high < rearmLevel * scale) armed = true;
                if (armed && atTop && low >= upLevel * scale && rise >= riseAmount * scale && TriggerJump(now))
                { armed = false; jumped = true; }
                Status = IsJumping ? "JUMP!" : armed ? "Jump ready: P" + p + " throw both hands up"
                    : "Jump: P" + p + " lower both hands below shoulders to re-arm";
            }

            float bestRise = 0;
            foreach (var sample in recentRises) bestRise = Mathf.Max(bestRise, sample.rise);
            Diagnostics = !hasLeft && !hasRight ? "P" + p + " not tracked" : string.Format(CultureInfo.InvariantCulture,
                "P{0} hands L {1:+0.00;-0.00} R {2:+0.00;-0.00} (conf {3:0.0}/{4:0.0}) | need both ≥ {5:0.00}, rise ≥ {6:0.00} in {7:0.0}s | best rise (3 s) {8:0.00} | top 30%: {9}",
                p, left, right, leftQuality, rightQuality, upLevel * scale, riseAmount * scale, riseWindow, bestRise,
                !requireHandsAtTop ? "not required" : atTop ? "YES" : "no");
            WriteLog(now, left, right, leftQuality, rightQuality, visible, rise, jumped);
        }

        // The bridge's measurement before neutral subtraction, plus its confidence.
        bool ReadRaw(int p, Movement movement, float now, out float value, out float quality)
        {
            value = quality = 0;
            if (!controller.IsTracked(p, now)) return false;
            if (!(ValuesField?.GetValue(controller) is float[,] values) || !(ConfidenceField?.GetValue(controller) is float[,] confidence))
                return false;
            int i = p - 1, k = (int)movement;
            if (i < 0 || i >= values.GetLength(0) || k >= values.GetLength(1)) return false;
            value = values[i, k];
            quality = confidence[i, k];
            return true;
        }

        void WriteLog(float now, float left, float right, float leftQuality, float rightQuality, bool visible, float rise, bool jumped)
        {
            if (!writeDebugLog || !Application.isPlaying) return;
            if (log == null)
            {
                try
                {
                    string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/digiphant-jump-log.csv"));
                    log = new StreamWriter(path, false) { AutoFlush = true };
                    log.WriteLine("time,left,right,leftConf,rightConf,visible,rise,armed,jumped,upperBody");
                }
                catch (IOException) { writeDebugLog = false; return; }
            }
            log.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:0.000},{1:0.000},{2:0.000},{3:0.00},{4:0.00},{5},{6:0.000},{7},{8},{9}",
                now, left, right, leftQuality, rightQuality, visible ? 1 : 0, rise, armed ? 1 : 0, jumped ? 1 : 0, controller.upperBodyOnly ? 1 : 0));
        }

        void ApplyPose(float arc)
        {
            foreach (var c in controller.controls)
            {
                if ((DisabledField != null && (bool)DisabledField.GetValue(c)) || c.bones == null || c.localAxis.sqrMagnitude < .0001f || string.IsNullOrEmpty(c.label)) continue;
                float amount = c.label.EndsWith(" leg") ? legTuck : c.label == "Head nod" ? headLift : c.label == "Trunk curl" ? trunkLift : 0;
                if (amount == 0) continue;
                var rotation = Quaternion.AngleAxis(amount * arc * c.degrees, c.localAxis.normalized);
                foreach (var bone in c.bones)
                    if (bone != null) bone.localRotation *= rotation;
            }
        }

        void OnGUI()
        {
            if (controller == null) return;
            float left = controller.showControls ? Mathf.Min(350, Screen.width * .32f) + 30 : 10;
            float width = Screen.width - left - 140;
            if (showStatus)
                GUI.Label(new Rect(left, 10, width, 40), IsJumping ? "JUMP!" : Status,
                    new GUIStyle(GUI.skin.label) { fontSize = IsJumping ? 28 : 14, fontStyle = FontStyle.Bold });
            if (showDiagnostics && !string.IsNullOrEmpty(Diagnostics))
                GUI.Label(new Rect(left, showStatus ? 44 : 10, width, 40), Diagnostics, new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true });
            if (showTestButton && controller.inputMode == InputMode.TestSliders && GUI.Button(new Rect(Screen.width - 130, 10, 120, 32), "Jump"))
                TriggerJump(Time.realtimeSinceStartup);
        }
    }
}
