using System.Collections.Generic;
using UnityEngine;

namespace StansGame.Additions
{
    /// <summary>
    /// Iron sights for aiming in first person. Weapons whose models carry their own sights (the Sten's
    /// rear peep plate and front blade, the pistol's notch and blade, ...) are aimed through those: only
    /// the measured sight line is stored and nothing is added to the model.
    /// For any other weapon the sights are built once from small generated meshes in the weapon's own material and
    /// parented to it: a front post near the muzzle and a rear peep aperture (Sten, Bren, rifles) or a
    /// front blade and rear notch (pistol). The sight line runs parallel to the bore just above the
    /// model, so the tip of the front post sits exactly on the point the rounds go to.
    /// The PIAT (launcher) is left out; its sight is on the side and is handled by the plain aimed view.
    /// With the Unity source these become real sight meshes on the weapon models.
    /// </summary>
    public sealed class IronSights : MonoBehaviour
    {
        /// <summary>Where the eye sits to look through the sights, in the weapon's local space.</summary>
        public Vector3 EyeLocal { get; private set; }

        /// <summary>Sight line height and front post position, in the weapon's local space (for tests).</summary>
        public Vector3 FrontTipLocal { get; private set; }
        public Vector3 RearCentreLocal { get; private set; }

        /// <summary>
        /// Turn that lays the sight line (rear sight to front sight) along the weapon's forward axis.
        /// Identity for generated sights; a fraction of a degree for most models' own sights.
        /// </summary>
        public Quaternion SightTurn { get; private set; }

        /// <summary>True when the weapon model has its own sights and nothing was generated.</summary>
        public bool ModelSights { get; private set; }

        /// <summary>
        /// The sights already on the weapon models, measured from renders of the bare models (their
        /// meshes cannot be read at run time). Millimetres from the muzzle point, in the weapon's own
        /// axes after <see cref="WeaponPose.AlignModel"/>: x right, y up, z forward (negative = behind
        /// the muzzle). Rear = centre of the peep hole, or the bottom of the notch; front = tip of the
        /// blade or post; eye = how far behind the rear sight the eye sits.
        /// </summary>
        struct Measured
        {
            public float rearX, rearY, rearZ, frontX, frontY, frontZ, eye;
            public Measured(float rearX, float rearY, float rearZ, float frontX, float frontY, float frontZ, float eye)
            {
                this.rearX = rearX; this.rearY = rearY; this.rearZ = rearZ;
                this.frontX = frontX; this.frontY = frontY; this.frontZ = frontZ; this.eye = eye;
            }
        }

        static readonly Dictionary<string, Measured> measured = new Dictionary<string, Measured>
        {
            // Rear peep plate 490 mm behind the muzzle (hole 4.8 x 5.0 mm), front blade 184 mm behind it.
            { "Sten Mk II", new Measured(0.0f, 28.8f, -490f, 0.2f, 28.9f, -184f, 100f) },
            // Rear notch (leaves' top 51.4 mm, notch bottom 49.9 mm) and front blade: the blade's top
            // level with the top of the rear sight, held out at arm's length.
            { "M1911", new Measured(0.5f, 51.4f, -172f, 0.4f, 51.0f, -14f, 450f) },
        };

        static readonly Dictionary<int, Mesh> rings = new Dictionary<int, Mesh>();

        /// <summary>The sights for this weapon, built on first use; null for weapons that do not get them.</summary>
        public static IronSights For(Weapon w)
        {
            if (w == null || w.muzzle == null || w.hold == HoldStyle.Launcher) return null;
            IronSights s = w.GetComponent<IronSights>();
            if (s == null)
            {
                WeaponPose.AlignModel(w);   // sights go on the straightened model
                s = w.gameObject.AddComponent<IronSights>();
                s.Build(w);
            }
            return s;
        }

