using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Random = UnityEngine.Random;

namespace StansGame.Additions
{
    /// <summary>
    /// "Flares" setting: flares light the battlefield. Each burning flare is a bright point light (the
    /// first one burning casts soft shadows), so the field, the hedges and the soldiers throw moving
    /// shadows while it burns.
    /// <para>With the special effects in the build (their <c>StansGame.Additions.Flares</c> class, found by
    /// reflection so this file also compiles without it), the effects fire the Very lights and draw them,
    /// and this class only adds the light to each one through their <c>Flares.Launched</c> event.</para>
    /// <para>Without them, this class fires flares of its own: the German defenders send one up every
    /// 16 to 34 s during the fight. Most are parachute flares that burst high and drift down for about
    /// 20 seconds; some are plain Very lights that arc up and burn out on the way down. These also draw
    /// a glowing star (caught by the bloom) and a smoke trail.</para>
    /// No flares on the bootcamp's daylight training ground.
    /// Only visual: the AI does not see further by flarelight. In co-op every PC lights its own flares.
    /// The materials use Sprites/Default, which is in the build; the textures are generated.
    /// </summary>
    public sealed class FlareLights : MonoBehaviour
    {
        const float MinInterval = 16f, MaxInterval = 34f;
        const float MinRange = 40f, MaxRange = 260f;
        const int MaxBurning = 2;

        static Material starMaterial, smokeMaterial;
        static Texture2D starTexture, smokeTexture;
        static Mesh quad;
        static readonly List<Flare> live = new List<Flare>();
        static bool hooked;

        float nextFlare;

        /// <summary>Flares fired by this class since the game started (for tests).</summary>
        public static int Launched { get; private set; }
        /// <summary>Flares this class is lighting now (its own, or the effects' Very lights).</summary>
        public static int Burning { get { live.RemoveAll(f => f == null); return live.Count; } }
        /// <summary>True when the effects' Flares class fires the flares and this class only lights them.</summary>
        public static bool LightingEffects { get; private set; }

        void Awake()
        {
            if (hooked) return;
            hooked = true;
            Type fx = typeof(FlareLights).Assembly.GetType("StansGame.Additions.Flares");
            EventInfo e = fx != null ? fx.GetEvent("Launched", BindingFlags.Public | BindingFlags.Static) : null;
            if (e == null || e.EventHandlerType != typeof(Action<Transform>)) return;
            e.AddEventHandler(null, new Action<Transform>(OnEffectsFlare));
            LightingEffects = true;
            Debug.Log("Stan's Game: flare lights attached to the effects' Very lights");
        }

        static void OnEffectsFlare(Transform t)
        {
            if (t == null || !GameSettings.Flares || Atmosphere.BootcampActive || Burning >= MaxBurning) return;
            Flare f = t.gameObject.AddComponent<Flare>();
            f.LightOnly();
            live.Add(f);
        }

        void Update()
        {
            if (LightingEffects)
            {
                if (!GameSettings.Flares) Clear();
                return;
            }
            MissionDirector md = MissionDirector.Instance;
            SoldierController p = AdditionsBootstrap.LocalPlayer();
            if (!GameSettings.Flares || md == null || p == null || !p.enabled || CinematicDirector.Playing || Net.InMenu || Atmosphere.BootcampActive)
            {
                nextFlare = Mathf.Max(nextFlare, Time.time + 12f);
                if (!GameSettings.Flares || Atmosphere.BootcampActive) Clear();
                return;
            }
            if (Time.time < nextFlare) return;
            nextFlare = Time.time + Random.Range(MinInterval, MaxInterval);
            if (Burning >= MaxBurning) return;

            Health me = p.GetComponent<Health>();
            List<Health> shooters = new List<Health>();
            foreach (Health h in Health.All)
            {
                if (h == null || h.IsDead || me == null || h.faction == me.faction || h.GetComponent<SoldierAI>() == null) continue;
                float d = Vector3.Distance(h.transform.position, p.transform.position);
                if (d >= MinRange && d <= MaxRange) shooters.Add(h);
            }
            if (shooters.Count == 0) return;
            Vector3 from = shooters[Random.Range(0, shooters.Count)].transform.position + Vector3.up * 1.6f;
            Vector3 toward = p.transform.position - from; toward.y = 0f;
            // Over the ground between them, nearer the shooter's side.
            Vector3 v = toward.normalized * Mathf.Clamp(toward.magnitude * 0.12f, 3f, 14f) + Random.insideUnitSphere * 2.5f;
            v.y = Random.Range(30f, 38f);
            Launch(from, v, Random.value < 0.65f);
        }

