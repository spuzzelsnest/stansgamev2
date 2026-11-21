using System.IO;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Smoke grenades (placeholder models until Joan finds real ones):
    ///   British No. 77 Mk I - a tin can of white phosphorus with a black-painted cap and a light-green band;
    ///   German Nebelhandgranate 39 - a stick grenade with a white "Nb" band round the head;
    ///   American AN-M8 HC - a grey sheet-steel canister with a yellow band and a spoon lever.
    /// Each is a Shell with smoke = true: thrown like a grenade, after a short fuse it makes a SmokeCloud.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        static Shell smokeNo77, smokeNb39, smokeM8, gammonBomb;

        /// <summary>Make the smoke grenades (once per build; the SME builder reuses them).</summary>
        static void BuildSmokePrefabs()
        {
            var mat = SmokeMaterial();
            var tin = Mat("Smoke_No77_Tin", new Color(0.30f, 0.33f, 0.24f), 0.35f, 0.4f);
            var cap = Mat("Smoke_No77_Cap", new Color(0.06f, 0.06f, 0.06f), 0.3f, 0.3f);
            var band = Mat("Smoke_No77_Band", new Color(0.55f, 0.70f, 0.45f));
            smokeNo77 = BuildSmokeGrenade("Smoke_No77", "No. 77 smoke grenade", 1.5f, new Color(0.86f, 0.86f, 0.84f, 0.8f), 7.5f, 55f, mat, t =>
            {
                Prim(PrimitiveType.Cylinder, "Tin", t, Vector3.zero, Vector3.zero, new Vector3(0.06f, 0.055f, 0.06f), tin);
                Prim(PrimitiveType.Cylinder, "Cap", t, new Vector3(0f, 0.06f, 0f), Vector3.zero, new Vector3(0.062f, 0.008f, 0.062f), cap);
                Prim(PrimitiveType.Cylinder, "Band", t, new Vector3(0f, 0.01f, 0f), Vector3.zero, new Vector3(0.0615f, 0.01f, 0.0615f), band);
                Prim(PrimitiveType.Cube, "Tape", t, new Vector3(0f, 0.068f, 0f), Vector3.zero, new Vector3(0.05f, 0.004f, 0.012f), band);
            }, 0.03f, 0.12f);

            var wood = Mat("Grenade_Handle", new Color(0.40f, 0.32f, 0.22f));
            var steel = Mat("Smoke_Nb39_Head", new Color(0.24f, 0.26f, 0.22f), 0.3f, 0.5f);
            var white = Mat("Smoke_Nb39_Band", new Color(0.88f, 0.88f, 0.85f));
            smokeNb39 = BuildSmokeGrenade("Smoke_Nb39", "Nebelhandgranate 39", 5f, new Color(0.80f, 0.80f, 0.78f, 0.75f), 6.5f, 60f, mat, t =>
            {
                if (FbxInto(t, "Smoke_Nebelhandgranate39")) return;   // _downloads/nebelhandgranate-39
                Prim(PrimitiveType.Cylinder, "Handle", t, new Vector3(0f, -0.06f, 0f), Vector3.zero, new Vector3(0.035f, 0.14f, 0.035f), wood);
                Prim(PrimitiveType.Cylinder, "Head", t, new Vector3(0f, 0.11f, 0f), Vector3.zero, new Vector3(0.07f, 0.05f, 0.07f), steel);
                Prim(PrimitiveType.Cylinder, "White band", t, new Vector3(0f, 0.115f, 0f), Vector3.zero, new Vector3(0.0715f, 0.012f, 0.0715f), white);
            }, 0.035f, 0.36f);

            var grey = Mat("Smoke_M8_Can", new Color(0.42f, 0.43f, 0.40f), 0.35f, 0.4f);
            var yellow = Mat("Smoke_M8_Band", new Color(0.86f, 0.72f, 0.20f));
            var lever = Mat("Gear_Metal", new Color(0.25f, 0.25f, 0.24f), 0.4f, 0.6f);
            smokeM8 = BuildSmokeGrenade("Smoke_ANM8", "AN-M8 smoke grenade", 2f, new Color(0.82f, 0.82f, 0.80f, 0.78f), 8f, 70f, mat, t =>
            {
                if (FbxInto(t, "Smoke_ANM8")) return;   // _downloads/m8-hc-smoke-grenade
                Prim(PrimitiveType.Cylinder, "Can", t, Vector3.zero, Vector3.zero, new Vector3(0.063f, 0.07f, 0.063f), grey);
                Prim(PrimitiveType.Cylinder, "Band", t, new Vector3(0f, 0.035f, 0f), Vector3.zero, new Vector3(0.064f, 0.008f, 0.064f), yellow);
                Prim(PrimitiveType.Cube, "Spoon", t, new Vector3(0.035f, 0.03f, 0f), new Vector3(0f, 0f, -8f), new Vector3(0.008f, 0.09f, 0.016f), lever);
            }, 0.032f, 0.14f);
            gammonBomb = BuildGammonBomb();
        }

        /// <summary>Put a downloaded grenade model (Models/Weapons/{file}.fbx) under t; false if it isn't there.</summary>
        static bool FbxInto(Transform t, string file)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Models/Weapons/{file}.fbx");
            if (!asset) return false;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset, t);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity; inst.transform.localScale = Vector3.one;
            foreach (var c in inst.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            return true;
        }

        /// <summary>
        /// The No. 82 "Gammon" bomb of the airborne forces: a stockinette bag of plastic explosive under an impact
        /// ("all-ways") fuse - it goes off where it lands, with a much heavier blast than a Mills bomb (enough to
        /// cripple a tank's tracks). Model from _downloads/no-82-gammonbomb (grenades_batch7.py).
        /// </summary>
        static Shell BuildGammonBomb()
        {
            string file = "Grenade_No82_Gammon";
            var go = new GameObject(file);
            if (!FbxInto(go.transform, file))
            {
                Prim(PrimitiveType.Sphere, "Bag", go.transform, Vector3.zero, Vector3.zero, new Vector3(0.15f, 0.13f, 0.15f), Mat("Gammon_Stockinette", new Color(0.55f, 0.50f, 0.38f)));
                Prim(PrimitiveType.Cylinder, "Cap", go.transform, new Vector3(0f, 0.07f, 0f), Vector3.zero, new Vector3(0.04f, 0.015f, 0.04f), Mat("Gammon_Cap", new Color(0.15f, 0.15f, 0.14f), 0.3f, 0.5f));
            }
            var c = go.AddComponent<SphereCollider>(); c.radius = 0.075f;
            string pmPath = $"{MatDir}/Grenade_Bounce.physicMaterial";
            var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(pmPath);
            if (pm) c.sharedMaterial = pm;
            var rb = go.AddComponent<Rigidbody>(); rb.mass = 1.0f; rb.angularDamping = 1.2f;
            var s = go.AddComponent<Shell>();
            s.displayName = "No. 82 Gammon bomb"; s.fuse = 0f; s.directDamage = 0f; s.blastDamage = 300f; s.blastRadius = 7f; s.blastForce = 650f;
            s.maxLifetime = 8f;
            var vis = new GameObject(file + "_Pickup");
            if (!FbxInto(vis.transform, file)) Prim(PrimitiveType.Sphere, "Bag", vis.transform, Vector3.zero, Vector3.zero, new Vector3(0.15f, 0.13f, 0.15f), Mat("Gammon_Stockinette", new Color(0.55f, 0.50f, 0.38f)));
            s.pickupVisual = PrefabUtility.SaveAsPrefabAsset(vis, $"{PrefabDir}/{file}_Pickup.prefab");
            Object.DestroyImmediate(vis);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{file}.prefab");
            Object.DestroyImmediate(go);
            return prefab.GetComponent<Shell>();
        }

        static Shell BuildSmokeGrenade(string file, string display, float fuse, Color colour, float radius, float duration, Material smokeMat,
            System.Action<Transform> model, float colRadius, float colHeight)
        {
            var go = new GameObject(file);
            model(go.transform);
            var c = go.AddComponent<CapsuleCollider>(); c.radius = colRadius; c.height = colHeight; c.direction = 1;
            string pmPath = $"{MatDir}/Grenade_Bounce.physicMaterial";
            var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(pmPath);
            if (!pm) { pm = new PhysicsMaterial("Grenade_Bounce") { bounciness = 0.25f, dynamicFriction = 0.7f, staticFriction = 0.8f }; AssetDatabase.CreateAsset(pm, pmPath); }
            c.sharedMaterial = pm;
            var rb = go.AddComponent<Rigidbody>(); rb.mass = 0.55f; rb.angularDamping = 0.8f;
            var s = go.AddComponent<Shell>();
            s.displayName = display; s.fuse = fuse; s.directDamage = 0f; s.blastDamage = 0f; s.blastRadius = 0f; s.blastForce = 0f;
            s.smoke = true; s.smokeMaterial = smokeMat; s.smokeColour = colour; s.smokeRadius = radius; s.smokeDuration = duration;
            s.maxLifetime = fuse + 2f;
            // the same thing without physics, lying on the ground to be picked up
            var vis = new GameObject(file + "_Pickup");
            model(vis.transform);
            foreach (var col in vis.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
            s.pickupVisual = PrefabUtility.SaveAsPrefabAsset(vis, $"{PrefabDir}/{file}_Pickup.prefab");
            Object.DestroyImmediate(vis);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{file}.prefab");
            Object.DestroyImmediate(go);
            return prefab.GetComponent<Shell>();
        }

        /// <summary>A soft, lumpy puff (generated), on a transparent lit particle material so the moon and the flashes light it.</summary>
        static Material SmokeMaterial()
        {
            string texPath = $"{GenTexDir}/SmokePuff.png";
            if (!File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath), texPath)))
            {
                const int N = 256;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
                var px = new Color32[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                        float n = 0f, amp = 0.5f, f = 3f;
                        for (int o = 0; o < 5; o++) { n += amp * Mathf.PerlinNoise(x / (float)N * f + 17.3f * o, y / (float)N * f + 5.1f * o); amp *= 0.5f; f *= 2f; }
                        float r = Mathf.Sqrt(u * u + v * v) + (n - 0.5f) * 0.55f;
                        float a = Mathf.Clamp01(1f - r); a = a * a * (3f - 2f * a);
                        byte shade = (byte)Mathf.Lerp(200f, 255f, n);
                        px[y * N + x] = new Color32(shade, shade, shade, (byte)(a * 255f));
                    }
                tex.SetPixels32(px); tex.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), texPath), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(texPath);
                if (AssetImporter.GetAtPath(texPath) is TextureImporter ti) { ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = true; ti.SaveAndReimport(); }
            }
            string matPath = $"{MatDir}/Smoke_Particle.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            var shader = Shader.Find("Universal Render Pipeline/Particles/Simple Lit") ?? Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
            if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, matPath); }
            else m.shader = shader;
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            m.SetTexture("_BaseMap", t); m.mainTexture = t;
            m.SetColor("_BaseColor", Color.white);
            // transparent, alpha-blended, no depth write, lit from both sides
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_AlphaClip", 0f); m.SetFloat("_Cull", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f); m.SetFloat("_SoftParticlesEnabled", 1f); m.SetFloat("_SoftParticlesFarFadeDistance", 1.5f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.EnableKeyword("_SOFTPARTICLES_ON");
            // at night the moon alone barely lights it: a faint glow of its own keeps the cloud readable as pale grey
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(0.20f, 0.21f, 0.23f));
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Give the garrison its smoke: about a third of the riflemen (not the MG crews) carry a Nebelhandgranate.</summary>
        static void GiveGermansSmoke(System.Collections.Generic.IEnumerable<Health> men)
        {
            int i = 0;
            foreach (var h in men)
            {
                if (!h) continue;
                var ai = h.GetComponent<SoldierAI>();
                if (!ai || h.name.Contains("MG42")) continue;
                ai.smokeGrenade = smokeNb39; ai.smokeGrenades = (i++ % 3 == 0) ? 1 : 0;
            }
        }
    }
}
