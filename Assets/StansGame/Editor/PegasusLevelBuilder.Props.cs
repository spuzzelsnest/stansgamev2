using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Sandbag positions and the small things seen in the 1944 photos (_downloads/Photos):
    /// the striped barrier poles at the bridge ends (pb05, pb08), iron railings along the quays at the bridge
    /// (pb09, pb14), telegraph poles and wires along the roads (pb04, pb05, pb10) and the sandy spoil heaps
    /// thrown up beside the trenches (pb02).
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        const string PropDir = Root + "/Models/Props";
        const float SandbagLength = 3.1f;   // one model segment (3.25 m) laid with a little overlap

        static GameObject sandbagAsset;
        static Material sandbagMat;

        static Material SandbagMaterial()
        {
            if (sandbagMat) return sandbagMat;
            sandbagMat = Mat("Sandbags", Color.white, 0.05f);
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(PropDir + "/sandbags_new2_d.png");
            string nPath = PropDir + "/sandbags_new_n.png";
            if (AssetImporter.GetAtPath(nPath) is TextureImporter ni && ni.textureType != TextureImporterType.NormalMap)
            { ni.textureType = TextureImporterType.NormalMap; ni.SaveAndReimport(); }
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(nPath);
            if (albedo) { sandbagMat.SetTexture("_BaseMap", albedo); sandbagMat.mainTexture = albedo; }
            else sandbagMat.SetColor("_BaseColor", new Color(0.55f, 0.50f, 0.40f));
            if (normal) { sandbagMat.SetTexture("_BumpMap", normal); sandbagMat.EnableKeyword("_NORMALMAP"); }
            EditorUtility.SetDirty(sandbagMat);
            return sandbagMat;
        }

        /// <summary>One sandbag wall segment, about 3.25 m long, standing on pos, its length along the yaw direction's X axis.</summary>
        static GameObject Sandbags(Transform parent, Vector3 pos, float yaw, float height = 1f)
        {
            var go = new GameObject("Sandbags");
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(pos, Quaternion.identity);   // measure unrotated, turn at the end
            if (!sandbagAsset) sandbagAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PropDir + "/Sandbags.fbx");
            if (sandbagAsset)
            {
                var m = (GameObject)PrefabUtility.InstantiatePrefab(sandbagAsset, go.transform);
                m.transform.localPosition = Vector3.zero; m.transform.localRotation = Quaternion.identity; m.transform.localScale = Vector3.one;
                var rs = m.GetComponentsInChildren<Renderer>();
                foreach (var r in rs) r.sharedMaterial = SandbagMaterial();
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                if (b.size.z > b.size.x) m.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                float len = Mathf.Max(b.size.x, b.size.z);
                float k = len > 0.01f ? 3.25f / len : 1f;
                m.transform.localScale = new Vector3(k, k * height, k);
                var b2 = rs[0].bounds; foreach (var r in rs) b2.Encapsulate(r.bounds);
                m.transform.position += new Vector3(pos.x - b2.center.x, pos.y - b2.min.y, pos.z - b2.center.z);
                foreach (var c in m.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            }
            else
                Prim(PrimitiveType.Cube, "Sandbag blockout", go.transform, new Vector3(0f, 0.65f * height, 0f), Vector3.zero, new Vector3(3.25f, 1.3f * height, 0.9f), Mat("Sandbag_Plain", new Color(0.55f, 0.50f, 0.40f)));
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.62f * height, 0f); col.size = new Vector3(3.2f, 1.24f * height, 0.85f);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        /// <summary>A straight wall of sandbags from a to b (as many segments as it takes).</summary>
        static void SandbagLine(Transform parent, Vector3 a, Vector3 b, float height = 1f)
        {
            Vector3 d = b - a; d.y = 0f;
            float len = d.magnitude; if (len < 0.5f) return;
            int n = Mathf.Max(1, Mathf.CeilToInt(len / SandbagLength));
            float yaw = Mathf.Atan2(-d.z, d.x) * Mathf.Rad2Deg;
            for (int i = 0; i < n; i++)
            {
                float t = (i + 0.5f) / n;
                Sandbags(parent, Vector3.Lerp(a, b, t), yaw, height);
            }
        }

        /// <summary>A ring of sandbag walls round a centre, leaving out the segments facing 'gapDir' (the way in).</summary>
        static void SandbagRing(Transform parent, Vector3 centre, float radius, float height, Vector3 gapDir, int gaps)
        {
            int n = Mathf.Max(4, Mathf.RoundToInt(Mathf.PI / Mathf.Asin(Mathf.Clamp(SandbagLength / (2f * radius), 0.05f, 0.99f))));
            float gapAngle = Mathf.Atan2(gapDir.z, gapDir.x);
            var segs = new List<(float off, int k)>();
            for (int k = 0; k < n; k++)
            {
                float a = gapAngle + Mathf.PI * 2f * (k + 0.5f) / n;
                segs.Add((Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, gapAngle * Mathf.Rad2Deg)), k));
            }
            segs.Sort((x, y) => x.off.CompareTo(y.off));
            var skip = new HashSet<int>(); for (int i = 0; i < gaps && i < segs.Count; i++) skip.Add(segs[i].k);
            for (int k = 0; k < n; k++)
            {
                if (skip.Contains(k)) continue;
                float a0 = gapAngle + Mathf.PI * 2f * k / n, a1 = gapAngle + Mathf.PI * 2f * (k + 1) / n;
                Vector3 p0 = centre + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
                Vector3 p1 = centre + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
                SandbagLine(parent, p0, p1, height);
            }
        }

        static GameObject knifeRestAsset;
        static Material knifeRestMat;

        /// <summary>A knife rest: two wooden X-trestles joined by a rail, wrapped in barbed wire (about 5.2 m long).</summary>
        static GameObject KnifeRest(Transform parent, Vector3 pos, float yaw)
        {
            var go = new GameObject("Barbed wire knife rest");
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(pos, Quaternion.identity);
            if (!knifeRestAsset) knifeRestAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PropDir + "/KnifeRest.fbx");
            if (!knifeRestMat)
            {
                knifeRestMat = Mat("Barbed_Wire_KnifeRest", Color.white, 0.2f);
                var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(PropDir + "/Wire_Alpha_BaseColor.png");
                string nPath = PropDir + "/Wire_Normal.png";
                if (AssetImporter.GetAtPath(nPath) is TextureImporter ni && ni.textureType != TextureImporterType.NormalMap)
                { ni.textureType = TextureImporterType.NormalMap; ni.SaveAndReimport(); }
                var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(nPath);
                if (albedo) { knifeRestMat.SetTexture("_BaseMap", albedo); knifeRestMat.mainTexture = albedo; }
                if (normal) { knifeRestMat.SetTexture("_BumpMap", normal); knifeRestMat.EnableKeyword("_NORMALMAP"); }
                // the wire is drawn with transparent texels: cut them out, show both faces
                knifeRestMat.SetFloat("_AlphaClip", 1f); knifeRestMat.SetFloat("_Cutoff", 0.4f); knifeRestMat.EnableKeyword("_ALPHATEST_ON");
                knifeRestMat.SetFloat("_Cull", 0f);
                knifeRestMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                EditorUtility.SetDirty(knifeRestMat);
            }
            if (knifeRestAsset)
            {
                var m = (GameObject)PrefabUtility.InstantiatePrefab(knifeRestAsset, go.transform);
                m.transform.localPosition = Vector3.zero; m.transform.localRotation = Quaternion.identity; m.transform.localScale = Vector3.one;
                var rs = m.GetComponentsInChildren<Renderer>();
                foreach (var r in rs) r.sharedMaterial = knifeRestMat;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                if (b.size.z > b.size.x) m.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                float len = Mathf.Max(b.size.x, b.size.z);
                if (len > 0.01f) m.transform.localScale = Vector3.one * (5.16f / len);
                var b2 = rs[0].bounds; foreach (var r in rs) b2.Encapsulate(r.bounds);
                m.transform.position += new Vector3(pos.x - b2.center.x, pos.y - b2.min.y, pos.z - b2.center.z);
                foreach (var c in m.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            }
            else
                Prim(PrimitiveType.Cube, "Wire blockout", go.transform, new Vector3(0f, 0.6f, 0f), Vector3.zero, new Vector3(5f, 1.2f, 1.4f), Mat("Barbed_Wire", new Color(0.15f, 0.14f, 0.13f)));
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            // stops anyone walking through, but low enough that it does not block sight or bullets at chest height
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.3f, 0f); col.size = new Vector3(5.0f, 0.6f, 1.4f);
            return go;
        }

        static float FloorAt(Vector3 p) => (real != null || smeRaw != null) && terrainRef ? GroundAt(p) : 0f;

        // ------------------------------------------------------------------ details from the photos

        static void BuildPhotoDetails(Transform env)
        {
            var g = Group("Details from the 1944 photos", env);
            Physics.SyncTransforms();
            var red = Mat("Barrier_Red", new Color(0.60f, 0.12f, 0.10f));
            var white = Mat("Barrier_White", new Color(0.86f, 0.85f, 0.82f));
            var iron = Mat("Railing_Iron", new Color(0.10f, 0.10f, 0.10f), 0.3f, 0.6f);
            var wood = Mat("Pole_Wood", new Color(0.30f, 0.24f, 0.17f));
            var sand = Mat("Spoil_Sand", new Color(0.58f, 0.53f, 0.43f));

            // striped barrier poles, raised, beside the road at both ends of the bridge (pb05, pb08)
            foreach (float side in new[] { -1f, 1f })
            {
                var b = new GameObject(side < 0 ? "Barrier (west end)" : "Barrier (east end)").transform; b.SetParent(g);
                Vector3 p = side < 0 ? new Vector3(WestBankRoadX - 3.2f, 0f, -RoadWidth * 0.5f - 0.9f) : new Vector3(BridgeEastEnd + 3f, 0f, -RoadWidth * 0.5f - 2.2f); p.y = FloorAt(p);
                b.position = p;
                Prim(PrimitiveType.Cube, "Post", b, new Vector3(0f, 0.55f, 0f), Vector3.zero, new Vector3(0.3f, 1.1f, 0.3f), white, true);
                Prim(PrimitiveType.Cube, "Counterweight", b, new Vector3(0f, 1.15f, -0.45f), Vector3.zero, new Vector3(0.35f, 0.45f, 0.6f), iron);
                for (int i = 0; i < 13; i++)   // the boom, standing up, painted in bands
                    Prim(PrimitiveType.Cylinder, "Boom", b, new Vector3(0f, 1.4f + i * 0.5f, 0.05f), new Vector3(-4f, 0f, 0f), new Vector3(0.12f, 0.25f, 0.12f), i % 2 == 0 ? red : white);
            }

            // iron railings along the quays either side of the bridge (pb09, pb14); gaps where the slipways come up
            foreach (float side in new[] { -1f, 1f })
                foreach (float dir in new[] { -1f, 1f })
                    foreach (var (z0, z1) in new[] { (side < 0 ? 4.4f : 5.0f, Water.QuayLength) })
                    {
                        var r = new GameObject("Quay railing").transform; r.SetParent(g);
                        float x = side * ((side < 0 ? Water.WestQuay : Water.QuayHalf) + 0.35f), za = dir * z0, zb = dir * z1, zc = (za + zb) * 0.5f, len = Mathf.Abs(zb - za);
                        float y = FloorAt(new Vector3(x + side * 0.6f, 0f, zc));
                        r.position = new Vector3(x, y, zc);
                        Prim(PrimitiveType.Cube, "Top rail", r, new Vector3(0f, 1.0f, 0f), Vector3.zero, new Vector3(0.05f, 0.05f, len), iron);
                        Prim(PrimitiveType.Cube, "Mid rail", r, new Vector3(0f, 0.5f, 0f), Vector3.zero, new Vector3(0.03f, 0.03f, len), iron);
                        for (float z = -len * 0.5f; z <= len * 0.5f + 0.01f; z += 2.5f)
                            Prim(PrimitiveType.Cube, "Post", r, new Vector3(0f, 0.5f, z), Vector3.zero, new Vector3(0.06f, 1.0f, 0.06f), iron);
                        var c = r.gameObject.AddComponent<BoxCollider>();   // keeps soldiers out of the canal
                        c.center = new Vector3(0f, 0.55f, 0f); c.size = new Vector3(0.12f, 1.1f, len);
                    }

            // brushwood piles between the landing zone and the canal road, south-west of glider 91 (pb11: seen from
            // beside the gliders looking north-west to the bridge, the piles stretch across the left of the picture,
            // the wreck of 91 on the right)
            Random.InitState(1106);
            // (south of the copse, between the bank road and gliders 92 and 93)
            BrushPile(g, new Vector3(30f, 0f, -64f), 7f, 3.5f, 20f);
            BrushPile(g, new Vector3(36.5f, 0f, -71f), 6f, 3f, -15f);
            BrushPile(g, new Vector3(28.5f, 0f, -79f), 8f, 4f, 35f);
            BrushPile(g, new Vector3(36f, 0f, -86f), 9f, 4.5f, 5f);
            BrushPile(g, new Vector3(31f, 0f, -93f), 6f, 3f, -30f);
            BrushPile(g, new Vector3(41f, 0f, -98f), 8f, 4f, 12f);
            // rolls of wire netting stacked on the west bank beside the bridge (pb01, bottom left)
            WireRolls(g, new Vector3(WestBankRoadX - 4f, 0f, -9.6f), 0f);

            // telegraph poles with two wires along the road to Ranville and the road to Bénouville (pb04, pb05, pb10)
            var lines = new List<List<Vector3>>
            {
                PolesAlongPath(new[] { CanalHalfWidth + 16f, 88f, 200f, 420f }, 45f),
                PolesAlong(new Vector3(-CanalHalfWidth - 16f, 0f, 5.2f), BenouvilleBend + new Vector3(0f, 0f, 5.2f), 40f),
                PolesAlong(BenouvilleBend + new Vector3(0f, 0f, 5.2f), BenouvilleCrossroads + new Vector3(0f, 0f, 5.2f), 45f),
            };
            var wire = Mat("Telegraph_Wire", new Color(0.08f, 0.08f, 0.08f));
            foreach (var poles in lines)
            {
                for (int i = 0; i < poles.Count; i++)
                {
                    var p = new GameObject("Telegraph pole").transform; p.SetParent(g); p.position = poles[i];
                    Prim(PrimitiveType.Cylinder, "Pole", p, new Vector3(0f, 4f, 0f), Vector3.zero, new Vector3(0.24f, 4f, 0.24f), wood, true);
                    Prim(PrimitiveType.Cube, "Crossarm", p, new Vector3(0f, 7.6f, 0f), Vector3.zero, new Vector3(0.1f, 0.1f, 1.6f), wood);
                    if (i == 0) continue;
                    foreach (float o in new[] { -0.7f, 0.7f })
                    {
                        Vector3 a = poles[i - 1] + new Vector3(0f, 7.68f, o), b2 = poles[i] + new Vector3(0f, 7.68f, o);
                        var lr = new GameObject("Wire").AddComponent<LineRenderer>(); lr.transform.SetParent(p, true);
                        lr.useWorldSpace = true; lr.positionCount = 7; lr.widthMultiplier = 0.025f; lr.sharedMaterial = wire;
                        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        for (int k = 0; k < 7; k++) { float t = k / 6f; lr.SetPosition(k, Vector3.Lerp(a, b2, t) + Vector3.down * (4f * t * (1f - t) * 0.6f)); }
                    }
                }
            }
        }

        /// <summary>The Ranville road's centre line (z) at this x.</summary>
        static float RoadZ(float x)
        {
            for (int i = 0; i < RanvilleRoad.Length - 1; i++)
                if (x <= RanvilleRoad[i + 1].x) return Mathf.Lerp(RanvilleRoad[i].y, RanvilleRoad[i + 1].y, Mathf.InverseLerp(RanvilleRoad[i].x, RanvilleRoad[i + 1].x, x));
            return RanvilleRoad[RanvilleRoad.Length - 1].y;
        }

        /// <summary>Poles 5.2 m north of the Ranville road, through these x stations, as one line of wires.</summary>
        static List<Vector3> PolesAlongPath(float[] xs, float spacing)
        {
            var all = new List<Vector3>();
            for (int i = 0; i < xs.Length - 1; i++)
            {
                var part = PolesAlong(new Vector3(xs[i], 0f, RoadZ(xs[i]) + 5.2f), new Vector3(xs[i + 1], 0f, RoadZ(xs[i + 1]) + 5.2f), spacing);
                if (i > 0) part.RemoveAt(0);
                all.AddRange(part);
            }
            return all;
        }

        static List<Vector3> PolesAlong(Vector3 a, Vector3 b, float spacing)
        {
            var list = new List<Vector3>();
            int n = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(a, b) / spacing));
            for (int i = 0; i <= n; i++) { var p = Vector3.Lerp(a, b, i / (float)n); p.y = FloorAt(p); list.Add(p); }
            return list;
        }

        /// <summary>A heap of sandy spoil thrown up from a trench (pb02).</summary>
        static void SpoilHeap(Transform parent, Vector3 pos, float yaw, float length)
        {
            var go = Prim(PrimitiveType.Sphere, "Spoil heap", parent, Vector3.zero, Vector3.zero, new Vector3(length, 0.9f, 1.8f),
                Mat("Spoil_Sand", new Color(0.58f, 0.53f, 0.43f)), true);
            go.transform.SetPositionAndRotation(pos + Vector3.down * 0.15f, Quaternion.Euler(0f, yaw, 0f));
        }
    }
}
