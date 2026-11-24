using UnityEngine;

namespace StansGame
{
    public enum WeatherKind { Clear, Cloudy, Rain, Fog, Snow }

    /// <summary>
    /// The weather of a level (level template 3a, 23 Nov 2026): one system for every level, with presets that set
    /// everything at once - cloud cover, how much light gets through, fog, mist over the water, rain or snow round the
    /// camera, wet ground, and the sound of it. It follows the mission clock (a schedule of changes) and blends from
    /// one to the next over half a minute. The wind (direction and strength) is the same for everything that moves
    /// in it: the clouds, the flags, the rain, the mist.
    /// NightSky reads LightFactor / FogFactor / AmbientFactor and the cloud cover; the water reads _StansRain.
    /// Stan's Game > Debug > Weather switches it by hand in play.
    /// </summary>
    public class Weather : MonoBehaviour
    {
        [System.Serializable]
        public struct Change { public float minutes; public WeatherKind kind; }

        public static Weather Instance { get; private set; }

        [Header("The day")]
        public WeatherKind start = WeatherKind.Cloudy;
        [Tooltip("Changes by the mission clock (minutes after the mission starts), in order.")]
        public Change[] schedule = new Change[0];
        [Tooltip("Seconds to blend from one weather to the next.")]
        public float blendSeconds = 30f;

        [Header("Wind (the same for everything)")]
        [Tooltip("Where the wind blows TO, on the ground (x east, z north). 6 June 1944: from the north-west.")]
        public Vector3 windTo = new Vector3(0.7f, 0f, -0.7f);
        [Range(0f, 1f)] public float windStrength = 0.7f;

        [Header("Made by the level builder")]
        public Material rainMaterial, snowMaterial;
        public ParticleSystem[] mistBanks = new ParticleSystem[0];

        // the blended state, read by NightSky, the water, the flags
        public static float Coverage = -1f, LightFactor = 1f, FogFactor = 1f, AmbientFactor = 1f, Rain, Snow, Mist, Wetness;
        public static Vector3 WindTo = new Vector3(0.7f, 0f, -0.7f);
        public static float WindStrength = 0.7f;
        public WeatherKind Current { get; private set; }

        struct Look { public float coverage, light, fog, ambient, rain, snow, mist; }
        static Look Preset(WeatherKind k)
        {
            switch (k)
            {
                case WeatherKind.Clear:  return new Look { coverage = 0.15f, light = 1.00f, fog = 0.7f, ambient = 1.0f, mist = 0.25f };
                case WeatherKind.Rain:   return new Look { coverage = 0.92f, light = 0.45f, fog = 1.7f, ambient = 0.8f, rain = 1f, mist = 0.35f };
                case WeatherKind.Fog:    return new Look { coverage = 0.75f, light = 0.60f, fog = 3.5f, ambient = 0.9f, mist = 1f };
                case WeatherKind.Snow:   return new Look { coverage = 0.85f, light = 0.70f, fog = 2.0f, ambient = 1.1f, snow = 1f, mist = 0.2f };
                default:                 return new Look { coverage = 0.55f, light = 0.80f, fog = 1.0f, ambient = 0.95f, mist = 0.5f };   // Cloudy
            }
        }

        Look now, target;
        bool forced;
        ParticleSystem rain, snow;
        static readonly int RainId = Shader.PropertyToID("_StansRain"), WetId = Shader.PropertyToID("_StansWetness");

        void Awake()
        {
            Instance = this;
            Current = start; now = target = Preset(start);
            WindTo = windTo.sqrMagnitude > 0.001f ? windTo.normalized : Vector3.right; WindStrength = windStrength;
            Publish();
        }

        void OnDestroy()
        {
            if (Instance == this) { Instance = null; Coverage = -1f; LightFactor = FogFactor = AmbientFactor = 1f; Rain = Snow = Mist = Wetness = 0f; }
            Shader.SetGlobalFloat(RainId, 0f); Shader.SetGlobalFloat(WetId, 0f);
        }

        /// <summary>Set the weather by hand (the debug menu); the schedule is ignored from then on.</summary>
        public void Force(WeatherKind k) { forced = true; Set(k); }

        void Set(WeatherKind k) { Current = k; target = Preset(k); }

        static float Clock()
        {
            if (MissionDirector.Instance) return MissionDirector.Instance.Minutes;
            if (SainteMereMission.Instance) return SainteMereMission.Instance.Minutes;
            if (BenouvilleMission.Instance) return BenouvilleMission.Instance.Minutes;
            return Time.timeSinceLevelLoad / 60f;
        }