        void Build(Weapon w)
        {
            Transform wt = w.transform;
            Vector3 muzzle = wt.InverseTransformPoint(w.muzzle.position);
            SightTurn = Quaternion.identity;

            Measured m;
            if (measured.TryGetValue(w.displayName ?? "", out m))
            {
                // Aim through the model's own sights: nothing is built, only the sight line is defined.
                ModelSights = true;
                RearCentreLocal = muzzle + new Vector3(m.rearX, m.rearY, m.rearZ) * 0.001f;
                FrontTipLocal = muzzle + new Vector3(m.frontX, m.frontY, m.frontZ) * 0.001f;
                Vector3 sightLine = (FrontTipLocal - RearCentreLocal).normalized;
                SightTurn = Quaternion.FromToRotation(sightLine, Vector3.forward);
                EyeLocal = RearCentreLocal - sightLine * (m.eye * 0.001f);
                return;
            }

            float minZ = float.MaxValue, maxY = float.MinValue;
            Material mat = null;
            foreach (MeshFilter mf in w.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Renderer r = mf.GetComponent<Renderer>();
                if (mat == null && r != null) mat = r.sharedMaterial;
                Bounds b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    Vector3 l = wt.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (l.z < minZ) minZ = l.z;
                    if (l.y > maxY) maxY = l.y;
                }
            }
            if (minZ == float.MaxValue) { minZ = muzzle.z - 0.7f; maxY = muzzle.y + 0.03f; }

            bool pistol = w.hold == HoldStyle.Pistol;
            float x = muzzle.x;
            float line = Mathf.Max(maxY, muzzle.y + 0.02f) + (pistol ? 0.004f : 0.012f);   // sight line, clear of the model
            float frontZ = muzzle.z - (pistol ? 0.012f : 0.03f);
            float length = muzzle.z - minZ;
            float rearZ = pistol ? minZ + 0.012f : muzzle.z - Mathf.Clamp(length * 0.62f, 0.25f, 0.55f);

            GameObject root = new GameObject("Iron sights (Stan's Game additions)");
            root.transform.SetParent(wt, false);
            int layer = w.gameObject.layer;
            root.layer = layer;

            float baseY = Mathf.Max(muzzle.y + 0.005f, line - (pistol ? 0.008f : 0.03f));   // posts stand on the model
            if (pistol)
            {
                // Front blade
                Box(root, mat, new Vector3(x, (baseY + line) * 0.5f, frontZ), new Vector3(0.003f, line - baseY, 0.010f));
                // Rear notch: two leaves with a 3.6 mm gap, the notch bottom on the sight line
                float leafW = 0.008f, gap = 0.0036f, top = line + 0.0045f;
                Box(root, mat, new Vector3(x - (gap + leafW) * 0.5f, (baseY + top) * 0.5f, rearZ), new Vector3(leafW, top - baseY, 0.004f));
                Box(root, mat, new Vector3(x + (gap + leafW) * 0.5f, (baseY + top) * 0.5f, rearZ), new Vector3(leafW, top - baseY, 0.004f));
                Box(root, mat, new Vector3(x, (baseY + line) * 0.5f, rearZ), new Vector3(gap, line - baseY, 0.004f));
                EyeLocal = new Vector3(x, line + 0.0018f, rearZ - 0.45f);   // arm's length; the body is hidden while aiming the pistol
            }
            else
            {
                // Front post with protecting ears
                Box(root, mat, new Vector3(x, (baseY + line) * 0.5f, frontZ), new Vector3(0.0028f, line - baseY, 0.008f));
                Box(root, mat, new Vector3(x - 0.008f, (baseY + line + 0.004f) * 0.5f, frontZ), new Vector3(0.0022f, line + 0.004f - baseY, 0.008f));
                Box(root, mat, new Vector3(x + 0.008f, (baseY + line + 0.004f) * 0.5f, frontZ), new Vector3(0.0022f, line + 0.004f - baseY, 0.008f));
                // Rear sight as on the Sten: a thin ring aperture with a wide hole, on a short block,
                // between two protecting ears (a U-shaped guard), so the sight picture is a ring
                // around the front post rather than a disc with a pinhole.
                const float inner = 0.0040f, outer = 0.0058f, depth = 0.0025f;
                GameObject ring = new GameObject("Rear aperture");
                ring.layer = layer;
                ring.transform.SetParent(root.transform, false);
                ring.transform.localPosition = new Vector3(x, line, rearZ);
                ring.AddComponent<MeshFilter>().sharedMesh = Ring(inner, outer, depth);
                ring.AddComponent<MeshRenderer>().sharedMaterial = mat;
                float under = line - outer + 0.0008f;                        // stem under the ring, overlapping it slightly
                float rearBase = Mathf.Max(baseY, line - 0.013f);
                if (under > rearBase)
                    Box(root, mat, new Vector3(x, (rearBase + under) * 0.5f, rearZ), new Vector3(0.0045f, under - rearBase, depth));
                // Ears: thin, set a little forward of the ring, their tops level with the top of the ring.
                float earTop = line + outer * 0.7f, earX = outer + 0.0035f;
                Box(root, mat, new Vector3(x - earX, (rearBase + earTop) * 0.5f, rearZ + 0.004f), new Vector3(0.0022f, earTop - rearBase, 0.008f));
                Box(root, mat, new Vector3(x + earX, (rearBase + earTop) * 0.5f, rearZ + 0.004f), new Vector3(0.0022f, earTop - rearBase, 0.008f));
                Box(root, mat, new Vector3(x, rearBase + 0.001f, rearZ + 0.004f), new Vector3(earX * 2f + 0.0022f, 0.002f, 0.008f));   // base of the guard
                EyeLocal = new Vector3(x, line, rearZ - 0.11f);   // cheek on the stock, about 11 cm behind the rear sight
            }
            FrontTipLocal = new Vector3(x, line, frontZ);
            RearCentreLocal = new Vector3(x, line, rearZ);
        }

