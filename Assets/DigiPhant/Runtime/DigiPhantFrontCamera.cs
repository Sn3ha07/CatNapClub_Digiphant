using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DigiPhant
{
    // Films the elephant from the front, keeping its face and the track in view. Course scenery has no
    // colliders, so line of sight is tested against renderer bounds; the scenery itself is never changed.
    [DefaultExecutionOrder(200)] // After DigiPhantController and DigiPhantCalf move things in LateUpdate.
    [RequireComponent(typeof(Camera))]
    public class DigiPhantFrontCamera : MonoBehaviour
    {
        public DigiPhantLocomotion leader;
        [Tooltip("Preferred distance in front of the elephant's face, in Unity units.")]
        [Min(2)] public float distance = 12;
        [Min(1)] public float minimumDistance = 4.5f;
        [Min(2)] public float maximumDistance = 20;
        [Tooltip("Preferred camera height above the ground.")]
        [Min(.5f)] public float height = 6;
        [Tooltip("Degrees to one side of straight ahead, so the face and the track behind it both show.")]
        [Range(-60, 60)] public float sideAngle = 22;
        [Tooltip("How far ahead of the elephant's face to aim, so the path it is walking toward stays in view.")]
        [Min(0)] public float lookAhead = 3;
        [Tooltip("How quickly the camera glides to a new spot. Higher is snappier.")]
        [Range(.5f, 10)] public float followSpeed = 3;
        [Tooltip("Scenery smaller than this (paths, markers, start line) never counts as blocking.")]
        [Min(0)] public float ignoreBelowHeight = .5f;
        [Tooltip("Draw the scene's sky behind the elephant instead of the camera's solid background colour.")]
        public bool showSky = true;
        public bool IsViewClear { get; private set; } = true;

        Transform face;
        readonly List<Renderer> occluders = new List<Renderer>();
        float nextRefresh;
        Vector3 velocity;
        Vector3 smoothedLook;
        float yaw;
        bool placed;
        Camera view;
        CameraClearFlags previousClear;

        void OnEnable()
        {
            placed = false; nextRefresh = 0;
            view = GetComponent<Camera>();
            previousClear = view.clearFlags;
            // A front view looks toward the horizon, so show the sky; the original setting returns when disabled.
            if (showSky && RenderSettings.skybox) view.clearFlags = CameraClearFlags.Skybox;
        }

        void OnDisable() { if (view) view.clearFlags = previousClear; }

        bool Ready()
        {
            if (!leader) leader = FindAnyObjectByType<DigiPhantLocomotion>();
            if (!leader || !leader.travelRoot || !leader.elephantAnimator) return false;
            if (!face) face = leader.elephantAnimator.GetComponentsInChildren<Transform>()
                .FirstOrDefault(t => t.name == "elephant_Head_bone") ?? leader.elephantAnimator.transform;
            return true;
        }

        void RefreshOccluders()
        {
            // Everything drawn except the elephants and flat ground pieces may block the view.
            var elephants = new HashSet<Transform> { leader.elephantAnimator.transform };
            foreach (var calf in FindObjectsByType<DigiPhantCalf>(FindObjectsSortMode.None)) elephants.Add(calf.transform);
            occluders.Clear();
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || elephants.Any(e => r.transform.IsChildOf(e))) continue;
                var size = r.bounds.size;
                if (size.y < ignoreBelowHeight || size.x > 50 || size.z > 50) continue; // flat or whole-ground pieces
                occluders.Add(r);
            }
        }

        bool Blocked(Vector3 from, Vector3 to)
        {
            Vector3 direction = to - from;
            float length = direction.magnitude;
            if (length < .01f) return false;
            var ray = new Ray(from, direction / length);
            foreach (var r in occluders)
            {
                if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var bounds = r.bounds;
                if (bounds.Contains(to)) return true; // camera inside an object
                if (bounds.IntersectRay(ray, out float hit) && hit < length - .2f) return true;
            }
            return false;
        }

        Vector3 Spot(Vector3 anchor, float angle, float range, float lift) =>
            anchor + Quaternion.Euler(0, angle, 0) * Vector3.forward * range + Vector3.up * lift;

        void LateUpdate()
        {
            if (!Ready()) return;
            if (Time.time >= nextRefresh) { RefreshOccluders(); nextRefresh = Time.time + 2; }
            Transform root = leader.travelRoot;
            Vector3 ground = root.position;
            Vector3 facePoint = face.position;
            // Look a little behind the face so the head and the track around the elephant share the frame.
            Vector3 subject = Vector3.Lerp(ground + Vector3.up * (facePoint.y - ground.y) * .8f, facePoint, .7f);
            // Aim a little ahead so the path the elephant is walking toward fills more of the view;
            // the line of sight that must stay clear is still the one to the elephant itself.
            Vector3 look = subject + Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized * lookAhead;

            // Swing around smoothly as the elephant turns.
            Vector3 forward = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
            float targetYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            bool teleported = placed && Vector3.Distance(transform.position, look) > maximumDistance * 3;
            yaw = placed && !teleported ? Mathf.LerpAngle(yaw, targetYaw, 1 - Mathf.Exp(-followSpeed * Time.deltaTime)) : targetYaw;

            // Preferred spot first, then closer, farther, higher and to either side until the face is visible.
            Vector3 anchor = new Vector3(facePoint.x, ground.y, facePoint.z);
            Vector3 chosen = Spot(anchor, yaw + sideAngle, distance, height);
            IsViewClear = !Blocked(subject, chosen);
            if (!IsViewClear)
            {
                // Try other spots, least different from the preferred view first. Rising or swinging
                // aside is preferred over moving in close, so the path ahead stays in view.
                var ranges = new[] { distance, distance * 1.3f, maximumDistance, distance * .75f, minimumDistance };
                var angles = new[] { 0f, -15, 15, -30, 30, -45, 45, -60, 60 };
                var lifts = new[] { height, height * 1.5f, height * 2 };
                var candidates =
                    from range in ranges from angle in angles from lift in lifts
                    let cost = Mathf.Abs(angle) / 15f + (lift - height) / height * 1.5f
                        + (range < distance ? (distance - range) / 1.5f : (range - distance) / 6f)
                    orderby cost
                    select Spot(anchor, yaw + sideAngle + angle, range, lift);
                foreach (var candidate in candidates)
                {
                    if (Blocked(subject, candidate)) continue;
                    chosen = candidate; IsViewClear = true; break;
                }
            }

            if (!placed || teleported)
            {
                transform.position = chosen; smoothedLook = look; velocity = Vector3.zero; placed = true;
            }
            else
            {
                transform.position = Vector3.SmoothDamp(transform.position, chosen, ref velocity, 1 / followSpeed);
                smoothedLook = Vector3.Lerp(smoothedLook, look, 1 - Mathf.Exp(-followSpeed * 2 * Time.deltaTime));
            }
            transform.rotation = Quaternion.LookRotation(smoothedLook - transform.position);
        }
    }
}
