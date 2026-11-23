using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Trees and hedges (to-do, 23 Nov): the procedural crowns (the far level of detail of every tree, the Proving
    /// Ground and Sainte-Mère-Église trees, the bocage hedges of Sainte-Mère-Église, the old hedges at the bridge and
    /// the château) were flat green balls, the trunks flat brown. No leaf or bark texture was among the downloads, so
    /// Claude drew them (Models/Source~/foliage_textures.py): Textures/Nature/Leaves_Canopy (layers of oak, beech and
    /// hawthorn leaves, deeper ones darker, tileable, with its normal map), Bark_Oak (plated and furrowed) and
    /// Bark_Poplar (smoother, grey, lichen). DressFoliage runs before each scene is saved: every renderer using one of
    /// the plain leaf or bark materials gets the textured one, tinted so the colour stays what it was.
    /// Report: Logs/foliage-&lt;level&gt;.txt.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        const string NatureDir = Root + "/Textures/Nature";

        // the plain materials and which texture dresses them (the downloaded tree models keep their own)
        static readonly Dictionary<string, string> FoliageSkins = new Dictionary<string, string>
        {
            { "Broadleaf_Leaves", "Leaves" }, { "Poplar_Leaves", "Leaves" }, { "Poplar_Leaves_Light", "Leaves" }, { "Orchard_Leaves", "Leaves" },
            { "SME_Lime_Leaves", "Leaves" }, { "SME_Apple_Leaves", "Leaves" }, { "Brush_Leaves", "Leaves" },
            { "SME_Hedge", "Leaves" }, { "SME_Hedge_2", "Leaves" }, { "SME_Hedge_3", "Leaves" }, { "Hedge", "Leaves" },
            { "Bark", "Bark_Oak" }, { "Brush_Bark", "Bark_Oak" }, { "Bark_Poplar", "Bark_Poplar" },
        };
        // the textures' mean colours (so the tint keeps each material's own colour)
        static readonly Dictionary<string, Color> FoliageMean = new Dictionary<string, Color>
        {
            { "Leaves", new Color(0.235f, 0.378f, 0.143f) }, { "Bark_Oak", new Color(0.301f, 0.262f, 0.219f) }, { "Bark_Poplar", new Color(0.409f, 0.394f, 0.354f) },
        };
        static readonly Dictionary<string, Material> foliageMats = new Dictionary<string, Material>();

        static Material FoliageMat(Material plain, string skin)
        {
            string key = plain.name + "_Textured";
            if (foliageMats.TryGetValue(key, out var made) && made) return made;
            string file = skin == "Leaves" ? "Leaves_Canopy" : skin;
            string tex = $"{NatureDir}/{file}.jpg", nrm = $"{NatureDir}/{file}_N.png";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(tex);
            if (!t) return null;
            if (AssetImporter.GetAtPath(nrm) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap)
            { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
            var n = AssetDatabase.LoadAssetAtPath<Texture2D>(nrm);
            Color c = plain.HasProperty("_BaseColor") ? plain.GetColor("_BaseColor") : plain.color, mean = FoliageMean[skin];
            var tint = new Color(Mathf.Clamp01(c.r / mean.r), Mathf.Clamp01(c.g / mean.g), Mathf.Clamp01(c.b / mean.b), 1f);
            var m = Mat(key, tint, skin == "Leaves" ? 0.12f : 0.05f);
            m.SetTexture("_BaseMap", t); m.mainTexture = t;
            if (n) { m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", skin == "Leaves" ? 1.2f : 1f); m.EnableKeyword("_NORMALMAP"); }
            // a crown ball is ~10 m round: leaves of hand size; a trunk 2-3 m round, a few metres of bark up it
            var tiling = skin == "Leaves" ? new Vector2(6f, 3f) : new Vector2(1f, 4f);
            m.SetTextureScale("_BaseMap", tiling); m.mainTextureScale = tiling;
            EditorUtility.SetDirty(m);
            foliageMats[key] = m;
            return m;
        }

        static void DressFoliage(Transform root, string level)
        {
            foliageMats.Clear();
            int swapped = 0; var counts = new Dictionary<string, int>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < ms.Length; i++)
                {
                    if (!ms[i]) continue;
                    string n = ms[i].name.Replace(" (Instance)", "");
                    if (!FoliageSkins.TryGetValue(n, out var skin)) continue;
                    var tm = FoliageMat(ms[i], skin);
                    if (!tm) continue;
                    ms[i] = tm; changed = true;
                    counts[n] = counts.TryGetValue(n, out var k) ? k + 1 : 1;
                }
                if (changed) { r.sharedMaterials = ms; swapped++; }
            }
            var report = new StringBuilder($"Foliage - {level} - {System.DateTime.Now:yyyy-MM-dd HH:mm}\n{swapped} renderers given leaf or bark textures\n");
            foreach (var kv in counts) report.AppendLine($"  {kv.Key}: {kv.Value}");
            if (AssetDatabase.LoadAssetAtPath<Texture2D>($"{NatureDir}/Leaves_Canopy.jpg") == null) report.AppendLine("  (Textures/Nature missing: nothing changed)");
            try
            {
                string logs = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Logs");
                System.IO.Directory.CreateDirectory(logs);
                System.IO.File.WriteAllText(System.IO.Path.Combine(logs, $"foliage-{level}.txt"), report.ToString());
            }
            catch (System.Exception) { }
        }
    }
}
