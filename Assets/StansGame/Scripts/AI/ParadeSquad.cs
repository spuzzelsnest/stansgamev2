using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// A platoon section on parade, marching round a closed route in its own camp (Proving Ground): three files
    /// (a column of threes), the section leader out in front, weapons at sling arms (WeaponHolder.slingArms), keeping
    /// step at quick time - 120 paces a minute, about 1.5 m/s. At the ends of the route the column wheels round.
    /// The men are placed along the route behind the front rank, so the column bends round the corners as a real one does.
    /// </summary>
    public class ParadeSquad : MonoBehaviour
    {
        [Tooltip("The route, a closed loop (world points on the ground).")]
        public Vector3[] route = new Vector3[0];
        [Tooltip("In order: the leader first, then the ranks, three to a rank.")]
        public Transform[] men = new Transform[0];
        public int files = 3;
        public float fileGap = 0.85f, rankGap = 1.35f, leaderLead = 2.4f;
        public float speed = 1.5f;
        [Tooltip("Halt for a few seconds each time round (at the start of the route), as a column does when dismissed and fallen in again.")]
        public float haltEvery = 0f, haltFor = 6f;

        float[] cum; float total, s, haltUntil, nextHalt;
        Animator[] anims; Vector3[] vel;

        void Start()
        {
            int n = route.Length;
            if (n < 2 || men.Length == 0) { enabled = false; return; }
            cum = new float[n + 1];
            for (int i = 0; i < n; i++) cum[i + 1] = cum[i] + Vector3.Distance(route[i], route[(i + 1) % n]);
            total = cum[n];
            anims = new Animator[men.Length]; vel = new Vector3[men.Length];
            for (int i = 0; i < men.Length; i++)
            {
                if (!men[i]) continue;
                anims[i] = men[i].GetComponentInChildren<Animator>();
                foreach (var h in men[i].GetComponentsInChildren<WeaponHolder>(true)) h.slingArms = true;
                foreach (var ai in men[i].GetComponentsInChildren<SoldierAI>(true)) ai.enabled = false;
                if (men[i].TryGetComponent<CharacterController>(out var cc)) cc.enabled = false;   // no shoving in the ranks
            }
            s = leaderLead + Mathf.CeilToInt((men.Length - 1f) / files) * rankGap;   // the whole column already on the route
            nextHalt = haltEvery > 0f ? Time.time + haltEvery : float.MaxValue;
        }

        Vector3 At(float d, out Vector3 dir)
        {
            d = Mathf.Repeat(d, total);
            int n = route.Length, i = 0;
            while (i < n - 1 && cum[i + 1] < d) i++;
            Vector3 a = route[i], b = route[(i + 1) % n];
            float t = Mathf.InverseLerp(cum[i], cum[i + 1], d);
            dir = (b - a); dir.y = 0f; dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            return Vector3.Lerp(a, b, t);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool halted = Time.time < haltUntil;
            if (!halted && Time.time >= nextHalt) { haltUntil = Time.time + haltFor; nextHalt = Time.time + haltFor + haltEvery; halted = true; }
            if (!halted) s += speed * dt;
            for (int i = 0; i < men.Length; i++)
            {
                var m = men[i];
                if (!m) continue;
                var hp = m.GetComponent<Health>();
                if (hp && hp.IsDead) continue;
                float d; float side;
                if (i == 0) { d = s; side = -fileGap; }   // the leader, ahead and to the left of the column
                else
                {
                    int k = i - 1, rank = k / files, file = k % files;
                    d = s - leaderLead - rank * rankGap;
                    side = (file - (files - 1) * 0.5f) * fileGap;
                }
                Vector3 p = At(d, out Vector3 dir);
                Vector3 right = Vector3.Cross(Vector3.up, dir);
                Vector3 target = p + right * side;
                Vector3 to = target - m.position; to.y = 0f;
                // keep station: the pace of the column, a little faster or slower to close up
                Vector3 want = halted ? to * 2f : dir * speed + to * 1.5f;
                want = Vector3.ClampMagnitude(want, speed * 1.4f);
                vel[i] = Vector3.MoveTowards(vel[i], want, dt * 4f);
                Vector3 pos = m.position + vel[i] * dt;
                if (Physics.Raycast(pos + Vector3.up * 1.5f, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(m))
                    pos.y = hit.point.y;
                m.position = pos;
                Vector3 face = halted ? dir : (vel[i].sqrMagnitude > 0.04f ? vel[i] : dir);
                face.y = 0f;
                if (face.sqrMagnitude > 1e-4f) m.rotation = Quaternion.RotateTowards(m.rotation, Quaternion.LookRotation(face), 160f * dt);
                if (anims[i]) Locomotion.Set(anims[i], m.InverseTransformDirection(vel[i]), 0.12f, true);
            }
        }
    }
}
