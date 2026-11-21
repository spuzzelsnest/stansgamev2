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
        }
    }
}
