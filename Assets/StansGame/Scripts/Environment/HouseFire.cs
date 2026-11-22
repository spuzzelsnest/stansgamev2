using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// The fire in Madame Pommier's house on the square (it broke out around 23:00 on 5 June): roaring flames out of the
    /// house, tongues of fire licking up over the roof, sparks and embers whirling up, a thick column of smoke drifting
    /// away on the wind, and flickering firelight on the square and the paratroopers coming down into it.
    /// The materials are assets made by the level builder (Textures/Generated/Fire*.mat), so they are in the build.
    /// </summary>
    public class HouseFire : MonoBehaviour
    {
        public float intensity = 9f, range = 55f;
        public float dangerRadius = 4f;            // anyone who comes down in it
        [Tooltip("The burning house's footprint (x, z) and height, metres: where the flames come from.")]
        public Vector3 size = new Vector3(10f, 7f, 9f);
        public Material flameMat, smokeMat, emberMat;
        Light glow, glowLow;

        void Start()
        {
            glow = MakeLight("Firelight", new Vector3(0f, 6f, 0f), range, new Color(1f, 0.55f, 0.2f), true);
            glowLow = MakeLight("Firelight (low)", new Vector3(0f, 1f, size.z * 0.6f), range * 0.4f, new Color(1f, 0.42f, 0.12f), false);
            float roof = size.y - 3f;   // the fire object sits 3 m up the house
            // the body of the fire: through the windows and the broken roof
            Particles("Flames", flameMat, true, new Vector3(0f, roof * 0.4f, 0f), new Vector3(size.x * 0.8f, roof, size.z * 0.8f),
                      rate: 110f, life: (0.7f, 1.5f), sizes: (1.6f, 3.4f), speed: (1.5f, 3.5f), stretch: 0f,
                      colours: new[] { new Color(1f, 0.95f, 0.75f), new Color(1f, 0.62f, 0.15f), new Color(0.85f, 0.18f, 0.03f) }, alpha: 0.9f, noise: 0.8f);
            // tongues of flame licking up over the roof
            Particles("Flame tongues", flameMat, true, new Vector3(0f, roof + 0.5f, 0f), new Vector3(size.x * 0.7f, 0.5f, size.z * 0.6f),
                      rate: 45f, life: (0.6f, 1.1f), sizes: (1.2f, 2.4f), speed: (4f, 7f), stretch: 0.25f,
                      colours: new[] { new Color(1f, 0.9f, 0.6f), new Color(1f, 0.5f, 0.1f), new Color(0.6f, 0.1f, 0.02f) }, alpha: 0.85f, noise: 1.2f);
            // sparks and embers whirling up
            var embers = Particles("Embers", emberMat, true, new Vector3(0f, roof, 0f), new Vector3(size.x * 0.8f, 1f, size.z * 0.8f),
                      rate: 30f, life: (2f, 4.5f), sizes: (0.06f, 0.16f), speed: (3f, 7f), stretch: 0.15f,
                      colours: new[] { new Color(1f, 0.85f, 0.4f), new Color(1f, 0.45f, 0.08f), new Color(0.7f, 0.12f, 0.02f) }, alpha: 1f, noise: 2.5f);
            var em = embers.main; em.gravityModifier = -0.05f;
            // the smoke column, drifting away on the wind
            var smoke = Particles("Smoke", smokeMat, false, new Vector3(0f, roof + 2f, 0f), new Vector3(size.x * 0.6f, 1f, size.z * 0.5f),
                      rate: 16f, life: (8f, 13f), sizes: (4f, 7f), speed: (1.8f, 3f), stretch: 0f,
                      colours: new[] { new Color(0.10f, 0.09f, 0.08f), new Color(0.18f, 0.17f, 0.16f), new Color(0.32f, 0.31f, 0.30f) }, alpha: 0.75f, noise: 0.3f);
            var sz = smoke.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.6f));
            var drift = smoke.velocityOverLifetime; drift.enabled = true; drift.space = ParticleSystemSimulationSpace.World;
            drift.x = new ParticleSystem.MinMaxCurve(1.2f); drift.y = new ParticleSystem.MinMaxCurve(0f); drift.z = new ParticleSystem.MinMaxCurve(0.4f);
            var src = gameObject.AddComponent<AudioSource>();
            var roar = Sfx.Recorded("house_fire");   // a real house fire (an 11 s loop); the old low wind if it is missing
            src.clip = roar ? roar : Sfx.Wind; src.loop = true; src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 6f; src.maxDistance = 70f; src.volume = roar ? 0.9f : 0.5f; src.pitch = roar ? 1f : 0.5f;
            src.time = Random.Range(0f, 10f); src.Play();
            nextCollapse = Time.time + Random.Range(40f, 90f);
        }

        Light MakeLight(string name, Vector3 at, float r, Color c, bool shadows)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.transform.SetParent(transform, false); l.transform.localPosition = at;
            l.type = LightType.Point; l.range = r; l.color = c; l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            return l;
        }

        ParticleSystem Particles(string name, Material mat, bool additive, Vector3 at, Vector3 box, float rate, (float a, float b) life, (float a, float b) sizes,
                                 (float a, float b) speed, float stretch, Color[] colours, float alpha, float noise)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false); go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // emit upwards
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.a, life.b);
            main.startSize = new ParticleSystem.MinMaxCurve(sizes.a, sizes.b);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.a, speed.b);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 600; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var e = ps.emission; e.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(box.x, box.z, box.y);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(colours[0], 0f), new GradientColorKey(colours[1], 0.35f), new GradientColorKey(colours[2], 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(alpha, 0.12f), new GradientAlphaKey(alpha * 0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            if (!additive) { }
            else { var s = ps.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.25f))); }
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
            if (noise > 0f) { var nz = ps.noise; nz.enabled = true; nz.strength = noise; nz.frequency = 0.6f; nz.scrollSpeed = 0.8f; nz.octaveCount = 2; }
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (!mat) mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default"));
            r.sharedMaterial = mat;
            if (stretch > 0f) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = stretch; r.lengthScale = 1.2f; }
            r.sortingFudge = additive ? -10f : 10f;   // the flames draw over the smoke
            return ps;
        }

        float nextCollapse;

        void Update()
        {
            // now and then something inside gives way: a beam, part of a wall
            if (Time.time > nextCollapse)
            {
                nextCollapse = Time.time + Random.Range(80f, 160f);
                var crash = Sfx.Recorded("wall_collapse");
                if (crash) Sfx.At(crash, transform.position, 0.8f, Random.Range(0.92f, 1.05f), 150f);
            }
            float f = 0.7f + 0.3f * Mathf.PerlinNoise(Time.time * 6f, 0.3f);
            if (glow) glow.intensity = intensity * f;
            if (glowLow) glowLow.intensity = intensity * 0.5f * (0.6f + 0.4f * Mathf.PerlinNoise(Time.time * 9f, 4.1f));
            // anyone who lands in the blaze does not get out
            for (int i = Health.All.Count - 1; i >= 0; i--)
            {
                if (i >= Health.All.Count) continue;   // a death can take others off the list
                var h = Health.All[i];
                if (!h || h.IsDead) continue;
                Vector3 d = h.transform.position - transform.position; d.y = 0f;
                if (d.sqrMagnitude < dangerRadius * dangerRadius && h.transform.position.y < transform.position.y + 9f)
                    h.TakeDamage(new DamageInfo { amount = 60f * Time.deltaTime, point = h.transform.position, direction = Vector3.up, source = gameObject });
            }
        }
    }
}
