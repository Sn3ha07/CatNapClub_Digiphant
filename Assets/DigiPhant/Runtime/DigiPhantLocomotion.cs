using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace DigiPhant
{
    public enum SignalCombination { Sum, Average }
    [Serializable] public class MovementSource
    {
        [Range(1, 4)] public int performer = 1;
        public Movement movement;
        [Range(-2, 2)] public float weight = 1;
        [Tooltip("Ignore this input below its neutral level, so only raising counts. Lowering then stops the effect instead of reversing it.")]
        public bool raiseOnly;
    }
    [Serializable] public class MovementDriver
    {
        public SignalCombination combination = SignalCombination.Average;
        public MovementSource[] sources = Array.Empty<MovementSource>();
        [Range(0, 3)] public float sensitivity = 1;
        [Range(0, .5f)] public float deadZone = .12f;
        public bool TryRead(DigiPhantController controller, float now, out float value)
        {
            value = 0;
            float totalWeight = 0;
            bool anySource = false, anyRead = false;
            foreach (var source in sources)
            {
                if (source == null || Mathf.Abs(source.weight) < .0001f) continue;
                anySource = true;
                if (!controller.TryReadMovement(source.performer, source.movement, now, out float input))
                {
                    // A raise-only hand the camera can't see clearly (often one resting low) counts as not raised,
                    // so it doesn't block the other hand. Any other unreadable input still stops the channel.
                    if (source.raiseOnly) continue;
                    return false;
                }
                anyRead = true;
                if (source.raiseOnly) input = Mathf.Max(0, input);
                value += input * source.weight;
                totalWeight += Mathf.Abs(source.weight);
            }
            if (!anySource) return true; // An empty mapping leaves this channel at zero.
            if (!anyRead) return false;  // Nobody readable for this channel (e.g. the performer left).
            if (totalWeight == 0) return true;
            if (combination == SignalCombination.Average) value /= totalWeight;
            value = Mathf.Clamp(value * sensitivity, -1, 1);
            value = Mathf.Abs(value) <= deadZone ? 0 : Mathf.Sign(value) * Mathf.InverseLerp(deadZone, 1, Mathf.Abs(value));
            return true;
        }
    }

    public class DigiPhantLocomotion : MonoBehaviour
    {
        public bool enableLocomotion = true;
        public Transform travelRoot;
        public Animator elephantAnimator;
        public Transform animationMotionRoot;
        public Camera followCamera;
        public bool followElephant = true;
        [Tooltip("Film the elephant from the front, steering around scenery (DigiPhantFrontCamera). Off restores the fixed follow camera.")]
        public bool frontCamera = true;
        public AnimationClip idle, walk, run, backward, turnLeft, turnRight, walkLeft, walkRight, runLeft, runRight;
        public AnimationClip lieDown, sleepLoop, getUp, eatLoop, drinkLoop;
        // P1 raises the left hand to go forward (higher runs) and the right hand to back up; hands at rest stand still.
        public MovementDriver forward = new MovementDriver { combination = SignalCombination.Sum, sources = new[] {
            new MovementSource { performer = 1, movement = Movement.LeftHandHeight, raiseOnly = true },
            new MovementSource { performer = 1, movement = Movement.RightHandHeight, weight = -1, raiseOnly = true } } };
        // P2 raises the right hand to turn right and the left hand to turn left; both together cancel.
        // Raise-only: lowering a hand stops the turn and the new heading stays.
        public MovementDriver steering = new MovementDriver { combination = SignalCombination.Sum, sources = new[] {
            new MovementSource { performer = 2, movement = Movement.RightHandHeight, raiseOnly = true },
            new MovementSource { performer = 2, movement = Movement.LeftHandHeight, weight = -1, raiseOnly = true } } };
        [Tooltip("While the sleep input is shown (P3 eyes closed with praying hands by the face, by default), the elephant lies down and sleeps.")]
        public bool enableSleep = true;
        public MovementDriver sleep = new MovementDriver { sources = new[] { new MovementSource { performer = 3, movement = Movement.SleepPose } } };
        [Range(.1f, 1)] public float sleepThreshold = .5f;
        [Tooltip("Once asleep, the elephant sleeps at least this long before it can get up, in seconds.")]
        [Min(0)] public float minimumSleepSeconds = 5;
        public bool testSleep;
        [Tooltip("While the eat input is shown (P3 holding a banana by default), the elephant stands and eats.")]
        public bool enableEat = true;
        public MovementDriver eat = new MovementDriver { sources = new[] { new MovementSource { performer = 3, movement = Movement.Banana } } };
        public bool testEat;
        [Tooltip("While the drink input is shown (P3 showing a water bottle by default), the elephant stands and drinks.")]
        public bool enableDrink = true;
        public MovementDriver drink = new MovementDriver { sources = new[] { new MovementSource { performer = 3, movement = Movement.Bottle } } };
        public bool testDrink;
        [Tooltip("Ignore sleep, eat and drink inputs (set by the game outside a round, e.g. on the title screen).")]
        public bool holdActivities;
        [Min(.01f)] public float walkSpeed = 1.8f;
        [Min(.01f)] public float runSpeed = 4.5f;
        [Min(.01f)] public float backwardSpeed = 1.2f;
        [Min(0)] public float turnDegreesPerSecond = 60;
        [Range(.2f, .95f)] public float runThreshold = .65f;
        [Min(.01f)] public float animationBlendSeconds = .25f;
        [Range(0, 20)] public float inputSmoothing = 6;
        [Tooltip("Maximum travel distance from the starting point, in Unity units. Zero disables the limit.")]
        [Min(0)] public float stageRadius = 15;
        [Range(-1, 1)] public float testForward;
        [Range(-1, 1)] public float testSteering;
        public string CurrentAction { get; private set; } = "Idle";
        public float CurrentSpeed { get; private set; }
        [Tooltip("In Play (camera mode), write why the elephant does or doesn't travel to Temp/digiphant-travel-log.csv.")]
        public bool writeTravelLog = true;
        System.IO.StreamWriter travelLog;

        // One line per frame: is calibration set, what each travel source reads (or 'hidden'), whether steering
        // is readable, and what the elephant ends up doing. Used to diagnose 'raising my hand does nothing'.
        void LogTravel(float now, bool moving, bool turning, float desiredForward)
        {
            if (!Application.isPlaying) return;
            if (travelLog == null)
            {
                try
                {
                    string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Temp/digiphant-travel-log.csv"));
                    travelLog = new System.IO.StreamWriter(path, false) { AutoFlush = true };
                    travelLog.WriteLine("time,calibrated,p1Tracked,p2Tracked,sources,forwardReadable,steeringReadable,desiredForward,throttle,rest,action");
                }
                catch (System.IO.IOException) { writeTravelLog = false; return; }
            }
            var sources = string.Join(" ", forward.sources.Where(s => s != null).Select(s =>
                $"P{s.performer}{s.movement}{(s.raiseOnly ? "(raise)" : "")}=" +
                (controller.TryReadMovement(s.performer, s.movement, now, out float v) ? v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                    : controller.IsMovementVisible(s.performer, s.movement, now) ? "uncalibrated" : "hidden")));
            travelLog.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.000},{1},{2},{3},{4},{5},{6},{7:0.00},{8:0.00},{9},{10}",
                now, controller.IsCalibrated ? 1 : 0, controller.IsTracked(1, now) ? 1 : 0, controller.IsTracked(2, now) ? 1 : 0,
                sources, moving ? 1 : 0, turning ? 1 : 0, desiredForward, throttle, rest, CurrentAction));
        }
        public bool AnimationActive => graph.IsValid();
        public bool Asleep => rest == Rest.LyingDown || rest == Rest.Sleeping || rest == Rest.GettingUp;
        bool SleepAvailable => enableSleep && lieDown && sleepLoop && getUp;
        bool EatAvailable => enableEat && eatLoop;
        bool DrinkAvailable => enableDrink && drinkLoop;
        public enum Rest { Awake, LyingDown, Sleeping, GettingUp, Eating, Drinking }
        Rest rest;
        float restTime;
        // Read by the calf so it can copy what the elephant is doing, in step.
        public Rest CurrentRest => rest;
        public float RestTime => restTime;
        bool wantsSleep, wantsEat, wantsDrink;
        DigiPhantController controller;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable[] playables;
        AnimationClip[] clips;
        readonly string[] actions = { "Idle", "Walk", "Run", "Backward", "Turn left", "Turn right", "Walk left", "Walk right", "Run left", "Run right" };
        readonly Dictionary<Transform, Pose> restPose = new Dictionary<Transform, Pose>();
        Vector3 startPosition, cameraOffset, cameraStart, motionPosition;
        Quaternion startRotation, motionRotation, cameraStartRotation;
        bool initialised;
        bool previousAnimatorEnabled, previousRootMotion;
        AnimatorCullingMode previousCulling;
        RuntimeAnimatorController previousController;
        float throttle, turn, cycle;

        void OnEnable() { controller = GetComponent<DigiPhantController>(); }
        public bool Initialise()
        {
            if (initialised) return true;
            if (!travelRoot || !elephantAnimator || !animationMotionRoot || !idle || !walk || !run || !backward || !turnLeft || !turnRight)
            { CurrentAction = "Locomotion setup incomplete"; return false; }
            controller = GetComponent<DigiPhantController>();
            startPosition = travelRoot.position;
            startRotation = travelRoot.rotation;
            if (followCamera)
            {
                cameraStart = followCamera.transform.position; cameraStartRotation = followCamera.transform.rotation;
                cameraOffset = cameraStart - startPosition;
            }
            motionPosition = animationMotionRoot.localPosition;
            motionRotation = animationMotionRoot.localRotation;
            foreach (var bone in elephantAnimator.GetComponentsInChildren<Transform>())
                restPose[bone] = new Pose(bone.localPosition, bone.localRotation);
            initialised = true;
            return true;
        }
        bool StartAnimation()
        {
            if (graph.IsValid()) return true;
            if (!Initialise()) return false;
            previousAnimatorEnabled = elephantAnimator.enabled;
            previousRootMotion = elephantAnimator.applyRootMotion;
            previousCulling = elephantAnimator.cullingMode;
            previousController = elephantAnimator.runtimeAnimatorController;
            elephantAnimator.runtimeAnimatorController = null;
            elephantAnimator.applyRootMotion = false;
            elephantAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            elephantAnimator.enabled = true;
            graph = PlayableGraph.Create("DigiPhant locomotion");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            clips = new[] { idle, walk, run, backward, turnLeft, turnRight, walkLeft ? walkLeft : walk,
                walkRight ? walkRight : walk, runLeft ? runLeft : run, runRight ? runRight : run,
                lieDown ? lieDown : idle, sleepLoop ? sleepLoop : idle, getUp ? getUp : idle, eatLoop ? eatLoop : idle,
                drinkLoop ? drinkLoop : idle };
            mixer = AnimationMixerPlayable.Create(graph, clips.Length);
            playables = new AnimationClipPlayable[clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                playables[i] = AnimationClipPlayable.Create(graph, clips[i]);
                playables[i].SetApplyFootIK(false);
                graph.Connect(playables[i], 0, mixer, i);
                mixer.SetInputWeight(i, i == 0 ? 1 : 0);
            }
            var output = AnimationPlayableOutput.Create(graph, "Elephant gait", elephantAnimator);
            output.SetSourcePlayable(mixer);
            graph.Play();
            return true;
        }
        public bool InputsVisible(float now)
        {
            if (!enableLocomotion) return true;
            if (controller == null) controller = GetComponent<DigiPhantController>();
            var drivers = new List<MovementDriver> { forward, steering };
            if (SleepAvailable) drivers.Add(sleep);
            if (EatAvailable) drivers.Add(eat);
            if (DrinkAvailable) drivers.Add(drink);
            foreach (var driver in drivers)
                foreach (var source in driver.sources)
                    if (source != null && Mathf.Abs(source.weight) > .0001f &&
                        !controller.IsMovementVisible(source.performer, source.movement, now)) return false;
            return true;
        }
        public void ClampPerformers(int count)
        {
            // Default roles: P1 travels, P2 steers, P3 sleeps and eats. Those need their own performer.
            foreach (var source in forward.sources)
                if (source != null) source.performer = Mathf.Clamp(source.performer, 1, count);
            foreach (var source in steering.sources)
                if (source != null) source.performer = Mathf.Min(2, count);
            foreach (var source in sleep.sources)
                if (source != null) source.performer = Mathf.Min(3, count);
            foreach (var source in eat.sources.Concat(drink.sources))
                if (source != null) source.performer = Mathf.Min(3, count);
            enableSleep = enableEat = enableDrink = count >= 3;
            StopMotion();
        }
        public void StopMotion()
        {
            testForward = testSteering = throttle = turn = CurrentSpeed = 0;
            CurrentAction = "Idle";
        }
        public bool Evaluate(float now, float dt)
        {
            if (!enableLocomotion) { StopAnimation(); return false; }
            if (!StartAnimation()) return false;
            dt = Mathf.Clamp(dt, 0, .1f);
            float desiredForward = testForward, desiredTurn = testSteering;
            bool valid = true, moving = true, turning = true;
            if (controller.inputMode == InputMode.Camera)
            {
                moving = forward.TryRead(controller, now, out desiredForward);
                turning = steering.TryRead(controller, now, out desiredTurn);
                // Travel depends only on its own driver: if steering can't be read, go straight instead of
                // stopping, so P1 is never held up by P2. If travel itself can't be read, the elephant stops.
                if (!turning) desiredTurn = 0;
                valid = moving;
                // Untracked sleep input keeps the current state; a lower release level avoids flicker.
                if (SleepAvailable && sleep.TryRead(controller, now, out float raise))
                    wantsSleep = raise >= sleepThreshold || (wantsSleep && raise >= sleepThreshold * .5f);
                if (EatAvailable && eat.TryRead(controller, now, out float shown)) wantsEat = shown >= .5f;
                if (DrinkAvailable && drink.TryRead(controller, now, out float bottle)) wantsDrink = bottle >= .5f;
            }
            else { wantsSleep = testSleep; wantsEat = testEat; wantsDrink = testDrink; }
            if (holdActivities) wantsSleep = wantsEat = wantsDrink = false;
            UpdateRest(dt);
            if (!valid || rest != Rest.Awake) { throttle = turn = 0; }
            else
            {
                float blend = inputSmoothing <= 0 ? 1 : 1 - Mathf.Exp(-inputSmoothing * dt);
                throttle = Mathf.Lerp(throttle, Mathf.Clamp(desiredForward, -1, 1), blend);
                turn = Mathf.Lerp(turn, Mathf.Clamp(desiredTurn, -1, 1), blend);
            }
            if (Mathf.Abs(throttle) < .015f) throttle = 0;
            if (Mathf.Abs(turn) < .015f) turn = 0;
            bool running = throttle > runThreshold;
            CurrentSpeed = throttle < 0 ? throttle * backwardSpeed : throttle <= runThreshold
                ? throttle / runThreshold * walkSpeed : Mathf.Lerp(walkSpeed, runSpeed, Mathf.InverseLerp(runThreshold, 1, throttle));
            int action = throttle < 0 ? 3 : throttle > 0 ? (running ? 2 : 1) : turn < 0 ? 4 : turn > 0 ? 5 : 0;
            if (throttle > 0 && Mathf.Abs(turn) > .15f) action = running ? (turn < 0 ? 8 : 9) : (turn < 0 ? 6 : 7);
            CurrentAction = actions[action];
            if (writeTravelLog && controller.inputMode == InputMode.Camera) LogTravel(now, moving, turning, desiredForward);
            float animationRate = action == 0 ? 1 : throttle != 0
                ? Mathf.Max(.15f, Mathf.Abs(CurrentSpeed) / (throttle < 0 ? backwardSpeed : running ? runSpeed : walkSpeed))
                : Mathf.Max(.15f, Mathf.Abs(turn) * turnDegreesPerSecond / 18.75f);
            cycle += dt * animationRate / Mathf.Max(.01f, clips[action].length);
            cycle = Mathf.Repeat(cycle, 1);
            if (rest != Rest.Awake)
            {
                action = 9 + (int)rest; // Lie down, sleep, get up, eat and drink follow the ten gait clips.
                CurrentAction = rest == Rest.LyingDown ? "Lying down" : rest == Rest.Sleeping ? "Sleeping"
                    : rest == Rest.GettingUp ? "Getting up" : rest == Rest.Eating ? "Eating" : "Drinking";
            }
            float weightBlend = 1 - Mathf.Exp(-dt / Mathf.Max(.01f, animationBlendSeconds));
            for (int i = 0; i < clips.Length; i++)
            {
                mixer.SetInputWeight(i, Mathf.Lerp(mixer.GetInputWeight(i), i == action ? 1 : 0, weightBlend));
                if (i < 10) playables[i].SetTime(cycle * clips[i].length);
            }
            // One-shot clips hold their final pose while fading out.
            playables[10].SetTime(rest == Rest.LyingDown ? Mathf.Min(restTime, clips[10].length) : clips[10].length);
            playables[11].SetTime(rest == Rest.Sleeping ? Mathf.Repeat(restTime, clips[11].length) : 0);
            playables[12].SetTime(rest == Rest.GettingUp ? Mathf.Min(restTime, clips[12].length) : clips[12].length);
            playables[13].SetTime(rest == Rest.Eating ? Mathf.Repeat(restTime, clips[13].length) : 0);
            playables[14].SetTime(rest == Rest.Drinking ? Mathf.Repeat(restTime, clips[14].length) : 0);
            graph.Evaluate(0);
            // The travel parent alone owns world movement. Remove exported motion-node drift,
            // except while lying down or asleep, when the clips lower the body to the ground.
            if (!Asleep)
            {
                animationMotionRoot.localPosition = motionPosition;
                animationMotionRoot.localRotation = motionRotation;
            }
            travelRoot.Rotate(Vector3.up, turn * turnDegreesPerSecond * dt, Space.World);
            Vector3 destination = travelRoot.position + travelRoot.forward * CurrentSpeed * dt;
            if (stageRadius > 0)
            {
                Vector3 offset = destination - startPosition;
                offset.y = 0;
                if (offset.magnitude > stageRadius)
                {
                    destination = startPosition + Vector3.ClampMagnitude(offset, stageRadius);
                    CurrentAction += " · stage edge";
                }
            }
            destination.y = startPosition.y;
            travelRoot.position = destination;
            if (followElephant && followCamera) FollowWithCamera(destination);
            return true;
        }
        void UpdateRest(float dt)
        {
            if (!SleepAvailable) { wantsSleep = false; if (Asleep) EnterRest(Rest.Awake); }
            if (!EatAvailable) { wantsEat = false; if (rest == Rest.Eating) EnterRest(Rest.Awake); }
            if (!DrinkAvailable) { wantsDrink = false; if (rest == Rest.Drinking) EnterRest(Rest.Awake); }
            restTime += dt;
            switch (rest)
            {
                // Sleep wins over eating; eating loops while the banana is shown.
                // Sleep wins over drinking and eating; each loops while its object is shown.
                case Rest.Awake:
                    if (wantsSleep) EnterRest(Rest.LyingDown);
                    else if (wantsDrink) EnterRest(Rest.Drinking);
                    else if (wantsEat) EnterRest(Rest.Eating);
                    break;
                case Rest.Eating: if (wantsSleep) EnterRest(Rest.LyingDown); else if (!wantsEat) EnterRest(Rest.Awake); break;
                case Rest.Drinking: if (wantsSleep) EnterRest(Rest.LyingDown); else if (!wantsDrink) EnterRest(Rest.Awake); break;
                // Lying down always leads to sleep, which lasts at least minimumSleepSeconds.
                case Rest.LyingDown: if (restTime >= lieDown.length) EnterRest(minimumSleepSeconds > 0 || wantsSleep ? Rest.Sleeping : Rest.GettingUp); break;
                case Rest.Sleeping: if (!wantsSleep && restTime >= minimumSleepSeconds) EnterRest(Rest.GettingUp); break;
                case Rest.GettingUp: if (restTime >= getUp.length) EnterRest(Rest.Awake); break;
            }
        }
        void EnterRest(Rest state)
        {
            if (rest != state) restTime = 0;
            rest = state;
        }
        void WakeImmediately() { rest = Rest.Awake; restTime = 0; wantsSleep = testSleep = wantsEat = testEat = wantsDrink = testDrink = false; }
        void FollowWithCamera(Vector3 destination)
        {
            // The front camera moves itself; otherwise keep the original fixed offset and angle.
            var front = followCamera.GetComponent<DigiPhantFrontCamera>();
            if (frontCamera && !front) front = followCamera.gameObject.AddComponent<DigiPhantFrontCamera>();
            if (front && front.enabled != frontCamera)
            {
                front.enabled = frontCamera;
                if (!frontCamera) followCamera.transform.rotation = cameraStartRotation;
            }
            if (!frontCamera) followCamera.transform.position = destination + cameraOffset;
        }
        public void ResetPosition()
        {
            if (!Initialise()) return;
            WakeImmediately();
            StopMotion();
            travelRoot.SetPositionAndRotation(startPosition, startRotation);
            if (followCamera) followCamera.transform.position = cameraStart;
            controller.InvalidateCalibration();
        }
        public void StopAnimation()
        {
            if (!graph.IsValid()) return;
            graph.Destroy();
            WakeImmediately();
            if (elephantAnimator)
            {
                elephantAnimator.runtimeAnimatorController = previousController;
                elephantAnimator.applyRootMotion = previousRootMotion;
                elephantAnimator.cullingMode = previousCulling;
                elephantAnimator.enabled = previousAnimatorEnabled;
            }
            foreach (var pair in restPose)
                if (pair.Key) { pair.Key.localPosition = pair.Value.position; pair.Key.localRotation = pair.Value.rotation; }
            StopMotion();
        }
        void OnDisable()
        {
            StopAnimation();
            travelLog?.Dispose();
            travelLog = null;
            if (initialised)
            {
                if (travelRoot) travelRoot.SetPositionAndRotation(startPosition, startRotation);
                if (followCamera) followCamera.transform.position = cameraStart;
            }
            initialised = false;
            restPose.Clear();
        }
        public void DrawControls(float width)
        {
            var label = new GUIStyle(GUI.skin.label) { wordWrap = true };
            bool selected = GUILayout.Toggle(enableLocomotion, "Enable locomotion");
            if (selected != enableLocomotion) { enableLocomotion = selected; StopMotion(); }
            if (!enableLocomotion) return;
            GUILayout.Label(CurrentAction + " | " + Mathf.Abs(CurrentSpeed).ToString("0.0") + " units/s", label, GUILayout.Width(width));
            if (controller.inputMode == InputMode.TestSliders)
            {
                GUILayout.Label("Travel: backward / stop / forward", label, GUILayout.Width(width));
                testForward = GUILayout.HorizontalSlider(testForward, -1, 1);
                GUILayout.Label("Turn: left / straight / right", label, GUILayout.Width(width));
                testSteering = GUILayout.HorizontalSlider(testSteering, -1, 1);
                if (GUILayout.Button("Stop moving")) StopMotion();
                if (SleepAvailable) testSleep = GUILayout.Toggle(testSleep, "Sleep");
                if (EatAvailable) testEat = GUILayout.Toggle(testEat, "Eat");
                if (DrinkAvailable) testDrink = GUILayout.Toggle(testDrink, "Drink");
            }
            else
            {
                string Describe(MovementDriver driver) => string.Join(" + ", Array.ConvertAll(driver.sources,
                    s => s == null ? "?" : (s.weight < 0 ? "−" : "") + "P" + s.performer + " " + s.movement));
                // Live state shows whether Unity is receiving the on/off inputs.
                string State(MovementDriver driver) => driver.TryRead(controller, Time.realtimeSinceStartup, out float v)
                    ? (v >= .5f ? " · shown" : " · not shown") : " · not tracked";
                GUILayout.Label("Travel: " + Describe(forward) + "\nTurn: " + Describe(steering) +
                    (SleepAvailable ? "\nSleep: " + Describe(sleep) + State(sleep) : "") +
                    (EatAvailable ? "\nEat: " + Describe(eat) + State(eat) : "") +
                    (DrinkAvailable ? "\nDrink: " + Describe(drink) + State(drink) : "") +
                    "\nChange these in the Inspector.",
                    label, GUILayout.Width(width));
            }
            if (GUILayout.Button("Return to starting position")) ResetPosition();
            GUILayout.Space(8);
        }
    }
}
