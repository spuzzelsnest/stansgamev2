using System.Linq;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Real tree models (from _downloads/tree1 and tree2) for the trees near the camera; the simple blob trees stay
    /// as the far level of detail, so hundreds of roadside trees still render quickly.
    /// Tree1 is a narrow, upright poplar; Tree2 a tall tree with a long bare trunk and a spreading crown high up -
    /// the shape of most of the trees along the canal roads and the Ranville road in pb01, pb03, pb10 and pb11.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        const string TreeDir = Root + "/Models/Trees";
        static GameObject tree1Asset, tree2Asset;
        static GameObject[] smallTreeAssets;   // _downloads/trees small: SmallTree_A (6.6 m), B (4.1 m bush tree), C (7.9 m)

        static Material TreeMat(string name, string texFile, bool cutout)
        {
            var m = Mat(name, Color.white, 0.05f);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TreeDir}/{texFile}");
            if (tex)
            {
                if (cutout && AssetImporter.GetAtPath($"{TreeDir}/{texFile}") is TextureImporter ti && !ti.alphaIsTransparency)
                { ti.alphaIsTransparency = true; ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.45f; ti.SaveAndReimport(); }
                m.SetTexture("_BaseMap", tex); m.mainTexture = tex;
            }
            if (cutout)
            {
                m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.45f); m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cull", 0f);
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                m.SetColor("_BaseColor", new Color(0.78f, 0.82f, 0.74f));   // a touch darker and greener for June at night
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// Adds a real tree model to a blob tree as its close-up level of detail. kind 1 = upright poplar, 2 = tall spreading.
        /// </summary>
        static void AddTreeModel(GameObject blobTree, int kind, float height)
        {
            if (!tree1Asset) tree1Asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{TreeDir}/Tree1_Poplar.fbx");
            if (!tree2Asset) tree2Asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{TreeDir}/Tree2_Tall.fbx");
            if (smallTreeAssets == null)
                smallTreeAssets = new[] { "A", "B", "C" }.Select(k => AssetDatabase.LoadAssetAtPath<GameObject>($"{TreeDir}/SmallTree_{k}.fbx")).ToArray();
            // kind 3: a small round-crowned tree (apple, hawthorn, hazel - A or C), kind 4: a bush-sized young tree (B)
            var asset = kind == 1 ? tree1Asset : kind == 2 ? tree2Asset
                      : kind == 4 ? smallTreeAssets[1] : smallTreeAssets[Random.value < 0.5f ? 0 : 2];
            if (!asset) return;
            var farRenderers = blobTree.GetComponentsInChildren<Renderer>();

            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, blobTree.transform);
            model.name = "Tree model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, Random.value * 360f, 0f);
            model.transform.localScale = Vector3.one;
            var rs = model.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float k = b.size.y > 0.1f ? height / b.size.y : 1f;
            model.transform.localScale = Vector3.one * k;
            foreach (var r in rs)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] ? mats[i].name : "";
                    mats[i] = n.Contains("Tree1_Bark") ? TreeMat("Tree1_Bark", "Tree1_Bark.png", false)
                            : n.Contains("Tree1_Leaves") ? TreeMat("Tree1_Leaves", "Tree1_Leaves.png", true)
                            : n.Contains("Tree2_Leaves") ? TreeMat("Tree2_Leaves", "Tree2_Leaves.png", true)
                            : n.Contains("Tree2_Branches") ? TreeMat("Tree2_Branches", "Tree2_Branches.png", true)
                            : n.Contains("Tree2_Trunk") ? TreeMat("Tree2_Trunk", "Tree2_Trunk.jpg", false)
                            : n.Contains("SmallTree_Leaves") ? TreeMat("SmallTree_Leaves", "SmallTree_Leaves.png", true)
                            : n.Contains("SmallTree_Bark") ? TreeMat("SmallTree_Bark", "SmallTree_Bark.jpg", false)
                            : mats[i];
                }
                r.sharedMaterials = mats;
            }
            foreach (var c in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);

            // close up: the model; further away: the blob tree; very far: nothing
            if (!blobTree.TryGetComponent<LODGroup>(out var lod)) lod = blobTree.AddComponent<LODGroup>();
            lod.SetLODs(new[]
            {
                new LOD(0.25f, rs),
                new LOD(0.006f, farRenderers),
            });
            lod.fadeMode = LODFadeMode.CrossFade; lod.animateCrossFading = true;
            lod.RecalculateBounds();
        }

        /// <summary>
        /// A small tree from _downloads/trees small on its own (no blob tree behind it): the model close up, nothing
        /// beyond ~250 m, and a thin trunk collider. kind 3 = round crown (A or C), 4 = bush-sized (B).
        /// </summary>
        static void AddSmallTree(GameObject root, int kind, float height)
        {
            var bark = Mat("Bark", new Color(0.20f, 0.17f, 0.13f));
            // a stand-in blob for the far distance, so AddTreeModel has something to fade to
            float h = height;
            Prim(PrimitiveType.Sphere, "Crown", root.transform, new Vector3(0f, h * 0.6f, 0f), Vector3.zero, new Vector3(h * 0.7f, h * 0.65f, h * 0.7f), Mat("Orchard_Leaves", new Color(0.11f, 0.17f, 0.09f)));
            Prim(PrimitiveType.Cylinder, "Trunk", root.transform, new Vector3(0f, h * 0.15f, 0f), Vector3.zero, new Vector3(0.18f, h * 0.15f, 0.18f), bark, true);
            AddTreeModel(root, kind, h);
            GameObjectUtility.SetStaticEditorFlags(root, StaticEditorFlags.BatchingStatic);
        }

        // ------------------------------------------------------------------ brushwood and wire rolls (pb11, pb01)

        /// <summary>
        /// A pile of cut branches and scrub, like the ones between the landing zone and the bridge in pb11
        /// (the Germans had been clearing the ground). Good cover; stops bullets.
        /// </summary>
        static void BrushPile(Transform parent, Vector3 centre, float length, float width, float yaw)
        {
            // cut branches with their dead leaves (Models/Props/Brushwood.fbx: three piles, 6.0 x 3.2, 4.5 x 2.6, 7.5 x 3.6 m)
            centre.y = FloorAt(centre);
            string pick = length < 5.5f ? "Brush_B" : length < 8.5f ? "Brush_A" : "Brush_C";
            float L = pick == "Brush_B" ? 4.5f : pick == "Brush_A" ? 6.0f : 7.5f, W = pick == "Brush_B" ? 2.6f : pick == "Brush_A" ? 3.2f : 3.6f;
            float kx = length / L, kz = width / W;
            var go = PlaceProp("Brushwood.fbx", pick, parent, centre, yaw, new Vector3(kx, (kx + kz) * 0.5f, kz), false, "Brushwood pile");
            if (!go) return;
            var cgo = new GameObject("Collider"); cgo.transform.SetParent(go.transform, true);
            cgo.transform.SetPositionAndRotation(centre + Vector3.up * 0.55f, Quaternion.Euler(0f, yaw, 0f));
            cgo.transform.localScale = Vector3.one;
            var col = cgo.AddComponent<BoxCollider>();
            col.size = new Vector3(length * 0.85f, 1.1f, width * 0.75f);
            cgo.transform.SetParent(go.transform.parent, true);
        }

        /// <summary>Rolls of wire netting stacked by the west end of the bridge (pb01, bottom left).</summary>
        static void WireRolls(Transform parent, Vector3 pos, float yaw)
        {
            var root = new GameObject("Wire rolls").transform; root.SetParent(parent);
            pos.y = FloorAt(pos);
            root.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var mesh = Mat("Wire_Netting", new Color(0.20f, 0.20f, 0.19f), 0.3f, 0.5f);
            var plank = Mat("Plank", new Color(0.40f, 0.34f, 0.26f));
            Prim(PrimitiveType.Cube, "Timber", root, new Vector3(0f, 0.12f, -0.9f), Vector3.zero, new Vector3(3.4f, 0.24f, 0.3f), plank);
            var rolls = new[] { new Vector3(0f, 0.32f, 0f), new Vector3(0f, 0.32f, 0.66f), new Vector3(0f, 0.32f, 1.32f), new Vector3(0f, 0.88f, 0.33f), new Vector3(0f, 0.88f, 0.99f) };
            foreach (var r in rolls)
                Prim(PrimitiveType.Cylinder, "Roll", root, r, new Vector3(0f, 0f, 90f), new Vector3(0.64f, 1.5f, 0.64f), mesh);
            var col = root.gameObject.AddComponent<BoxCollider>(); col.center = new Vector3(0f, 0.6f, 0.66f); col.size = new Vector3(3f, 1.2f, 2f);
        }

        // ------------------------------------------------------------------ hedgerow (pb02, pb11)

        static Mesh shrubMesh;

        /// <summary>
        /// A bush made of crossed leaf cards (the leaf-cluster texture from tree 2), about 2 m across and 1.8 m high.
        /// Normals point out from the middle so it lights like a rounded mass of leaves.
        /// </summary>
        static Mesh ShrubMesh()
        {
            if (shrubMesh) return shrubMesh;
            string path = GenMeshDir + "/Shrub.asset";
            var rnd = new System.Random(606);
            float R() => (float)rnd.NextDouble();
            var v = new System.Collections.Generic.List<Vector3>(); var n = new System.Collections.Generic.List<Vector3>();
            var uv = new System.Collections.Generic.List<Vector2>(); var t = new System.Collections.Generic.List<int>();
            Vector3 centre = new Vector3(0f, 0.85f, 0f);
            for (int q = 0; q < 34; q++)
            {
                // a point inside a squashed ball, denser near the ground
                Vector3 p; do { p = new Vector3(R() * 2f - 1f, R() * 2f - 1f, R() * 2f - 1f); } while (p.sqrMagnitude > 1f);
                p = new Vector3(p.x * 1.0f, p.y * 0.75f + 0.85f, p.z * 1.0f);
                float size = 0.9f + R() * 0.8f;
                var rot = Quaternion.Euler(R() * 70f - 35f, R() * 360f, R() * 360f);
                Vector3 right = rot * Vector3.right * size * 0.5f, up = rot * Vector3.up * size * 0.5f;
                int b = v.Count;
                v.Add(p - right - up); v.Add(p + right - up); v.Add(p + right + up); v.Add(p - right + up);
                for (int k = 0; k < 4; k++) n.Add(((v[b + k] - centre).normalized + Vector3.up * 0.3f).normalized);
                // the big leaf cluster in the top-left of the texture
                uv.Add(new Vector2(0.05f, 0.26f)); uv.Add(new Vector2(0.74f, 0.26f)); uv.Add(new Vector2(0.74f, 0.99f)); uv.Add(new Vector2(0.05f, 0.99f));
                t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
            var m = new Mesh { name = "Shrub" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds();
            shrubMesh = SaveMesh(m, "Shrub");
            return shrubMesh;
        }

        /// <summary>
        /// A field-edge hedgerow of bushes of mixed size, as along the landing zone in pb02 and pb11 (replacing the
        /// old green box). Soldiers can hide in it but it does not stop bullets.
        /// </summary>
        static void Hedgerow(Transform parent, Vector3 a, Vector3 b)
        {
            if (!AssetDatabase.IsValidFolder(GenMeshDir)) AssetDatabase.CreateFolder(Root + "/Models", "Generated");
            var mesh = ShrubMesh();
            var mat = TreeMat("Tree2_Leaves", "Tree2_Leaves.png", true);
            Vector3 d = b - a; float len = d.magnitude; Vector3 dir = d / len, side = Vector3.Cross(Vector3.up, dir);
            float nextTree = Random.Range(6f, 14f);
            for (float s = 0f; s < len; s += 1.3f + Random.value * 0.6f)
            {
                // now and then a small tree grows out of the hedge (hawthorn, hazel, a young oak)
                if (s >= nextTree)
                {
                    nextTree = s + Random.Range(12f, 26f);
                    var tree = new GameObject("Hedgerow tree");
                    tree.transform.SetParent(parent);
                    tree.transform.position = a + dir * s + side * Random.Range(-0.4f, 0.4f);
                    AddSmallTree(tree, Random.value < 0.4f ? 4 : 3, Random.Range(4.5f, 7f));
                    continue;
                }
                var go = new GameObject("Hedge bush");
                go.transform.SetParent(parent);
                go.transform.SetPositionAndRotation(a + dir * s + side * Random.Range(-0.5f, 0.5f), Quaternion.Euler(0f, Random.value * 360f, 0f));
                float k = Random.Range(0.85f, 1.6f);
                go.transform.localScale = new Vector3(k * Random.Range(0.9f, 1.3f), k, k * Random.Range(0.9f, 1.3f));
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
                var lod = go.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(0.012f, new Renderer[] { r }) });
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            }
        }
    }
}
