using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// The weather in a level (level template 3a): the Weather component with its day's schedule and wind, the rain
    /// and snow materials, and banks of mist over every water surface (WaterRipple) that thicken at night and at dawn.
    /// Stan's Game > Debug > Weather: ... switches it by hand while playing.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        static Texture2D SoftPuffTexture()
        {
            string path = $"{GenTexDir}/SoftPuff.png";
            if (!File.Exists(path))
            {
                if (!AssetDatabase.IsValidFolder(GenTexDir)) Directory.CreateDirectory(GenTexDir);
                const int N = 128; var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy);
                        float n = 0.75f + 0.25f * Mathf.PerlinNoise(x * 0.09f, y * 0.09f);
                        float a = Mathf.Clamp01(1f - r); a = a * a * (3f - 2f * a) * n;
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
                if (AssetImporter.GetAtPath(path) is TextureImporter ti) { ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport(); }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>A soft, see-through particle material (URP Particles/Unlit, alpha blended).</summary>
        static Material ParticleMat(string name, Color tint)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            if (sh) m.shader = sh;
            var tex = SoftPuffTexture();
            m.SetTexture("_BaseMap", tex); m.mainTexture = tex;
            m.SetColor("_BaseColor", tint); m.color = tint;
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// Puts the weather into a level: the starting weather, the changes by the mission clock, the wind (where it
        /// blows to, 0..1), and mist banks over every water surface already built.
        /// </summary>
        static Weather AddWeather(Transform root, WeatherKind start, Weather.Change[] schedule, Vector3 windTo, float windStrength)
        {
            var go = new GameObject("Weather"); go.transform.SetParent(root);
            var w = go.AddComponent<Weather>();
            w.start = start; w.schedule = schedule ?? new Weather.Change[0];
            w.windTo = windTo; w.windStrength = windStrength;
            w.rainMaterial = ParticleMat("Weather_Rain", new Color(0.8f, 0.85f, 0.95f, 0.55f));
            w.snowMaterial = ParticleMat("Weather_Snow", Color.white);
            var mistMat = ParticleMat("Weather_Mist", new Color(0.78f, 0.82f, 0.88f, 1f));
            var banks = new List<ParticleSystem>();
            foreach (var wr in Object.FindObjectsByType<WaterRipple>(FindObjectsInactive.Include))
            {
                var r = wr.GetComponent<Renderer>(); if (!r) continue;
                var b = r.bounds;
                if (b.size.x * b.size.z < 40f) continue;   // a puddle, not a river
                banks.Add(MistBank(go.transform, b, mistMat));
            }
            w.mistBanks = banks.ToArray();
            return w;
        }

        /// <summary>Low, slow, wide puffs of mist lying over a water surface (and a little over its banks).</summary>
        static ParticleSystem MistBank(Transform parent, Bounds water, Material mat)
        {
            var go = new GameObject("Mist bank"); go.transform.SetParent(parent);
            go.transform.position = new Vector3(water.center.x, water.center.y + 0.6f, water.center.z);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.prewarm = true; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(18f, 30f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(9f, 18f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.8f, 0.84f, 0.9f, 0.05f);
            float area = (water.size.x + 10f) * (water.size.z + 10f);
            main.maxParticles = Mathf.Clamp(Mathf.RoundToInt(area / 60f), 20, 900);
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(water.size.x + 10f, 0.8f, water.size.z + 10f);
            var em = ps.emission; em.rateOverTime = main.maxParticles / 24f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;   // lying flat on the water
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.sortingFudge = 10f;
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------ switch the weather by hand (play mode)

        static void ForceWeather(WeatherKind k)
        {
            if (!Application.isPlaying || !Weather.Instance) { Debug.LogWarning("Stan's Game: the weather can be switched while playing a level that has it"); return; }
            Weather.Instance.Force(k); Debug.Log($"Stan's Game: weather - {k}");
        }
        [MenuItem("Stan's Game/Debug/Weather: Clear", priority = 170)] static void WeatherClear() => ForceWeather(WeatherKind.Clear);
        [MenuItem("Stan's Game/Debug/Weather: Cloudy", priority = 171)] static void WeatherCloudy() => ForceWeather(WeatherKind.Cloudy);
        [MenuItem("Stan's Game/Debug/Weather: Rain", priority = 172)] static void WeatherRain() => ForceWeather(WeatherKind.Rain);
        [MenuItem("Stan's Game/Debug/Weather: Fog", priority = 173)] static void WeatherFog() => ForceWeather(WeatherKind.Fog);
        [MenuItem("Stan's Game/Debug/Weather: Snow", priority = 174)] static void WeatherSnow() => ForceWeather(WeatherKind.Snow);
    }
}
