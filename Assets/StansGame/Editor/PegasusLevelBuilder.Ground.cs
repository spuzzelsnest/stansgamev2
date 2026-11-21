using System.IO;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Ground textures (Poly Haven scans, 2K): where each one goes is worked out from the level's own data — the
    /// roads and canal-bank roads, the LiDAR ground (slopes, low wet ground), the river and pond, the German
    /// positions, the glider skid tracks, the gardens — following the June 1944 photographs:
    ///   0 Pasture   rough tussocky grass (the fields and the landing zone)
    ///   1 Turf      short green turf with stones (patches in the fields, lawns and gardens)
    ///   2 Towpath   pale packed gravel (the tree-lined roads along both canal banks)
    ///   3 Gravel    grey crushed stone (the main roads, the bridge approaches, the quay edges)
    ///   4 Mud       wet earth (Orne banks, pond, ditches, low marsh)
    ///   5 Trampled  bare earth (trenches, gun pits, pillbox, MG nests, glider skid marks, steep banks)
    /// Plus the canal and Orne water shader and the box bushes in the gardens.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        const string GroundDir = Root + "/Textures/Ground";
        const string BuxusDir = Root + "/Models/Props/Buxus";

        static readonly (string name, float tile, float smoothMax)[] GroundSpec =
        {
            ("Pasture", 3.5f, 0.25f), ("Turf", 4.5f, 0.25f), ("Towpath", 3f, 0.35f),
            ("Gravel", 2.6f, 0.4f), ("Mud", 3f, 0.75f), ("Trampled", 3f, 0.35f),
        };

        static Texture2D GroundTex(string file, TextureImporterType type, bool srgb)
        {
            string path = $"{GroundDir}/{file}";
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                bool dirty = ti.textureType != type || ti.sRGBTexture != srgb || ti.maxTextureSize != 2048 || ti.wrapMode != TextureWrapMode.Repeat;
                if (dirty)
                {
                    ti.textureType = type; ti.sRGBTexture = srgb; ti.maxTextureSize = 2048; ti.wrapMode = TextureWrapMode.Repeat;
                    ti.mipmapEnabled = true; ti.anisoLevel = 8;
                    ti.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>The six ground layers, or null when the textures are not in the project (the old flat layers are used).</summary>
        static TerrainLayer[] GroundLayers()
        {
            if (!File.Exists($"{GroundDir}/Ground_Pasture_Albedo.jpg")) return null;
            var layers = new TerrainLayer[GroundSpec.Length];
            for (int i = 0; i < GroundSpec.Length; i++)
            {
                var (n, tile, smooth) = GroundSpec[i];
                var l = new TerrainLayer
                {
                    diffuseTexture = GroundTex($"Ground_{n}_Albedo.jpg", TextureImporterType.Default, true),
                    normalMapTexture = GroundTex($"Ground_{n}_Normal.png", TextureImporterType.NormalMap, false),
                    maskMapTexture = GroundTex($"Ground_{n}_Mask.png", TextureImporterType.Default, false),
                    tileSize = new Vector2(tile, tile),
                    normalScale = 1f,
                    maskMapRemapMin = new Vector4(0f, 0f, 0f, 0f),
                    maskMapRemapMax = new Vector4(0f, 1f, 1f, smooth),
                };
                // the mud is darker and wetter than the dry scan
                if (n == "Mud") l.diffuseRemapMax = new Vector4(0.62f, 0.58f, 0.52f, 1f);
                // June grass: the dry pasture scan pulled towards green, the turf a touch fresher
                if (n == "Pasture") l.diffuseRemapMax = new Vector4(0.78f, 1.0f, 0.6f, 1f);
                if (n == "Turf") l.diffuseRemapMax = new Vector4(0.9f, 1.05f, 0.85f, 1f);
                string p = $"{MatDir}/Ground_{n}.terrainlayer";
                AssetDatabase.DeleteAsset(p);
                AssetDatabase.CreateAsset(l, p);
                layers[i] = l;
            }
            return layers;
        }

        static float Smooth01(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        /// <summary>Paints the six layers; returns the alphamap so the grass can be thinned on bare ground.</summary>
        static float[,,] PaintGround(int amRes, float size, Seg[] segs, float[,] h, int res, float height, float baseY)
        {
            var am = new float[amRes, amRes, 6];
            // German positions and other worn ground: (x, z, radius)
            var worn = new[]
            {
                new Vector3(CanalHalfWidth + 12f, 14f, 9f),     // MG pillbox
                new Vector3(CanalHalfWidth + 10f, -10f, 7f),    // AT gun pit
                new Vector3(CanalHalfWidth + 23f, 18f, 13f),    // trenches north of the road
                new Vector3(CanalHalfWidth + 23f, -18f, 13f),   // trenches south of the road
                new Vector3(-CanalHalfWidth - 10f, 12f, 6f),    // west MG nest
                new Vector3(-CanalHalfWidth - 6f, -8f, 6f),     // sentry post by the café
            };
            // glider skid tracks: from where each one touched down to where it stopped (they came in from the south-east)
            var skids = new (Vector2 a, Vector2 b)[GliderRests.Length];
            for (int i = 0; i < GliderRests.Length; i++)
            {
                var r = GliderRests[i];
                Vector3 f = Quaternion.Euler(0f, r.yaw, 0f) * Vector3.forward;
                skids[i] = (new Vector2(r.pos.x - f.x * 60f, r.pos.z - f.z * 60f), new Vector2(r.pos.x, r.pos.z));
            }
            // gardens: the café and the houses west of the bridge
            var gardens = new[] { new Rect(-CanalHalfWidth - 70f, -40f, 50f, 26f) };

            float cell = size / (res - 1);
            for (int z = 0; z < amRes; z++)
                for (int x = 0; x < amRes; x++)
                {
                    float wx = x / (float)(amRes - 1) * size - size / 2f;
                    float wz = z / (float)(amRes - 1) * size - size / 2f;
                    var p = new Vector2(wx, wz);
                    float[] w = new float[6];

                    // fields: rough pasture and short turf, mixed at two scales so no tiling shows from the bridge
                    float big = Mathf.PerlinNoise(wx * 0.011f + 7f, wz * 0.011f + 3f), fine = Mathf.PerlinNoise(wx * 0.06f + 1f, wz * 0.06f + 9f);
                    float turf = Smooth01(0.32f, 0.62f, big * 0.7f + fine * 0.3f);
                    bool lz = wx > CanalHalfWidth + 15f && wx < CanalHalfWidth + 190f && wz < -18f && wz > -230f;
                    if (lz) turf *= 0.5f;                       // the landing zone was rough, long grass
                    w[0] = 1f - turf; w[1] = turf;

                    // terrain samples: height and slope
                    int hx = Mathf.Clamp(Mathf.RoundToInt((wx + size / 2f) / cell), 1, res - 2), hz = Mathf.Clamp(Mathf.RoundToInt((wz + size / 2f) / cell), 1, res - 2);
                    float y = h[hz, hx] * height + baseY;
                    float gx = (h[hz, hx + 1] - h[hz, hx - 1]) * height / (2f * cell), gz = (h[hz + 1, hx] - h[hz - 1, hx]) * height / (2f * cell);
                    float slope = Mathf.Sqrt(gx * gx + gz * gz);

                    // roads: which one is nearest (the two canal-bank roads are the last two segments)
                    float best = float.MaxValue; int seg = -1;
                    for (int s = 0; s < segs.Length; s++) { float d = SegDist(p, segs[s]); if (d < best) { best = d; seg = s; } }
                    bool bankRoad = seg >= segs.Length - 2;
                    float road = 1f - Smooth01(-0.3f, 1.2f, best);
                    float verge = (1f - Smooth01(0.5f, 3f, best)) * (1f - road);
                    if (bankRoad) { w[2] += road * 3f; w[5] += verge * 0.6f; }
                    else { w[3] += road * 3f; w[2] += verge * 0.8f; }

                    // bridge approaches and the quay edges: crushed stone
                    float ax = Mathf.Abs(wx) - Water.QuayHalf;
                    if (Mathf.Abs(wz) <= Water.QuayLength + 1f) { if (ax > 0f && ax < 3f) w[3] += 2f; }   // stone quay edging
                    else
                    {
                        // natural banks: wet mud at the water's edge, pale sandy shore above it (very light on the east
                        // shore by the landing field in the photos)
                        float sd = Mathf.Abs(wx) - Water.CanalShore(wz, Mathf.Sign(wx));
                        if (sd > -2f && sd < 1.2f) w[4] += 2.5f;
                        else if (sd >= 1.2f && sd < (wx > 0f && wz < -25f ? 7f : 4f)) w[2] += 2.5f;
                    }
                    if (ax > 0f && ax < 30f && Mathf.Abs(wz) < 9f) w[3] += 3f * (1f - Smooth01(18f, 30f, ax));

                    // wet ground: Orne banks, the pond, the low marsh
                    float od = OrneDistance(wx, wz);
                    float mud = 1f - Smooth01(1f, 9f, od);
                    if (real != null) mud = Mathf.Max(mud, 1f - Smooth01(MarshWaterY + 0.2f, MarshWaterY + 1.2f, y));
                    mud = Mathf.Max(mud, 1f - Smooth01(7f, 13f, Vector2.Distance(p, new Vector2(CanalHalfWidth + 64f, -70f))));
                    w[4] += mud * 3f;

                    // bare earth: positions, skid tracks, steep banks
                    float bare = 0f;
                    foreach (var c in worn) bare = Mathf.Max(bare, 1f - Smooth01(c.z * 0.5f, c.z, Vector2.Distance(p, new Vector2(c.x, c.y))));
                    foreach (var s in skids)
                    {
                        Vector2 ab = s.b - s.a; float t = Mathf.Clamp01(Vector2.Dot(p - s.a, ab) / ab.sqrMagnitude);
                        float d = Vector2.Distance(p, s.a + ab * t);
                        bare = Mathf.Max(bare, (1f - Smooth01(1.2f, 3.5f, d)) * Mathf.Lerp(0.4f, 1f, t));   // deeper where it stopped
                    }
                    bare = Mathf.Max(bare, Smooth01(0.45f, 0.9f, slope));
                    w[5] += bare * 2.5f * Mathf.Lerp(0.6f, 1f, fine);

                    // gardens: lawns
                    foreach (var g in gardens) if (g.Contains(p)) { w[1] += 2f; w[0] *= 0.2f; }

                    float sum = 0f; for (int i = 0; i < 6; i++) sum += w[i];
                    for (int i = 0; i < 6; i++) am[z, x, i] = w[i] / sum;
                }
            return am;
        }

        // ---------------------------------------------------------------- water

        static Material WaterShaderMaterial(bool muddy)
        {
            var sh = Shader.Find("StansGame/Water");
            if (!sh) return null;
            string path = $"{MatDir}/{(muddy ? "Water_Orne" : "Water_Canal")}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.SetTexture("_NormalMap", MakeWaterNormal());
            if (muddy)
            {
                // the tidal Orne: brown with silt, a little current towards the sea
                m.SetColor("_Color", new Color(0.075f, 0.062f, 0.042f));
                m.SetColor("_SkyColor", new Color(0.26f, 0.29f, 0.36f));
                m.SetVector("_Flow", new Vector4(0.01f, 0.05f, 0f, 0f));
                m.SetFloat("_NormalScale", 0.75f); m.SetFloat("_Tiling", 0.3f); m.SetFloat("_Smoothness", 0.88f);
            }
            else
            {
                // the canal: deep, dark green, nearly still
                m.SetColor("_Color", new Color(0.028f, 0.05f, 0.046f));
                m.SetColor("_SkyColor", new Color(0.30f, 0.36f, 0.48f));
                m.SetVector("_Flow", new Vector4(0.003f, 0.01f, 0f, 0f));
                m.SetFloat("_NormalScale", 0.45f); m.SetFloat("_Tiling", 0.2f); m.SetFloat("_Smoothness", 0.94f);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------------------------------------------------------------- box bushes

        static GameObject buxusModel;
        static Material buxusMat;

        static GameObject BuxusBush(Transform parent, Vector3 pos, float height, float yaw)
        {
            if (!buxusModel)
            {
                string fbx = $"{BuxusDir}/Buxus.fbx";
                if (AssetImporter.GetAtPath(fbx) is ModelImporter mi && mi.materialImportMode != ModelImporterMaterialImportMode.None)
                { mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.SaveAndReimport(); }
                buxusModel = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                if (!buxusModel) return null;
                buxusMat = Mat("Buxus_Leaves", Color.white, 0.15f);
                string colPath = $"{BuxusDir}/Buxus_BaseColor.png", nrmPath = $"{BuxusDir}/Buxus_Normal.png";
                if (AssetImporter.GetAtPath(colPath) is TextureImporter tc && !tc.alphaIsTransparency)
                { tc.alphaIsTransparency = true; tc.mipMapsPreserveCoverage = true; tc.alphaTestReferenceValue = 0.4f; tc.SaveAndReimport(); }
                if (AssetImporter.GetAtPath(nrmPath) is TextureImporter tn && tn.textureType != TextureImporterType.NormalMap)
                { tn.textureType = TextureImporterType.NormalMap; tn.SaveAndReimport(); }
                var col = AssetDatabase.LoadAssetAtPath<Texture2D>(colPath);
                buxusMat.SetTexture("_BaseMap", col); buxusMat.mainTexture = col;
                buxusMat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(nrmPath)); buxusMat.EnableKeyword("_NORMALMAP");
                buxusMat.SetColor("_BaseColor", new Color(0.8f, 0.85f, 0.8f));
                buxusMat.SetFloat("_AlphaClip", 1f); buxusMat.SetFloat("_Cutoff", 0.4f); buxusMat.EnableKeyword("_ALPHATEST_ON");
                buxusMat.SetFloat("_Cull", 0f);
                buxusMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                EditorUtility.SetDirty(buxusMat);
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(buxusModel);
            go.name = "Box bush";
            go.transform.SetParent(parent, false);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = buxusMat;
            // scale to a real clipped box bush, feet on the ground
            var rs = go.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            if (b.size.y > 0.0001f) go.transform.localScale *= height / b.size.y;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.position = pos;
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            go.transform.position += Vector3.up * (FloorAt(pos) - b.min.y);
            return go;
        }

        /// <summary>Clipped box (Buxus) along the garden fronts of the café and the houses west of the bridge.</summary>
        static void BuildGardens(Transform env)
        {
            if (!File.Exists($"{BuxusDir}/Buxus.fbx")) return;
            var g = Group("Gardens (box bushes)", env);
            Random.InitState(1944);
            void Row(Vector3 a, Vector3 b, float spacing)
            {
                float len = Vector3.Distance(a, b); int n = Mathf.Max(1, Mathf.RoundToInt(len / spacing));
                for (int i = 0; i <= n; i++)
                {
                    var p = Vector3.Lerp(a, b, i / (float)n) + new Vector3(Random.Range(-0.08f, 0.08f), 0f, Random.Range(-0.08f, 0.08f));
                    BuxusBush(g, p, Random.Range(0.7f, 0.95f), Random.Range(0f, 360f));
                }
            }
            // café garden: along its south end and the west side of the plot
            Row(new Vector3(-CanalHalfWidth - 23f, 0f, -22.5f), new Vector3(-CanalHalfWidth - 12f, 0f, -22.5f), 1.1f);
            Row(new Vector3(-CanalHalfWidth - 24.5f, 0f, -21f), new Vector3(-CanalHalfWidth - 24.5f, 0f, -9f), 1.1f);
            // the two houses: a low hedge along the front garden, facing the road
            Row(new Vector3(-CanalHalfWidth - 44f, 0f, -14.5f), new Vector3(-CanalHalfWidth - 32f, 0f, -14.5f), 1.0f);
            Row(new Vector3(-CanalHalfWidth - 63f, 0f, -18.5f), new Vector3(-CanalHalfWidth - 49f, 0f, -18.5f), 1.0f);
            // a few single bushes by the doors
            foreach (var p in new[] { new Vector3(-CanalHalfWidth - 36f, 0f, -16.5f), new Vector3(-CanalHalfWidth - 54f, 0f, -20.5f), new Vector3(-CanalHalfWidth - 13f, 0f, -21f) })
                BuxusBush(g, p, Random.Range(1.0f, 1.25f), Random.Range(0f, 360f));
            Debug.Log("Stan's Game: box bushes planted in the gardens");
        }
    }
}
