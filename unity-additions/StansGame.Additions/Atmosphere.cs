using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StansGame.Additions
{
    /// <summary>
    /// Night lighting and image quality for the level, switched in the settings.
    /// <para>"Enhanced night lighting": three-colour ambient light (moonlit sky above, dark ground below)
    /// instead of one flat colour, so shapes read; a stronger, cooler moon with sharper, slightly lighter
    /// shadows out to 80 m (tighter cascades near the player); a moonlit haze (lighter fog that matches
    /// the new night sky, <see cref="NightSky"/>, so distance reads as depth instead of black); the sky as
    /// the reflection for glossy surfaces; a stronger moon glint on the canal and the Orne; muzzle flashes
    /// that light a wider circle; up to 8 lights per object (flares and muzzle flashes together);
    /// dithering against banding in the dark; and a global post-processing volume with neutral
    /// tonemapping, bloom (muzzle flashes, tracers, flares and fires glow), a cool grade with blue-grey
    /// shadows, and a vignette. Its priority (5) is under the game's own
    /// "Film" volume (10), so the black-and-white film look still wins when the game blends it in.</para>
    /// <para>"Smooth edges": 4x MSAA, and alpha to coverage on alpha-clipped leaf and grass materials,
    /// so tree crowns and hedges no longer break up into flickering single pixels. The leaves' gloss is
    /// also capped, as the moon made them sparkle.</para>
    /// The night lighting stands down while the bootcamp (when it is in the build) runs its daylight
    /// training ground.
    /// Everything is put back as it was when a setting is turned off. With the Unity source these
    /// belong in the scene's lighting settings, the URP asset and the materials.
    /// </summary>
    public sealed class Atmosphere : MonoBehaviour
    {
        // Tuned values (gamma-space colours).
        static readonly Color AmbientSky = new Color(0.135f, 0.155f, 0.215f);
        static readonly Color AmbientEquator = new Color(0.09f, 0.098f, 0.12f);
        static readonly Color AmbientGround = new Color(0.035f, 0.035f, 0.038f);
        static readonly Color MoonColour = new Color(0.72f, 0.79f, 0.98f);
        const float MoonScale = 2.1f;
        const float MoonShadowStrength = 0.8f;   // night shadows are filled by the sky; full crowns would otherwise go black inside
        const float FogDensity = 0.0078f;
        const float ShadowDistance = 80f;
        static readonly Vector3 CascadeSplits = new Vector3(0.07f, 0.2f, 0.45f);
        const float MuzzleRangeScale = 2.2f, MuzzleIntensityScale = 1.6f;
        const float WaterGlintScale = 1.7f;

        // What the level had, to put back.
        bool saved, urpSaved;
        AmbientMode ambientMode;
        Color ambientLight, ambientSky, ambientEquator, ambientGround;
        Light moon;
        float moonIntensity, moonShadowStrength;
        Color moonColour;
        bool fog;
        FogMode fogMode;
        Color fogColour;
        float fogDensity;
        DefaultReflectionMode reflectionMode;
        Texture reflectionTexture;
        float reflectionIntensity;
        float shadowDistance;
        int cascadeCount, maxAdditionalLights;
        Vector3 cascadeSplit;
        int msaa;
        readonly Dictionary<Material, Vector2> leaves = new Dictionary<Material, Vector2>();   // x = _AlphaToMask, y = _Smoothness
        readonly Dictionary<Light, Vector2> muzzleLights = new Dictionary<Light, Vector2>();   // x = range, y = intensity
        readonly Dictionary<Material, float> water = new Dictionary<Material, float>();   // _Glint
        readonly Dictionary<UniversalAdditionalCameraData, bool> dither = new Dictionary<UniversalAdditionalCameraData, bool>();

        Volume volume;
        NightSky sky;
        bool lightingOn, edgesOn;
        float nextCheck, nextMuzzleScan;

        public static bool LightingApplied { get; private set; }
        public static bool EdgesApplied { get; private set; }
        public static int LeafMaterials { get; private set; }

        MissionDirector level;

        static PropertyInfo bootcampActive;
        static bool bootcampLooked;

        /// <summary>True while the bootcamp (<c>StansGame.Additions.Bootcamp</c>, when it is in the build) runs its
        /// daylight training ground. Found by reflection, so this compiles without it.</summary>
        public static bool BootcampActive
        {
            get
            {
                if (!bootcampLooked)
                {
                    bootcampLooked = true;
                    System.Type t = typeof(Atmosphere).Assembly.GetType("StansGame.Additions.Bootcamp");
                    bootcampActive = t != null ? t.GetProperty("Active", BindingFlags.Public | BindingFlags.Static) : null;
                    if (bootcampActive != null && bootcampActive.PropertyType != typeof(bool)) bootcampActive = null;
                }
                return bootcampActive != null && (bool)bootcampActive.GetValue(null, null);
            }
        }

        void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.5f;

            MissionDirector md = MissionDirector.Instance;
            if (md == null) return;
            bool bootcamp = BootcampActive;
            if (!saved || md != level)
            {
                // A (re)loaded level: forget the old one and read this one's settings.
                if (lightingOn && sky != null) sky.Hide();
                saved = false;
                LightingApplied = EdgesApplied = lightingOn = edgesOn = false;
                muzzleLights.Clear();
                dither.Clear();
                if (bootcamp) return;   // read the night level, not the bootcamp's daylight
                if (!Save()) return;
                level = md;
            }

            // The night lighting stands down on the bootcamp's daylight training ground.
            bool wantLight = GameSettings.EnhancedLighting && !bootcamp, wantEdges = GameSettings.SmoothEdges;
            if (wantLight != lightingOn) { Lighting(wantLight); lightingOn = wantLight; }
            if (wantEdges != edgesOn) { Edges(wantEdges); edgesOn = wantEdges; }
            KeepCameras();
            if (lightingOn && Time.unscaledTime >= nextMuzzleScan) { nextMuzzleScan = Time.unscaledTime + 1f; MuzzleLights(true); }
        }

        bool Save()
        {
            moon = RenderSettings.sun;
            if (moon == null)
                foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { moon = l; break; }
            UniversalRenderPipelineAsset urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null) return false;

            ambientMode = RenderSettings.ambientMode;
            ambientLight = RenderSettings.ambientLight;
            ambientSky = RenderSettings.ambientSkyColor;
            ambientEquator = RenderSettings.ambientEquatorColor;
            ambientGround = RenderSettings.ambientGroundColor;
            fog = RenderSettings.fog;
            fogMode = RenderSettings.fogMode;
            fogColour = RenderSettings.fogColor;
            fogDensity = RenderSettings.fogDensity;
            reflectionMode = RenderSettings.defaultReflectionMode;
            reflectionTexture = RenderSettings.customReflectionTexture;
            reflectionIntensity = RenderSettings.reflectionIntensity;
            if (moon != null) { moonIntensity = moon.intensity; moonColour = moon.color; moonShadowStrength = moon.shadowStrength; }
            if (!urpSaved)
            {
                // The asset outlives a level reload.
                shadowDistance = urp.shadowDistance;
                cascadeCount = urp.shadowCascadeCount;
                cascadeSplit = urp.cascade4Split;
                maxAdditionalLights = urp.maxAdditionalLightsCount;
                msaa = urp.msaaSampleCount;
                urpSaved = true;
            }

            foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r is ParticleSystemRenderer) continue;
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null || leaves.ContainsKey(m) || !m.HasProperty("_AlphaClip") || !m.HasProperty("_AlphaToMask")) continue;
                    if (m.GetFloat("_AlphaClip") < 0.5f) continue;
                    leaves[m] = new Vector2(m.GetFloat("_AlphaToMask"), m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : -1f);
                }
            }
            LeafMaterials = leaves.Count;
            foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Material m = r.sharedMaterial;
                if (m != null && !water.ContainsKey(m) && m.HasProperty("_Glint") && m.shader != null && m.shader.name.Contains("Water")) water[m] = m.GetFloat("_Glint");
            }
            saved = true;
            Debug.Log("Stan's Game: atmosphere read the level (moon " + (moon != null ? moon.name : "none") + ", " + leaves.Count + " alpha-clipped materials)");
            return true;
        }

        void Lighting(bool on)
        {
            UniversalRenderPipelineAsset urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (sky == null) sky = gameObject.AddComponent<NightSky>();
            if (on)
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = AmbientSky;
                RenderSettings.ambientEquatorColor = AmbientEquator;
                RenderSettings.ambientGroundColor = AmbientGround;
                if (moon != null) { moon.intensity = moonIntensity * MoonScale; moon.color = MoonColour; moon.shadowStrength = MoonShadowStrength; }

                // Moonlit haze: thin near the player, thick in the distance, in the sky's horizon colour.
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = NightSky.Horizon;
                RenderSettings.fogDensity = FogDensity;

                sky.Show(moon != null ? -moon.transform.forward : Vector3.up);
                if (NightSky.Reflection != null)
                {
                    RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                    RenderSettings.customReflectionTexture = NightSky.Reflection;
                    RenderSettings.reflectionIntensity = 1f;
                }

                if (urp != null)
                {
                    urp.shadowDistance = Mathf.Max(shadowDistance, ShadowDistance);
                    urp.shadowCascadeCount = 4;
                    urp.cascade4Split = CascadeSplits;
                    urp.maxAdditionalLightsCount = Mathf.Max(maxAdditionalLights, 8);
                }
                foreach (KeyValuePair<Material, float> kv in water) if (kv.Key != null) kv.Key.SetFloat("_Glint", kv.Value * WaterGlintScale);
                if (volume == null) volume = BuildVolume();
                volume.enabled = true;
                MuzzleLights(true);
            }
            else
            {
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambientLight;
                RenderSettings.ambientSkyColor = ambientSky;
                RenderSettings.ambientEquatorColor = ambientEquator;
                RenderSettings.ambientGroundColor = ambientGround;
                if (moon != null) { moon.intensity = moonIntensity; moon.color = moonColour; moon.shadowStrength = moonShadowStrength; }
                RenderSettings.fog = fog;
                RenderSettings.fogMode = fogMode;
                RenderSettings.fogColor = fogColour;
                RenderSettings.fogDensity = fogDensity;
                RenderSettings.defaultReflectionMode = reflectionMode;
                RenderSettings.customReflectionTexture = reflectionTexture;
                RenderSettings.reflectionIntensity = reflectionIntensity;
                sky.Hide();
                if (urp != null)
                {
                    urp.shadowDistance = shadowDistance;
                    urp.shadowCascadeCount = cascadeCount;
                    urp.cascade4Split = cascadeSplit;
                    urp.maxAdditionalLightsCount = maxAdditionalLights;
                }
                foreach (KeyValuePair<Material, float> kv in water) if (kv.Key != null) kv.Key.SetFloat("_Glint", kv.Value);
                if (volume != null) volume.enabled = false;
                MuzzleLights(false);
            }
            LightingApplied = on;
        }

        /// <summary>Muzzle flashes light a wider circle at night. New weapons are picked up as they appear.</summary>
        void MuzzleLights(bool on)
        {
            if (on)
            {
                foreach (Weapon w in FindObjectsByType<Weapon>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    Light l = w.muzzleLight;
                    if (l == null || muzzleLights.ContainsKey(l)) continue;
                    muzzleLights[l] = new Vector2(l.range, l.intensity);
                    l.range *= MuzzleRangeScale;
                    l.intensity *= MuzzleIntensityScale;
                }
            }
            else
            {
                foreach (KeyValuePair<Light, Vector2> kv in muzzleLights)
                    if (kv.Key != null) { kv.Key.range = kv.Value.x; kv.Key.intensity = kv.Value.y; }
                muzzleLights.Clear();
            }
        }

        Volume BuildVolume()
        {
            GameObject go = new GameObject("Atmosphere volume (Stan's Game additions)");
            go.transform.SetParent(transform, false);
            Volume v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = 5f;
            v.weight = 1f;
            VolumeProfile p = ScriptableObject.CreateInstance<VolumeProfile>();
            p.name = "Atmosphere";

            Tonemapping tone = p.Add<Tonemapping>(false);
            tone.mode.Override(TonemappingMode.Neutral);

            Bloom bloom = p.Add<Bloom>(false);
            bloom.threshold.Override(0.9f);
            bloom.intensity.Override(0.7f);
            bloom.scatter.Override(0.7f);
            bloom.tint.Override(new Color(1f, 0.9f, 0.8f));

            ColorAdjustments grade = p.Add<ColorAdjustments>(false);
            grade.postExposure.Override(0.4f);
            grade.contrast.Override(12f);
            grade.saturation.Override(-18f);
            grade.colorFilter.Override(new Color(0.97f, 0.985f, 1f));

            // Blue in the shadows, a touch of warmth in the highlights (moonlight against fire and flares).
            SplitToning split = p.Add<SplitToning>(false);
            split.shadows.Override(new Color(0.44f, 0.48f, 0.56f));
            split.highlights.Override(new Color(0.58f, 0.55f, 0.50f));
            split.balance.Override(-10f);

            Vignette vig = p.Add<Vignette>(false);
            vig.intensity.Override(0.24f);
            vig.smoothness.Override(0.45f);

            v.sharedProfile = p;
            return v;
        }

        void Edges(bool on)
        {
            UniversalRenderPipelineAsset urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null) urp.msaaSampleCount = on ? 4 : msaa;
            foreach (KeyValuePair<Material, Vector2> kv in leaves)
            {
                Material m = kv.Key;
                if (m == null) continue;
                m.SetFloat("_AlphaToMask", on ? 1f : kv.Value.x);
                if (kv.Value.y >= 0f) m.SetFloat("_Smoothness", on ? Mathf.Min(kv.Value.y, 0.15f) : kv.Value.y);
            }
            EdgesApplied = on;
        }

        /// <summary>
        /// The game's cameras must render post-processing (for the volume) and allow MSAA; with the night
        /// lighting they also dither, against banding in dark gradients.
        /// </summary>
        void KeepCameras()
        {
            foreach (Camera c in Camera.allCameras)
            {
                if (c == null || c.targetTexture != null) continue;
                UniversalAdditionalCameraData d = c.GetComponent<UniversalAdditionalCameraData>();
                if (d == null || d.renderType != CameraRenderType.Base) continue;
                if (lightingOn && !d.renderPostProcessing) d.renderPostProcessing = true;
                if (edgesOn && !c.allowMSAA) c.allowMSAA = true;
                if (lightingOn && !d.dithering) { if (!dither.ContainsKey(d)) dither[d] = false; d.dithering = true; }
            }
            if (!lightingOn && dither.Count > 0)
            {
                foreach (KeyValuePair<UniversalAdditionalCameraData, bool> kv in dither)
                    if (kv.Key != null) kv.Key.dithering = kv.Value;
                dither.Clear();
            }
        }
    }
}