        static void Box(GameObject parent, Material mat, Vector3 centre, Vector3 size)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "Sight part";
            Collider c = g.GetComponent<Collider>();
            if (c != null) Destroy(c);   // shots and movement must not hit the sights
            g.layer = parent.layer;
            g.transform.SetParent(parent.transform, false);
            g.transform.localPosition = centre;
            g.transform.localScale = size;
            if (mat != null) g.GetComponent<Renderer>().sharedMaterial = mat;
        }

        /// <summary>A flat ring (washer) facing the weapon's forward axis, flat-shaded, visible from both sides.</summary>
        static Mesh Ring(float inner, float outer, float depth)
        {
            int key = Mathf.RoundToInt(inner * 1e5f) * 1000 + Mathf.RoundToInt(outer * 1e4f);
            Mesh m;
            if (rings.TryGetValue(key, out m) && m != null) return m;
            const int seg = 40;
            List<Vector3> v = new List<Vector3>();
            List<Vector3> n = new List<Vector3>();
            List<int> t = new List<int>();
            // Two faces (towards the eye and away from it), each with its own vertices and normal.
            for (int face = 0; face < 2; face++)
            {
                float z = face == 0 ? -depth * 0.5f : depth * 0.5f;
                Vector3 normal = face == 0 ? Vector3.back : Vector3.forward;
                int start = v.Count;
                for (int i = 0; i < seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    v.Add(d * inner + Vector3.forward * z); n.Add(normal);
                    v.Add(d * outer + Vector3.forward * z); n.Add(normal);
                }
                for (int i = 0; i < seg; i++)
                {
                    int j = (i + 1) % seg;
                    int a0 = start + i * 2, a1 = start + j * 2;
                    t.AddRange(new[] { a0, a1, a0 + 1, a1, a1 + 1, a0 + 1 });
                    t.AddRange(new[] { a0, a0 + 1, a1, a1, a0 + 1, a1 + 1 });
                }
            }
            // Outer rim, radial normals.
            int rim = v.Count;
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v.Add(d * outer + Vector3.back * depth * 0.5f); n.Add(d);
                v.Add(d * outer + Vector3.forward * depth * 0.5f); n.Add(d);
            }
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                int a0 = rim + i * 2, a1 = rim + j * 2;
                t.AddRange(new[] { a0, a1, a0 + 1, a1, a1 + 1, a0 + 1 });
                t.AddRange(new[] { a0, a0 + 1, a1, a1, a0 + 1, a1 + 1 });
            }
            m = new Mesh();
            m.name = "Peep aperture";
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            rings[key] = m;
            return m;
        }
    }
}
