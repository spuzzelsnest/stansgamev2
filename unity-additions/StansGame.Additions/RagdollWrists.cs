using System.Collections.Generic;
using UnityEngine;

namespace StansGame.Additions
{
    /// <summary>
    /// Relaxes the wrists of the dead. The soldiers' ragdolls have rigidbodies and joints down to the
    /// forearms, but none on the hands, so a hand keeps the rotation it had in the last animated frame.
    /// With the weapon hand IK that can be a wrist bent 150 degrees, which then lies frozen on the
    /// ground. When a soldier's animator stops (the ragdoll takes over), each hand is eased back to its
    /// rest rotation from the model's bind pose over a quarter of a second.
    /// With the Unity source the better fix is on the ragdoll itself: a rigidbody and a CharacterJoint
    /// on each hand with limits of about 60 degrees.
    /// </summary>
    public sealed class RagdollWrists : MonoBehaviour
    {
        const float BlendSeconds = 0.25f, WaitForRagdoll = 3f;

        sealed class Body
        {
            public Animator animator;
            public float diedAt, relaxStart = -1f;
            public Transform[] hands;
            public Quaternion[] from, to;
            public bool done;
        }

        readonly Dictionary<Health, Body> bodies = new Dictionary<Health, Body>();
        float nextScan;

        /// <summary>For tests: how many hands were relaxed.</summary>
        public static int Relaxed { get; private set; }

        void Update()
        {
            if (Time.time < nextScan) return;
            nextScan = Time.time + 0.2f;
            foreach (Health h in FindObjectsByType<Health>(FindObjectsSortMode.None))
            {
                if (h == null || !h.IsDead || bodies.ContainsKey(h)) continue;
                Animator a = h.GetComponentInChildren<Animator>();
                bodies[h] = new Body { animator = a, diedAt = Time.time, done = a == null || !a.isHuman };
            }
            // Forget bodies that were removed or came back to life (a respawn reuses the player).
            List<Health> gone = null;
            foreach (KeyValuePair<Health, Body> kv in bodies)
                if (kv.Key == null || !kv.Key.IsDead) { if (gone == null) gone = new List<Health>(); gone.Add(kv.Key); }
            if (gone != null) foreach (Health h in gone) bodies.Remove(h);
        }

        void LateUpdate()
        {
            foreach (Body b in bodies.Values)
            {
                if (b.done) continue;
                if (b.animator == null) { b.done = true; continue; }
                if (b.relaxStart < 0f)
                {
                    if (b.animator.enabled && Time.time - b.diedAt < WaitForRagdoll) continue;   // still animated
                    if (b.animator.enabled) { b.done = true; continue; }   // no ragdoll on this one
                    Relax(b);
                    if (b.done) continue;
                }
                float k = Mathf.Clamp01((Time.time - b.relaxStart) / BlendSeconds);
                k = k * k * (3f - 2f * k);
                for (int i = 0; i < b.hands.Length; i++)
                    if (b.hands[i] != null) b.hands[i].localRotation = Quaternion.Slerp(b.from[i], b.to[i], k);
                if (k >= 1f) { b.done = true; Relaxed += b.hands.Length; }
            }
        }

        static void Relax(Body b)
        {
            List<Transform> hands = new List<Transform>();
            List<Quaternion> to = new List<Quaternion>();
            foreach (HumanBodyBones bone in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
            {
                Transform t = b.animator.GetBoneTransform(bone);
                Quaternion rest;
                if (t == null || t.GetComponent<Rigidbody>() != null || !BindLocal(b.animator, t, out rest)) continue;   // a hand with its own physics is left alone
                hands.Add(t);
                to.Add(rest);
            }
            if (hands.Count == 0) { b.done = true; return; }
            b.hands = hands.ToArray();
            b.to = to.ToArray();
            b.from = new Quaternion[b.hands.Length];
            for (int i = 0; i < b.hands.Length; i++) b.from[i] = b.hands[i].localRotation;
            b.relaxStart = Time.time;
        }

        /// <summary>
        /// The bone's rotation against its parent in the model's bind pose, from the skinned mesh that
        /// uses both bones. False if no skinned mesh has them.
        /// </summary>
        public static bool BindLocal(Animator a, Transform bone, out Quaternion local)
        {
            local = Quaternion.identity;
            Transform parent = bone.parent;
            if (parent == null) return false;
            foreach (SkinnedMeshRenderer smr in a.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Transform[] bones = smr.bones;
                Matrix4x4[] bind = smr.sharedMesh != null ? smr.sharedMesh.bindposes : null;
                if (bones == null || bind == null || bind.Length != bones.Length) continue;
                int c = System.Array.IndexOf(bones, bone), p = System.Array.IndexOf(bones, parent);
                if (c < 0 || p < 0) continue;
                // bindpose = bone world-to-local at bind time, so parent-from-child = bind[p] * inverse(bind[c]).
                Matrix4x4 m = bind[p] * bind[c].inverse;
                local = Quaternion.LookRotation(m.GetColumn(2), m.GetColumn(1));
                return true;
            }
            return false;
        }
    }
}
