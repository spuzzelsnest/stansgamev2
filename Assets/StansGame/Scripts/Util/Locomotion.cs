using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Keeps the feet on the ground: the walk and run clips in the locomotion blend tree are placed at their own speeds
    /// (walk 2.2 m/s, run 4.2 m/s - CharacterImporter), which are faster than a man in full kit really moves. This picks
    /// the clip for the real speed (walking about 1.5 m/s, a loaded run about 3.4 m/s, a sprint about 5.6 m/s) and
    /// plays it slower or faster to match, so the steps cover exactly the ground the soldier covers - no sliding.
    /// </summary>
    public static class Locomotion
    {
        const float WalkClip = 2.2f, RunClip = 4.2f;
        /// <summary>The real speeds the walk and the run clips are shown at (m/s).</summary>
        public const float Walk = 1.5f, Run = 3.4f;

        public static void Set(Animator a, Vector3 localVelocity, float damp, bool upright)
        {
            if (!a) return;
            Vector2 flat = new Vector2(localVelocity.x, localVelocity.z);
            float v = flat.magnitude, p, rate;
            if (v <= Walk) { p = v / Walk * WalkClip; rate = Mathf.Lerp(1f, Walk / WalkClip, v / Walk); }
            else if (v <= Run) { float t = (v - Walk) / (Run - Walk); p = Mathf.Lerp(WalkClip, RunClip, t); rate = Mathf.Lerp(Walk / WalkClip, Run / RunClip, t); }
            else { p = RunClip; rate = v / RunClip; }
            Vector2 dir = v > 0.001f ? flat / v * p : Vector2.zero;
            float dt = Time.deltaTime;
            a.SetFloat("Speed", v, damp, dt);
            a.SetFloat("MoveX", dir.x, damp, dt);
            a.SetFloat("MoveZ", dir.y, damp, dt);
            // (kneeling and crawling have their own clips, played at their own pace)
            float want = upright ? Mathf.Clamp(rate, 0.6f, 1.5f) : 1f;
            a.speed = Mathf.MoveTowards(a.speed, want, dt * 3f);
            Stairs(a);
        }

        // ------------------------------------------------------------------ stairs

        static readonly System.Collections.Generic.Dictionary<Animator, StairsState> stairs = new System.Collections.Generic.Dictionary<Animator, StairsState>();
        class StairsState { public Vector3 last; public float grade, since; public int now; public bool has, param; public float t; public CharacterController cc; }
        static readonly int StairsId = Animator.StringToHash("Stairs");

        /// <summary>
        /// Going up or down stairs (or a bank as steep): sets the animator's "Stairs" to 1 or -1, so the stair clips play
        /// instead of the flat walk. Worked out from how the character really moves - more than about 1 in 2.5 up or
        /// down while walking - so it needs nothing in the level. Ordinary terrain slopes stay a normal walk.
        /// </summary>
        public static void Stairs(Animator a)
        {
            if (!stairs.TryGetValue(a, out var s))
            {
                s = new StairsState();
                foreach (var p in a.parameters)
                    if (p.nameHash == StairsId && p.type == AnimatorControllerParameterType.Int) s.param = true;
                s.cc = a.GetComponentInParent<CharacterController>();
                stairs[a] = s;
            }
            if (!s.param) return;
            Vector3 pos = a.transform.position; float now = Time.time;
            if (!s.has) { s.has = true; s.last = pos; s.t = now; return; }
            float dt = now - s.t;
            if (dt < 0.1f) return;   // measured over a tenth of a second, so one bump doesn't count
            Vector3 d = pos - s.last; s.last = pos; s.t = now;
            float flat = new Vector2(d.x, d.z).magnitude / dt, rise = d.y / dt;
            float grade = flat > 0.4f && flat < 6f ? rise / flat : 0f;
            if (s.cc && s.cc.enabled && !s.cc.isGrounded) grade = 0f;   // a jump or a fall is not a stair
            s.grade = Mathf.Lerp(s.grade, grade, 0.5f);
            int want = s.grade > 0.4f ? 1 : s.grade < -0.4f ? -1 : (Mathf.Abs(s.grade) < 0.2f ? 0 : s.now);
            if (want != s.now) { if (now - s.since > 0.2f) { s.now = want; s.since = now; a.SetInteger(StairsId, want); } }
            else s.since = now;
        }
    }
}
