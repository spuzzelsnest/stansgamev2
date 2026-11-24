using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// The last pass of every level build (23 Nov): the buildings.
    /// 1. Texture: the houses, walls and roofs are built from Unity cubes in a flat colour (SME_Wall_*, SME_Slate,
    ///    House_Render, Roof_Slate, Park_Wall_Stone...). Each such cube gets a copy of the cube mesh whose UVs run in
    ///    metres along the wall (courses level on a turned gable, slate rows along the eaves), and a textured copy of
    ///    its material ("..._Tex") tinted to keep the old colour: granite rubble and ashlar from the church, the slate of
    ///    the church roof, the village limestone and rubble, and a lime render (Textures/Village/Village_Render).
    /// 2. Ground: a house's "Walls" whose foot stands above the ground anywhere under it gets a foundation down into the
    ///    ground; a modelled building (an FBX) that floats or sinks is listed.
    /// Report: Logs/buildings-&lt;level&gt;.txt.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        const string VillTex = Root + "/Textures/Village/";
        const string GenTex = Root + "/Textures/Generated/";

        /// <summary>A flat building material and its skin: texture, normal map, metres per tile, the texture's average
        /// colour (to tint it back to the material's own colour). tex null = keep the material's own texture.</summary>
        struct Skin { public string tex, nrm; public float metres; public Color mean; }

        static Skin S(string tex, string nrm, float metres, float r, float g, float b) => new Skin { tex = tex, nrm = nrm, metres = metres, mean = new Color(r, g, b) };

        static readonly Dictionary<string, Skin> BuildingSkins = new Dictionary<string, Skin>
        {
            // Sainte-Mere-Eglise: Cotentin granite rubble and dressed stone as on the church, a lime render, slate roofs
            { "SME_Wall_Granite", S(GenTex + "SME_Rubble_d.png", GenTex + "SME_Rubble_n.png", 4f, 0.501f, 0.482f, 0.444f) },
            { "SME_Church_Stone", S(GenTex + "SME_Rubble_d.png", GenTex + "SME_Rubble_n.png", 4f, 0.501f, 0.482f, 0.444f) },
            { "SME_Wall_Grey", S(VillTex + "Village_Rubble.jpg", VillTex + "Village_Rubble_N.jpg", 2f, 0.367f, 0.344f, 0.279f) },
            { "SME_Wall_Limestone", S(GenTex + "SME_Ashlar_d.png", GenTex + "SME_Ashlar_n.png", 2f, 0.686f, 0.664f, 0.607f) },
            { "SME_Wall_Render", S(VillTex + "Village_Render.jpg", VillTex + "Village_Render_N.jpg", 3f, 0.725f, 0.689f, 0.617f) },
            { "SME_Slate", S(GenTex + "SME_Slate_d.png", GenTex + "SME_Slate_n.png", 2f, 0.189f, 0.208f, 0.245f) },
            { "SME_Roof_Tile", S(VillTex + "Village_RoofTile.jpg", VillTex + "Village_RoofTile_N.jpg", 2.5f, 0.553f, 0.320f, 0.240f) },
            { "Roof_Tile", S(VillTex + "Village_RoofTile.jpg", VillTex + "Village_RoofTile_N.jpg", 2.5f, 0.553f, 0.320f, 0.240f) },
            // Pegasus Bridge and Benouville: rendered houses, slate roofs, the limestone park walls of the chateau
            { "House_Render", S(VillTex + "Village_Render.jpg", VillTex + "Village_Render_N.jpg", 3f, 0.725f, 0.689f, 0.617f) },
            { "Roof_Slate", S(GenTex + "SME_Slate_d.png", GenTex + "SME_Slate_n.png", 2f, 0.189f, 0.208f, 0.245f) },
            { "Park_Wall_Stone", S(VillTex + "Village_Limestone.jpg", VillTex + "Village_Limestone_N.jpg", 2.5f, 0.483f, 0.448f, 0.416f) },
            // Benouville village: already textured, but stretched over each cube - laid on in metres instead
            { "Village_Render", S(null, null, 2.5f, 1f, 1f, 1f) },
            { "Village_Stone", S(null, null, 2.5f, 1f, 1f, 1f) },
            { "Village_Slate", S(null, null, 2f, 1f, 1f, 1f) },
        };

        static Mesh builtinCube;
        static readonly Dictionary<string, Mesh> metricCubes = new Dictionary<string, Mesh>();
        static readonly Dictionary<Material, Material> skinned = new Dictionary<Material, Material>();

        /// <summary>The textured copy of a flat building material.</summary>
        static Material SkinFor(Material orig, Skin s)
        {
            if (skinned.TryGetValue(orig, out var done)) return done;
            Color c = orig.HasProperty("_BaseColor") ? orig.GetColor("_BaseColor") : orig.color;
            float smooth = orig.HasProperty("_Smoothness") ? orig.GetFloat("_Smoothness") : 0.1f;
            Material m;
            if (s.tex == null)
            {
                // the material's own texture: only the tiling changes (the UVs are now in metres)
                m = Mat(orig.name + "_Tex", c, smooth);
                var t = orig.GetTexture("_BaseMap"); if (t) { m.SetTexture("_BaseMap", t); m.mainTexture = t; }
                if (orig.HasProperty("_BumpMap") && orig.GetTexture("_BumpMap")) { m.SetTexture("_BumpMap", orig.GetTexture("_BumpMap")); m.EnableKeyword("_NORMALMAP"); }
            }
            else
            {
                var tint = new Color(Mathf.Clamp(c.r / s.mean.r, 0.55f, 1.45f), Mathf.Clamp(c.g / s.mean.g, 0.55f, 1.45f), Mathf.Clamp(c.b / s.mean.b, 0.55f, 1.45f), 1f);
                if (AssetImporter.GetAtPath(s.tex) is TextureImporter ti && ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; ti.SaveAndReimport(); }
                m = TexMat(orig.name + "_Tex", s.tex, tint, smooth, false, s.nrm);
            }
            var scale = Vector2.one / s.metres;
            m.SetTextureScale("_BaseMap", scale); m.mainTextureScale = scale;
            EditorUtility.SetDirty(m);
            skinned[orig] = m;
            return m;
        }

        /// <summary>
        /// A unit cube whose UVs are in metres for an object of this scale and rotation (in its house's frame): on each
        /// face u runs level along the face and v up it (on a roof: up the slope), so courses and slate rows lie level.
        /// </summary>
        static Mesh MetricCube(Vector3 s, Quaternion r, string container)
        {
            var e = r.eulerAngles;
            string key = $"MC_{s.x:0.00}_{s.y:0.00}_{s.z:0.00}_{Mathf.Round(e.x) % 360}_{Mathf.Round(e.y) % 360}_{Mathf.Round(e.z) % 360}".Replace(',', '.');
            if (metricCubes.TryGetValue(key, out var made) && made) return made;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var ax in axes)
            {
                // two in-face axes of the unit cube, ordered so (a x b) points out
                Vector3 a = Mathf.Abs(ax.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 b = Vector3.Cross(ax, a);
                if (Vector3.Dot(Vector3.Cross(a, b), ax) < 0f) b = -b;
                // the face's normal and its level / up directions in the house's frame
                Vector3 np = (r * new Vector3(ax.x / s.x, ax.y / s.y, ax.z / s.z)).normalized;
                Vector3 u = Mathf.Abs(np.y) < 0.9f ? Vector3.Cross(Vector3.up, np).normalized : (r * Vector3.right).normalized;
                Vector3 w = Vector3.Cross(np, u).normalized;
                int i0 = v.Count;
                foreach (var (sa, sb) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                {
                    Vector3 p = ax * 0.5f + a * (0.5f * sa) + b * (0.5f * sb);
                    Vector3 q = r * Vector3.Scale(p, s);
                    v.Add(p); n.Add(ax); uv.Add(new Vector2(Vector3.Dot(q, u), Vector3.Dot(q, w)));
                }
                tri.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });
            }
            var m = new Mesh { name = key };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            // the winding must face out. Unity draws the side whose triangle runs clockwise as seen from the camera,
            // so the right-hand cross product of a front face points INTO the cube (printscreen 38: the first version
            // had it the other way round, every block inside out - walls and roofs seen through from outside)
            Vector3 tn = Vector3.Cross(v[tri[1]] - v[tri[0]], v[tri[2]] - v[tri[0]]);
            if (Vector3.Dot(tn, n[tri[0]]) > 0f)
            {
                for (int i = 0; i < tri.Count; i += 3) { int t = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = t; }
                m.SetTriangles(tri, 0);
            }
            m.RecalculateBounds(); m.RecalculateTangents();
            AssetDatabase.AddObjectToAsset(m, container);
            metricCubes[key] = m;
            return m;
        }

        static void DressBuildings(Transform root, string level)
        {
            if (!builtinCube) builtinCube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            metricCubes.Clear(); skinned.Clear();
            string dir = Root + "/Models/Generated";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Root + "/Models", "Generated");
            string container = $"{dir}/BuildingCubes_{level}.asset";
            AssetDatabase.DeleteAsset(container);
            var holder = new Mesh { name = "BuildingCubes" };   // the container's main asset
            AssetDatabase.CreateAsset(holder, container);

            var report = new StringBuilder();
            report.AppendLine($"Buildings - {level} - {System.DateTime.Now:yyyy-MM-dd HH:mm}");
            var counts = new Dictionary<string, int>();
            int dressed = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!IsBuiltinCube(mf.sharedMesh)) continue;
                var mr = mf.GetComponent<MeshRenderer>(); if (!mr || !mr.sharedMaterial) continue;
                if (!BuildingSkins.TryGetValue(mr.sharedMaterial.name, out var skin)) continue;
                var t = mf.transform;
                Vector3 s = t.lossyScale; s = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
                // the rotation in its house's frame (a house turns only about the vertical, so "up" is kept)
                var house = t.parent ? t.parent : t;
                Quaternion r = Quaternion.Inverse(house.rotation) * t.rotation;
                mf.sharedMesh = MetricCube(s, r, container);
                string key = mr.sharedMaterial.name;
                mr.sharedMaterial = SkinFor(mr.sharedMaterial, skin);
                counts[key] = counts.TryGetValue(key, out var k) ? k + 1 : 1; dressed++;
            }
            report.AppendLine($"\nTextured in metres: {dressed} wall, roof and chimney blocks");
            foreach (var kv in counts.OrderByDescending(kv => kv.Value)) report.AppendLine($"  {kv.Key}: {kv.Value}");

            // ---- on the ground
            int pads = CutHousePads(root);
            report.AppendLine($"\nTerrain cut down to the floor round {pads} houses dug into a slope");
            int footings = 0; var floating = new List<string>(); var sunk = new List<string>();
            Physics.SyncTransforms();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!IsWallBlock(t)) continue;
                var mf = t.GetComponent<MeshFilter>(); if (!mf) continue;
                var bottom = new List<Vector3>();
                for (int ix = -1; ix <= 1; ix++)
                    for (int iz = -1; iz <= 1; iz++) bottom.Add(t.TransformPoint(new Vector3(ix * 0.5f, -0.5f, iz * 0.5f)));
                float foot = bottom.Min(p => p.y), lowest = float.MaxValue, highest = float.MinValue;
                foreach (var p in bottom) { float g = GroundAt(p); lowest = Mathf.Min(lowest, g); highest = Mathf.Max(highest, TerrainY(p, g)); }
                if (lowest < foot - 0.03f)
                {
                    // carry the walls down into the ground: a footing of the same stone under the whole house
                    float depth = foot - (lowest - 0.6f);
                    var f = Object.Instantiate(t.gameObject, t.parent);
                    f.name = "Footing (to the ground)";
                    foreach (Transform c in f.transform) Object.DestroyImmediate(c.gameObject);
                    var ls = t.localScale; f.transform.localScale = new Vector3(ls.x, depth, ls.z);
                    f.transform.position = t.TransformPoint(new Vector3(0f, -0.5f, 0f)) - t.up * (depth / 2f - 0.05f);
                    var fm = f.GetComponent<MeshFilter>();
                    if (fm && fm.sharedMesh && fm.sharedMesh.name.StartsWith("MC_"))
                    {
                        var s = f.transform.lossyScale; s = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
                        var house = t.parent ? t.parent : t;
                        fm.sharedMesh = MetricCube(s, Quaternion.Inverse(house.rotation) * f.transform.rotation, container);
                    }
                    footings++;
                }
                // a door more than 0.6 m under the ground somewhere along the house
                float sill = Mathf.Max(t.parent ? t.parent.position.y : foot, lowest);
                if (highest > sill + 0.6f && sunk.Count < 60) sunk.Add($"{BldPath(t.parent ? t.parent : t)}: ground {highest - sill:0.0} m above its floor on the uphill side");
            }
            // modelled buildings (FBX): their lowest point against the ground under them
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.parent == null || t.GetComponent<MeshRenderer>() == null && t.GetComponent<SkinnedMeshRenderer>() == null) continue;
                var mr = t.GetComponent<Renderer>();
                if (!(mr.sharedMaterials.Any(m => m && (m.name.Contains("Exterior") || m.name.Contains("Cottage") || m.name.Contains("Gondree") || m.name.Contains("Cafe") || m.name.Contains("Chateau_Facade"))))) continue;
                var b = mr.bounds;
                float g = float.MaxValue;
                foreach (var p in new[] { new Vector3(b.min.x, 0f, b.min.z), new Vector3(b.max.x, 0f, b.min.z), new Vector3(b.min.x, 0f, b.max.z), new Vector3(b.max.x, 0f, b.max.z) }) g = Mathf.Min(g, GroundAt(p));
                if (b.min.y > g + 0.1f)
                {
                    // settled: down onto the lowest ground under it (a cottage sits into its plot on the uphill side)
                    floating.Add($"{BldPath(t)}: was {b.min.y - g:0.00} m above the lowest ground under it - lowered");
                    t.position += Vector3.down * (b.min.y - g + 0.05f);
                }
            }
            // ---- nothing inside a house, and nobody walks into one (level template 3b; printscreen 50: bushes inside a
            // house at Pegasus Bridge, seen through its walls from inside)
            var inside = KeepHousesClear(root);
            report.AppendLine($"\nInside houses: {inside}");
            report.AppendLine($"\nFootings added under walls that stood above the ground: {footings}");
            report.AppendLine($"\nModelled buildings that stood above the ground at a corner ({floating.Count}):");
            foreach (var s in floating) report.AppendLine("  - " + s);
            report.AppendLine($"\nHouses dug into a slope (ground over 0.6 m above the floor on one side) ({sunk.Count}):");
            foreach (var s in sunk) report.AppendLine("  - " + s);
            AssetDatabase.SaveAssets();
            try
            {
                string logs = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Logs");
                System.IO.Directory.CreateDirectory(logs);
                System.IO.File.WriteAllText(System.IO.Path.Combine(logs, $"buildings-{level}.txt"), report.ToString());
            }
            catch (System.Exception ex) { Debug.LogWarning("Stan's Game: buildings report not written: " + ex.Message); }
            Debug.Log($"Stan's Game: {level} buildings - {dressed} blocks textured, {footings} footings, {floating.Count} modelled buildings above the ground (Logs/buildings-{level}.txt)");
        }

        /// <summary>
        /// Template rule 3b: no tree, bush, hedge or brushwood inside a house, and every house solid to walk against.
        /// A house is its "Walls" block (any rotation) or a modelled building (by its facade materials). Foliage whose
        /// centre or foot is inside one is removed, terrain trees in one too; walls without a collider get one.
        /// </summary>
        /// <summary>A house's walls: "Walls", "Walls (brick)" (the café), or the café's "South block".</summary>
        static bool IsWallBlock(Transform t) => t.name == "Walls" || t.name.StartsWith("Walls (") || t.name == "South block";

        static string KeepHousesClear(Transform root)
        {
            Physics.SyncTransforms();
            var shells = new List<Transform>(); var models = new List<Bounds>();
            int colliders = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (IsWallBlock(t) && t.GetComponent<MeshFilter>())
                {
                    shells.Add(t);
                    if (!t.GetComponent<Collider>()) { t.gameObject.AddComponent<BoxCollider>(); colliders++; }
                    continue;
                }
                var mr = t.GetComponent<MeshRenderer>(); if (!mr) continue;
                if (mr.sharedMaterials.Any(m => m && (m.name.Contains("Exterior") || m.name.Contains("Cottage") || m.name.Contains("Gondree") || m.name.Contains("Cafe") || m.name.Contains("Chateau_Facade") || m.name.StartsWith("House_Render"))))
                {
                    var b = mr.bounds; if (b.size.x < 2.5f || b.size.z < 2.5f || b.size.y < 2f) continue;   // not a shutter or a sill
                    b.Expand(new Vector3(-0.6f, 0f, -0.6f));
                    models.Add(b);
                    if (!t.GetComponentInParent<Collider>() && !t.GetComponent<Collider>()) { var mf = t.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh) { t.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh; colliders++; } }
                }
            }
            bool Inside(Vector3 p)
            {
                foreach (var w in shells)
                {
                    var l = w.InverseTransformPoint(p);
                    if (Mathf.Abs(l.x) < 0.47f && Mathf.Abs(l.z) < 0.47f && l.y > -0.5f && l.y < 0.5f) return true;
                }
                foreach (var b in models) if (b.Contains(p)) return true;
                return false;
            }
            static bool Foliage(Renderer r) => r.sharedMaterials.Any(m => m && (m.name.Contains("Leaves") || m.name.Contains("Bark") || m.name.Contains("Hedge") || m.name.Contains("Foliage")))
                                           || r.name.Contains("Bush") || r.name.Contains("Hedge") || r.name.Contains("Shrub") || r.name.Contains("Brush");
            var gone = new HashSet<GameObject>(); var where = new List<string>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r || gone.Contains(r.gameObject) || !Foliage(r)) continue;
                var b = r.bounds;
                if (!Inside(b.center) && !Inside(new Vector3(b.center.x, b.min.y + 0.3f, b.center.z))) continue;
                // the whole bush or tree, not only the leaf ball that is in the house
                var go = r.gameObject;
                var up = go.transform.parent;
                if (up && up != root && up.GetComponentsInChildren<Renderer>(true).All(Foliage) && up.GetComponentsInChildren<Renderer>(true).Length <= 8) go = up.gameObject;
                if (where.Count < 40) where.Add($"{BldPath(go.transform)} at ({b.center.x:0}, {b.center.z:0})");
                gone.Add(go);
            }
            foreach (var go in gone) if (go) Object.DestroyImmediate(go);
            int trees = 0;
            foreach (var terrain in root.GetComponentsInChildren<Terrain>(true))
            {
                var td = terrain.terrainData; if (!td || td.treeInstanceCount == 0) continue;
                var keep = new List<TreeInstance>();
                foreach (var ti in td.treeInstances)
                {
                    var wp = terrain.transform.position + Vector3.Scale(ti.position, td.size);
                    if (Inside(wp + Vector3.up * 0.5f)) { trees++; continue; }
                    keep.Add(ti);
                }
                if (trees > 0) td.treeInstances = keep.ToArray();
            }
            var sb = new StringBuilder($"{shells.Count} houses and {models.Count} modelled building blocks checked; removed {gone.Count} bushes/trees/hedges and {trees} terrain trees from inside; {colliders} walls given a collider");
            foreach (var w in where) sb.Append("\n  - removed ").Append(w);
            return sb.ToString();
        }

        /// <summary>
        /// Where a house is dug into a slope, the terrain is cut down to its floor all round it (0.5 m clear, blended
        /// back to the slope over 2 m), so no door or ground-floor window is under the ground. Only ever lowers.
        /// </summary>
        static int CutHousePads(Transform root)
        {
            var terrains = Terrain.activeTerrains;
            if (terrains.Length == 0) return 0;
            int cut = 0;
            var data = new Dictionary<Terrain, float[,]>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "Walls" || !t.parent || !t.GetComponent<MeshFilter>()) continue;
                var house = t.parent;
                // the floor: the house's own level, or the lowest ground at its corners if that is higher (Pegasus
                // Bridge sets its houses 0.3 m into the ground) - so nothing is dug below the low side
                float low = float.MaxValue;
                foreach (var q in new[] { new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f) })
                    low = Mathf.Min(low, GroundAt(t.TransformPoint(q)));
                float floor = Mathf.Max(house.position.y, low) - 0.03f;
                Vector3 c = t.position; Vector3 ax = t.right, az = t.forward;
                float hx = t.lossyScale.x * 0.5f, hz = t.lossyScale.z * 0.5f;
                const float clear = 0.5f, blend = 2f;
                float reach = Mathf.Sqrt(hx * hx + hz * hz) + clear + blend;
                bool changed = false;
                foreach (var ter in terrains)
                {
                    var td = ter.terrainData; var tp = ter.transform.position; int res = td.heightmapResolution;
                    if (!data.TryGetValue(ter, out var h)) { h = td.GetHeights(0, 0, res, res); data[ter] = h; }
                    float cx = (c.x - tp.x) / td.size.x * (res - 1), cz = (c.z - tp.z) / td.size.z * (res - 1);
                    int rx = Mathf.CeilToInt(reach / td.size.x * (res - 1)) + 1, rz = Mathf.CeilToInt(reach / td.size.z * (res - 1)) + 1;
                    for (int iz = Mathf.Max(0, (int)cz - rz); iz <= Mathf.Min(res - 1, (int)cz + rz); iz++)
                        for (int ix = Mathf.Max(0, (int)cx - rx); ix <= Mathf.Min(res - 1, (int)cx + rx); ix++)
                        {
                            var w = new Vector3(tp.x + ix / (float)(res - 1) * td.size.x, 0f, tp.z + iz / (float)(res - 1) * td.size.z) - c;
                            float dx = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(w, ax)) - hx), dz = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(w, az)) - hz);
                            float d = Mathf.Sqrt(dx * dx + dz * dz);
                            if (d > clear + blend) continue;
                            float y = tp.y + h[iz, ix] * td.size.y;
                            if (y <= floor) continue;
                            float k = d <= clear ? 0f : (d - clear) / blend;
                            float ny = Mathf.Lerp(floor, y, k * k * (3f - 2f * k));
                            if (ny < y - 0.005f) { h[iz, ix] = (ny - tp.y) / td.size.y; changed = true; }
                        }
                }
                if (changed) cut++;
            }
            foreach (var kv in data) { kv.Key.terrainData.SetHeights(0, 0, kv.Value); EditorUtility.SetDirty(kv.Key.terrainData); }
            return cut;
        }

        static float TerrainY(Vector3 p, float fallback)
        {
            foreach (var ter in Terrain.activeTerrains)
            {
                var td = ter.terrainData; var tp = ter.transform.position;
                float u = (p.x - tp.x) / td.size.x, v = (p.z - tp.z) / td.size.z;
                if (u >= 0f && u <= 1f && v >= 0f && v <= 1f) return td.GetInterpolatedHeight(u, v) + tp.y;
            }
            return fallback;
        }

        static bool IsBuiltinCube(Mesh m)
            => m && (m == builtinCube || m.name == "Cube" && AssetDatabase.GetAssetPath(m).Contains("unity default resources"));

        static string BldPath(Transform t)
        {
            var parts = new List<string>();
            for (var p = t; p != null && parts.Count < 4; p = p.parent) parts.Insert(0, p.name);
            return string.Join("/", parts);
        }
    }
}