        void Update()
        {
            // the schedule: the last change whose time has come
            if (!forced && schedule != null && schedule.Length > 0)
            {
                float m = Clock(); var k = start;
                foreach (var c in schedule) if (m >= c.minutes) k = c.kind;
                if (k != Current) Set(k);
            }
            float a = 1f - Mathf.Exp(-Time.deltaTime * 3f / Mathf.Max(1f, blendSeconds));
            now.coverage = Mathf.Lerp(now.coverage, target.coverage, a); now.light = Mathf.Lerp(now.light, target.light, a);
            now.fog = Mathf.Lerp(now.fog, target.fog, a); now.ambient = Mathf.Lerp(now.ambient, target.ambient, a);
            now.rain = Mathf.Lerp(now.rain, target.rain, a); now.snow = Mathf.Lerp(now.snow, target.snow, a);
            now.mist = Mathf.Lerp(now.mist, target.mist, a);
            // the ground gets wet quickly in the rain and dries slowly
            Wetness = Mathf.MoveTowards(Wetness, now.rain > 0.2f ? 1f : 0f, Time.deltaTime / (now.rain > 0.2f ? 40f : 400f));
            WindTo = windTo.sqrMagnitude > 0.001f ? windTo.normalized : Vector3.right; WindStrength = windStrength;
            Publish();
            Particles();
        }

        void Publish()
        {
            Coverage = now.coverage; LightFactor = now.light; FogFactor = now.fog; AmbientFactor = now.ambient;
            Rain = now.rain; Snow = now.snow; Mist = now.mist;
            Shader.SetGlobalFloat(RainId, Rain); Shader.SetGlobalFloat(WetId, Wetness);
        }

        // ------------------------------------------------------------------ rain, snow and mist

        void Particles()
        {
            var cam = Camera.main;
            if (cam)
            {
                if (Rain > 0.01f && !rain && rainMaterial) rain = MakeFall("Rain", rainMaterial, true);
                if (Snow > 0.01f && !snow && snowMaterial) snow = MakeFall("Snow", snowMaterial, false);
                Vector3 p = cam.transform.position + Vector3.up * 8f;
                if (rain) { rain.transform.position = p; Rate(rain, Rain * 2600f); }
                if (snow) { snow.transform.position = p; Rate(snow, Snow * 900f); }
                SoundOfIt(cam.transform.position);
            }
            // particles are not lit by the scene's lights: give them the colour of the light there is (dim at night)
            Color lit = RenderSettings.ambientLight * 2.4f;
            var sunL = RenderSettings.sun;
            if (sunL && sunL.enabled) lit += sunL.color * sunL.intensity * 0.5f;
            lit.r = Mathf.Clamp01(lit.r); lit.g = Mathf.Clamp01(lit.g); lit.b = Mathf.Clamp01(lit.b);
            if (rain) { var rm = rain.main; rm.startColor = new Color(lit.r * 1.1f, lit.g * 1.15f, lit.b * 1.25f, 0.35f); }
            if (snow) { var sm = snow.main; sm.startColor = new Color(Mathf.Clamp01(lit.r * 1.6f), Mathf.Clamp01(lit.g * 1.6f), Mathf.Clamp01(lit.b * 1.6f), 0.85f); }
            // mist over the water: thicker at night and at dawn, thinned by a strong wind
            float night = 1f - Mathf.Clamp01(Shader.GetGlobalFloat("_SkyDay"));
            float dawn = Shader.GetGlobalFloat("_SkyTwilight");
            float m = Mist * Mathf.Clamp01(0.45f + 0.55f * Mathf.Max(night, dawn * 1.5f)) * (1.1f - 0.5f * WindStrength);
            foreach (var ps in mistBanks)
            {
                if (!ps) continue;
                var main = ps.main; main.startColor = new Color(lit.r, lit.g, lit.b, 0.07f * m);
                var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
                Vector3 w = WindTo * (0.25f + 0.9f * WindStrength);
                vel.x = new ParticleSystem.MinMaxCurve(w.x); vel.y = new ParticleSystem.MinMaxCurve(0f); vel.z = new ParticleSystem.MinMaxCurve(w.z);
            }
        }

        static void Rate(ParticleSystem ps, float r) { var em = ps.emission; em.rateOverTime = r; }

