using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// A civilian carrying something in his right hand (a bucket of the bucket chain): both arms hang down by his
    /// sides (humanoid IK, so the soldiers' idle with its rifle-holding arms doesn't show), the right hand a little out
    /// from the body with the prop hanging from it. On the Animator's object.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class HeldProp : MonoBehaviour
    {
        public Transform prop;
        public float hang = 0.2f;      // from the hand down to the prop's centre (a bucket hangs by its handle)

        Animator anim;

        void Awake() => anim = GetComponent<Animator>();

        void OnAnimatorIK(int layer)
        {
            if (!anim || !anim.isHuman) return;
            var t = transform; Vector3 fwd = t.forward, right = t.right, up = Vector3.up;
            foreach (var (goal, hint, bone, side) in new[]
            {
                (AvatarIKGoal.RightHand, AvatarIKHint.RightElbow, HumanBodyBones.RightUpperArm, 1f),
                (AvatarIKGoal.LeftHand, AvatarIKHint.LeftElbow, HumanBodyBones.LeftUpperArm, -1f),
            })
            {
                var sh = anim.GetBoneTransform(bone);
                if (!sh) continue;
                // the arm straight down, the hand by the thigh (a touch further out on the side with the load)
                Vector3 p = sh.position - up * 0.62f + right * side * (side > 0f ? 0.1f : 0.04f) + fwd * 0.03f;
                anim.SetIKPositionWeight(goal, 1f); anim.SetIKPosition(goal, p);
                anim.SetIKRotationWeight(goal, 1f); anim.SetIKRotation(goal, Quaternion.LookRotation(-up, -right * side));
                anim.SetIKHintPositionWeight(hint, 1f); anim.SetIKHintPosition(hint, sh.position - up * 0.3f - fwd * 0.2f + right * side * 0.1f);
            }
        }

        void LateUpdate()
        {
            if (!prop || !anim) return;
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (!hand) return;
            prop.position = hand.position - Vector3.up * hang;
            prop.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        }
    }
}