        /// <summary>Fires a flare from <paramref name="from"/> with the given launch velocity.</summary>
        public static void Launch(Vector3 from, Vector3 velocity, bool parachute)
        {
            if (!Ready()) return;
            GameObject go = new GameObject(parachute ? "Parachute flare (Stan's Game additions)" : "Very light (Stan's Game additions)");
            go.transform.position = from;
            Flare f = go.AddComponent<Flare>();
            f.Init(velocity, parachute, false);
            live.Add(f);
            Launched++;
        }

        /// <summary>A parachute flare already burning at <paramref name="position"/> (used by the screenshot probe).</summary>
        public static void Spawn(Vector3 position)
        {
            if (!Ready()) return;
            GameObject go = new GameObject("Parachute flare (Stan's Game additions)");
            go.transform.position = position;
            Flare f = go.AddComponent<Flare>();
            f.Init(Vector3.zero, true, true);
            live.Add(f);
            Launched++;
        }

        static void Clear()
        {
            foreach (Flare f in live)
            {
                if (f == null) continue;
                if (f.lightOnly) { f.light.enabled = false; Destroy(f); }   // the effects' flare itself stays (it is destroyed after 7 s)
                else Destroy(f.gameObject);
            }
            live.Clear();
        }

        static bool Ready()
        {
            if (starMaterial != null) return true;
            Shader s = Shader.Find("Sprites/Default");
            if (s == null) return false;
            const int n = 64;
            starTexture = new Texture2D(n, n, TextureFormat.RGBA32, true, false);
            smokeTexture = new Texture2D(n, n, TextureFormat.RGBA32, true, false);
            starTexture.wrapMode = smokeTexture.wrapMode = TextureWrapMode.Clamp;
            Color32[] a = new Color32[n * n], b = new Color32[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float u = (i + 0.5f) / n * 2f - 1f, v = (j + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float core = Mathf.Clamp01((0.16f - r) * 30f);
                    float glow = Mathf.Pow(Mathf.Clamp01(1f - r), 4f) * 0.55f;
                    a[j * n + i] = new Color(1f, 1f, 1f, Mathf.Clamp01(core + glow));
                    float puff = Mathf.Pow(Mathf.Clamp01(1f - r), 1.6f) * (0.75f + 0.25f * Mathf.PerlinNoise(u * 3f + 7f, v * 3f + 3f));
                    b[j * n + i] = new Color(1f, 1f, 1f, puff);
                }
            starTexture.SetPixels32(a); starTexture.Apply(true, true);
            smokeTexture.SetPixels32(b); smokeTexture.Apply(true, true);

            starMaterial = new Material(s);
            starMaterial.name = "Flare star (Stan's Game additions)";
            starMaterial.mainTexture = starTexture;
            starMaterial.color = new Color(7f, 6.6f, 5.8f, 1f);   // well over 1 for the bloom
            starMaterial.renderQueue = 3100;
            smokeMaterial = new Material(s);
            smokeMaterial.name = "Flare smoke (Stan's Game additions)";
            smokeMaterial.mainTexture = smokeTexture;
            smokeMaterial.renderQueue = 3050;

            quad = new Mesh();
            quad.name = "Flare quad";
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            quad.RecalculateBounds();
            return true;
        }

        sealed class Flare : MonoBehaviour
        {
            const float Brightness = 1500f;     // point light intensity; lights the ground about as much as the moon at 50 m
            const float EffectsBurnTime = 7f;   // the effects' VeryLight.BurnTime
            Vector3 velocity;
            bool parachute, burstDone;
            float born, burnFrom, burnFor, seed;
            internal Light light;
            internal bool lightOnly;
            Transform star;
            TrailRenderer trail;

            public void Init(Vector3 v, bool chute, bool alreadyBurning)
            {
                velocity = v;
                parachute = chute;
                born = Time.time;
                seed = Random.value * 100f;
                burnFor = chute ? Random.Range(18f, 24f) : Random.Range(6f, 8f);
                burnFrom = alreadyBurning ? Time.time - 1f : (chute ? float.MaxValue : Time.time + 0.4f);
                burstDone = alreadyBurning;
                AddLight(140f);

                GameObject s = new GameObject("Star");
                s.transform.SetParent(transform, false);
                s.AddComponent<MeshFilter>().sharedMesh = quad;
                MeshRenderer r = s.AddComponent<MeshRenderer>();
                r.sharedMaterial = starMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                star = s.transform;
                star.localScale = Vector3.zero;

                trail = gameObject.AddComponent<TrailRenderer>();
                trail.sharedMaterial = smokeMaterial;
                trail.time = chute ? 9f : 5f;
                trail.minVertexDistance = 0.6f;
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(1f, 2.6f));
                Gradient g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(new Color(0.85f, 0.83f, 0.80f), 0f), new GradientColorKey(new Color(0.30f, 0.31f, 0.33f), 0.25f), new GradientColorKey(new Color(0.16f, 0.17f, 0.19f), 1f) },
                          new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0.30f, 0.3f), new GradientAlphaKey(0f, 1f) });
                trail.colorGradient = g;
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.textureMode = LineTextureMode.Stretch;
            }

            /// <summary>Lights one of the effects' Very lights, which moves itself and is destroyed after 7 s.</summary>
            public void LightOnly()
            {
                lightOnly = true;
                born = Time.time;
                seed = Random.value * 100f;
                burnFor = EffectsBurnTime;
                AddLight(110f);   // a Very light is smaller than a parachute flare
            }

            void AddLight(float range)
            {
                light = gameObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.94f, 0.84f);
                light.range = range;
                light.intensity = 0f;
                // Shadows from one flare at a time: each shadowed point light renders the scene six more times.
                light.shadows = Shadowed() ? LightShadows.None : LightShadows.Soft;
                light.shadowStrength = 0.85f;
                light.shadowBias = 0.06f;
                light.shadowNormalBias = 0.6f;
                light.renderMode = LightRenderMode.ForcePixel;
                UniversalAdditionalLightData ld = gameObject.GetComponent<UniversalAdditionalLightData>();
                if (ld == null) ld = gameObject.AddComponent<UniversalAdditionalLightData>();
                ld.additionalLightsShadowResolutionTier = 1;   // 6 cube faces at 512 fit the 2048 shadow atlas, two flares too
            }

            bool Shadowed()
            {
                foreach (Flare f in live) if (f != null && f != this && f.light != null && f.light.shadows != LightShadows.None) return true;
                return false;
            }

            void Update()
            {
                if (lightOnly) { Glow(); return; }
                float dt = Time.deltaTime;
                Vector3 p = transform.position;
                if (!burstDone && parachute)
                {
                    // Rising on the charge; bursts at the top.
                    velocity += Physics.gravity * dt;
                    if (velocity.y <= 2f) { burstDone = true; burnFrom = Time.time; }
                }
                else if (parachute)
                {
                    // Hanging under the parachute: slow fall, a little drift and swing.
                    velocity = Vector3.Lerp(velocity, new Vector3(1.2f, -2.4f, 0.6f), dt * 1.5f);
                    p += new Vector3(Mathf.Sin((Time.time + seed) * 0.8f), 0f, Mathf.Cos((Time.time + seed) * 0.6f)) * 0.6f * dt;
                }
                else velocity += Physics.gravity * 0.8f * dt;
                p += velocity * dt;
                transform.position = p;

                float burning = Time.time - burnFrom;
                float k;
                if (burning < 0f) k = 0.06f;                         // the rising charge: a dim spark
                else if (burning < 0.5f) k = burning / 0.5f;          // ignition
                else if (burning > burnFor) k = 0f;
                else k = Mathf.Clamp01((burnFor - burning) / 1.5f);   // dying out
                k *= 0.88f + 0.12f * Mathf.PerlinNoise(seed, Time.time * 9f);
                light.intensity = Brightness * k;
                light.enabled = k > 0.001f;

                Camera cam = Camera.main;
                if (cam == null) { SoldierController pl = AdditionsBootstrap.LocalPlayer(); if (pl != null) cam = pl.cam; }
                if (cam != null) star.rotation = Quaternion.LookRotation(star.position - cam.transform.position);
                star.localScale = Vector3.one * (burning < 0f ? 0.6f : 2.2f * Mathf.Sqrt(Mathf.Max(0f, k)));
                if (burning >= 0f && burning <= burnFor) trail.time = parachute ? 9f : 5f;

                Ground(p, burning);
            }

            /// <summary>The light of an effects Very light: lit in 0.4 s, gutters out over the last 1.2 s like their star.</summary>
            void Glow()
            {
                float age = Time.time - born;
                float k = Mathf.Min(Mathf.Clamp01(age / 0.4f), Mathf.Clamp01((burnFor - age) / 1.2f));
                k *= 0.8f + 0.2f * Mathf.PerlinNoise(Time.time * 9f, seed);
                light.intensity = Brightness * 0.75f * k;
                light.enabled = k > 0.001f;
                if (age > burnFor) { light.enabled = false; live.Remove(this); Destroy(this); }
            }

            void OnDestroy() { live.Remove(this); }

            void Ground(Vector3 p, float burning)
            {
                bool landed = Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, 0.6f, ~0, QueryTriggerInteraction.Ignore);
                if (burning > burnFor || landed || Time.time - born > 60f)
                {
                    light.enabled = false;
                    star.gameObject.SetActive(false);
                    trail.emitting = false;
                    enabled = false;
                    live.Remove(this);
                    Destroy(gameObject, trail.time);
                }
            }
        }
    }
}
