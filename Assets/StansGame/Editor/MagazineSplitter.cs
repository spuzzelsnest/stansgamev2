using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Cuts the magazine out of a weapon model (Sten, MP 40) into its own "Magazine" child, so a reload can pull it
    /// out, drop it and bring a fresh one from the pouch (see Weapon.magazine and HandIK's reload).
    ///
    /// The model is one mesh, so the magazine is found from its loose parts: the parts that reach well out from the
    /// barrel's line (sideways for the Sten, downwards for the MP 40), plus every small part lying inside them (the base
    /// plate, the ribs). The two meshes are saved once to Models/Weapons/Split/ and reused by every soldier.
    /// Called from AddWeapon for every gun the level builders make.
    /// </summary>
    public static class MagazineSplitter
    {
        const string Dir = "Assets/StansGame/Models/Weapons/Split";

        enum Kind { None, Side, Down }

        static Kind KindOf(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("sten")) return Kind.Side;
            if (n.Contains("mp40") || n.Contains("mp 40")) return Kind.Down;
            return Kind.None;
        }

        [MenuItem("Stan's Game/Setup/Re-cut Weapon Magazines (after a weapon model changes)", priority = 260)]
        static void ClearCache()
        {
            if (AssetDatabase.IsValidFolder(Dir)) AssetDatabase.DeleteAsset(Dir);
            cache.Clear();
            Debug.Log("Stan's Game: the cut magazines will be made again at the next level build");
        }

        static readonly Dictionary<string, (Mesh body, Mesh mag)> cache = new Dictionary<string, (Mesh, Mesh)>();

        /// <summary>Gives the weapon a separate magazine, if it has one to cut.</summary>
        public static void Apply(Weapon w)
        {
            if (!w || w.magazine) return;
            var kind = KindOf(w.displayName);
            if (kind == Kind.None) return;
            var mf = w.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh)
                      .OrderByDescending(m => m.sharedMesh.vertexCount).FirstOrDefault();
            if (!mf) return;
            var mr = mf.GetComponent<MeshRenderer>();
            if (!mr) return;
            string key = Safe(Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(mf.sharedMesh)) + "_" + mf.sharedMesh.name);
            if (!cache.TryGetValue(key, out var pair) || !pair.body || !pair.mag)
            {
                string path = $"{Dir}/{key}.asset";
                var all = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToArray();
                pair = (all.FirstOrDefault(m => m.name.EndsWith("_body")), all.FirstOrDefault(m => m.name.EndsWith("_mag")));
                if (!pair.body || !pair.mag)
                {
                    pair = Cut(w, mf, kind, key, true, out string why);
                    if (!pair.body) { pair = Cut(w, mf, kind, key, false, out string why2); why = pair.body ? why2 + " (parts not welded)" : why + "; unwelded: " + why2; }
                    if (!pair.body) { Debug.LogWarning($"Stan's Game: no magazine cut from {w.displayName}: {why}"); return; }
                    if (!AssetDatabase.IsValidFolder(Dir)) { Directory.CreateDirectory(Dir); AssetDatabase.Refresh(); }
                    AssetDatabase.CreateAsset(pair.body, path);
                    AssetDatabase.AddObjectToAsset(pair.mag, path);
                    AssetDatabase.SaveAssets();
                    Debug.Log($"Stan's Game: magazine cut from {w.displayName}: {why}");
                }
                cache[key] = pair;
            }
            mf.sharedMesh = pair.body;
            var mag = new GameObject("Magazine");
            mag.transform.SetParent(mf.transform, false);
            mag.AddComponent<MeshFilter>().sharedMesh = pair.mag;
            var r = mag.AddComponent<MeshRenderer>();
            r.sharedMaterials = mr.sharedMaterials;
            w.magazine = mag.transform;
        }

        static string Safe(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray());

        static (Mesh, Mesh) Cut(Weapon w, MeshFilter mf, Kind kind, string key, bool weld, out string why)
        {
            var src = mf.sharedMesh;
            Vector3[] v = src.vertices;
            int n = v.Length;
            // vertices in the weapon's own space (the space the hand points use)
            var p = new Vector3[n];
            for (int i = 0; i < n; i++) p[i] = w.transform.InverseTransformPoint(mf.transform.TransformPoint(v[i]));

            // loose parts: vertices joined by triangles, and vertices at the same spot (split by UV seams) joined too
            var parent = Enumerable.Range(0, n).ToArray();
            int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
            void Join(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
            var spot = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < n && weld; i++)
            {
                var q = Vector3Int.RoundToInt(v[i] * 20000f);
                if (spot.TryGetValue(q, out int j)) Join(i, j); else spot[q] = i;
            }
            int subs = src.subMeshCount;
            var tris = new int[subs][];
            for (int s = 0; s < subs; s++)
            {
                tris[s] = src.GetTriangles(s);
                for (int t = 0; t < tris[s].Length; t += 3) { Join(tris[s][t], tris[s][t + 1]); Join(tris[s][t], tris[s][t + 2]); }
            }
            var parts = new Dictionary<int, Bounds>();
            for (int i = 0; i < n; i++)
            {
                int r = Find(i);
                if (parts.TryGetValue(r, out var b)) { b.Encapsulate(p[i]); parts[r] = b; } else parts[r] = new Bounds(p[i], Vector3.zero);
            }

            // the barrel's line
            Vector3 bore = w.muzzle ? w.transform.InverseTransformPoint(w.muzzle.position) : Vector3.zero;
            var core = parts.Where(kv => kind == Kind.Side
                    ? Mathf.Max(Mathf.Abs(kv.Value.min.x - bore.x), Mathf.Abs(kv.Value.max.x - bore.x)) > 0.09f
                    : kv.Value.min.y < bore.y - 0.19f).Select(kv => kv.Key).ToList();
            if (kind == Kind.Down)
            {
                // the MP 40's pistol grip hangs low too, but behind the trigger: keep only the part(s) furthest down
                float lowest = core.Count > 0 ? core.Min(k => parts[k].min.y) : 0f;
                core = core.Where(k => parts[k].min.y < lowest + 0.06f).ToList();
            }
            if (core.Count == 0) { why = $"nothing reaches out from the barrel line {bore:F3} ({parts.Count} parts)"; return (null, null); }
            var box = parts[core[0]];
            foreach (var k in core) box.Encapsulate(parts[k]);
            box.Expand(0.006f);
            var inMag = new HashSet<int>(core);
            foreach (var kv in parts)
                if (box.Contains(kv.Value.min) && box.Contains(kv.Value.max) && kv.Value.size.magnitude < box.size.magnitude * 0.9f) inMag.Add(kv.Key);
            // never more than half the gun
            int magVerts = Enumerable.Range(0, n).Count(i => inMag.Contains(Find(i)));
            if (magVerts > n / 2) { why = $"the cut took {magVerts} of {n} vertices: too much, left whole"; return (null, null); }

            Mesh Make(bool mag, string name)
            {
                var map = new int[n];
                for (int i = 0; i < n; i++) map[i] = -1;
                var keep = new List<int>();
                var sub = new List<int>[subs];
                for (int s = 0; s < subs; s++)
                {
                    sub[s] = new List<int>();
                    for (int t = 0; t < tris[s].Length; t += 3)
                    {
                        if (inMag.Contains(Find(tris[s][t])) != mag) continue;
                        for (int c = 0; c < 3; c++)
                        {
                            int i = tris[s][t + c];
                            if (map[i] < 0) { map[i] = keep.Count; keep.Add(i); }
                            sub[s].Add(map[i]);
                        }
                    }
                }
                var m = new Mesh { name = name };
                if (keep.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.vertices = keep.Select(i => v[i]).ToArray();
                var nn = src.normals; if (nn.Length == n) m.normals = keep.Select(i => nn[i]).ToArray();
                var tt = src.tangents; if (tt.Length == n) m.tangents = keep.Select(i => tt[i]).ToArray();
                var cc = src.colors; if (cc.Length == n) m.colors = keep.Select(i => cc[i]).ToArray();
                for (int ch = 0; ch < 4; ch++)
                {
                    var uv = new List<Vector2>(); src.GetUVs(ch, uv);
                    if (uv.Count == n) m.SetUVs(ch, keep.Select(i => uv[i]).ToList());
                }
                m.subMeshCount = subs;
                for (int s = 0; s < subs; s++) m.SetTriangles(sub[s], s);
                m.RecalculateBounds();
                return m;
            }
            why = $"{inMag.Count} parts, {magVerts} of {n} vertices, magazine box {box.center:F3} size {box.size:F3}, barrel line {bore:F3}";
            return (Make(false, key + "_body"), Make(true, key + "_mag"));
        }
    }
}
