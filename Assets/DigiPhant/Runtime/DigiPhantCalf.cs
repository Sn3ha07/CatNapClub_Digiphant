using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace DigiPhant
{
    // A smaller elephant that trails the controlled one. It reads no performer input.
    [DefaultExecutionOrder(100)] // After DigiPhantController moves the leader in LateUpdate.
    public class DigiPhantCalf : MonoBehaviour
    {
        public DigiPhantLocomotion leader;
        public Animator calfAnimator;
        [Tooltip("Calf node whose exported animation drift is removed, as for the leader.")]
        public Transform motionRoot;
        [Tooltip("Distance kept behind the leader's centre, in Unity units.")]
        [Min(.5f)] public float followDistance = 4.5f;
        [Tooltip("The calf steps around the leader rather than coming closer than this.")]
        [Min(.5f)] public float minimumGap = 3.5f;
        [Min(.1f)] public float maxSpeed = 6;
        [Min(.1f)] public float acceleration = 6;
        [Min(0)] public float turnDegreesPerSecond = 180;
        [Tooltip("Copy the elephant's eating, drinking and sleeping, standing still while it does.")]
        public bool copyActivities = true;
        [Tooltip("Where the calf goes to eat while the elephant eats. Empty: the course's \"01 Feeding bush\".")]
        public Transform feedingSpot;
        [Tooltip("Where the calf goes to drink while the elephant drinks. Empty: the course's \"02 Water basin\".")]
        public Transform drinkingSpot;
        [Tooltip("How far from the bush or basin centre the calf stands to eat or drink.")]
        [Min(.5f)] public float spotDistance = 2.6f;
        [Tooltip("Seconds the calf stays at the water after the elephant stops drinking.")]
        [Min(0)] public float lingerSeconds = 5;
        [Tooltip("Top speed while running to catch up with the elephant after lingering.")]
        [Min(.1f)] public float catchUpSpeed = 10;
        public float CurrentSpeed { get; private set; }
        public DigiPhantLocomotion.Rest CurrentActivity { get; private set; }
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable[] playables;
        AnimationClip[] clips;
        Vector3 motionPosition;
        Quaternion motionRotation;
        RuntimeAnimatorController previousController;
        bool previousRootMotion;
        float cycle;
        Vector3 lastLeadPosition;
        bool hasLastLead;
        Transform lingerSpot;
        float lingerUntil;
        bool catchingUp;

        void OnEnable()
        {
            if (!Application.isPlaying || !leader || !calfAnimator || !leader.idle || !leader.walk || !leader.run) return;
            if (motionRoot) { motionPosition = motionRoot.localPosition; motionRotation = motionRoot.localRotation; }
            previousController = calfAnimator.runtimeAnimatorController;
            previousRootMotion = calfAnimator.applyRootMotion;
            calfAnimator.runtimeAnimatorController = null;
            calfAnimator.applyRootMotion = false;
            calfAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("DigiPhant calf");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            // Gaits, then the elephant's activities in DigiPhantLocomotion.Rest order (missing clips fall back to idle).
            clips = new[] { leader.idle, leader.walk, leader.run,
                leader.lieDown ? leader.lieDown : leader.idle, leader.sleepLoop ? leader.sleepLoop : leader.idle,
                leader.getUp ? leader.getUp : leader.idle, leader.eatLoop ? leader.eatLoop : leader.idle,
                leader.drinkLoop ? leader.drinkLoop : leader.idle };
            mixer = AnimationMixerPlayable.Create(graph, clips.Length);
            playables = new AnimationClipPlayable[clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                playables[i] = AnimationClipPlayable.Create(graph, clips[i]);
                playables[i].SetApplyFootIK(false);
                graph.Connect(playables[i], 0, mixer, i);
                mixer.SetInputWeight(i, i == 0 ? 1 : 0);
            }
            var output = AnimationPlayableOutput.Create(graph, "Calf gait", calfAnimator);
            output.SetSourcePlayable(mixer);
            graph.Play();
            if (!feedingSpot) feedingSpot = FindSpot("01 Feeding bush");
            if (!drinkingSpot) drinkingSpot = FindSpot("02 Water basin");
            hasLastLead = false;
            lingerSpot = null;
            catchingUp = false;
        }

        static Transform FindSpot(string name)
        {
            var found = GameObject.Find(name); // Active objects only, so the hidden original course is skipped.
            return found ? found.transform : null;
        }

        Transform SpotFor(DigiPhantLocomotion.Rest activity) =>
            activity == DigiPhantLocomotion.Rest.Eating ? feedingSpot : activity == DigiPhantLocomotion.Rest.Drinking ? drinkingSpot : null;

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        Vector3 Behind(Transform lead, float height)
        {
            var target = lead.position - Flat(lead.forward).normalized * followDistance;
            target.y = height;
            return target;
        }

        void LateUpdate()
        {
            if (!graph.IsValid() || !leader.travelRoot) return;
            float dt = Mathf.Clamp(Time.deltaTime, 0, .1f);
            Transform lead = leader.travelRoot;
            Vector3 position = transform.position;
            // A teleport (Return to starting position) places the calf straight behind again.
            bool leaderJumped = hasLastLead && Flat(lead.position - lastLeadPosition).magnitude > followDistance * 2;
            lastLeadPosition = lead.position;
            hasLastLead = true;
            if (leaderJumped)
            {
                transform.SetPositionAndRotation(Behind(lead, position.y), Quaternion.LookRotation(Flat(lead.forward)));
                position = transform.position;
                CurrentSpeed = 0;
                lingerSpot = null;
                catchingUp = false;
            }
            // While the elephant eats or drinks, the calf walks to the bush or the water and does the same there.
            // While it sleeps, the calf lies down where it is.
            var leaderActivity = copyActivities ? leader.CurrentRest : DigiPhantLocomotion.Rest.Awake;
            Transform spot = SpotFor(leaderActivity);
            Vector3 target = Behind(lead, position.y);
            // When the elephant finishes drinking, the calf stays by the water for a while, then runs to catch up.
            // After eating it follows straight away.
            var previousActivity = CurrentActivity;
            if (leaderActivity != DigiPhantLocomotion.Rest.Awake)
                lingerSpot = null;
            else if (previousActivity == DigiPhantLocomotion.Rest.Drinking && drinkingSpot)
            {
                lingerSpot = drinkingSpot;
                lingerUntil = Time.time + lingerSeconds;
            }
            CurrentActivity = DigiPhantLocomotion.Rest.Awake;
            if (lingerSpot)
            {
                if (Time.time < lingerUntil)
                {
                    CurrentSpeed = 0;
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(Flat(lingerSpot.position - position)), turnDegreesPerSecond * dt);
                    Animate(0, dt);
                    return;
                }
                lingerSpot = null;
                catchingUp = true;
            }
            if (spot)
            {
                // Stand at the edge of the bush or basin, on the side the calf comes from.
                Vector3 fromSpot = Flat(position - spot.position);
                if (fromSpot.sqrMagnitude < .0001f) fromSpot = -Flat(lead.forward);
                target = spot.position + fromSpot.normalized * spotDistance;
                // If the elephant stands on that side, use the far side so the calf isn't pushed away from it.
                Vector3 leadFromSpot = Flat(lead.position - spot.position);
                if (Flat(target - lead.position).magnitude < minimumGap && leadFromSpot.sqrMagnitude > .0001f)
                    target = spot.position - leadFromSpot.normalized * spotDistance;
                target.y = position.y;
                if (Flat(target - position).magnitude < .3f)
                {
                    CurrentActivity = leaderActivity;
                    CurrentSpeed = 0;
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(Flat(spot.position - position)), turnDegreesPerSecond * dt);
                    Animate(0, dt);
                    return;
                }
            }
            else if (leaderActivity != DigiPhantLocomotion.Rest.Awake)
            {
                CurrentActivity = leaderActivity;
                CurrentSpeed = 0;
                Animate(0, dt);
                return;
            }
            Vector3 toTarget = Flat(target - position);
            if (catchingUp && (spot || toTarget.magnitude < 1)) catchingUp = false;
            float desired = toTarget.magnitude < .05f ? 0 : Mathf.Min(catchingUp ? catchUpSpeed : maxSpeed, toTarget.magnitude * 2);
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, desired, acceleration * dt);
            Vector3 next = position + toTarget.normalized * Mathf.Min(CurrentSpeed * dt, toTarget.magnitude);
            // Walk around the leader rather than through it.
            Vector3 away = Flat(next - lead.position);
            if (away.magnitude < minimumGap)
            {
                Vector3 side = away.sqrMagnitude > .0001f ? away.normalized : -Flat(lead.forward).normalized;
                next = new Vector3(lead.position.x, position.y, lead.position.z) + side * minimumGap;
            }
            Vector3 moved = Flat(next - position);
            transform.position = next;
            float speed = dt > 0 ? moved.magnitude / dt : 0;
            // Face the way it walks; when standing, turn to match the leader.
            Vector3 facing = speed > .2f ? moved : Flat(lead.forward);
            if (facing.sqrMagnitude > .000001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(facing), turnDegreesPerSecond * dt);
            Animate(speed, dt);
        }

        void Animate(float speed, float dt)
        {
            // Shorter legs take quicker steps: playback rate scales with the calf's size.
            float scale = Mathf.Max(.01f, transform.lossyScale.y / Mathf.Max(.01f, leader.elephantAnimator.transform.lossyScale.y));
            int action = speed < .1f ? 0 : speed < leader.walkSpeed * scale * 1.6f ? 1 : 2;
            var activity = CurrentActivity;
            if (activity != DigiPhantLocomotion.Rest.Awake) action = 2 + (int)activity;
            float rate = action == 0 ? 1 : speed / ((action == 1 ? leader.walkSpeed : leader.runSpeed) * scale);
            cycle = Mathf.Repeat(cycle + dt * rate / Mathf.Max(.01f, clips[action].length), 1);
            float weightBlend = 1 - Mathf.Exp(-dt / Mathf.Max(.01f, leader.animationBlendSeconds));
            for (int i = 0; i < clips.Length; i++)
            {
                mixer.SetInputWeight(i, Mathf.Lerp(mixer.GetInputWeight(i), i == action ? 1 : 0, weightBlend));
                if (i < 3) playables[i].SetTime(cycle * clips[i].length);
            }
            // Activity clips use the elephant's own timer, so both lie down, sleep, eat and drink in step.
            float t = leader.RestTime;
            playables[3].SetTime(activity == DigiPhantLocomotion.Rest.LyingDown ? Mathf.Min(t, clips[3].length) : clips[3].length);
            playables[4].SetTime(activity == DigiPhantLocomotion.Rest.Sleeping ? Mathf.Repeat(t, clips[4].length) : 0);
            playables[5].SetTime(activity == DigiPhantLocomotion.Rest.GettingUp ? Mathf.Min(t, clips[5].length) : clips[5].length);
            playables[6].SetTime(activity == DigiPhantLocomotion.Rest.Eating ? Mathf.Repeat(t, clips[6].length) : 0);
            playables[7].SetTime(activity == DigiPhantLocomotion.Rest.Drinking ? Mathf.Repeat(t, clips[7].length) : 0);
            graph.Evaluate(0);
            // Remove animation drift, except while lying down or asleep, when the clips lower the body.
            bool lowered = activity == DigiPhantLocomotion.Rest.LyingDown || activity == DigiPhantLocomotion.Rest.Sleeping
                || activity == DigiPhantLocomotion.Rest.GettingUp;
            if (motionRoot && !lowered) { motionRoot.localPosition = motionPosition; motionRoot.localRotation = motionRotation; }
        }

        void OnDisable()
        {
            if (!graph.IsValid()) return;
            graph.Destroy();
            if (calfAnimator)
            {
                calfAnimator.runtimeAnimatorController = previousController;
                calfAnimator.applyRootMotion = previousRootMotion;
            }
        }
    }
}