        ParticleSystem MakeFall(string name, Material mat, bool isRain)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = isRain ? 6000 : 3000;
            main.startLifetime = isRain ? 1.1f : 6f;
            main.startSpeed = 0f;
            main.startSize = isRain ? new ParticleSystem.MinMaxCurve(0.015f, 0.025f) : new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.startColor = isRain ? new Color(0.75f, 0.8f, 0.9f, 0.35f) : new Color(1f, 1f, 1f, 0.85f);
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(40f, 1f, 40f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            Vector3 w = WindTo * WindStrength * (isRain ? 3f : 1.5f);
            vel.x = new ParticleSystem.MinMaxCurve(w.x - 0.3f, w.x + 0.3f);
            vel.y = isRain ? new ParticleSystem.MinMaxCurve(-11f, -9f) : new ParticleSystem.MinMaxCurve(-1.4f, -0.9f);
            vel.z = new ParticleSystem.MinMaxCurve(w.z - 0.3f, w.z + 0.3f);
            if (!isRain) { var noise = ps.noise; noise.enabled = true; noise.strength = 0.5f; noise.frequency = 0.4f; }
            var em = ps.emission; em.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            if (isRain) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.045f; r.lengthScale = 1f; }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------ the sound of it

        AudioSource heavy, light, wind, onCanvas, onRoof;
        Transform[] shelters; float shelterCheck;

        AudioSource Loop(string clip, bool spatial)
        {
            var c = Sfx.Recorded(clip);
            if (!c) return null;
            var go = new GameObject("Sound: " + clip); go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>(); src.clip = c; src.loop = true; src.volume = 0f;
            src.spatialBlend = spatial ? 1f : 0f; src.rolloffMode = AudioRolloffMode.Linear; src.minDistance = 2f; src.maxDistance = 25f;
            src.time = Random.Range(0f, c.length * 0.9f); src.Play();
            return src;
        }

        /// <summary>Rain (Joan's recordings, Resources/Audio: rain_loop, rain_light), the patter on the nearest tent or
        /// hut roof (rain_tent, rain_roof) and the wind (wind_loop), each as loud as the weather is.</summary>
        void SoundOfIt(Vector3 ear)
        {
            if (Rain > 0.01f && !heavy) { heavy = Loop("rain_loop", false); light = Loop("rain_light", false); }
            if (heavy) heavy.volume = Mathf.Clamp01((Rain - 0.3f) / 0.7f) * 0.7f;
            if (light) light.volume = Mathf.Clamp01(Rain * 2f) * (1f - 0.6f * Mathf.Clamp01((Rain - 0.3f) / 0.7f)) * 0.5f;
            if (!wind) wind = Loop("wind_loop", false);
            if (wind) wind.volume = WindStrength * 0.16f * (1f + 0.5f * Rain);
            // the nearest tent and the nearest hut or shed: rain drumming on canvas and on a roof
            if (Rain > 0.05f)
            {
                if (shelters == null)
                {
                    var list = new System.Collections.Generic.List<Transform>();
                    foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
                    {
                        string n = t.name.ToLowerInvariant();
                        if ((n.Contains("tent") || n.Contains("hut") || n.Contains("shed") || n.Contains("marquee")) && t.GetComponentInChildren<Renderer>())
                            { list.Add(t); if (list.Count > 400) break; }
                    }
                    shelters = list.ToArray();
                }
                if ((shelterCheck -= Time.deltaTime) <= 0f)
                {
                    shelterCheck = 1f;
                    Transform tent = null, roof = null; float dt = 30f, dr = 30f;
                    foreach (var t in shelters)
                    {
                        if (!t) continue;
                        float d = Vector3.Distance(t.position, ear);
                        bool canvas = t.name.ToLowerInvariant().Contains("tent") || t.name.ToLowerInvariant().Contains("marquee");
                        if (canvas && d < dt) { dt = d; tent = t; } else if (!canvas && d < dr) { dr = d; roof = t; }
                    }
                    if (tent && !onCanvas) onCanvas = Loop("rain_tent", true);
                    if (roof && !onRoof) onRoof = Loop("rain_roof", true);
                    if (onCanvas) { if (tent) onCanvas.transform.position = tent.position + Vector3.up * 1.5f; onCanvas.volume = tent ? Rain * 0.9f : 0f; }
                    if (onRoof) { if (roof) onRoof.transform.position = roof.position + Vector3.up * 2.5f; onRoof.volume = roof ? Rain * 0.8f : 0f; }
                }
            }
            else { if (onCanvas) onCanvas.volume = 0f; if (onRoof) onRoof.volume = 0f; }
        }
    }
}
