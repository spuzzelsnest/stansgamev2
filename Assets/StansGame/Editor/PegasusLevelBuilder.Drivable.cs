using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Every vehicle in every sector can be driven (Joan, 21 Nov 2026): walk up, E, third-person chase camera.
    /// Wheeled ones get a TruckController (rear-axle steering, front wheels turn with the inner one turning more, all
    /// wheels roll forwards and backwards with the ground covered); tanks have their TankController, and tracks textured
    /// along their length run with the hull (TrackScroll). Wrecks stay where they burnt.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        /// <summary>A parked Opel Blitz anyone can drive away (the moving ones are AmbientLorry).</summary>
        static GameObject ParkedLorry(string model, Transform parent, Vector3 pos, float yaw, string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Models/Props/SME/{model}.fbx");
            if (!prefab) return null;
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(new Vector3(pos.x, GroundAt(pos), pos.z), Quaternion.Euler(0f, yaw, 0f));
            var body = (GameObject)PrefabUtility.InstantiatePrefab(prefab, go.transform);
            body.name = "Body";
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * prefab.transform.localRotation;   // front to +Z
            PaintTruck(body);
            var rs = body.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            var box = go.AddComponent<BoxCollider>();
            box.center = go.transform.InverseTransformPoint(b.center) + new Vector3(0f, 0.15f, 0f);
            var size = go.transform.InverseTransformVector(b.size);
            box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y) - 0.3f, Mathf.Abs(size.z));
            var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true;
            var wheels = new List<Transform>();
            foreach (var t in body.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("Wheel_")) wheels.Add(t);
            var lamp = new GameObject("Notek blackout lamp").AddComponent<Light>();
            lamp.transform.SetParent(go.transform, false);
            lamp.transform.localPosition = new Vector3(-0.6f, 1.0f, box.center.z + box.size.z / 2f + 0.05f);
            lamp.transform.localRotation = Quaternion.Euler(9f, 0f, 0f);
            lamp.type = LightType.Spot; lamp.spotAngle = 75f; lamp.range = 24f; lamp.intensity = 4f;
            lamp.color = new Color(0.78f, 0.82f, 1f); lamp.shadows = LightShadows.None; lamp.enabled = false;
            var tc = go.AddComponent<TruckController>();
            tc.displayName = "Opel Blitz";
            tc.maxForwardSpeed = 21f; tc.maxReverseSpeed = 3.5f; tc.acceleration = 2.2f;   // 3.6-36S: 73 hp for 3.3 t empty, ~75 km/h
            tc.wheels = wheels.ToArray(); tc.wheelRadius = 0.44f; tc.lamps = new[] { lamp };
            if (wheels.Count == 0) Debug.LogWarning($"Stan's Game: {name} has no Wheel_ parts - it drives, but its wheels won't turn");
            NetScenery(go, false);
#if STANS_NET
            if (!go.GetComponent<NetTruck>()) go.AddComponent<NetTruck>();
