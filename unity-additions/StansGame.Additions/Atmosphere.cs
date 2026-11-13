using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StansGame.Additions
{
    /// <summary>
    /// Night lighting and image quality for the level, switched in the settings.
    /// <para>"Enhanced night lighting": three-colour ambient light (moonlit sky above, dark ground below)
    /// instead of one flat colour, so shapes read; a slightly stronger moon with shadows out to 70 m;
    /// and a global post-processing volume with neutral tonemapping, soft bloom (muzzle flashes and
    /// fires glow) and a gentle cool grade. Its priority (5) is under the game's own "Film" volume
    /// (10), so the black-and-white film look still wins when the game blends it in.</para>
    /// <para>"Smooth edges": 4x MSAA, and alpha to coverage on alpha-clipped leaf and grass materials,
    /// so tree crowns and hedges no longer break up into flickering single pixels. The leaves' gloss is
    /// also capped, as the moon made them sparkle.</para>
    /// Everything is put back as it was when a setting is turned off. With the Unity source these
    /// belong in the scene's lighting settings, the URP asset and the materials.
    /// </summary>
    public sealed class Atmosphere : MonoBehaviour
    {
        // What the level had, to put back.
        bool saved, urpSaved;
        AmbientMode ambientMode;
        Color ambientLight, ambientSky, ambientEquator, ambientGround;
        Light moon;
        float moonIntensity;
        Color moonColour;
        float shadowDistance;
        int msaa;
        readonly Dictionary<Material, Vector2> leaves = new Dictionary<Material, Vector2>();   // x = _AlphaToMask, y = _Smoothness


        Volume volume;
        bool lightingOn, edgesOn;
        float nextCheck;

        public static bool LightingApplied { get; private set; }
        public static bool EdgesApplied { get; private set; }
        public static int LeafMaterials { get; private set; }

        MissionDirector level;

        void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.5f;

            MissionDirector md = MissionDirector.Instance;
            if (md == null) return;
            if (!saved || md != level)
            {
                // A (re)loaded level: forget the old one and read this one's settings.
                saved = false;
                LightingApplied = EdgesApplied = lightingOn = edgesOn = false;
                if (!Save()) return;
                level = md;
            }

            bool wantLight = GameSettings.EnhancedLighting, wantEdges = GameSettings.SmoothEdges;
            if (wantLight != lightingOn) { Lighting(wantLight); lightingOn = wantLight; }
            if (wantEdges != edgesOn) { Edges(wantEdges); edgesOn = wantEdges; }
            KeepCameras();
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
            if (moon != null) { moonIntensity = moon.intensity; moonColour = moon.color; }
            if (!urpSaved) { shadowDistance = urp.shadowDistance; msaa = urp.msaaSampleCount; urpSaved = true; }   // the asset outlives a level reload

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
            saved = true;
            Debug.Log("Stan's Game: atmosphere read the level (moon " + (moon != null ? moon.name : "none") + ", " + leaves.Count + " alpha-clipped materials)");
            return true;
        }

        void Lighting(bool on)
        {
            UniversalRenderPipelineAsset urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (on)
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.13f, 0.155f, 0.235f);
                RenderSettings.ambientEquatorColor = new Color(0.08f, 0.09f, 0.12f);
                RenderSettings.ambientGroundColor = new Color(0.028f, 0.028f, 0.032f);
                if (moon != null) { moon.intensity = moonIntensity * 1.5f; moon.color = new Color(0.62f, 0.70f, 0.95f); }
                if (urp != null) urp.shadowDistance = Mathf.Max(shadowDistance, 70f);
                if (volume == null) volume = BuildVolume();
                volume.enabled = true;
            }
            else
            {
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambientLight;
                RenderSettings.ambientSkyColor = ambientSky;
                RenderSettings.ambientEquatorColor = ambientEquator;
                RenderSettings.ambientGroundColor = ambientGround;
                if (moon != null) { moon.intensity = moonIntensity; moon.color = moonColour; }
                if (urp != null) urp.shadowDistance = shadowDistance;
                if (volume != null) volume.enabled = false;
            }
            LightingApplied = on;
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
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.65f);
            bloom.tint.Override(new Color(1f, 0.9f, 0.8f));

            ColorAdjustments grade = p.Add<ColorAdjustments>(false);
            grade.postExposure.Override(0.2f);
            grade.contrast.Override(12f);
            grade.saturation.Override(-12f);
            grade.colorFilter.Override(new Color(0.94f, 0.97f, 1f));

            Vignette vig = p.Add<Vignette>(false);
            vig.intensity.Override(0.22f);
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

        /// <summary>The game's cameras must render post-processing (for the volume) and allow MSAA.</summary>
        void KeepCameras()
        {
            foreach (Camera c in Camera.allCameras)
            {
                if (c == null || c.targetTexture != null) continue;
                UniversalAdditionalCameraData d = c.GetComponent<UniversalAdditionalCameraData>();
                if (d == null || d.renderType != CameraRenderType.Base) continue;
                if (lightingOn && !d.renderPostProcessing) d.renderPostProcessing = true;
                if (edgesOn && !c.allowMSAA) c.allowMSAA = true;
            }
        }
    }
}
