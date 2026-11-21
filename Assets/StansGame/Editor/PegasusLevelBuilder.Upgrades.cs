using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Modelled props (Models/Props, made by Models/Source~/props1944.py) and the ground work that goes with them:
    /// brushwood piles, the east-bank pillbox, slit trenches dug into the ground with spoil heaped behind, and the
    /// Horsa Bridge over the Orne (lattice girders, the centre pier of the swing bridge and masonry abutments set
    /// into the banks, with the road graded up to them).
    /// </summary>
    public static partial class PegasusLevelBuilder
    {

        static Material TexMat(string name, string texFile, Color tint, float smooth = 0.1f, bool cutout = false, string normalFile = null)
        {
            var m = Mat(name, tint, smooth);
            string path = texFile.Contains("/") ? texFile : $"{GenTexDir}/{texFile}";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t) { m.SetTexture("_BaseMap", t); m.mainTexture = t; }
            if (normalFile != null)
            {
                string np = normalFile.Contains("/") ? normalFile : $"{GenTexDir}/{normalFile}";
                if (AssetImporter.GetAtPath(np) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap)
                { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
                var n = AssetDatabase.LoadAssetAtPath<Texture2D>(np);
                if (n) { m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            }
            if (cutout)
            {
                m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.45f); m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cull", 0f); m.SetOverrideTag("RenderType", "TransparentCutout"); m.renderQueue = 2450;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Dictionary<string, Material> propMats;
        static Material PropMaterial(string imported)
        {
            if (propMats == null)
                propMats = new Dictionary<string, Material>
                {
                    { "Brush_Bark", TexMat("Brush_Bark", "Bark_Branches.png", new Color(0.85f, 0.82f, 0.78f), 0.1f, false, "Bark_Branches_N.png") },
                    { "Brush_Leaves", TexMat("Brush_Leaves", "Leaves_Dead.png", Color.white, 0.05f, true) },
                    { "Pillbox_Concrete", TexMat("Pillbox_Concrete", "Concrete_Board.png", new Color(0.92f, 0.92f, 0.90f), 0.1f, false, "Concrete_Board_N.png") },
                    { "Pillbox_Turf", TexMat("Pillbox_Turf", Root + "/Textures/Ground/Ground_Turf_Albedo.jpg", new Color(0.62f, 0.72f, 0.48f), 0.05f, false, Root + "/Textures/Ground/Ground_Turf_Normal.png") },
                    { "HorsaBridge_Stone", TexMat("HorsaBridge_Stone", "Ashlar_Stone.png", new Color(0.95f, 0.93f, 0.90f), 0.1f, false, "Ashlar_Stone_N.png") },
                    { "HorsaBridge_Iron", Mat("HorsaBridge_Iron", new Color(0.27f, 0.28f, 0.27f), 0.3f, 0.5f) },
                    { "HorsaBridge_Road", Mat("HorsaBridge_Road", new Color(0.24f, 0.23f, 0.21f), 0.05f) },
                };
            foreach (var kv in propMats) if (imported.StartsWith(kv.Key)) return kv.Value;
            return null;
        }

        /// <summary>
        /// One object out of a props FBX (Blender X east, Y north, Z up), placed at pos and turned by yaw.
        /// Materials are swapped for the textured ones; optional mesh colliders.
        /// </summary>
        static GameObject PlaceProp(string file, string objectName, Transform parent, Vector3 pos, float yaw, Vector3 scale, bool colliders, string name = null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{PropDir}/{file}");
            if (!asset) { Debug.LogWarning($"Stan's Game: {PropDir}/{file} missing"); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name ?? objectName;
            var keep = new List<Transform>();
            foreach (Transform c in go.transform) if (!c.name.StartsWith(objectName)) keep.Add(c);   // (Pillbox keeps Pillbox_Turf)
            foreach (var c in keep) Object.DestroyImmediate(c.gameObject);
            // the same turn that stood the 1934 bridge up the right way (Blender X -> X, Y -> Z, Z -> Y)
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, 180f, 0f) * asset.transform.rotation);
            go.transform.localScale = Vector3.Scale(asset.transform.localScale, scale);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++) { var m = ms[i] ? PropMaterial(ms[i].name) : null; if (m) ms[i] = m; }
                r.sharedMaterials = ms;
            }
            if (colliders)
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                    if (!mf.GetComponent<Collider>()) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            foreach (var t in go.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
            return go;
        }

        // ------------------------------------------------------------------ editing the ground after it is built

        static float TerrainY(float x, float z)
        {
            if (!terrainRef) return 0f;
            var td = terrainRef.terrainData; var tp = terrainRef.transform.position;
            return td.GetInterpolatedHeight((x - tp.x) / td.size.x, (z - tp.z) / td.size.z) + tp.y;
        }

        /// <summary>Rewrite the terrain heights in a rectangle: f(x, z, height) -> new height (world metres).</summary>
        static void TerrainEdit(float x0, float x1, float z0, float z1, System.Func<float, float, float, float> f)
        {
            if (!terrainRef) return;
            var td = terrainRef.terrainData; var tp = terrainRef.transform.position;
            int res = td.heightmapResolution;
            float sx = td.size.x / (res - 1), sz = td.size.z / (res - 1);
            int i0 = Mathf.Clamp(Mathf.FloorToInt((x0 - tp.x) / sx), 0, res - 1), i1 = Mathf.Clamp(Mathf.CeilToInt((x1 - tp.x) / sx), 0, res - 1);
            int j0 = Mathf.Clamp(Mathf.FloorToInt((z0 - tp.z) / sz), 0, res - 1), j1 = Mathf.Clamp(Mathf.CeilToInt((z1 - tp.z) / sz), 0, res - 1);
            int w = i1 - i0 + 1, h = j1 - j0 + 1;
            if (w <= 0 || h <= 0) return;
            var hs = td.GetHeights(i0, j0, w, h);
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    float wx = tp.x + (i0 + i) * sx, wz = tp.z + (j0 + j) * sz;
                    float y = hs[j, i] * td.size.y + tp.y;
                    hs[j, i] = Mathf.Clamp01((f(wx, wz, y) - tp.y) / td.size.y);
                }
            td.SetHeights(i0, j0, hs);
        }

        /// <summary>A low heap of earth (spoil from a trench, a berm): raised ground, so it takes the ground's texture.</summary>
        static void EarthMound(Vector3 centre, float yaw, float length, float width, float height)
        {
            var rot = Quaternion.Euler(0f, -yaw, 0f);
            float r = Mathf.Max(length, width) * 0.5f + 1.5f;
            TerrainEdit(centre.x - r, centre.x + r, centre.z - r, centre.z + r, (x, z, y) =>
            {
                var l = rot * new Vector3(x - centre.x, 0f, z - centre.z);
                float u = Mathf.Abs(l.x) / (length * 0.5f), v = Mathf.Abs(l.z) / (width * 0.5f);
                float k = Mathf.Clamp01(1f - (u * u * u * u + v * v)) ;
                k = k * k * (3f - 2f * k);
                float lump = 0.85f + 0.15f * Mathf.PerlinNoise(x * 0.9f, z * 0.9f);
                return y + height * k * lump;
            });
        }

        // ------------------------------------------------------------------ slit trench dug into the ground

        /// <summary>
        /// A slit trench about 5.5 x 1.4 m and 1.3 m deep along X at p: the ground is opened (terrain hole, whole
        /// cells) and lined with planks, spoil heaped on the side away from the road. Returns the trench floor
        /// (relative to the ground, for the rifleman standing in it).
        /// </summary>
        static void SlitTrench(Transform parent, Vector3 p, float awayZ)
        {
            var tr = new GameObject("Slit trench").transform; tr.SetParent(parent); tr.position = new Vector3(p.x, 0f, p.z);
            CutTerrainHole(p.x - 2.8f, p.x + 2.8f, p.z - 0.75f, p.z + 0.75f, out float x0, out float x1, out float z0, out float z1);
            var planks = TexMat("Trench_Planks", "Planks_Revetment.png", new Color(0.85f, 0.8f, 0.75f), 0.1f, false, "Planks_Revetment_N.png");
            var mud = TexMat("Trench_Floor", Root + "/Textures/Ground/Ground_Mud_Albedo.jpg", new Color(0.7f, 0.65f, 0.6f), 0.25f, false, Root + "/Textures/Ground/Ground_Mud_Normal.png");
            var bark = PropMaterial("Brush_Bark");
            var box = Mat("Ammo_Box", new Color(0.33f, 0.30f, 0.20f));
            const float D = 1.3f, T = 0.08f;
            float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f, lx = x1 - x0, lz = z1 - z0;
            // (heights relative to the ground: the group is set on the ground afterwards)
            void Part(string n, Vector3 c, Vector3 s, Material m) => Prim(PrimitiveType.Cube, n, tr, c - tr.position, Vector3.zero, s, m, true);
            Part("Revetment N", new Vector3(cx, -D * 0.5f + 0.05f, z1 - T * 0.5f), new Vector3(lx, D + 0.1f, T), planks);
            Part("Revetment S", new Vector3(cx, -D * 0.5f + 0.05f, z0 + T * 0.5f), new Vector3(lx, D + 0.1f, T), planks);
            Part("Revetment W", new Vector3(x0 + T * 0.5f, -D * 0.5f + 0.05f, cz), new Vector3(T, D + 0.1f, lz), planks);
            Part("Revetment E", new Vector3(x1 - T * 0.5f, -D * 0.5f + 0.05f, cz), new Vector3(T, D + 0.1f, lz), planks);
            Part("Floor", new Vector3(cx, -D - 0.1f, cz), new Vector3(lx, 0.2f, lz), mud);
            Part("Fire step", new Vector3(cx, -D + 0.25f, awayZ > 0 ? z0 + 0.3f : z1 - 0.3f), new Vector3(lx - 0.2f, 0.5f, 0.45f), planks);
            // duckboards on the muddy floor, stakes holding the revetment, a couple of ammunition boxes
            for (float x = x0 + 0.35f; x < x1 - 0.3f; x += 0.32f)
                Prim(PrimitiveType.Cube, "Duckboard slat", tr, new Vector3(x, -D + 0.04f, cz) - tr.position, new Vector3(0f, Random.Range(-4f, 4f), 0f), new Vector3(0.12f, 0.04f, Mathf.Min(0.6f, lz - 0.6f)), planks);
            for (float x = x0 + 0.5f; x < x1 - 0.3f; x += 1.0f)
                foreach (float zz in new[] { z0 + 0.12f, z1 - 0.12f })
                    Prim(PrimitiveType.Cylinder, "Stake", tr, new Vector3(x, -D * 0.5f + 0.2f, zz) - tr.position, new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f)), new Vector3(0.09f, D * 0.5f + 0.2f, 0.09f), bark);
            Prim(PrimitiveType.Cube, "Ammo box", tr, new Vector3(x1 - 0.5f, -D + 0.15f, cz) - tr.position, new Vector3(0f, 12f, 0f), new Vector3(0.45f, 0.22f, 0.3f), box, true);
            // an entrenching tool leaning against the back wall
            Spade(tr, new Vector3(x0 + 0.7f, -D, awayZ > 0 ? z1 - 0.2f : z0 + 0.2f) - tr.position, 0f, awayZ > 0 ? 16f : -16f);
            Prim(PrimitiveType.Cube, "Ammo box", tr, new Vector3(x1 - 0.55f, -D + 0.37f, cz + 0.05f) - tr.position, new Vector3(0f, -8f, 0f), new Vector3(0.45f, 0.22f, 0.3f), box, true);
            EarthMound(new Vector3(cx, 0f, cz + awayZ * (lz * 0.5f + 1.4f)), 0f, lx + 1.2f, 2.4f, 0.6f);
        }

        // ------------------------------------------------------------------ Horsa Bridge (Ranville)

        /// <summary>
        /// The swing bridge over the Orne on the Ranville road (photo of 1944 and the model Joan sent): lattice deck
        /// girders on a round centre pier, masonry abutments set into the banks. The span is fitted to where the
        /// ground rises out of the river along the road, the deck at the height of the banks, and the road on both
        /// sides graded up to the abutments.
        /// </summary>
        static float horsaWestX, horsaEastX, horsaDeckY;   // where the Orne bridge ended up (for its guard and the Orne gliders)

        static void BuildHorsaBridge(Transform orne)
        {
            var hb = Group("Horsa Bridge (Ranville, River Orne)", orne);
            float bx = OrneCentreX(0f);
            float wl = real != null ? MarshWaterY : -1.6f;
            float xw = bx, xe = bx;
            for (; xw > bx - 90f; xw -= 0.5f) if (TerrainY(xw, 0f) > wl + 1.3f) break;
            for (; xe < bx + 90f; xe += 0.5f) if (TerrainY(xe, 0f) > wl + 1.3f) break;
            float gap = Mathf.Clamp(xe - xw, 24f, 90f), mid = (xw + xe) * 0.5f;
            xw = mid - gap * 0.5f; xe = mid + gap * 0.5f;
            float deckY = Mathf.Max(Mathf.Max(TerrainY(xw - 12f, 0f), TerrainY(xe + 12f, 0f)), wl + 3.4f) + 0.05f;
            const float AL = 11f;                                   // abutment length (model)
            // the road climbs to the deck over 30 m beyond each abutment; under the abutments the bank is cut to the top
            foreach (int s in new[] { -1, 1 })
            {
                float face = s < 0 ? xw : xe, back = face + s * AL;
                float a = Mathf.Min(face, back + s * 32f), b = Mathf.Max(face, back + s * 32f);
                TerrainEdit(a, b, -12f, 12f, (x, z, y) =>
                {
                    float along = s < 0 ? Smooth01(back - 30f, back, x) : 1f - Smooth01(back, back + 30f, x);
                    bool under = s < 0 ? x >= back : x <= back;
                    float across = 1f - Smooth01(5.5f, 11f, Mathf.Abs(z));
                    float target = deckY - 0.03f;
                    if (under) return Mathf.Abs(z) < 5f ? Mathf.Min(y, target - 0.3f) : Mathf.Lerp(y, target, across);
                    return Mathf.Lerp(y, target, along * across);
                });
            }
            var span = PlaceProp("HorsaBridge.fbx", "Horsa_Span", hb, new Vector3(mid, deckY, 0f), 0f, new Vector3(gap / 50f, 1f, 1f), true, "Horsa Bridge span");
            PlaceProp("HorsaBridge.fbx", "Horsa_Pier", hb, new Vector3(mid, deckY, 0f), 0f, Vector3.one, true, "Horsa Bridge pier (swing)");
            PlaceProp("HorsaBridge.fbx", "Horsa_Abutment", hb, new Vector3(xe, deckY, 0f), 0f, Vector3.one, true, "Horsa Bridge abutment (east)");
            PlaceProp("HorsaBridge.fbx", "Horsa_Abutment", hb, new Vector3(xw, deckY, 0f), 180f, Vector3.one, true, "Horsa Bridge abutment (west)");
            horsaWestX = xw; horsaEastX = xe; horsaDeckY = deckY;
            Debug.Log($"Stan's Game: Horsa Bridge from x {xw:F1} to {xe:F1} ({gap:F1} m between the abutments), deck at {deckY:F2} m");
        }
    }
}
