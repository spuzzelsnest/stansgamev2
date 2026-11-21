using System.Collections.Generic;
using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// A smoke screen from a smoke grenade (British No. 77, German Nebelhandgranate 39, American AN-M8): it billows out
    /// to a cloud ~7 m across in a few seconds, drifts on the night breeze, hangs for about a minute and thins away.
    /// Besides the particles it is a soft sphere that blocks sight: the AI can't see through it (SmokeCloud.Blocks),
    /// and a camera inside it is veiled (SmokeVeil).
    /// </summary>
    public class SmokeCloud : MonoBehaviour
    {
        public static readonly List<SmokeCloud> All = new List<SmokeCloud>();
        /// <summary>A smoke grenade has just gone off here (thrown by owner): the AI reacts to it.</summary>
        public static event System.Action<SmokeCloud> Popped;
        /// <summary>The light breeze of the night of 5/6 June (from the south-west, as the gliders' wind).</summary>
        public static Vector3 Wind = new Vector3(0.28f, 0f, 0.38f);

        public float radius = 7f, duration = 60f, buildUp = 5f;
        public Color colour = new Color(0.78f, 0.78f, 0.76f, 0.6f);
        public Material material;
        public GameObject owner;

        public Vector3 Centre { get; private set; }
        public float Radius { get; private set; }
        /// <summary>0..1: how thick it is (building up, then thinning at the end).</summary>
        public float Density { get; private set; }
        public float Age => Time.time - born;

        float born; Vector3 origin;
        ParticleSystem ps;
        AudioSource hiss;

        /// <summary>Set off a smoke cloud at a point.</summary>
        public static SmokeCloud Spawn(Vector3 at, Material mat, Color colour, float radius, float duration, GameObject owner)
        {
            var go = new GameObject("Smoke screen");
            go.transform.position = at;
            var s = go.AddComponent<SmokeCloud>();
            s.material = mat; s.colour = colour; s.radius = radius; s.duration = duration; s.owner = owner;
            return s;
        }

        void Start()
        {
            born = Time.time; origin = transform.position; Centre = origin; Radius = 1f;
            All.Add(this);
            BuildParticles();
            hiss = gameObject.AddComponent<AudioSource>();
            hiss.clip = Sfx.SmokeHiss; hiss.loop = true; hiss.volume = 0f; hiss.spatialBlend = 1f;
            hiss.minDistance = 3f; hiss.maxDistance = 60f; hiss.rolloffMode = AudioRolloffMode.Logarithmic; hiss.Play();
            Sfx.SmokePop(origin);
            NoiseEvents.Report(origin, 40f, owner);
            if (!SmokeVeil.Instance) new GameObject("Smoke veil").AddComponent<SmokeVeil>();
            Popped?.Invoke(this);
        }

        void OnDestroy() => All.Remove(this);

        void BuildParticles()
        {
            ps = gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = Mathf.Max(1f, duration - 12f); main.loop = false; main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(12f, 18f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(2.5f, 5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = colour;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 500;
            main.gravityModifier = -0.004f;
            var em = ps.emission;
            em.rateOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 14f, 1f, 4f));
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 28), new ParticleSystem.Burst(0.6f, 18) });
            // a hemisphere pointing up, just off the ground (the default one points along the ground and smears the smoke flat)
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Hemisphere; shape.radius = 0.6f;
            shape.rotation = new Vector3(-90f, 0f, 0f); shape.position = new Vector3(0f, 0.6f, 0f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(Wind.x * 0.8f, Wind.x * 1.2f);
            vel.y = new ParticleSystem.MinMaxCurve(0.08f, 0.3f);
            vel.z = new ParticleSystem.MinMaxCurve(Wind.z * 0.8f, Wind.z * 1.2f);
            var limit = ps.limitVelocityOverLifetime; limit.enabled = true; limit.limit = 1.2f; limit.dampen = 0.15f;
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.25f, 1.4f), new Keyframe(1f, 2.6f)));
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.9f, 0.9f, 0.88f), 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.85f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 0.25f; noise.scrollSpeed = 0.1f;
            var r = GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.maxParticleSize = 3f;
            if (material) r.sharedMaterial = material;
            ps.Play();
        }

        void Update()
        {
            float age = Age;
            // the cloud's body: grows, drifts downwind and rises a little; thins over the last 15 s
            Centre = origin + Wind * age * 0.85f + Vector3.up * Mathf.Min(1.2f + age * 0.05f, 3f);
            Radius = Mathf.Lerp(1.5f, radius, Mathf.Clamp01(age / buildUp)) + age * 0.03f;
            float fadeIn = Mathf.Clamp01(age / (buildUp * 0.6f)), fadeOut = Mathf.Clamp01((duration - age) / 15f);
            Density = fadeIn * fadeOut;
            // the steady hiss comes in as the recorded burst dies away, and stops as the can burns out
            if (hiss) hiss.volume = 0.42f * Mathf.Clamp01((age - 1.5f) / 2.5f) * Mathf.Clamp01((duration - 15f - age) / 8f);
            if (age > duration + 20f) Destroy(gameObject);
        }

        // ------------------------------------------------------------------ seeing through smoke

        /// <summary>How thick the smoke is at a point (0 = clear air, 1 = in the thick of it).</summary>
        public static float DensityAt(Vector3 p)
        {
            float best = 0f;
            foreach (var s in All)
            {
                if (!s || s.Density <= 0f) continue;
                float d = (p - s.Centre).magnitude / s.Radius;
                if (d < 1f) best = Mathf.Max(best, s.Density * (1f - d * d));
            }
            return best;
        }

        /// <summary>
        /// Is the line of sight from a to b hidden by smoke? Adds up how many metres of thick smoke it passes
        /// through (each cloud a soft sphere); a few metres is enough to lose a man in it.
        /// </summary>
        public static bool Blocks(Vector3 a, Vector3 b, float metres = 3.5f)
        {
            if (All.Count == 0) return false;
            Vector3 d = b - a; float len = d.magnitude; if (len < 0.01f) return false; d /= len;
            float total = 0f;
            foreach (var s in All)
            {
                if (!s || s.Density <= 0.05f) continue;
                Vector3 m = s.Centre - a;
                float t = Vector3.Dot(m, d);
                float off2 = m.sqrMagnitude - t * t, r2 = s.Radius * s.Radius;
                if (off2 >= r2) continue;
                float half = Mathf.Sqrt(r2 - off2);
                float t0 = Mathf.Max(0f, t - half), t1 = Mathf.Min(len, t + half);
                if (t1 <= t0) continue;
                // thicker towards the middle of the cloud
                total += (t1 - t0) * s.Density * (1f - 0.5f * off2 / r2);
                if (total >= metres) return true;
            }
            return false;
        }

        /// <summary>The nearest cloud to a point, if any is within range.</summary>
        public static SmokeCloud Nearest(Vector3 p, float range)
        {
            SmokeCloud best = null; float bd = range;
            foreach (var s in All) { if (!s || s.Density < 0.2f) continue; float d = Vector3.Distance(p, s.Centre) - s.Radius; if (d < bd) { bd = d; best = s; } }
            return best;
        }
    }

    /// <summary>Greys out the screen when the camera is in the smoke (one per scene, made by the first cloud).</summary>
    public class SmokeVeil : MonoBehaviour
    {
        public static SmokeVeil Instance { get; private set; }
        float shown;
        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void OnGUI()
        {
            var cam = Camera.main;
            float want = cam ? SmokeCloud.DensityAt(cam.transform.position) : 0f;
            shown = Mathf.MoveTowards(shown, want, Time.deltaTime * 1.5f);
            if (shown < 0.02f) return;
            // grey-white smoke, dimmed by the night (it's lit by the moon and the flashes)
            GUI.color = new Color(0.42f, 0.43f, 0.44f, Mathf.Clamp01(shown * 0.8f));
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
