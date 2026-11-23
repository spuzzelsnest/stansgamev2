using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// The vehicles' last pass of every level build (23 Nov, "Texture adjust and colour schema").
    /// 1. Glass: every window, windscreen and cockpit pane becomes see-through (it was a dark mirror-like pane).
    /// 2. Markings, after the June 1944 practice. They are laid on the hull by casting a ray at it, so they sit on the
    ///    real surface, and hang on the part they hit (a turret number turns with the turret):
    ///    - American: the white star in a circle on the hull sides and on the engine deck (air recognition).
    ///    - British lorries: the Allied white star on the cab doors and roof, the yellow bridge-classification disc and
    ///      the arm-of-service sign (RASC) on the front.
    ///    - German: the Balkenkreuz on the hull sides and rear, the three-figure turret number of the 21st Panzer
    ///      Division's tanks; "WH" (Wehrmacht Heer) number plates on the lorries and cars.
    /// The camouflage itself is in the textures (Models/Source~/normandy_paint.py): Dunkelgelb with Olivgruen and
    /// Rotbraun bands for the Germans, Olive Drab / SCC 15 with Normandy dust for the Allies.
    /// Report: Logs/vehicles-&lt;level&gt;.txt.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        const string MarkDir = Root + "/Textures/Markings";

        struct VMark { public string tex; public char side; public float along, height, size, aspect, metres; }

        /// <summary>side: L (left, -x), R (right, +x), F (front, +z), B (back, -z), T (top), U (turret roof:
        /// only the turret is hit, and the mark turns with it - printscreen 39, the deck star floated beside the turret). along: -0.5 (rear / left)
        /// to 0.5 (front / right). height: 0 (ground) to 1 (top of the vehicle). size: height in metres.</summary>
        static VMark Mk(string tex, char side, float along, float height, float size, float aspect = 1f)
            => new VMark { tex = tex, side = side, along = along, height = height, size = size, aspect = aspect };

        /// <summary>A marking at a set height in metres above the ground (from the regulations and photographs),
        /// rather than a share of the vehicle's height, which an aerial or a stowage rack can stretch.</summary>
        static VMark MkAt(string tex, char side, float along, float metres, float size, float aspect = 1f)
            => new VMark { tex = tex, side = side, along = along, metres = metres, size = size, aspect = aspect };

        static readonly (string match, string[] exclude, VMark[] marks)[] VehicleMarks =
        {
            ("Sherman", null, new[] { Mk("Star_Circle", 'L', 0.05f, 0.42f, 0.75f), Mk("Star_Circle", 'R', 0.05f, 0.42f, 0.75f), Mk("Star_Circle", 'U', 0f, 0f, 0.8f) }),
            ("Stuart", null, new[] { Mk("Star", 'L', 0.05f, 0.45f, 0.5f), Mk("Star", 'R', 0.05f, 0.45f, 0.5f), Mk("Star_Circle", 'U', 0f, 0f, 0.6f) }),
            ("Bedford", null, new[] { Mk("Star", 'L', 0.33f, 0.5f, 0.42f), Mk("Star", 'R', 0.33f, 0.5f, 0.42f), Mk("Star_Circle", 'T', 0.33f, 0f, 0.6f),
                                      Mk("Bridge_5", 'F', 0.28f, 0.32f, 0.22f), Mk("ArmOfService_RASC", 'F', -0.28f, 0.32f, 0.2f) }),
            // Panzer IV H in Normandy (photographs of 21. Pz.Div. and 12. SS-Pz.Div. tanks, e.g. Bundesarchiv
            // 101I-297-1722-29): the cross centred on the hull Schuerzen a little behind mid-length, about 1.3 m up;
            // the turret number (30-35 cm digits, black with a white edge) on both sides of the turret Schuerzen
            ("Panzer IV", new[] { "Wirbelwind", "Flakpanzer" }, new[] { MkAt("Balkenkreuz", 'L', -0.1f, 1.3f, 0.55f), MkAt("Balkenkreuz", 'R', -0.1f, 1.3f, 0.55f),
                                      MkAt("Balkenkreuz", 'B', 0f, 1.25f, 0.45f), MkAt("Turret_421", 'L', 0.05f, 2.05f, 0.33f, 2.67f), MkAt("Turret_421", 'R', 0.05f, 2.05f, 0.33f, 2.67f) }),
            ("Hotchkiss", null, new[] { Mk("Balkenkreuz_Outline", 'L', -0.2f, 0.45f, 0.42f), Mk("Balkenkreuz_Outline", 'R', -0.2f, 0.45f, 0.42f) }),
            ("Kübelwagen", null, new[] { Mk("Plate_WH", 'F', 0f, 0.28f, 0.11f, 4f), Mk("Plate_WH", 'B', 0f, 0.35f, 0.11f, 4f) }),
            ("Kettenkrad", null, new[] { Mk("Plate_WH", 'B', 0f, 0.55f, 0.1f, 4f) }),
            ("Opel Blitz", null, new[] { Mk("Plate_WH", 'F', 0f, 0.14f, 0.12f, 4f), Mk("Plate_WH", 'B', 0f, 0.16f, 0.12f, 4f) }),
        };

        static readonly Dictionary<string, Material> markMats = new Dictionary<string, Material>();

        static Material MarkMat(string tex)
        {
            if (markMats.TryGetValue(tex, out var done) && done) return done;
            string path = $"{MarkDir}/{tex}.png";
            if (AssetImporter.GetAtPath(path) is TextureImporter ti && (!ti.alphaIsTransparency || ti.wrapMode != TextureWrapMode.Clamp))
            { ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport(); }
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var m = Mat("Mark_" + tex, Color.white, 0.25f);
            m.SetTexture("_BaseMap", t); m.mainTexture = t;
            m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = 2450;
            EditorUtility.SetDirty(m);
            return markMats[tex] = m;
        }

        /// <summary>Window glass you can see through: a faint, smooth, transparent pane.</summary>
        static Material SeeThroughGlass()
        {
            var m = Mat("Glass_SeeThrough", new Color(0.55f, 0.62f, 0.66f, 0.2f), 0.95f, 0f);
            m.SetColor("_BaseColor", new Color(0.55f, 0.62f, 0.66f, 0.2f));
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f);   // both faces: seen from inside too
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = 3000;
            EditorUtility.SetDirty(m);
            return m;
        }

        static bool IsWindowGlass(Material m)
        {
            if (!m) return false;
            string n = m.name.ToLowerInvariant();
            if (n.Contains("leaded") || n.Contains("headlight") || n.Contains("lamp") || n.Contains("bottle") || n == "glass_seethrough") return false;
            return n.Contains("glass") || n.Contains("window_pane") || n.Contains("windscreen") || n.Contains("windshield");
        }

        static void DressVehicles(Transform root, string level)
        {
            markMats.Clear();
            var report = new StringBuilder();
            report.AppendLine($"Vehicles - {level} - {System.DateTime.Now:yyyy-MM-dd HH:mm}");

            // ---- the vehicles: anything that drives (or is driven), plus parked lorries and cars by name
            var roots = new List<Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Sign") || t.name.StartsWith("Label")) continue;
                bool driven = t.GetComponent<TankController>() || t.GetComponent<TruckController>();
                bool named = VehicleMarks.Any(v => t.name.Contains(v.match)) || t.name.Contains("Willys") || t.name.Contains("jeep");
                if (!driven && !named) continue;
                if (!driven && (t.GetComponent<Animator>() || t.GetComponent<Health>())) continue;   // a driver, not his lorry
                if (roots.Any(r => t.IsChildOf(r))) continue;
                if (!t.GetComponentInChildren<Renderer>()) continue;
                roots.RemoveAll(r => r.IsChildOf(t));
                roots.Add(t);
            }

            // ---- glass
            var glass = SeeThroughGlass(); int panes = 0;
            foreach (var v in roots)
                foreach (var r in v.GetComponentsInChildren<Renderer>(true))
                {
                    var ms = r.sharedMaterials; bool changed = false;
                    for (int i = 0; i < ms.Length; i++) if (IsWindowGlass(ms[i])) { ms[i] = glass; changed = true; }
                    if (changed) { r.sharedMaterials = ms; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; panes++; }
                }
            report.AppendLine($"\n{roots.Count} vehicles; see-through glass on {panes} parts");

            // ---- markings
            int placed = 0;
            foreach (var v in roots)
            {
                var spec = VehicleMarks.FirstOrDefault(s => v.name.Contains(s.match) && (s.exclude == null || !s.exclude.Any(x => v.name.Contains(x))));
                if (spec.marks == null) continue;
                // a non-convex mesh collider is switched off under a moving rigidbody: hold the tanks still meanwhile
                var bodies = v.GetComponentsInChildren<Rigidbody>(true).Select(rb => (rb, rb.isKinematic)).ToList();
                foreach (var (rb, _) in bodies) rb.isKinematic = true;
                var temp = new List<MeshCollider>();
                foreach (var mf in v.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!mf.sharedMesh || mf.GetComponent<Renderer>() is Renderer rr && rr.sharedMaterials.Any(IsWindowGlass)) continue;
                    if (mf.name.StartsWith("Mark ")) continue;
                    var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; temp.Add(mc);
                }
                Physics.SyncTransforms();
                // the vehicle's box in its own frame
                var b = new Bounds(); bool first = true;
                foreach (var mf in v.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!mf.sharedMesh) continue;
                    var mb = mf.sharedMesh.bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        var p = v.InverseTransformPoint(mf.transform.TransformPoint(new Vector3(c % 2 == 0 ? mb.min.x : mb.max.x, (c / 2) % 2 == 0 ? mb.min.y : mb.max.y, c / 4 == 0 ? mb.min.z : mb.max.z)));
                        if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
                    }
                }
                if (Mathf.Max(b.size.x, b.size.z) < 2.5f) { foreach (var mc in temp) Object.DestroyImmediate(mc); foreach (var (rb, kin) in bodies) rb.isKinematic = kin; continue; }   // not a vehicle after all
                var mine = new HashSet<Collider>(temp);
                var line = new StringBuilder($"  {BldPath(v)}:");
                foreach (var mk in spec.marks)
                {
                    Vector3 o, d, up = Vector3.up;
                    float y = b.min.y + (mk.metres > 0f ? mk.metres : mk.height * b.size.y), z = b.center.z + mk.along * b.size.z, x = b.center.x + mk.along * b.size.x;
                    Transform tur = null;
                    if (mk.side == 'U')
                    {
                        tur = v.GetComponentsInChildren<Transform>(true).FirstOrDefault(c => c.name == "Turret");
                        var tr = tur ? tur.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh).ToArray() : new MeshFilter[0];
                        if (tr.Length == 0) { line.Append($" {mk.tex}@U no turret;"); continue; }
                        var tb = new Bounds(v.InverseTransformPoint(tr[0].transform.TransformPoint(tr[0].sharedMesh.bounds.center)), Vector3.zero);
                        foreach (var f in tr) { var mb = f.sharedMesh.bounds; for (int c = 0; c < 8; c++) tb.Encapsulate(v.InverseTransformPoint(f.transform.TransformPoint(new Vector3(c % 2 == 0 ? mb.min.x : mb.max.x, (c / 2) % 2 == 0 ? mb.min.y : mb.max.y, c / 4 == 0 ? mb.min.z : mb.max.z)))); }
                        x = tb.center.x; z = tb.center.z - 0.15f * tb.size.z + mk.along * tb.size.z;   // behind the gun mantlet, on the flat roof
                    }
                    switch (mk.side)
                    {
                        case 'U': o = new Vector3(x, b.max.y + 1f, z); d = Vector3.down; up = Vector3.forward; break;
                        case 'L': o = new Vector3(b.min.x - 1f, y, z); d = Vector3.right; break;
                        case 'R': o = new Vector3(b.max.x + 1f, y, z); d = Vector3.left; break;
                        case 'F': o = new Vector3(x, y, b.max.z + 1f); d = Vector3.back; break;
                        case 'B': o = new Vector3(x, y, b.min.z - 1f); d = Vector3.forward; break;
                        default: o = new Vector3(b.center.x, b.max.y + 1f, z); d = Vector3.down; up = Vector3.forward; break;
                    }
                    Vector3 wo = v.TransformPoint(o), wd = v.TransformDirection(d);
                    var hits = Physics.RaycastAll(wo, wd, b.size.magnitude + 3f).Where(h => mine.Contains(h.collider) && (!tur || h.collider.transform.IsChildOf(tur))).OrderBy(h => h.distance).ToArray();
                    if (hits.Length == 0) { line.Append($" {mk.tex}@{mk.side} missed;"); continue; }
                    var hit = hits[0];
                    if (Vector3.Dot(hit.normal, -wd) < 0.2f) { line.Append($" {mk.tex}@{mk.side} too slanted;"); continue; }
                    var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Object.DestroyImmediate(q.GetComponent<Collider>());
                    q.name = $"Mark {mk.tex}";
                    var wup = v.TransformDirection(up);
                    q.transform.SetPositionAndRotation(hit.point + hit.normal * 0.012f, Quaternion.LookRotation(-hit.normal, Vector3.ProjectOnPlane(wup, hit.normal).sqrMagnitude > 0.01f ? wup : v.forward));
                    q.transform.localScale = new Vector3(mk.size * mk.aspect, mk.size, 1f);
                    q.transform.SetParent(hit.collider.transform, true);
                    var qr = q.GetComponent<MeshRenderer>();
                    qr.sharedMaterial = MarkMat(mk.tex);
                    qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    placed++; line.Append($" {mk.tex}@{mk.side};");
                }
                foreach (var mc in temp) Object.DestroyImmediate(mc);
                foreach (var (rb, kin) in bodies) rb.isKinematic = kin;
                report.AppendLine(line.ToString());
            }
            report.Insert(report.ToString().IndexOf('\n') + 1, $"\nMarkings placed: {placed}\n");
            try
            {
                string logs = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Logs");
                System.IO.Directory.CreateDirectory(logs);
                System.IO.File.WriteAllText(System.IO.Path.Combine(logs, $"vehicles-{level}.txt"), report.ToString());
            }
            catch (System.Exception ex) { Debug.LogWarning("Stan's Game: vehicles report not written: " + ex.Message); }
            Debug.Log($"Stan's Game: {level} vehicles - {roots.Count} vehicles, {panes} glass parts see-through, {placed} markings (Logs/vehicles-{level}.txt)");
        }
    }
}
