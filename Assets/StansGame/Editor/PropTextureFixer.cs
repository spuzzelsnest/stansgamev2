using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Some models (the Blender props, the downloaded weapons) carry their textures inside the FBX (embedded media).
    /// Unity imports their materials plain white until the textures are extracted AND the materials point at them.
    /// Extracting alone was not enough: the textures went into Textures/&lt;model&gt;, where the model's material
    /// search does not look (Joan's printscreens 09-11: white rifles, tyres, crates, buckets, ladder, wheelbarrow).
    /// So now each white material is taken out of the model as a .mat of its own and given its texture by name
    /// (colour map, and the normal map where there is one). Runs at the start of every level build, and from the menu.
    /// </summary>
    public static class PropTextureFixer
    {
        static readonly string[] Folders = { "Assets/StansGame/Models" };

        [MenuItem("Stan's Game/Tools/Extract Embedded Prop Textures", priority = 70)]
        static void Menu() => Debug.Log($"Stan's Game: textures given to the materials of {Fix()} models");

        public static int Fix()
        {
            int n = 0;
            var folders = System.Array.FindAll(Folders, AssetDatabase.IsValidFolder);
            if (folders.Length == 0) return 0;
            // materials taken out of a model earlier with a built-in shader: they draw pink in URP (the rifles, 22 Nov)
            foreach (var g in AssetDatabase.FindAssets("t:Material", folders))
            {
                string mp = AssetDatabase.GUIDToAssetPath(g);
                if (!mp.Contains("/Textures/") || !mp.Contains("/Materials/")) continue;
                var em = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (!em || (em.shader && em.shader.name.StartsWith("Universal Render Pipeline"))) continue;
                var tex = em.mainTexture; var bump = em.HasProperty("_BumpMap") ? em.GetTexture("_BumpMap") : null;
                em.shader = Shader.Find("Universal Render Pipeline/Lit");
                if (tex) { em.SetTexture("_BaseMap", tex); em.mainTexture = tex; em.SetColor("_BaseColor", Color.white); }
                if (bump) { em.SetTexture("_BumpMap", bump); em.EnableKeyword("_NORMALMAP"); }
                EditorUtility.SetDirty(em);
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Model", folders))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) || path.Contains("/Source~/")) continue;
                if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) continue;
                var white = new List<Material>();
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is Material m && !m.mainTexture) white.Add(m);
                if (white.Count == 0) continue;
                string dir = Path.GetDirectoryName(path).Replace('\\', '/') + "/Textures/" + Path.GetFileNameWithoutExtension(path);
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    Directory.CreateDirectory(dir);
                    AssetDatabase.Refresh();
                    if (!imp.ExtractTextures(dir)) continue;
                    AssetDatabase.Refresh();
                }
                var texs = new List<Texture2D>();
                foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { dir })) { var t = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g)); if (t) texs.Add(t); }
                if (texs.Count == 0) continue;
                string matDir = dir + "/Materials";
                bool changed = false;
                foreach (var m in white)
                {
                    var colour = Pick(m.name, texs, false, white.Count == 1);
                    if (!colour) continue;
                    var normal = Pick(m.name, texs, true, white.Count == 1);
                    if (!AssetDatabase.IsValidFolder(matDir)) { Directory.CreateDirectory(matDir); AssetDatabase.Refresh(); }
                    string mp = $"{matDir}/{Safe(m.name)}.mat";
                    if (!File.Exists(mp))
                    {
                        string err = AssetDatabase.ExtractAsset(m, mp);
                        if (!string.IsNullOrEmpty(err)) { Debug.LogWarning($"Stan's Game: {path}: {m.name}: {err}"); continue; }
                    }
                    var em = AssetDatabase.LoadAssetAtPath<Material>(mp);
                    if (!em) continue;
                    // a model imported with a built-in shader (Standard, Autodesk Interactive) draws pink in URP once it
                    // has a material of its own: put it on URP Lit
                    if (!em.shader || !em.shader.name.StartsWith("Universal Render Pipeline")) em.shader = Shader.Find("Universal Render Pipeline/Lit");
                    if (em.HasProperty("_BaseMap")) em.SetTexture("_BaseMap", colour);
                    em.mainTexture = colour;
                    if (em.HasProperty("_BaseColor")) em.SetColor("_BaseColor", Color.white);
                    if (normal && em.HasProperty("_BumpMap"))
                    {
                        if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(normal)) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap)
                        { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
                        em.SetTexture("_BumpMap", normal); em.EnableKeyword("_NORMALMAP");
                    }
                    EditorUtility.SetDirty(em);
                    changed = true;
                }
                if (changed) { AssetDatabase.SaveAssets(); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); n++; }
            }
            RepairPrefabs();
            return n;
        }

        /// <summary>
        /// Prefabs made from a model before its materials were taken out still point at the old (now gone) built-in
        /// materials: their slots are empty and draw magenta (22 Nov: the paratrooper's rifle). Give each empty slot
        /// the material the model now has on the same mesh.
        /// </summary>
        public static int RepairPrefabs()
        {
            int fixedSlots = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/StansGame" }))
            {
                string pp = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(pp);
                bool changed = false;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    if (System.Array.TrueForAll(mats, m => m)) continue;
                    Mesh mesh = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.GetComponent<MeshFilter>() ? r.GetComponent<MeshFilter>().sharedMesh : null;
                    if (!mesh) continue;
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(mesh));
                    if (!model) continue;
                    Material[] src = null;
                    foreach (var mr in model.GetComponentsInChildren<Renderer>(true))
                    {
                        Mesh m2 = mr is SkinnedMeshRenderer s2 ? s2.sharedMesh : mr.GetComponent<MeshFilter>() ? mr.GetComponent<MeshFilter>().sharedMesh : null;
                        if (m2 == mesh) { src = mr.sharedMaterials; break; }
                    }
                    if (src == null) continue;
                    for (int i = 0; i < mats.Length && i < src.Length; i++) if (!mats[i] && src[i]) { mats[i] = src[i]; fixedSlots++; changed = true; }
                    r.sharedMaterials = mats;
                }
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, pp);
                PrefabUtility.UnloadPrefabContents(root);
            }
            Debug.Log($"Stan's Game: {fixedSlots} empty material slots in prefabs filled from their models");
            return fixedSlots;
        }

        static string Safe(string s) { foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s; }

        static readonly string[] NormalWords = { "normal", "_n.", "_n_", "nrm", "_nor" };
        static readonly string[] OtherWords = { "rough", "metal", "_ao", "ambient", "spec", "height", "disp", "emis", "gloss", "opacity", "alpha", "mask" };
        static readonly string[] ColourWords = { "albedo", "diff", "base", "col", "_d.", "_d_", "_c." };

        /// <summary>The texture for a material: by the name they share, the colour map or the normal map.</summary>
        static Texture2D Pick(string material, List<Texture2D> texs, bool normal, bool only)
        {
            string mat = Key(material);
            Texture2D best = null; int bestScore = -1;
            foreach (var t in texs)
            {
                string name = t.name.ToLower() + ".";
                bool isNormal = System.Array.Exists(NormalWords, w => name.Contains(w));
                bool isOther = System.Array.Exists(OtherWords, w => name.Contains(w));
                if (normal != isNormal || (!normal && isOther)) continue;
                int score = Common(mat, Key(t.name)) * 4;
                if (!normal && System.Array.Exists(ColourWords, w => name.Contains(w))) score += 3;
                if (score > bestScore) { bestScore = score; best = t; }
            }
            // a model with several white materials: only take a texture that shares a name with the material
            if (!only && best && Common(mat, Key(best.name)) < 3) return null;
            return best;
        }

        static string Key(string s) { var b = new System.Text.StringBuilder(); foreach (char c in s.ToLower()) if (char.IsLetterOrDigit(c)) b.Append(c); return b.ToString(); }
        static int Common(string a, string b) { int i = 0; while (i < a.Length && i < b.Length && a[i] == b[i]) i++; return i; }
    }
}
