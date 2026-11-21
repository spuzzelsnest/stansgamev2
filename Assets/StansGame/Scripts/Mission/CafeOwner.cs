using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Georges Gondrée, the café owner by Pegasus Bridge. He and his family spent the first hour of 6 June in the
    /// cellar; once the bridge was in British hands he came out and dug up the champagne he had hidden from the
    /// Germans. Here: hidden until the mission reaches <see cref="showFromPhase"/>, then standing at his café door,
    /// left hand on his hip and a bottle in his right, turning to look at the nearest soldier and raising the bottle
    /// to anyone who comes close. Cosmetic only: the same on every machine because the mission phase is shared.
    /// The hands are placed with humanoid IK (goal +Z = fingers, -Y = palm, as measured for HandIK).
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class CafeOwner : MonoBehaviour
    {
        public Transform bottle;                 // the champagne bottle (origin at its base, up = neck)
        public int showFromPhase = 2;            // 2 = "Ham and Jam": the bridge and the café are taken
        public float lookRange = 18f, toastRange = 7f, maxTurn = 70f;

        Animator anim;
        Renderer[] renderers;
        Collider[] colliders;
        float baseYaw, toast, toastTimer;
        bool shown = true;
        Quaternion rightGoal;

        void Awake()
        {
            anim = GetComponent<Animator>();
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            baseYaw = transform.eulerAngles.y;
            renderers = transform.root == transform ? GetComponentsInChildren<Renderer>(true) : transform.parent.GetComponentsInChildren<Renderer>(true);
            colliders = transform.parent ? transform.parent.GetComponentsInChildren<Collider>(true) : GetComponentsInChildren<Collider>(true);
        }

        void Show(bool on)
        {
            if (shown == on) return;
            shown = on;
            foreach (var r in renderers) if (r) r.enabled = on;
            foreach (var c in colliders) if (c) c.enabled = on;
        }

        Camera Viewer() => Camera.main;

        void Update()
        {
            var md = MissionDirector.Instance;
            Show(!md || md.CurrentPhase >= showFromPhase);
            if (!shown) return;

            // turn (the whole body, slowly) towards whoever is watching, within a comfortable arc from the door
            var cam = Viewer();
            float want = baseYaw, near = float.MaxValue;
            if (cam)
            {
                Vector3 d = cam.transform.position - transform.position; d.y = 0f;
                near = d.magnitude;
                if (near < lookRange && near > 0.3f)
                    want = baseYaw + Mathf.Clamp(Mathf.DeltaAngle(baseYaw, Quaternion.LookRotation(d).eulerAngles.y), -maxTurn, maxTurn);
            }
            float y = Mathf.MoveTowardsAngle(transform.eulerAngles.y, want, 60f * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0f, y, 0f);

            // "Vive les Anglais!": the bottle goes up when someone comes close, again every few seconds
            toastTimer -= Time.deltaTime;
            bool close = near < toastRange;
            if (close && toastTimer <= 0f) toastTimer = 5f;
            float up = close && toastTimer > 3.2f ? 1f : 0f;
            toast = Mathf.MoveTowards(toast, up, 2.5f * Time.deltaTime);
        }

        void OnAnimatorIK(int layer)
        {
            if (!shown || !anim.isHuman) return;
            var t = transform;
            Vector3 fwd = t.forward, right = t.right, upv = Vector3.up;
            var rs = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var ls = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var lh = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            if (!rs || !ls || !lh) return;

            // right hand: bottle held at the chest, raised to head height for a toast
            float k = Mathf.SmoothStep(0f, 1f, toast);
            Vector3 rp = rs.position + (fwd * 0.30f + right * 0.06f + upv * Mathf.Lerp(-0.22f, 0.22f, k));
            Vector3 fingers = (fwd - right * 0.35f + upv * Mathf.Lerp(-0.1f, 0.25f, k)).normalized;
            rightGoal = Quaternion.LookRotation(fingers, right);           // palm turned in: thumb up, bottle upright
            anim.SetIKPositionWeight(AvatarIKGoal.RightHand, 1f); anim.SetIKRotationWeight(AvatarIKGoal.RightHand, 1f);
            anim.SetIKPosition(AvatarIKGoal.RightHand, rp); anim.SetIKRotation(AvatarIKGoal.RightHand, rightGoal);
            anim.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 1f);
            anim.SetIKHintPosition(AvatarIKHint.RightElbow, rs.position + (right * 0.25f - upv * 0.30f - fwd * 0.10f));

            // left hand on the hip, elbow out
            Vector3 lp = lh.position + (-right * 0.10f + upv * 0.10f - fwd * 0.03f);
            Vector3 lf = (-upv + fwd * 0.4f + right * 0.2f).normalized;
            anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, 1f); anim.SetIKRotationWeight(AvatarIKGoal.LeftHand, 1f);
            anim.SetIKPosition(AvatarIKGoal.LeftHand, lp); anim.SetIKRotation(AvatarIKGoal.LeftHand, Quaternion.LookRotation(lf, -right));
            anim.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 1f);
            anim.SetIKHintPosition(AvatarIKHint.LeftElbow, ls.position + (-right * 0.40f - upv * 0.15f - fwd * 0.10f));

            // look at the viewer
            var cam = Viewer();
            if (cam && (cam.transform.position - t.position).sqrMagnitude < lookRange * lookRange)
            {
                anim.SetLookAtWeight(0.8f, 0.15f, 0.7f, 1f, 0.6f);
                anim.SetLookAtPosition(cam.transform.position);
            }
            else anim.SetLookAtWeight(0f);
        }

        void LateUpdate()
        {
            if (!shown || !bottle) return;
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (!hand) return;
            // the bottle sits in the palm (palm = -Y of the goal), a little along the fingers, held around its body
            Vector3 palm = rightGoal * Vector3.down, fingers = rightGoal * Vector3.forward, thumb = rightGoal * Vector3.left;
            Vector3 grip = hand.position + (fingers * 0.07f + palm * 0.045f);
            bottle.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(fingers, thumb), thumb);
            bottle.position = grip - thumb * 0.11f;
        }
    }
}
