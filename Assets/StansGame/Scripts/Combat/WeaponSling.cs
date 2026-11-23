using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// The carrying sling of a long weapon: a strap between the front and rear sling swivels, simulated as a short
    /// chain of points under gravity (so it sags under the gun, swings when the soldier moves and drapes over the
    /// shoulder when the gun is slung). Added to every weapon that had one in 1944 (Weapon.Init): the rifles
    /// (Lee-Enfield, Kar 98k, M1 Garand), the M1 carbine, the sub-machine guns (Sten, MP 40, Thompson), the Bren and
    /// the MG 42, the bazooka and the PIAT; not to pistols (a lanyard, not a sling). British and American slings are
    /// khaki webbing, German ones brown leather.
    /// The swivel points are found from the gun's own shape: the front one under the barrel two-thirds of the way to
    /// the muzzle, the rear one under the butt. WeaponHolder hangs the gun from it over the right shoulder ("sling arms").
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponSling : MonoBehaviour
    {
        [Tooltip("Front and rear sling swivels in the weapon's own space (found from its shape if left at zero).")]
        public Vector3 frontSwivel, rearSwivel;
        [Tooltip("Strap length as a multiple of the distance between the swivels (the slack it hangs with).")]
        public float slack = 1.3f;
        public float width = 0.032f;
        const int N = 14;

        /// <summary>Set by WeaponHolder when the gun hangs over the shoulder: the strap runs over this point (world).</summary>
        [System.NonSerialized] public bool overShoulder;
        [System.NonSerialized] public Vector3 shoulderPoint;

        Vector3[] p = new Vector3[N], q = new Vector3[N];
        GameObject strap; Mesh mesh; MeshRenderer mr;
        Renderer[] gunRenderers;
        bool ready;

        /// <summary>The middle of the swivels, in the weapon's space (where it hangs from).</summary>
        public Vector3 MidLocal => (frontSwivel + rearSwivel) * 0.5f;
        public Vector3 FrontWorld => transform.TransformPoint(frontSwivel);
        public Vector3 RearWorld => transform.TransformPoint(rearSwivel);

        void Start()
        {
            gunRenderers = GetComponentsInChildren<Renderer>(true);
            if (frontSwivel == Vector3.zero && rearSwivel == Vector3.zero) FindSwivels();
            strap = new GameObject(name + " (sling)");
            mesh = new Mesh { name = "Sling" }; mesh.MarkDynamic();
            strap.AddComponent<MeshFilter>().sharedMesh = mesh;
            mr = strap.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.sharedMaterial = Material(IsGerman());
            Vector3 a = FrontWorld, b = RearWorld;
            for (int i = 0; i < N; i++) { p[i] = q[i] = Vector3.Lerp(a, b, i / (N - 1f)) + Vector3.down * 0.05f * Mathf.Sin(Mathf.PI * i / (N - 1f)); }
            ready = true;
        }

        void OnDestroy() { if (strap) Destroy(strap); if (mesh) Destroy(mesh); }
        void OnDisable() { if (strap) strap.SetActive(false); }
        void OnEnable() { if (strap) strap.SetActive(true); }

        bool IsGerman()
        {
            var w = GetComponent<Weapon>();
            string n = w ? w.displayName.ToLowerInvariant() : name.ToLowerInvariant();
            return n.Contains("kar") || n.Contains("mp 40") || n.Contains("mp40") || n.Contains("mg 42") || n.Contains("mg42") || n.Contains("panzer");
        }

        static Material webbing, leather;
        static Material Material(bool german)
        {
            ref Material m = ref german ? ref leather : ref webbing;
            if (m) return m;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(sh) { name = german ? "Sling_Leather" : "Sling_Webbing" };
            var c = german ? new Color(0.27f, 0.17f, 0.09f) : new Color(0.52f, 0.47f, 0.33f);
            m.color = c; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", german ? 0.35f : 0.1f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);   // both faces of the strap
            return m;
        }

        /// <summary>Swivels from the gun's shape: the bore line from the muzzle, the length from the meshes.</summary>
        void FindSwivels()
        {
            var w = GetComponent<Weapon>();
            bool any = false; var b = new Bounds();
            foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh || mf.name.Contains("sling")) continue;
                var mb = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    var lp = transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!any) { b = new Bounds(lp, Vector3.zero); any = true; } else b.Encapsulate(lp);
                }
            }
            if (!any) b = new Bounds(new Vector3(0f, 0f, 0.3f), new Vector3(0.06f, 0.15f, 1f));
            Vector3 mz = w && w.muzzle ? transform.InverseTransformPoint(w.muzzle.position) : new Vector3(0f, b.center.y, b.max.z);
            float rear = b.min.z, front = Mathf.Max(mz.z, b.max.z * 0.9f), len = front - rear;
            float bore = mz.y;
            frontSwivel = new Vector3(mz.x, bore - 0.035f, rear + len * 0.72f);
            rearSwivel = new Vector3(mz.x, Mathf.Lerp(b.min.y, bore, 0.25f), rear + len * 0.1f);
        }

        void LateUpdate()
        {
            if (!ready) return;
            bool shown = false;
            foreach (var r in gunRenderers) if (r && r.enabled && r.gameObject.activeInHierarchy) { shown = true; break; }
            if (strap.activeSelf != shown) strap.SetActive(shown);
            if (!shown) return;
            Simulate(Mathf.Min(Time.deltaTime, 1f / 30f));
            Draw();
        }

        void Simulate(float dt)
        {
            Vector3 a = FrontWorld, b = RearWorld;
            float span = Vector3.Distance(a, b);
            int mid = N / 2;
            float seg = overShoulder
                ? (Vector3.Distance(a, shoulderPoint) + Vector3.Distance(shoulderPoint, b)) * 1.03f / (N - 1)
                : span * (GetComponentInParent<WeaponHolder>() ? slack : 1.03f) / (N - 1);   // lying on a table: drawn tight along the gun
            Vector3 g = Physics.gravity * dt * dt;
            for (int i = 1; i < N - 1; i++)
            {
                Vector3 v = (p[i] - q[i]) * 0.96f;   // a little air and cloth friction
                q[i] = p[i];
                p[i] += v + g;
            }
            for (int it = 0; it < 8; it++)
            {
                p[0] = a; p[N - 1] = b;
                if (overShoulder) p[mid] = shoulderPoint;
                for (int i = 0; i < N - 1; i++)
                {
                    Vector3 d = p[i + 1] - p[i]; float l = d.magnitude;
                    if (l < 1e-5f) continue;
                    Vector3 corr = d * (1f - seg / l) * 0.5f;
                    bool fi = i == 0 || (overShoulder && i == mid), fj = i + 1 == N - 1 || (overShoulder && i + 1 == mid);
                    if (!fi && !fj) { p[i] += corr; p[i + 1] -= corr; }
                    else if (!fi) p[i] += corr * 2f;
                    else if (!fj) p[i + 1] -= corr * 2f;
                }
            }
            p[0] = a; p[N - 1] = b; if (overShoulder) p[mid] = shoulderPoint;
        }

        readonly Vector3[] verts = new Vector3[N * 2];
        readonly Vector3[] norms = new Vector3[N * 2];
        int[] tris; bool hasTris;

        void Draw()
        {
            Vector3 side = transform.right;   // the strap lies flat against the side of the gun
            for (int i = 0; i < N; i++)
            {
                Vector3 t = (p[Mathf.Min(i + 1, N - 1)] - p[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 across = Vector3.Cross(t, Vector3.Cross(side, t)).normalized;
                if (across.sqrMagnitude < 0.5f) across = side;
                verts[i * 2] = p[i] - across * width * 0.5f;
                verts[i * 2 + 1] = p[i] + across * width * 0.5f;
                norms[i * 2] = norms[i * 2 + 1] = Vector3.Cross(t, across);
            }
            if (tris == null)
            {
                tris = new int[(N - 1) * 12];
                for (int i = 0, k = 0; i < N - 1; i++)
                {
                    int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                    tris[k++] = a; tris[k++] = c; tris[k++] = b; tris[k++] = b; tris[k++] = c; tris[k++] = d;   // front
                    tris[k++] = a; tris[k++] = b; tris[k++] = c; tris[k++] = b; tris[k++] = d; tris[k++] = c;   // back
                }
            }
            mesh.vertices = verts; mesh.normals = norms;
            if (!hasTris) { mesh.triangles = tris; hasTris = true; }
            mesh.RecalculateBounds();
        }
    }
}
