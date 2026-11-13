using System.Collections.Generic;
using UnityEngine;

namespace StansGame.Additions
{
    /// <summary>
    /// Smoke from the muzzle: a puff that rolls forward and drifts up on every shot, and a thin wisp
    /// from the barrel for a second after the last one. One particle system per weapon, simulated in
    /// world space so the smoke stays where it was fired while the weapon moves on. The smoke texture is
    /// generated (no asset); the material uses a URP particle shader that is in the build, or, if none
    /// is, a copy of the game's own impact dust material.
    /// <see cref="WeaponFeel"/> calls <see cref="Shot"/> for the local player's shots.
    /// </summary>
    public static class MuzzleSmoke
    {
        sealed class Smoker
        {
            public ParticleSystem ps;
            public float wispUntil, nextWisp;
        }

        static readonly Dictionary<Weapon, Smoker> smokers = new Dictionary<Weapon, Smoker>();
        static Material material;
        static bool triedMaterial;
        static Texture2D puff;

        public static int Puffs;

        public static void Shot(Weapon w)
        {
            Smoker s = For(w);
            if (s == null) return;
            bool heavy = w.hold == HoldStyle.Rifle || w.hold == HoldStyle.Heavy;
            int n = heavy ? 5 : 3;
            Vector3 fwd = w.muzzle.forward;
            for (int i = 0; i < n; i++)
            {
                ParticleSystem.EmitParams e = new ParticleSystem.EmitParams();
                e.position = w.muzzle.position + fwd * Random.Range(0.02f, 0.12f);
                e.velocity = fwd * Random.Range(0.4f, heavy ? 2.2f : 1.3f) + Random.insideUnitSphere * 0.15f + Vector3.up * 0.12f;
                e.startSize = Random.Range(0.06f, heavy ? 0.16f : 0.11f);
                e.startLifetime = Random.Range(1.0f, heavy ? 2.4f : 1.7f);
                e.rotation = Random.Range(0f, 360f);
                e.angularVelocity = Random.Range(-40f, 40f);
                float g = Random.Range(0.46f, 0.58f);   // the shader is unlit: kept grey so it does not glow at night
                e.startColor = new Color(g, g, g, heavy ? 0.38f : 0.30f);
                s.ps.Emit(e, 1);
            }
            s.wispUntil = Time.time + (heavy ? 1.6f : 1.1f);
            Puffs++;
        }

        /// <summary>Keeps the barrel smoking a little after firing. Called every frame for the held weapon.</summary>
        public static void Tick(Weapon w)
        {
            Smoker s;
            if (w == null || w.muzzle == null || !smokers.TryGetValue(w, out s) || s.ps == null) return;
            if (Time.time > s.wispUntil || Time.time < s.nextWisp) return;
            s.nextWisp = Time.time + 0.07f;
            float left = (s.wispUntil - Time.time);
            ParticleSystem.EmitParams e = new ParticleSystem.EmitParams();
            e.position = w.muzzle.position;
            e.velocity = Vector3.up * Random.Range(0.15f, 0.35f) + Random.insideUnitSphere * 0.04f;
            e.startSize = Random.Range(0.025f, 0.05f);
            e.startLifetime = Random.Range(0.8f, 1.3f);
            e.rotation = Random.Range(0f, 360f);
            e.startColor = new Color(0.55f, 0.55f, 0.55f, 0.10f * Mathf.Clamp01(left));
            s.ps.Emit(e, 1);
        }

        static Smoker For(Weapon w)
        {
            if (w == null || w.muzzle == null || w.hold == HoldStyle.Launcher) return null;
            Smoker s;
            if (smokers.TryGetValue(w, out s) && s.ps != null) return s;
            Material m = Material();
            if (m == null) return null;

            GameObject go = new GameObject("Muzzle smoke (Stan's Game additions)");
            go.transform.SetParent(w.muzzle, false);
            go.layer = 0;   // world layer, so it is not drawn with the first-person weapon
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            main.gravityModifier = -0.03f;   // warm smoke rises slowly
            ParticleSystem.EmissionModule em = ps.emission;
            em.enabled = false;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;

            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            AnimationCurve grow = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.25f, 3.2f), new Keyframe(1f, 6.5f));
            size.size = new ParticleSystem.MinMaxCurve(1f, grow);

            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            Gradient fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.06f), new GradientAlphaKey(0.55f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(fade);

            ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
            vel.enabled = false;
            ParticleSystem.LimitVelocityOverLifetimeModule drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 2.2f;
            drag.limit = 100f;
            drag.dampen = 0f;

            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.18f;
            noise.frequency = 0.6f;
            noise.scrollSpeed = 0.3f;

            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = 10f;
            ps.Play();

            s = new Smoker { ps = ps };
            smokers[w] = s;
            return s;
        }

        static Material Material()
        {
            if (triedMaterial) return material;
            triedMaterial = true;
            Texture2D tex = PuffTexture();
            // The URP particle shaders are stripped from this build; Sprites/Default is in it and is
            // unlit and alpha blended with the particle colour.
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) { Shader sprite = Shader.Find("Sprites/Default"); if (sprite != null) { material = new Material(sprite); material.name = "Muzzle smoke"; material.mainTexture = tex; material.renderQueue = 3000; return material; } }
            if (sh != null)
            {
                material = new Material(sh);
                material.name = "Muzzle smoke";
                // Transparent, alpha blended, no depth write, soft particles off.
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                material.SetTexture("_BaseMap", tex);
                material.mainTexture = tex;
                material.SetColor("_BaseColor", Color.white);
                return material;
            }
            // Fall back on the game's own dust material from a weapon's impact effect.
            foreach (Weapon w in Object.FindObjectsByType<Weapon>(FindObjectsInactive.Include))
            {
                if (w.impactPrefab == null) continue;
                foreach (ParticleSystemRenderer pr in w.impactPrefab.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    if (pr.sharedMaterial != null && pr.sharedMaterial.renderQueue >= 3000)
                    {
                        material = new Material(pr.sharedMaterial);
                        material.name = "Muzzle smoke (from " + pr.sharedMaterial.name + ")";
                        material.mainTexture = tex;
                        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", tex);
                        return material;
                    }
            }
            Debug.LogWarning("Stan's Game: no particle shader for the muzzle smoke");
            return null;
        }

        /// <summary>A soft, uneven round puff, 64 x 64, white with the shape in the alpha.</summary>
        static Texture2D PuffTexture()
        {
            if (puff != null) return puff;
            const int n = 64;
            puff = new Texture2D(n, n, TextureFormat.RGBA32, true);
            puff.wrapMode = TextureWrapMode.Clamp;
            float ox = Random.Range(0f, 100f), oy = Random.Range(0f, 100f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float edge = Mathf.Clamp01(1f - r);
                    edge = edge * edge * (3f - 2f * edge);
                    float noise = Mathf.PerlinNoise(ox + x * 0.09f, oy + y * 0.09f) * 0.65f + Mathf.PerlinNoise(ox + x * 0.23f, oy + y * 0.23f) * 0.35f;
                    float a = Mathf.Clamp01(edge * (0.45f + noise * 0.9f));
                    puff.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            puff.Apply(true);
            return puff;
        }
    }
}
