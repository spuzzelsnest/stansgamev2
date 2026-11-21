using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Switches a humanoid between animated and ragdoll. Build the ragdoll bones once with
    /// GameObject > 3D Object > Ragdoll... (Unity's Ragdoll Wizard); this script finds them.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class Ragdoll : MonoBehaviour
    {
        public Animator animator;
        public float hitForceMultiplier = 1f;

        Rigidbody[] bodies;
        Collider[] boneColliders;
        Collider mainCollider;
        CharacterController controller;

        void Awake()
        {
            if (!animator) animator = GetComponentInChildren<Animator>();
            controller = GetComponent<CharacterController>();
            mainCollider = GetComponent<Collider>();
            bodies = GetComponentsInChildren<Rigidbody>();
            boneColliders = GetComponentsInChildren<Collider>();
            SetRagdoll(false);
            GetComponent<Health>().Died += OnDied;
        }

        void OnDied(Health h, DamageInfo info)
        {
            if (bodies.Length == 0 || (bodies.Length == 1 && bodies[0].gameObject == gameObject))
            {
                // placeholder soldier without ragdoll bones: topple the whole body
                if (controller) controller.enabled = false;
                foreach (var mb in GetComponentsInChildren<MonoBehaviour>()) if (mb is IDisableOnDeath) mb.enabled = false;
                var col = gameObject.AddComponent<CapsuleCollider>();
                col.height = 1.8f; col.radius = 0.3f; col.center = new Vector3(0f, 0.9f, 0f);
                var rb = gameObject.GetComponent<Rigidbody>();
                if (!rb) rb = gameObject.AddComponent<Rigidbody>();
                rb.mass = 80f; rb.isKinematic = false;
                rb.AddForceAtPosition(info.direction.normalized * Mathf.Max(info.force, 40f), transform.position + Vector3.up * 1.4f, ForceMode.Impulse);
                return;
            }
            SetRagdoll(true);
            Rigidbody target = info.hitBody;
            if (!target && bodies.Length > 0)
            {
                // nearest bone to the hit point
                float best = float.MaxValue;
                foreach (var b in bodies)
                {
                    float d = (b.worldCenterOfMass - info.point).sqrMagnitude;
                    if (d < best) { best = d; target = b; }
                }
            }
            if (target) target.AddForceAtPosition(info.direction.normalized * info.force * hitForceMultiplier, info.point, ForceMode.Impulse);
        }

        public void SetRagdoll(bool on)
        {
            foreach (var b in bodies)
            {
                if (b.gameObject == gameObject) continue;
                b.isKinematic = !on;
                b.interpolation = on ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            }
            foreach (var c in boneColliders)
            {
                if (c == mainCollider || c == controller) continue;
                c.enabled = on;
            }
            if (animator) animator.enabled = !on;
            if (controller) controller.enabled = !on;
            foreach (var mb in GetComponentsInChildren<MonoBehaviour>())
            {
                if (on && mb is IDisableOnDeath) mb.enabled = false;
            }
        }
    }

    /// <summary>Marker for scripts (AI, input) that should stop when the soldier dies.</summary>
    public interface IDisableOnDeath { }
}