#endif
            return go;
        }

        /// <summary>The tracks run: for a track material textured along the track, measure from the mesh which way the
        /// texture runs and how fast (texture units a metre along the top run, in the tank's own frame).</summary>
        static void AddTrackScroll(GameObject tank)
        {
            foreach (var r in tank.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh || r.name.StartsWith("Track belt") || !r.enabled) continue;   // belts run already
                var mesh = mf.sharedMesh; var mats = r.sharedMaterials;
                var verts = mesh.vertices; var uvs = mesh.uv;
                if (uvs == null || uvs.Length != verts.Length) continue;
                for (int s = 0; s < mats.Length && s < mesh.subMeshCount; s++)
                {
                    if (!mats[s] || !mats[s].name.ToLower().Contains("track")) continue;
                    var ids = new HashSet<int>(mesh.GetIndices(s));
                    var pts = new List<(Vector3 p, Vector2 uv)>();
                    foreach (int i in ids) pts.Add((tank.transform.InverseTransformPoint(r.transform.TransformPoint(verts[i])), uvs[i]));
                    if (pts.Count < 20) continue;
                    pts.Sort((a, b) => b.p.y.CompareTo(a.p.y));
                    var top = pts.GetRange(0, Mathf.Max(10, pts.Count * 15 / 100));
                    float best = 0f, slope = 0f; int axis = 1;
                    for (int ax = 0; ax < 2; ax++)
                    {
                        double n = top.Count, sz = 0, su = 0, szz = 0, suu = 0, szu = 0;
                        foreach (var q in top) { double z = q.p.z, u = q.uv[ax]; sz += z; su += u; szz += z * z; suu += u * u; szu += z * u; }
                        double cov = szu / n - sz / n * su / n, vz = szz / n - sz * sz / n / n, vu = suu / n - su * su / n / n;
                        if (vz <= 1e-9 || vu <= 1e-9) continue;
                        double corr = cov / System.Math.Sqrt(vz * vu);
                        if (System.Math.Abs(corr) > System.Math.Abs(best)) { best = (float)corr; slope = (float)(cov / vz); axis = ax; }
                    }
                    if (Mathf.Abs(best) < 0.8f) { Debug.Log($"Stan's Game: {tank.name}: track '{mats[s].name}' is modelled link by link - no scrolling"); continue; }
                    var ts = tank.AddComponent<TrackScroll>();
                    ts.track = r; ts.submesh = s; ts.axis = axis; ts.uvPerMetre = slope;
                    Debug.Log($"Stan's Game: {tank.name}: tracks run ({mats[s].name}, {(axis == 0 ? "U" : "V")} {slope:0.00}/m)");
                }
            }
        }

        /// <summary>
        /// Running tracks for a tank whose tracks are modelled link by link (Hotchkiss, StuG), which no texture can slide
        /// round: lay a belt over each side's track outline (the convex outline of its links seen from the side, 1 cm
        /// proud of them), 6 cm thick, with a link texture that repeats (Textures/Generated/TrackLinks.png). TrackScroll
        /// then runs each belt with the tank - each side at its own speed when it turns. Separate track objects
        /// (the Hotchkiss's track_l / track_r) are hidden under their belt; tracks fused with the road wheels stay.
        /// </summary>
        static void AddTrackBelts(GameObject tank)
        {
            var pts = new List<Vector3>();
            foreach (var r in tank.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool isTrack = false;
                foreach (var m in r.sharedMaterials) if (m && m.name.ToLower().Contains("track") && !m.name.StartsWith("Track_Dark")) isTrack = true;
                var mf = r.GetComponent<MeshFilter>();
                if (!isTrack || !mf || !mf.sharedMesh) continue;
                foreach (var v in mf.sharedMesh.vertices) pts.Add(tank.transform.InverseTransformPoint(r.transform.TransformPoint(v)));
                if (Mathf.Min(r.bounds.size.x, r.bounds.size.z) < 0.6f) r.enabled = false;   // one side's own track object: the belt replaces it
            }
            if (pts.Count < 50) { Debug.Log($"Stan's Game: {tank.name}: no link-by-link tracks found"); return; }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/Generated/TrackLinks.png");
            string matPath = $"{Root}/Textures/Generated/TrackBelt.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "TrackBelt" };
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseMap", tex); mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.25f); mat.SetFloat("_Metallic", 0.5f); mat.SetFloat("_Cull", 0f);   // both faces
            mat.doubleSidedGI = true;
            EditorUtility.SetDirty(mat);
            foreach (float side in new[] { -1f, 1f })
            {
                var mine = pts.FindAll(p => p.x * side > 0.05f);
                if (mine.Count < 20) continue;
                float outer = side > 0f ? mine.Max(p => p.x) : mine.Min(p => p.x);
                float inner = outer - side * Mathf.Min(0.42f, Mathf.Abs(outer) - 0.05f);
                var hull = Hull2D(mine.ConvertAll(p => new Vector2(p.z, p.y)));
                if (hull.Count < 3) continue;
                var go = new GameObject(side < 0f ? "Track belt (left)" : "Track belt (right)");
                go.transform.SetParent(tank.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = BeltMesh(hull, Mathf.Min(inner, outer), Mathf.Max(inner, outer), out float uvPerMetre);
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                var ts = tank.AddComponent<TrackScroll>();
                ts.track = go.GetComponent<MeshRenderer>(); ts.submesh = 0; ts.axis = 1; ts.uvPerMetre = uvPerMetre;
                ts.side = side; ts.halfTrack = Mathf.Abs(outer + inner) * 0.5f;
            }
            Debug.Log($"Stan's Game: {tank.name}: track belts laid, they run with the tank");
        }

        /// <summary>Convex outline (counter-clockwise, z right / y up) of the side view of a track.</summary>
        static List<Vector2> Hull2D(List<Vector2> p)
        {
            p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var h = new List<Vector2>();
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                for (int k = 0; k < p.Count; k++)
                {
                    var q = pass == 0 ? p[k] : p[p.Count - 1 - k];
                    while (h.Count >= start + 2 && Cross(h[h.Count - 2], h[h.Count - 1], q) <= 0f) h.RemoveAt(h.Count - 1);
                    h.Add(q);
                }
                h.RemoveAt(h.Count - 1);
            }
            // drop points closer than 2 cm (the hull of thousands of link vertices is very dense round the sprockets)
            var o = new List<Vector2>();
            foreach (var q in h) if (o.Count == 0 || (q - o[o.Count - 1]).sqrMagnitude > 0.0004f) o.Add(q);
            return o;
        }

        /// <summary>The belt round an outline: outer and inner faces and the two edges, v along the belt = links
        /// (a whole number of them round the loop, so the seam can't be seen).</summary>
        static Mesh BeltMesh(List<Vector2> loop, float x0, float x1, out float uvPerMetre)
        {
            int n = loop.Count;
            var normal = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 a = loop[(i + n - 1) % n], b = loop[i], c = loop[(i + 1) % n];
                Vector2 e1 = (b - a).normalized, e2 = (c - b).normalized;
                Vector2 nn = new Vector2(e1.y + e2.y, -(e1.x + e2.x));   // outward for a counter-clockwise loop
                normal[i] = nn.sqrMagnitude > 1e-6f ? nn.normalized : new Vector2(e2.y, -e2.x);
            }
            var outerL = new Vector2[n]; var innerL = new Vector2[n];
            for (int i = 0; i < n; i++) { outerL[i] = loop[i] + normal[i] * 0.012f; innerL[i] = loop[i] - normal[i] * 0.05f; }
            float len = 0f; var at = new float[n + 1];
            for (int i = 0; i < n; i++) { at[i] = len; len += (outerL[(i + 1) % n] - outerL[i]).magnitude; }
            at[n] = len;
            float links = Mathf.Max(8f, Mathf.Round(len / 0.15f));   // a link every ~15 cm
            float k = links / len;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int s0 = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
                tri.Add(s0); tri.Add(s0 + 1); tri.Add(s0 + 2); tri.Add(s0); tri.Add(s0 + 2); tri.Add(s0 + 3);
            }
            Vector3 P(float x, Vector2 q) => new Vector3(x, q.y, q.x);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n; float va = at[i] * k, vb = at[i + 1] * k;
                Quad(P(x0, outerL[i]), P(x1, outerL[i]), P(x1, outerL[j]), P(x0, outerL[j]), new Vector2(0, va), new Vector2(1, va), new Vector2(1, vb), new Vector2(0, vb));
                Quad(P(x0, innerL[j]), P(x1, innerL[j]), P(x1, innerL[i]), P(x0, innerL[i]), new Vector2(0, vb), new Vector2(1, vb), new Vector2(1, va), new Vector2(0, va));
                Quad(P(x0, innerL[i]), P(x0, outerL[i]), P(x0, outerL[j]), P(x0, innerL[j]), new Vector2(0, va), new Vector2(0.05f, va), new Vector2(0.05f, vb), new Vector2(0, vb));
                Quad(P(x1, outerL[i]), P(x1, innerL[i]), P(x1, innerL[j]), P(x1, outerL[j]), new Vector2(0.95f, va), new Vector2(1, va), new Vector2(1, vb), new Vector2(0.95f, vb));
            }
            var mesh = new Mesh { name = "Track belt" };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            // counter-clockwise (z right, y up): along the top run the loop runs towards -z, so v grows backwards
            uvPerMetre = -k;
            return mesh;
        }
    }
}
