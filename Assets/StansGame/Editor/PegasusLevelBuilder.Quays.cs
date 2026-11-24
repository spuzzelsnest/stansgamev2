using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// The canal at the bridge as it was in June 1944 (photos "Pegasus bridge - river side", pb01, the RAF verticals):
    /// concrete quays - a short straight one on the west bank, a long buttressed one on the east bank - and, on the west,
    /// the concrete pier in the canal that the free end of the lifting span rested on, joined to the west quay by a short
    /// fixed concrete span. Concrete from _downloads/concrete-wall (made seamless, with a normal map, by
    /// Models/Source~/concrete_tex.py); timber fender piles and walings from the downloaded log (quay_timber.py).
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        const float QuayTop = 0.08f;          // top of the coping, a touch proud of the bank
        const float PierX0 = -13.6f, PierX1 = -11.4f, PierHalfZ = 6f, PierSeat = -0.35f;

        static Material Concrete() =>
            TexMat("Quay_Concrete", "Concrete_d.png", new Color(0.93f, 0.92f, 0.89f), 0.12f, false, "Concrete_n.png");

        /// <summary>A box of concrete whose texture is laid in metres (one repeat per 2.5 m), so long walls aren't stretched.</summary>
        static GameObject ConcreteBox(string name, Transform parent, Vector3 pos, Vector3 size, float yaw = 0f, bool collider = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            go.AddComponent<MeshFilter>().sharedMesh = UvBoxMesh(name, size, 2.5f);
            go.AddComponent<MeshRenderer>().sharedMaterial = Concrete();
            if (collider) go.AddComponent<BoxCollider>().size = size;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic);
            return go;
        }

        static Mesh UvBoxMesh(string name, Vector3 size, float tile)
        {
            Vector3 h = size * 0.5f;
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            void Face(Vector3 n, Vector3 u, Vector3 v)
            {
                int b = verts.Count;
                Vector3 c = Vector3.Scale(n, h);
                float su = Mathf.Abs(Vector3.Dot(u, h)), sv = Mathf.Abs(Vector3.Dot(v, h));
                var corners = new[] { c - u * su - v * sv, c + u * su - v * sv, c + u * su + v * sv, c - u * su + v * sv };
                foreach (var p in corners)
                {
                    verts.Add(p); norms.Add(n);
                    // world-ish UVs: continuous round the box, the streaks of the concrete running down the walls
                    uvs.Add(new Vector2(Vector3.Dot(p, u) / tile, Vector3.Dot(p, v) / tile));
                }
                int[] t = { 0, 2, 1, 0, 3, 2 };
                if (Vector3.Dot(Vector3.Cross(corners[2] - corners[0], corners[1] - corners[0]), n) < 0f) t = new[] { 0, 1, 2, 0, 2, 3 };
                foreach (int i in t) tris.Add(b + i);
            }
            Face(Vector3.right, Vector3.forward, Vector3.up);
            Face(Vector3.left, Vector3.back, Vector3.up);
            Face(Vector3.forward, Vector3.left, Vector3.up);
            Face(Vector3.back, Vector3.right, Vector3.up);
            Face(Vector3.up, Vector3.right, Vector3.forward);
            Face(Vector3.down, Vector3.right, Vector3.back);
            var m = new Mesh { name = name };
            m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }

        /// <summary>A timber pile with its top at this point (it runs 6.5 m down), or a waling timber along a direction.</summary>
        static GameObject Timber(string file, Transform parent, Vector3 pos, Quaternion rot, string name)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{SmePropDir}/{file}.fbx");
            if (!asset) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;
            go.transform.SetPositionAndRotation(pos, rot * asset.transform.rotation);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        /// <summary>A straight run of quay wall along the canal (water face at |x| = face) from z0 to z1, with its coping.</summary>
        static void QuayRun(Transform parent, string name, float side, float face, float z0, float z1, float buttressEvery = 0f)
        {
            float len = Mathf.Abs(z1 - z0), zc = (z0 + z1) * 0.5f;
            if (len < 0.2f) return;
            ConcreteBox(name, parent, new Vector3(side * (face + 0.6f), -3.05f, zc), new Vector3(1.2f, 6.1f, len));
            ConcreteBox(name + " coping", parent, new Vector3(side * (face + 0.42f), QuayTop - 0.15f, zc), new Vector3(1.15f, 0.3f, len), 0f, false);
            if (buttressEvery <= 0f) return;
            // pb01: the long east quay is ribbed - counterforts standing proud of the wall face every few metres
            int n = Mathf.FloorToInt(len / buttressEvery);
            float start = zc - (n - 1) * buttressEvery * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float z = start + i * buttressEvery;
                ConcreteBox(name + " buttress", parent, new Vector3(side * (face - 0.22f), -3.2f, z), new Vector3(0.45f, 5.9f, 0.7f));
                ConcreteBox(name + " buttress foot", parent, new Vector3(side * (face - 0.4f), -5.0f, z), new Vector3(0.8f, 2.2f, 0.9f), 0f, false);
            }
        }

        static void Apron(Transform parent, float side, float face, float z0, float z1)
        {
            float len = Mathf.Abs(z1 - z0), zc = (z0 + z1) * 0.5f;
            if (len < 0.2f) return;
            ConcreteBox("Quay apron", parent, new Vector3(side * (face + 1.2f + 2f), QuayTop - 0.12f - 1.5f, zc), new Vector3(4f, 3f, len));
        }

        static void QuayEnd(Transform parent, float side, float face, float z)
        {
            float zs = Mathf.Sign(z);
            ConcreteBox("Quay end", parent, new Vector3(side * (face + 1.6f), -3.05f, z + zs * 0.4f), new Vector3(3.6f, 6.1f, 1.0f), side * zs * 25f);
        }

        /// <summary>The quays at the bridge: west short and straight, east long and buttressed, with timber fenders.</summary>
        static void BuildQuays(Transform canal)
        {
            var g = Group("Quays (1944)", canal);
            float wq = Water.WestQuay, eq = Water.QuayHalf;

            // west quay: straight, the fixed span lands on it - part of the bridge model now (WestSpan, bridge1934.py),
            // in one piece with the pier and the fixed span so nothing can gape between them (printscreen 51)
            bool westInModel = WestSpanInModel();
            if (!westInModel)
            {
                QuayRun(g, "Quay wall (west)", -1f, wq, -Water.QuayLength - 0.5f, Water.QuayLength + 0.5f);
                QuayEnd(g, -1f, wq, -Water.QuayLength); QuayEnd(g, -1f, wq, Water.QuayLength);
            }

            // east quay: interrupted by the counterweight pit (part of the bridge model), long and ribbed to the south
            QuayRun(g, "Quay wall (east)", 1f, eq, PitHalf, Water.EastQuayNorth + 0.5f, 4.5f);
            QuayRun(g, "Quay wall (east)", 1f, eq, -PitHalf, -Water.EastQuaySouth - 0.5f, 4.5f);
            QuayEnd(g, 1f, eq, Water.EastQuayNorth); QuayEnd(g, 1f, eq, -Water.EastQuaySouth);
            // the apron behind the quays (printscreen 51: a gap between the bank, the quay and the bridge): a concrete
            // apron 4 m wide at the height of the coping, so wherever the cut bank dips below the quay top it is filled
            if (!westInModel) Apron(g, -1f, wq, -Water.QuayLength - 0.5f, Water.QuayLength + 0.5f);
            // (east: only by the bridge - further south the German fire trench is dug in right behind the quay)
            Apron(g, 1f, eq, PitHalf, Mathf.Min(Water.EastQuayNorth, Water.QuayLength) + 0.5f);
            Apron(g, 1f, eq, -PitHalf, -Mathf.Min(Water.EastQuaySouth, Water.QuayLength) - 0.5f);

            // timber fender piles along the east quay by the bridge (boats lining up for the opening span), tied by a waling
            var t = Group("Timber fenders", g);
            foreach (float zs in new[] { -1f, 1f })
            {
                for (float z = PitHalf + 1.2f; z <= Water.QuayLength + 0.1f; z += 2.4f)
                    Timber("TimberPile", t, new Vector3(eq - 0.18f, QuayTop + 0.45f, zs * z), Quaternion.Euler(0f, z * 37f, 0f), "Fender pile");
                for (float z = PitHalf + 2.4f; z < Water.QuayLength - 1f; z += 3.9f)
                    Timber("TimberWale", t, new Vector3(eq - 0.4f, -0.9f, zs * (z + 1.95f)), Quaternion.identity, "Fender waling");
            }
        }

        /// <summary>
        /// The west end of the 1934 bridge: the concrete pier in the canal under the free end of the lifting span (it
        /// sits in a seat 0.35 m down), and the fixed concrete span with solid parapets from the pier to the west quay.
        /// Timber guard piles off both ends of the pier.
        /// </summary>
        /// <summary>Does the bridge model carry the west pier, fixed span and quay itself (object "WestSpan")?</summary>
        static bool WestSpanInModel()
        {
            var m = AssetDatabase.LoadAssetAtPath<GameObject>(BridgeModel);
            return m && FindDeep(m.transform, "WestSpan");
        }

        static void BuildWestSpan(Transform bridgeRoot)
        {
            var g = Group("West pier and fixed span", bridgeRoot);
            float wq = Water.WestQuay;
            float px = (PierX0 + PierX1) * 0.5f, pw = PierX1 - PierX0;
            var inModel = FindDeep(bridgeRoot, "WestSpan");
            if (inModel)
            {
                // the model's own pier, span and quay: our concrete on it (UVs laid in metres in Blender), solid to walk on
                var r = inModel.GetComponent<MeshRenderer>();
                if (r)
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) if (mats[i] && mats[i].name.Contains("Concrete")) mats[i] = Concrete();
                    r.sharedMaterials = mats;
                }
                var mf = inModel.GetComponent<MeshFilter>();
                if (mf && !inModel.GetComponent<Collider>()) inModel.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                GameObjectUtility.SetStaticEditorFlags(inModel.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic);
            }

            // the pier, from the canal bed to the seat, with a cap and rounded-off (chamfered) ends
            if (!inModel) ConcreteBox("Bridge pier (west)", g, new Vector3(px, (CanalDepth - 0.2f + PierSeat - 0.3f) * 0.5f, 0f), new Vector3(pw, PierSeat - 0.3f - CanalDepth + 0.2f, PierHalfZ * 2f));
            if (!inModel) ConcreteBox("Bridge pier cap", g, new Vector3(px, PierSeat - 0.15f, 0f), new Vector3(pw + 0.25f, 0.3f, PierHalfZ * 2f + 0.25f));
            foreach (float zs in new[] { -1f, 1f })
            {
                if (!inModel) ConcreteBox("Bridge pier cutwater", g, new Vector3(px, (CanalDepth + PierSeat - 0.3f) * 0.5f, zs * PierHalfZ), new Vector3(pw * 0.7071f, PierSeat - 0.3f - CanalDepth, pw * 0.7071f), 45f);
                // timber guard: three piles off each end of the pier, tied with walings
                var t = Group(zs < 0 ? "Pier guard (south)" : "Pier guard (north)", g);
                for (int i = -1; i <= 1; i++)
                    Timber("TimberPile", t, new Vector3(px + i * 0.9f, PierSeat + 0.6f, zs * (PierHalfZ + 1.6f)), Quaternion.Euler(0f, i * 70f, 0f), "Guard pile");
                foreach (float y in new[] { -0.2f, -1.2f })
                    Timber("TimberWale", t, new Vector3(px, y, zs * (PierHalfZ + 1.35f)), Quaternion.Euler(0f, 90f, 0f), "Guard waling");
                foreach (float dx in new[] { -1.4f, 1.4f })   // raking piles bracing the guard back to the pier
                    Timber("TimberPile", t, new Vector3(px + dx, PierSeat + 0.2f, zs * (PierHalfZ + 0.9f)), Quaternion.Euler(-zs * 12f, 0f, Mathf.Sign(dx) * 8f), "Raking pile");
            }

            // the fixed span: a concrete deck from the west quay to the pier, solid parapets, beams under it
            float x0 = -(wq + 0.35f), x1 = -(BridgeWestEnd + 0.05f);   // from over the quay coping to the leaf's end
            float xc = (x0 + x1) * 0.5f, deck = 0.55f, halfW = 4.1f;
            if (inModel) { Debug.Log("Stan's Game: west pier, fixed span and west quay from the bridge model (WestSpan)"); return; }
            ConcreteBox("Fixed span deck", g, new Vector3(xc, QuayTop - deck * 0.5f, 0f), new Vector3(x1 - x0, deck, halfW * 2f));
            foreach (float zs in new[] { -1f, 1f })
            {
                ConcreteBox("Fixed span parapet", g, new Vector3(xc, QuayTop + 0.45f, zs * (halfW - 0.15f)), new Vector3(x1 - x0, 0.9f, 0.3f));
                ConcreteBox("Fixed span parapet coping", g, new Vector3(xc, QuayTop + 0.94f, zs * (halfW - 0.15f)), new Vector3(x1 - x0 + 0.1f, 0.08f, 0.4f), 0f, false);
                ConcreteBox("Fixed span beam", g, new Vector3(xc, QuayTop - deck - 0.3f, zs * 2.6f), new Vector3(x1 - x0, 0.6f, 0.5f), 0f, false);
            }
            ConcreteBox("Fixed span beam", g, new Vector3(xc, QuayTop - deck - 0.3f, 0f), new Vector3(x1 - x0, 0.6f, 0.5f), 0f, false);
            Debug.Log($"Stan's Game: west pier x {PierX0:F1}..{PierX1:F1}, fixed span x {x0:F1}..{x1:F1} ({x1 - x0:F1} m), west quay at {-wq:F1}, east quay {Water.QuayHalf:F1} from z {-Water.EastQuaySouth} to {Water.EastQuayNorth}");
        }

        /// <summary>Is this spot over water (the canal, the Orne, the pond and ditches), not counting the bridge?</summary>
        static bool Wet(Vector3 p)
        {
            bool onBridge = Mathf.Abs(p.z) < 4.1f && p.x > -(Water.WestQuay + 0.4f) && p.x < BridgeEastEnd;
            if (onBridge && p.y > WaterLevel + 0.5f) return false;
            return Mathf.Abs(p.x) < Water.CanalShore(p.z, Mathf.Sign(p.x)) + 0.4f || OrneDistance(p.x, p.z) < 2f
                || (real != null && RealY(p.x, p.z) < MarshWaterY + 0.15f);
        }

        /// <summary>
        /// Nobody starts the level in the water or under the ground: every soldier (anything with Health) and every
        /// spawn or start marker that ended up in the canal, a ditch or buried in the terrain is moved to the nearest dry
        /// ground (spiralling out up to 40 m) and set on it. Logs whom it moved.
        /// </summary>
        static void RescueFromWater(Transform root)
        {
            Physics.SyncTransforms();
            var check = new List<Transform>();
            foreach (var h in root.GetComponentsInChildren<Health>(true))
                if (!h.GetComponentInParent<GliderApproach>(true) && !h.GetComponentInParent<TankController>(true)) check.Add(h.transform);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if ((n.Contains("spawn") || n.Contains(" start")) && !t.GetComponentInParent<GliderApproach>(true)) check.Add(t);
            }
            int moved = 0;
            foreach (var t in check)
            {
                if (check.IndexOf(t) != check.LastIndexOf(t)) continue;
                Vector3 p = t.position;
                float ground = GroundAt(p);
                bool wet = Wet(p), buried = p.y < ground - 1.6f;   // (men in slit trenches and gun pits stand ~0.75 m down on purpose)
                if (!wet && !buried) continue;
                Vector3 best = p; bool found = !wet;
                for (float r = 1.5f; r <= 40f && !found; r += 1.5f)
                    for (int a = 0; a < 16 && !found; a++)
                    {
                        var q = p + Quaternion.Euler(0f, a * 22.5f, 0f) * Vector3.forward * r;
                        if (!Wet(q)) { best = q; found = true; }
                    }
                if (!found) { Debug.LogWarning($"Stan's Game: {t.name} at {p} is in the water and no dry ground was found near it"); continue; }
                best.y = GroundAt(best) + (t.GetComponent<Health>() ? 0.05f : 0f);
                Debug.Log($"Stan's Game: moved {t.name} out of {(wet ? "the water" : "the ground")}: {p} -> {best}");
                t.position = best; moved++;
            }
            Debug.Log($"Stan's Game: water check - {check.Count} soldiers and spawn points, {moved} moved to dry ground");
        }
    }
}
