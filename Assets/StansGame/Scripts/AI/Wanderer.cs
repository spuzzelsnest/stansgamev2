using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// A character strolling about at peace (the Proving Ground): walks to a random spot in its area at a walking pace,
    /// stands a while, picks another. Keeps out of the keep-out boxes (the gun table, the firing lane, the parked
    /// vehicles), steps round other walkers, never fights. Civilians let their arms hang (IK); soldiers keep their
    /// rifles as they carry them. A name tag floats over each, so the line-up can be checked by name.
    /// </summary>
    public class Wanderer : MonoBehaviour
    {
        public Bounds area = new Bounds(Vector3.zero, new Vector3(80f, 10f, 80f));
        public Bounds[] keepOut = new Bounds[0];
        public Vector2 pause = new Vector2(2f, 7f);
        public float speed = 1.3f;
        [Tooltip("Civilians: hands down by the sides (needs the IK pass on the animator layer)")] public bool armsDown;
        public TextMesh label;

        public static readonly System.Collections.Generic.List<Wanderer> All = new System.Collections.Generic.List<Wanderer>();
        Animator anim; CharacterController cc; Health health;
        Vector3 goal; float waitUntil; bool walking; Vector3 vel;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Start()
        {
            anim = GetComponentInChildren<Animator>();
            cc = GetComponent<CharacterController>();
            health = GetComponent<Health>();
            if (health) health.Died += (h, d) => { if (label) label.gameObject.SetActive(false); enabled = false; };
            speed *= Random.Range(0.85f, 1.15f);
            waitUntil = Time.time + Random.Range(0f, pause.y);
            // a soldier's animator expects his scripts to say he is on his feet: without SoldierController/SoldierAI
            // "Grounded" stays false and he hangs in the falling pose, both legs together
            if (anim)
                foreach (var p in anim.parameters)
                    if (p.type == AnimatorControllerParameterType.Bool)
                        anim.SetBool(p.nameHash, p.name == "Grounded");
        }

        bool Blocked(Vector3 p)
        {
            foreach (var b in keepOut) { var e = b; e.Expand(1.2f); if (e.Contains(new Vector3(p.x, e.center.y, p.z))) return true; }
            return !area.Contains(new Vector3(p.x, area.center.y, p.z));
        }

        bool PathClear(Vector3 a, Vector3 b)
        {
            float d = Vector3.Distance(a, b);
            for (float s = 1f; s < d; s += 1f) if (Blocked(Vector3.Lerp(a, b, s / d))) return false;
            return true;
        }

        void PickGoal()
        {
            for (int tries = 0; tries < 20; tries++)
            {
                var p = new Vector3(Random.Range(area.min.x, area.max.x), transform.position.y, Random.Range(area.min.z, area.max.z));
                if ((p - transform.position).sqrMagnitude < 25f || Blocked(p) || !PathClear(transform.position, p)) continue;
                goal = p; walking = true; return;
            }
            waitUntil = Time.time + 1f;
        }

        void Update()
        {
            Vector3 want = Vector3.zero;
            if (!walking && Time.time >= waitUntil) PickGoal();
            if (walking)
            {
                Vector3 to = goal - transform.position; to.y = 0f;
                if (to.magnitude < 0.6f) { walking = false; waitUntil = Time.time + Random.Range(pause.x, pause.y); }
                else
                {
                    want = to.normalized * speed;
                    // step round the other walkers
                    foreach (var o in All)
                    {
                        if (o == this) continue;
                        Vector3 away = transform.position - o.transform.position; away.y = 0f;
                        float d = away.magnitude;
                        if (d < 1.6f && d > 0.01f) want += away / d * (1.6f - d) * 1.5f;
                    }
                    if (Blocked(transform.position + want.normalized * 0.8f)) { walking = false; waitUntil = Time.time + 0.5f; want = Vector3.zero; }
                }
            }
            vel = Vector3.MoveTowards(vel, want, Time.deltaTime * 3f);
            if (vel.sqrMagnitude > 0.01f)
            {
                var look = Quaternion.LookRotation(new Vector3(vel.x, 0f, vel.z));
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, 220f * Time.deltaTime);
            }
            Vector3 step = vel * Time.deltaTime;
            if (cc && cc.enabled) cc.Move(step + Vector3.down * 2f * Time.deltaTime);
            else
            {
                transform.position += step;
                if (Physics.Raycast(transform.position + Vector3.up * 1.5f, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(transform))
                    transform.position = new Vector3(transform.position.x, hit.point.y, transform.position.z);
            }
            if (anim) Locomotion.Set(anim, transform.InverseTransformDirection(vel), 0.15f, true);
        }

        void LateUpdate()
        {
            if (!label) return;
            var cam = Camera.main;
            if (!cam) return;
            bool near = (cam.transform.position - label.transform.position).sqrMagnitude < 30f * 30f;
            label.gameObject.SetActive(near);
            if (near) label.transform.rotation = Quaternion.LookRotation(label.transform.position - cam.transform.position);
        }

        void OnAnimatorIK(int layer)
        {
            if (!armsDown || !anim) return;
            foreach (var (goalIk, side) in new[] { (AvatarIKGoal.LeftHand, -1f), (AvatarIKGoal.RightHand, 1f) })
            {
                var hip = anim.GetBoneTransform(side < 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                if (!hip) continue;
                Vector3 p = hip.position + transform.right * side * 0.12f - transform.up * 0.12f;
                anim.SetIKPositionWeight(goalIk, 0.85f);
                anim.SetIKPosition(goalIk, p);
            }
        }
    }
}
