using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Keeps a humanoid's feet on the ground. The animations come from another character (the 2012 Hero set),
    /// so after retargeting the feet can hover above or sink into the ground; this moves the model up or down,
    /// smoothly, so the lower foot rests on whatever is under the soldier.
    /// </summary>
    [DefaultExecutionOrder(900)]
    public class FootGrounder : MonoBehaviour
    {
        public Animator animator;
        public float sole = 0.03f;          // the boot sole below the foot bone's lowest point
        const float maxOffset = 2f;   // not serialized: older scenes stored 0.4

        Transform lFoot, rFoot, lToe, rToe, root;
        CharacterController cc;
        float groundedFor;
        Vector3 basePos;
        float offset, started;
        bool reported;

        void Start()
        {
            if (!animator) animator = GetComponent<Animator>();
            if (!animator || !animator.isHuman) { enabled = false; return; }
            lFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            rFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            lToe = animator.GetBoneTransform(HumanBodyBones.LeftToes);
            rToe = animator.GetBoneTransform(HumanBodyBones.RightToes);
            root = transform.parent;
            cc = root ? root.GetComponent<CharacterController>() : null;
            basePos = transform.localPosition;
            started = Time.time;
            if (!lFoot || !rFoot || !root) enabled = false;
            if (!lToe || !rToe) sole = 0.08f;   // measuring from the ankle
        }

        void LateUpdate()
        {
            if (!animator.enabled) return;   // ragdoll
            // only when standing on something (not riding in the glider, jumping or falling)
            if (cc && (!cc.enabled || !cc.isGrounded))
            {
                groundedFor = 0f;
                return;   // keep the last correction while in the air
            }
            groundedFor += Time.deltaTime;
            float lowest = Mathf.Min(lFoot.position.y, rFoot.position.y);
            if (lToe) lowest = Mathf.Min(lowest, lToe.position.y);
            if (rToe) lowest = Mathf.Min(lowest, rToe.position.y);
            float ground = GroundBelow();
            float error = (lowest - sole) - ground;   // > 0: floating, < 0: sinking
            // first contact: snap; afterwards follow smoothly (steps, slopes)
            float k = groundedFor <= Time.deltaTime * 1.5f || Mathf.Abs(error) > 0.3f ? 1f : 1f - Mathf.Exp(-10f * Time.deltaTime);
            offset = Mathf.Clamp(offset - error * k, -maxOffset, maxOffset);
            transform.localPosition = basePos + Vector3.up * offset;
            if (!reported && groundedFor > 3f && root.GetComponent<SoldierController>())
            {
                reported = true;
                Debug.Log($"Stan's Game: {root.name} feet grounded, model moved {offset * 100f:F0} cm (foot {lowest:F2}, ground {ground:F2})");
            }
        }

        float GroundBelow()
        {
            Vector3 from = root.position + Vector3.up * 0.6f;
            float best = root.position.y;
            bool found = false;
            foreach (var h in Physics.RaycastAll(from, Vector3.down, 1.6f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(root)) continue;
                if (!found || h.point.y > best) { best = h.point.y; found = true; }
            }
            return best;
        }
    }
}
