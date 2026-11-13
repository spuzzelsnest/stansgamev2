using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace StansGame.Additions
{
    /// <summary>
    /// Dressing for the bootcamp, made only from what level 1 already loads (no new assets): textured
    /// materials from the terrain layers, copies of level props (shrubs, poplars, knife-rest wire, brushwood,
    /// the StuG, a Horsa fuselage), grass clumps from the terrain's grass texture, the game's own British
    /// soldier models as recruits and instructor (<see cref="Drill"/>), a sky dome with clouds, and a
    /// daylight post-processing volume. Everything is made under the training ground root, so leaving
    /// the bootcamp (a scene reload) removes it.
    /// </summary>
    public static class TrainingKit
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        // ------------------------------------------------------------------ lookups

        static readonly Dictionary<string, Texture> textures = new Dictionary<string, Texture>();
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static readonly Dictionary<string, MeshFilter> meshes = new Dictionary<string, MeshFilter>();

        public static Texture Tex(string name)
        {
            Texture t;
            if (textures.TryGetValue(name, out t) && t != null) return t;
            t = null;
            foreach (Texture2D x in Resources.FindObjectsOfTypeAll<Texture2D>()) if (x.name == name) { t = x; break; }
            textures[name] = t;
            return t;
        }

        public static Material Mat(string name)
        {
            Material m;
            if (materials.TryGetValue(name, out m) && m != null) return m;
            m = null;
            foreach (Material x in Resources.FindObjectsOfTypeAll<Material>()) if (x.name == name) { m = x; break; }
            materials[name] = m;
            return m;
        }

        /// <summary>A mesh used somewhere in the loaded level, with the renderer that shows it (for its materials).</summary>
        public static MeshFilter SceneMesh(string meshName)
        {
            MeshFilter f;
            if (meshes.TryGetValue(meshName, out f) && f != null) return f;
            f = null;
            foreach (MeshFilter x in Resources.FindObjectsOfTypeAll<MeshFilter>())
                if (x.sharedMesh != null && x.sharedMesh.name == meshName && x.gameObject.scene.IsValid() && x.GetComponent<Renderer>() != null) { f = x; break; }
            meshes[meshName] = f;
            return f;
        }

        /// <summary>A scene object by name (active or not), optionally under a parent of the given name.</summary>
        public static Transform SceneObject(string name, string parentName)
        {
            foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t.name != name || !t.gameObject.scene.IsValid()) continue;
                if (parentName == null || (t.parent != null && t.parent.name == parentName)) return t;
            }
            return null;
        }

        /// <summary>A prefab asset in memory (not a scene object), such as the British soldier types.</summary>
        public static GameObject Prefab(string name)
        {
            foreach (GameObject g in Resources.FindObjectsOfTypeAll<GameObject>())
                if (g.name == name && !g.scene.IsValid() && g.transform.parent == null) return g;
            return null;
        }

        // ------------------------------------------------------------------ materials

        static Material litTemplate;

        /// <summary>An opaque URP/Lit material with a normal map from the level, to clone (keeps a shader variant the build has).</summary>
        static Material LitTemplate()
        {
            if (litTemplate != null) return litTemplate;
            litTemplate = Mat("Sandbags");
            if (litTemplate == null)
                foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
                    if (m.shader != null && m.shader.name == "Universal Render Pipeline/Lit") { litTemplate = m; break; }
            return litTemplate;
        }

        /// <summary>A textured material: albedo and normal map by name, tiled every <paramref name="tile"/> metres over a size.</summary>
        public static Material Textured(string albedo, string normal, Vector2 size, float tile, Color tint, float smoothness)
        {
            Material t = LitTemplate();
            if (t == null) return null;
            Material m = new Material(t);
            m.name = "bootcamp " + albedo;
            Texture a = Tex(albedo), n = normal != null ? Tex(normal) : null;
            if (a != null) { m.SetTexture("_BaseMap", a); m.mainTexture = a; }
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", n);
            if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", n != null ? 1f : 0f);
            Vector2 scale = new Vector2(Mathf.Max(0.05f, size.x / tile), Mathf.Max(0.05f, size.y / tile));
            m.SetTextureScale("_BaseMap", scale);
            m.mainTextureScale = scale;
            m.SetColor("_BaseColor", tint);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_MetallicGlossMap") && m.IsKeywordEnabled("_METALLICSPECGLOSSMAP")) m.SetTexture("_MetallicGlossMap", Texture2D.blackTexture);
            if (m.HasProperty("_OcclusionMap") && m.IsKeywordEnabled("_OCCLUSIONMAP")) m.SetTexture("_OcclusionMap", Texture2D.whiteTexture);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            return m;
        }

        /// <summary>One of the terrain's ground layers (Pasture, Turf, Towpath, Gravel, Mud, Trampled) as a tiled material.</summary>
        public static Material Ground(string layer, Vector2 size, Color tint)
        {
            float tile = layer == "Gravel" ? 2.6f : layer == "Turf" ? 4.5f : layer == "Pasture" ? 3.5f : 3f;
            return Textured("Ground_" + layer + "_Albedo", "Ground_" + layer + "_Normal", size, tile, tint, 0.08f);
        }

        /// <summary>A copy of a level material with tiling for a size (for planks, sandbags and the like).</summary>
        public static Material Tiled(string levelMaterial, Vector2 size, Vector2 tileSize, Color fallback)
        {
            Material src = Mat(levelMaterial);
            Material m = src != null ? new Material(src) : new Material(LitTemplate());
            if (src == null) m.SetColor("_BaseColor", fallback);
            Vector2 scale = new Vector2(Mathf.Max(0.05f, size.x / tileSize.x), Mathf.Max(0.05f, size.y / tileSize.y));
            m.SetTextureScale("_BaseMap", scale);
            m.mainTextureScale = scale;
            return m;
        }

        /// <summary>Plain colour on the level's lit shader, no textures.</summary>
        public static Material Plain(string name, Color c, float smoothness)
        {
            Material m = new Material(LitTemplate());
            m.name = name;
            m.SetTexture("_BaseMap", null);
            m.mainTexture = null;
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        /// <summary>An alpha-cut, two-sided material (the level's leaves) with another texture: grass.</summary>
        public static Material Cutout(string templateMaterial, string texture, Color tint)
        {
            Material src = Mat(templateMaterial);
            if (src == null) return null;
            Material m = new Material(src);
            m.name = "bootcamp " + texture;
            Texture t = Tex(texture);
            if (t != null) { m.SetTexture("_BaseMap", t); m.mainTexture = t; }
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetColor("_BaseColor", tint);
            return m;
        }

        /// <summary>
        /// A material for <see cref="MetricBox"/> pieces: texture coordinates are in metres, so one tile
        /// covers <paramref name="tile"/> metres on every face whatever the size of the piece.
        /// </summary>
        public static Material Metric(string albedo, string normal, Vector2 tile, Color tint, float smoothness)
        {
            Material m = Textured(albedo, normal, Vector2.one, 1f, tint, smoothness);
            if (m == null) return null;
            Vector2 scale = new Vector2(1f / tile.x, 1f / tile.y);
            m.SetTextureScale("_BaseMap", scale);
            m.mainTextureScale = scale;
            return m;
        }

        /// <summary>A level material (planks, sandbags) for <see cref="MetricBox"/> pieces, one tile per <paramref name="tile"/> metres.</summary>
        public static Material MetricFrom(string levelMaterial, Vector2 tile, Color tint, Color fallback)
        {
            Material m = Tiled(levelMaterial, Vector2.one, tile, fallback);
            if (Mat(levelMaterial) != null) m.SetColor("_BaseColor", tint);
            return m;
        }

        /// <summary>The lit shader with a texture made in code (flags, target faces).</summary>
        public static Material WithTexture(string name, Texture2D tex, float smoothness)
        {
            Material m = Plain(name, Color.white, smoothness);
            m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            m.SetTextureScale("_BaseMap", Vector2.one);
            return m;
        }

        // ------------------------------------------------------------------ boxes with metre texture coordinates

        /// <summary>
        /// A box mesh whose texture coordinates are world metres (offset by <paramref name="origin"/>, the box's
        /// place on the field), so neighbouring pieces carry on the same texture without seams or stretching.
        /// </summary>
        public static Mesh MetricBox(Vector3 size, Vector3 origin)
        {
            List<Vector3> v = new List<Vector3>(24);
            List<Vector3> n = new List<Vector3>(24);
            List<Vector2> uv = new List<Vector2>(24);
            List<int> tri = new List<int>(36);
            Vector3 h = size * 0.5f;
            BoxFace(v, n, uv, tri, Vector3.right, Vector3.forward, Vector3.up, h, origin);
            BoxFace(v, n, uv, tri, Vector3.left, Vector3.forward, Vector3.up, h, origin);
            BoxFace(v, n, uv, tri, Vector3.forward, Vector3.right, Vector3.up, h, origin);
            BoxFace(v, n, uv, tri, Vector3.back, Vector3.right, Vector3.up, h, origin);
            BoxFace(v, n, uv, tri, Vector3.up, Vector3.right, Vector3.forward, h, origin);
            BoxFace(v, n, uv, tri, Vector3.down, Vector3.right, Vector3.forward, h, origin);
            Mesh m = new Mesh();
            m.name = "bootcamp block";
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        static void BoxFace(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tri, Vector3 normal, Vector3 u, Vector3 w, Vector3 h, Vector3 origin)
        {
            Vector3 c = Vector3.Scale(normal, h);
            float hu = Mathf.Abs(Vector3.Dot(u, h)), hw = Mathf.Abs(Vector3.Dot(w, h));
            Vector3[] corners = { c - u * hu - w * hw, c + u * hu - w * hw, c + u * hu + w * hw, c - u * hu + w * hw };
            int i = v.Count;
            foreach (Vector3 p in corners)
            {
                v.Add(p);
                n.Add(normal);
                Vector3 o = p + origin;
                uv.Add(new Vector2(Vector3.Dot(o, u), Vector3.Dot(o, w)));
            }
            if (Vector3.Dot(Vector3.Cross(u, w), normal) > 0f) { tri.Add(i); tri.Add(i + 1); tri.Add(i + 2); tri.Add(i); tri.Add(i + 2); tri.Add(i + 3); }
            else { tri.Add(i); tri.Add(i + 2); tri.Add(i + 1); tri.Add(i); tri.Add(i + 3); tri.Add(i + 2); }
        }

        /// <summary>
        /// A prism: a convex cross-section in (x, y), counter-clockwise, run along z for <paramref name="length"/>,
        /// with metre texture coordinates (an earth bank, a ramp). Centred on z.
        /// </summary>
        public static Mesh Prism(Vector2[] section, float length, Vector3 origin)
        {
            List<Vector3> v = new List<Vector3>();
            List<Vector3> n = new List<Vector3>();
            List<Vector2> uv = new List<Vector2>();
            List<int> tri = new List<int>();
            float hz = length * 0.5f;
            float s = 0f;
            for (int i = 0; i < section.Length; i++)
            {
                Vector2 a = section[i], b = section[(i + 1) % section.Length];
                Vector2 e = b - a;
                float len = e.magnitude;
                if (len < 1e-4f) continue;
                Vector3 normal = new Vector3(e.y, -e.x, 0f) / len;
                Vector3 a0 = new Vector3(a.x, a.y, -hz), b0 = new Vector3(b.x, b.y, -hz), b1 = new Vector3(b.x, b.y, hz), a1 = new Vector3(a.x, a.y, hz);
                int k = v.Count;
                v.Add(a0); v.Add(b0); v.Add(b1); v.Add(a1);
                for (int q = 0; q < 4; q++) n.Add(normal);
                uv.Add(new Vector2(-hz + origin.z, s)); uv.Add(new Vector2(-hz + origin.z, s + len)); uv.Add(new Vector2(hz + origin.z, s + len)); uv.Add(new Vector2(hz + origin.z, s));
                Facet(v, tri, k, k + 1, k + 2, normal);
                Facet(v, tri, k, k + 2, k + 3, normal);
                s += len;
            }
            foreach (float zc in new float[] { -hz, hz })
            {
                Vector3 normal = new Vector3(0f, 0f, Mathf.Sign(zc));
                int k = v.Count;
                foreach (Vector2 p in section)
                {
                    v.Add(new Vector3(p.x, p.y, zc));
                    n.Add(normal);
                    uv.Add(new Vector2(p.x + origin.x, p.y + origin.y));
                }
                for (int i = 1; i + 1 < section.Length; i++) Facet(v, tri, k, k + i, k + i + 1, normal);
            }
            Mesh m = new Mesh();
            m.name = "bootcamp prism";
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        /// <summary>Adds a triangle wound so that it faces <paramref name="normal"/> (Unity: front = Cross(b - a, c - a)).</summary>
        static void Facet(List<Vector3> v, List<int> tri, int a, int b, int c, Vector3 normal)
        {
            if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), normal) < 0f) { int t = b; b = c; c = t; }
            tri.Add(a); tri.Add(b); tri.Add(c);
        }

        /// <summary>A box piece on the field: metre-mapped mesh, material, and a box collider when it should be solid.</summary>
        public static GameObject Block(string name, Transform parent, Vector3 localCentre, Vector3 size, Material m, bool collide)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCentre;
            go.AddComponent<MeshFilter>().sharedMesh = MetricBox(size, localCentre);
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            if (collide) go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        // ------------------------------------------------------------------ textures made in code

        /// <summary>The Union flag (2:1), drawn in code.</summary>
        public static Texture2D UnionFlag(int w)
        {
            int h = w / 2;
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, true);
            Color blue = new Color(0.0f, 0.14f, 0.41f), white = new Color(0.95f, 0.95f, 0.93f), red = new Color(0.78f, 0.06f, 0.18f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float X = (x + 0.5f) / h - 1f, Y = (y + 0.5f) / h - 0.5f;   // width 2, height 1, centred
                    // Signed distance to this quarter's diagonal (positive above it).
                    float sd = (Y > 0f) == (X < 0f) ? (Y + X * 0.5f) / 1.118f : (Y - X * 0.5f) / 1.118f;
                    Color c = blue;
                    if (Mathf.Abs(sd) < 0.1f) c = white;
                    // Counterchanged red saltire: broad white uppermost on the hoist side, turned round on the fly side.
                    if (X < 0f ? (sd > -0.0667f && sd < 0f) : (sd > 0f && sd < 0.0667f)) c = red;
                    if (Mathf.Abs(X) < 1f / 6f || Mathf.Abs(Y) < 1f / 6f) c = white;
                    if (Mathf.Abs(X) < 0.1f || Mathf.Abs(Y) < 0.1f) c = red;
                    t.SetPixel(x, y, c);
                }
            t.wrapMode = TextureWrapMode.Clamp;
            t.Apply(true);
            return t;
        }

        /// <summary>A white flag with a red cross (aid post).</summary>
        public static Texture2D RedCross(int w)
        {
            int h = w * 2 / 3;
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, true);
            Color white = new Color(0.95f, 0.95f, 0.93f), red = new Color(0.78f, 0.06f, 0.08f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float X = Mathf.Abs((x + 0.5f) / w - 0.5f) * w, Y = Mathf.Abs((y + 0.5f) / h - 0.5f) * h;
                    float arm = h * 0.3f, half = h * 0.1f;
                    bool cross = (X < half && Y < arm) || (Y < half && X < arm);
                    t.SetPixel(x, y, cross ? red : white);
                }
            t.wrapMode = TextureWrapMode.Clamp;
            t.Apply(true);
            return t;
        }

        /// <summary>A target face: a dark head-and-shoulders figure with an aiming ring on a khaki board, a weathered edge.</summary>
        public static Texture2D TargetFace(int w, int seed)
        {
            int h = w * 5 / 3;
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, true);
            Color board = new Color(0.74f, 0.66f, 0.48f), figure = new Color(0.15f, 0.17f, 0.12f), ring = new Color(0.86f, 0.82f, 0.7f);
            float s = seed * 3.1f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    float dx = u - 0.5f;
                    bool head = (dx * dx) / (0.13f * 0.13f) + ((v - 0.8f) * (v - 0.8f)) / (0.085f * 0.085f) < 1f;
                    float shoulder = v < 0.6f ? 0.36f : 0.36f * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((v - 0.6f) / 0.11f, 2f)));
                    bool body = v > 0.04f && v < 0.71f && Mathf.Abs(dx) < shoulder;
                    Color c = head || body ? figure : board;
                    float r = Mathf.Sqrt(dx * dx * 1f + (v - 0.42f) * (v - 0.42f) * 2.8f);
                    if (body && Mathf.Abs(r - 0.13f) < 0.008f) c = ring;
                    float grime = Mathf.PerlinNoise(s + u * 9f, s + v * 15f);
                    c *= 0.86f + 0.2f * grime;
                    float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    if (edge < 0.025f) c *= 0.75f;
                    c.a = 1f;
                    t.SetPixel(x, y, c);
                }
            t.wrapMode = TextureWrapMode.Clamp;
            t.Apply(true);
            return t;
        }

        // ------------------------------------------------------------------ copies of level props

        /// <summary>Puts a copy of a level mesh on the field: <paramref name="groundY"/> is the world height its base sits on.</summary>
        public static GameObject CopyMesh(string meshName, Transform parent, Vector3 world, float yaw, float scale, float groundY, bool collide)
        {
            MeshFilter src = SceneMesh(meshName);
            if (src == null) return null;
            GameObject go = new GameObject(meshName + " (bootcamp)");
            go.transform.SetParent(parent, true);
            go.transform.position = world;
            go.transform.rotation = Turned(src.transform, yaw);
            go.transform.localScale = src.transform.lossyScale * scale;
            go.AddComponent<MeshFilter>().sharedMesh = src.sharedMesh;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = src.GetComponent<Renderer>().sharedMaterials;
            Sit(go, groundY);
            if (collide)
            {
                BoxCollider bc = go.AddComponent<BoxCollider>();
                bc.center = src.sharedMesh.bounds.center;
                bc.size = src.sharedMesh.bounds.size;
            }
            return go;
        }

        /// <summary>A copy of a whole level object (all its parts), with only its meshes kept.</summary>
        public static GameObject CopyObject(Transform src, Transform parent, Vector3 world, float yaw, float groundY, bool keepColliders)
        {
            if (src == null) return null;
            GameObject holder = new GameObject("copy holder");
            holder.SetActive(false);
            GameObject go = Object.Instantiate(src.gameObject, holder.transform);
            go.name = src.name + " (bootcamp)";
            go.transform.localScale = src.lossyScale;
            go.SetActive(true);
            foreach (MonoBehaviour b in go.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(b);
            foreach (Rigidbody rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true)) if (ps.gameObject != go) Object.DestroyImmediate(ps.gameObject);
            foreach (AudioSource a in go.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(a);
            foreach (Light l in go.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);
            if (!keepColliders) foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                r.gameObject.SetActive(true);
                r.enabled = true;
            }
            go.transform.SetParent(parent, true);
            go.transform.position = world;
            go.transform.rotation = Turned(src, yaw);
            Object.Destroy(holder);
            Sit(go, groundY);
            return go;
        }

        /// <summary>The source's rotation with its heading replaced by <paramref name="yaw"/> (keeps import tilts such as -90 on x).</summary>
        static Quaternion Turned(Transform src, float yaw)
        {
            return Quaternion.Euler(0f, yaw, 0f) * Quaternion.Inverse(Quaternion.Euler(0f, src.eulerAngles.y, 0f)) * src.rotation;
        }

        /// <summary>Turns a copy a quarter round its middle if its long side does not run along world x (or z).</summary>
        public static void LongAlong(GameObject go, bool worldX)
        {
            Bounds b = Bounds(go);
            bool ok = worldX ? b.size.x >= b.size.z : b.size.z >= b.size.x;
            if (!ok) go.transform.RotateAround(b.center, Vector3.up, 90f);
        }

        /// <summary>Turns an object round the middle of its renderers.</summary>
        public static void TurnAboutCentre(GameObject go, float degrees)
        {
            go.transform.RotateAround(Bounds(go).center, Vector3.up, degrees);
        }

        public static Bounds Bounds(GameObject go)
        {
            bool any = false;
            Bounds b = new Bounds(go.transform.position, Vector3.zero);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        /// <summary>Moves an object up or down so the bottom of its renderers is at <paramref name="groundY"/>.</summary>
        public static void Sit(GameObject go, float groundY)
        {
            bool any = false;
            Bounds b = new Bounds();
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (any) go.transform.position += Vector3.up * (groundY - b.min.y);
        }

        // ------------------------------------------------------------------ meshes made here

        /// <summary>A bell tent: a cone roof on a short wall, open at the bottom, two-sided by doubling.</summary>
        public static Mesh BellTent(float radius, float wall, float height, int sides)
        {
            List<Vector3> v = new List<Vector3>();
            List<Vector3> n = new List<Vector3>();
            List<Vector2> uv = new List<Vector2>();
            List<int> tri = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                Vector3 p1 = new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
                Vector3 q0 = p0 + Vector3.up * wall, q1 = p1 + Vector3.up * wall;
                Vector3 top = Vector3.up * height;
                Quad(v, n, uv, tri, p0, p1, q1, q0);
                Tri(v, n, uv, tri, q0, q1, top);
            }
            Mesh m = new Mesh();
            m.name = "bootcamp bell tent";
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        static void Quad(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tri, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 nr = Vector3.Cross(b - a, d - a).normalized;
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            n.Add(-nr); n.Add(-nr); n.Add(-nr); n.Add(-nr);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
            tri.Add(i); tri.Add(i + 3); tri.Add(i + 2); tri.Add(i); tri.Add(i + 2); tri.Add(i + 1);   // outside
            int j = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            n.Add(nr); n.Add(nr); n.Add(nr); n.Add(nr);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
            tri.Add(j); tri.Add(j + 1); tri.Add(j + 2); tri.Add(j); tri.Add(j + 2); tri.Add(j + 3);   // inside
        }

        static void Tri(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tri, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 nr = Vector3.Cross(b - a, c - a).normalized;
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            n.Add(-nr); n.Add(-nr); n.Add(-nr);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(0.5f, 1));
            tri.Add(i); tri.Add(i + 2); tri.Add(i + 1);
            int j = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            n.Add(nr); n.Add(nr); n.Add(nr);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(0.5f, 1));
            tri.Add(j); tri.Add(j + 1); tri.Add(j + 2);
        }

        /// <summary>Grass: many crossed quads in one mesh, positions in the parent's local space (y = ground).</summary>
        public static Mesh GrassClumps(List<Vector3> spots, float minH, float maxH, float minW, float maxW, System.Random rng)
        {
            List<Vector3> v = new List<Vector3>(spots.Count * 8);
            List<Vector3> n = new List<Vector3>(spots.Count * 8);
            List<Vector2> uv = new List<Vector2>(spots.Count * 8);
            List<Color> col = new List<Color>(spots.Count * 8);
            List<int> tri = new List<int>(spots.Count * 12);
            foreach (Vector3 p in spots)
            {
                float h = Mathf.Lerp(minH, maxH, (float)rng.NextDouble());
                float w = Mathf.Lerp(minW, maxW, (float)rng.NextDouble());
                float a = (float)rng.NextDouble() * Mathf.PI;
                float shade = 0.8f + 0.2f * (float)rng.NextDouble();
                for (int k = 0; k < 2; k++)
                {
                    float ang = a + k * Mathf.PI * 0.5f;
                    Vector3 d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (w * 0.5f);
                    int i = v.Count;
                    v.Add(p - d); v.Add(p + d); v.Add(p + d + Vector3.up * h); v.Add(p - d + Vector3.up * h);
                    for (int q = 0; q < 4; q++) { n.Add(Vector3.up); col.Add(new Color(shade, shade, shade, 1f)); }
                    uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                    tri.Add(i); tri.Add(i + 2); tri.Add(i + 1); tri.Add(i); tri.Add(i + 3); tri.Add(i + 2);
                }
            }
            Mesh m = new Mesh();
            m.name = "bootcamp grass";
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetColors(col);
            m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ soldiers

        static readonly string[] Strip =
        {
            "AiUnstick", "SoldierAI", "NetworkAnimator", "NetworkTransform", "OwnerNetworkTransform", "NetEntity", "NetPlayer",
            "VehicleEntry", "LootOnDeath", "BurnOnDeath", "NetworkObject",
        };
        static readonly PropertyInfo holderActive = typeof(WeaponHolder).GetProperty("Active", Any);

        /// <summary>
        /// One of the game's own soldier models (BritishSoldier, BritishRifleman, BritishBrenGunner,
        /// BritishParaOfficer) as a figure for the training ground: no AI, no networking, cannot be hurt,
        /// posed and moved by <see cref="Drill"/>.
        /// </summary>
        public static Drill Soldier(string prefab, Transform parent, Vector3 world, float yaw, DrillPose pose)
        {
            GameObject pf = Prefab(prefab);
            if (pf == null) return null;
            GameObject holder = new GameObject("drill holder");
            holder.SetActive(false);
            GameObject s = Object.Instantiate(pf, holder.transform);
            s.name = prefab + " (bootcamp)";
            SoldierAI ai = s.GetComponent<SoldierAI>();
            Weapon weapon = ai != null ? ai.weapon : s.GetComponentInChildren<Weapon>(true);
            foreach (string type in Strip)
                foreach (Component c in s.GetComponentsInChildren<Component>(true))
                    if (c != null && c.GetType().Name == type) Object.DestroyImmediate(c);
            Health h = s.GetComponent<Health>();
            if (h != null) { h.armour = 1000000f; h.regenPerSecond = 0f; }
            s.transform.SetPositionAndRotation(world, Quaternion.Euler(0f, yaw, 0f));
            s.transform.SetParent(parent, true);
            Object.Destroy(holder);
            Drill d = s.AddComponent<Drill>();
            d.Init(weapon, pose);
            return d;
        }

        internal static void Hold(WeaponHolder holder, Weapon w)
        {
            if (holder == null || w == null || holderActive == null) return;
            MethodInfo set = holderActive.GetSetMethod(true);
            if (set != null) set.Invoke(holder, new object[] { w });
        }

        // ------------------------------------------------------------------ sky, light, post

        /// <summary>Sky dome (gradient) and clouds that follow the camera, drawn before everything else.</summary>
        public static void Sky(Transform parent, Color zenith, Color horizon, Color below)
        {
            Shader sprite = Shader.Find("Sprites/Default");
            if (sprite == null) return;
            GameObject sky = new GameObject("bootcamp sky");
            sky.transform.SetParent(parent, false);
            SkyFollow f = sky.AddComponent<SkyFollow>();

            Mesh dome = Dome(48, 24, zenith, horizon, below);
            GameObject d = new GameObject("dome");
            d.transform.SetParent(sky.transform, false);
            d.AddComponent<MeshFilter>().sharedMesh = dome;
            MeshRenderer r = d.AddComponent<MeshRenderer>();
            Material m = new Material(sprite);
            m.renderQueue = 1000;
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            f.dome = d.transform;

            Texture2D cloud = CloudTexture(256, 128, 7);
            Material cm = new Material(sprite);
            cm.mainTexture = cloud;
            cm.renderQueue = 1001;
            System.Random rng = new System.Random(11);
            for (int i = 0; i < 22; i++)
            {
                GameObject c = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.Destroy(c.GetComponent<Collider>());
                c.name = "cloud";
                c.transform.SetParent(sky.transform, false);
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float elev = (7f + 26f * (float)rng.NextDouble()) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(ang) * Mathf.Cos(elev), Mathf.Sin(elev), Mathf.Sin(ang) * Mathf.Cos(elev));
                float w = 0.16f + 0.2f * (float)rng.NextDouble();
                Renderer cr = c.GetComponent<Renderer>();
                cr.sharedMaterial = cm;
                cr.shadowCastingMode = ShadowCastingMode.Off;
                cr.receiveShadows = false;
                f.Add(c.transform, dir, new Vector2(w, w * (0.32f + 0.18f * (float)rng.NextDouble())));
            }
        }

        static Mesh Dome(int seg, int rings, Color zenith, Color horizon, Color below)
        {
            List<Vector3> v = new List<Vector3>();
            List<Color> col = new List<Color>();
            List<int> tri = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float lat = Mathf.Lerp(-0.35f, 1f, (float)r / rings) * Mathf.PI * 0.5f;
                float y = Mathf.Sin(lat), cr = Mathf.Cos(lat);
                Color c = y >= 0f ? Color.Lerp(horizon, zenith, Mathf.Pow(y, 0.55f)) : Color.Lerp(horizon, below, Mathf.Clamp01(-y * 4f));
                for (int s = 0; s <= seg; s++)
                {
                    float lon = (float)s / seg * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(lon) * cr, y, Mathf.Sin(lon) * cr));
                    col.Add(c);
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * (seg + 1) + s, b = a + seg + 1;
                    tri.Add(a); tri.Add(b); tri.Add(a + 1);
                    tri.Add(a + 1); tri.Add(b); tri.Add(b + 1);
                }
            Mesh m = new Mesh();
            m.name = "bootcamp sky dome";
            m.SetVertices(v);
            m.SetColors(col);
            m.SetTriangles(tri, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            return m;
        }

        static Texture2D CloudTexture(int w, int h, int seed)
        {
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, true);
            t.wrapMode = TextureWrapMode.Clamp;
            float ox = seed * 13.7f, oy = seed * 7.3f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (float)x / w, vv = (float)y / h;
                    float n = 0f, amp = 0.5f, freq = 3f;
                    for (int o = 0; o < 5; o++) { n += amp * Mathf.PerlinNoise(ox + u * freq * 2f, oy + vv * freq); amp *= 0.5f; freq *= 2f; }
                    float dx = (u - 0.5f) * 2f, dy = (vv - 0.42f) * 2.4f;
                    float mask = Mathf.Clamp01(1f - (dx * dx + dy * dy));
                    float a = Mathf.Clamp01((n - 0.42f) * 3.2f) * mask;
                    float shade = Mathf.Lerp(0.78f, 1f, vv);
                    t.SetPixel(x, y, new Color(shade, shade, shade * 1.02f, a * 0.9f));
                }
            t.Apply(true);
            return t;
        }

        /// <summary>A global daylight look: gentle contrast, a little bloom and a soft vignette.</summary>
        public static void PostProcessing(Transform parent)
        {
            try
            {
                GameObject go = new GameObject("bootcamp daylight volume");
                go.transform.SetParent(parent, false);
                Volume v = go.AddComponent<Volume>();
                v.isGlobal = true;
                v.priority = 50f;
                v.weight = 1f;
                VolumeProfile p = ScriptableObject.CreateInstance<VolumeProfile>();
                ColorAdjustments ca = p.Add<ColorAdjustments>(true);
                ca.postExposure.Override(0.1f);
                ca.contrast.Override(12f);
                ca.saturation.Override(6f);
                ca.colorFilter.Override(new Color(1f, 0.985f, 0.95f));
                Bloom b = p.Add<Bloom>(true);
                b.threshold.Override(1.05f);
                b.intensity.Override(0.35f);
                b.scatter.Override(0.6f);
                Vignette vg = p.Add<Vignette>(true);
                vg.intensity.Override(0.22f);
                vg.smoothness.Override(0.45f);
                FilmGrain g = p.Add<FilmGrain>(true);
                g.intensity.Override(0.12f);
                v.sharedProfile = p;
                foreach (Camera c in Camera.allCameras)
                {
                    UniversalAdditionalCameraData data = c.GetComponent<UniversalAdditionalCameraData>();
                    if (data != null) data.renderPostProcessing = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Stan's Game: bootcamp post-processing not available (" + e.Message + ")");
            }
        }
    }

    /// <summary>
    /// Sound of the training ground: a light wind, birds in the hedges, and now and then the thump of a
    /// distant range or exercise (the level's own far explosion sounds, quiet).
    /// </summary>
    public sealed class TrainingAmbience : MonoBehaviour
    {
        public TrainingGround ground;
        AudioSource wind;
        AudioClip[] birds;
        AudioClip[] far;
        float nextBird, nextFar;

        void Start()
        {
            AudioClip w = null;
            try { w = Sfx.Wind; } catch (Exception) { }
            if (w != null)
            {
                wind = gameObject.AddComponent<AudioSource>();
                wind.clip = w;
                wind.loop = true;
                wind.spatialBlend = 0f;
                wind.volume = 0.16f;
                wind.pitch = 0.92f;
                wind.Play();
            }
            birds = new AudioClip[] { Chirp("bootcamp bird a", 3200f, 4600f, 3, 7), Chirp("bootcamp bird b", 2400f, 3300f, 5, 11), Chirp("bootcamp bird c", 4100f, 3600f, 2, 3) };
            List<AudioClip> f = new List<AudioClip>();
            foreach (AudioClip c in Resources.FindObjectsOfTypeAll<AudioClip>())
                if (c.name == "FarExplosionA" || c.name == "FarExplosionB") f.Add(c);
            far = f.ToArray();
            nextBird = Time.time + 2f;
            nextFar = Time.time + 25f;
        }

        void Update()
        {
            if (ground == null || ground.Root == null) return;
            Camera c = Camera.main;
            Vector3 eye = c != null ? c.transform.position : ground.Root.position;
            if (Time.time > nextBird)
            {
                nextBird = Time.time + UnityEngine.Random.Range(2.5f, 8f);
                // A bird somewhere in the hedge nearest the listener.
                Vector3 l = ground.Local(eye);
                float x = UnityEngine.Random.value < 0.5f ? -18.8f : 70.8f;
                float z = Mathf.Clamp(l.z + UnityEngine.Random.Range(-35f, 35f), -10f, 181f);
                if (UnityEngine.Random.value < 0.3f) { x = UnityEngine.Random.Range(-18f, 70f); z = l.z < 85f ? -10.2f : 181.8f; }
                Sfx.At(birds[UnityEngine.Random.Range(0, birds.Length)], ground.W(x, UnityEngine.Random.Range(1.5f, 3f), z), UnityEngine.Random.Range(0.25f, 0.45f), UnityEngine.Random.Range(0.92f, 1.1f), 90f);
            }
            if (far.Length > 0 && Time.time > nextFar)
            {
                nextFar = Time.time + UnityEngine.Random.Range(18f, 40f);
                Vector3 dir = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward;
                Sfx.At(far[UnityEngine.Random.Range(0, far.Length)], eye + dir * 260f + Vector3.up * 20f, 0.3f, UnityEngine.Random.Range(0.8f, 1f), 700f);
            }
        }

        /// <summary>A short bird call: a few quick rising (or falling) whistles.</summary>
        static AudioClip Chirp(string name, float f0, float f1, int notes, int seed)
        {
            const int rate = 44100;
            System.Random rng = new System.Random(seed);
            float note = 0.075f, gap = 0.05f;
            int n = (int)(rate * (notes * (note + gap) + 0.05f));
            float[] data = new float[n];
            for (int k = 0; k < notes; k++)
            {
                int start = (int)(rate * k * (note + gap));
                float len = note * (0.7f + 0.5f * (float)rng.NextDouble());
                float a0 = f0 * (0.95f + 0.1f * (float)rng.NextDouble()), a1 = f1 * (0.95f + 0.1f * (float)rng.NextDouble());
                double phase = 0;
                int m = (int)(rate * len);
                for (int i = 0; i < m && start + i < n; i++)
                {
                    float t = (float)i / m;
                    float f = Mathf.Lerp(a0, a1, t) + Mathf.Sin(t * 60f) * 120f;
                    phase += 2 * Math.PI * f / rate;
                    float env = Mathf.Sin(t * Mathf.PI);
                    data[start + i] += (float)Math.Sin(phase) * env * env * 0.5f;
                }
            }
            AudioClip c = AudioClip.Create(name, n, 1, rate, false);
            c.SetData(data, 0);
            return c;
        }
    }

    /// <summary>A tracer round: a short bright streak flying along a line, then gone (drawn only, hits nothing).</summary>
    public sealed class TracerStreak : MonoBehaviour
    {
        LineRenderer lr;
        Vector3 from, dir;
        float range, travelled;
        const float Speed = 650f, Length = 7f;

        public static void Fire(Vector3 from, Vector3 dir, float range, Material m)
        {
            if (m == null) return;
            GameObject go = new GameObject("bootcamp tracer");
            TracerStreak t = go.AddComponent<TracerStreak>();
            t.from = from;
            t.dir = dir.normalized;
            t.range = range;
            t.lr = go.AddComponent<LineRenderer>();
            t.lr.sharedMaterial = m;
            t.lr.positionCount = 2;
            t.lr.useWorldSpace = true;
            t.lr.startWidth = 0.07f;
            t.lr.endWidth = 0.025f;
            t.lr.startColor = new Color(1f, 0.92f, 0.65f, 1f);
            t.lr.endColor = new Color(1f, 0.45f, 0.15f, 0.6f);
            t.lr.shadowCastingMode = ShadowCastingMode.Off;
            t.lr.receiveShadows = false;
            t.lr.SetPosition(0, from);
            t.lr.SetPosition(1, from);
        }

        void Update()
        {
            travelled += Speed * Time.deltaTime;
            if (travelled > range) { Destroy(gameObject); return; }
            lr.SetPosition(0, from + dir * travelled);
            lr.SetPosition(1, from + dir * Mathf.Max(0f, travelled - Length));
        }
    }

    /// <summary>Keeps the sky dome and clouds around the camera.</summary>
    public sealed class SkyFollow : MonoBehaviour
    {
        public Transform dome;
        readonly List<Transform> clouds = new List<Transform>();
        readonly List<Vector3> dirs = new List<Vector3>();
        readonly List<Vector2> sizes = new List<Vector2>();
        float drift;

        /// <summary>A cloud at a direction from the camera (unit vector) with a size as a fraction of the sky radius.</summary>
        public void Add(Transform cloud, Vector3 dir, Vector2 size)
        {
            clouds.Add(cloud);
            dirs.Add(dir.normalized);
            sizes.Add(size);
        }

        void LateUpdate()
        {
            Camera c = Camera.main;
            if (c == null) foreach (Camera x in Camera.allCameras) if (x.enabled) { c = x; break; }
            if (c == null) return;
            float r = Mathf.Min(c.farClipPlane * 0.85f, 900f);
            Vector3 eye = c.transform.position;
            transform.position = eye;
            if (dome != null) dome.localScale = Vector3.one * r;
            drift += Time.deltaTime * 0.12f;
            Quaternion turn = Quaternion.Euler(0f, drift, 0f);
            for (int i = 0; i < clouds.Count; i++)
            {
                Vector3 d = turn * dirs[i];
                Transform t = clouds[i];
                t.position = eye + d * (r * 0.92f);
                t.rotation = Quaternion.LookRotation(d, Vector3.up);
                t.localScale = new Vector3(sizes[i].x * r, sizes[i].y * r, 1f);
            }
        }
    }

    public enum DrillPose { Stand, Aim, Crouch, CrouchAim, Prone, ProneAim }

    /// <summary>
    /// A model soldier on the training ground: holds a pose, turns to face someone, walks or jogs along a
    /// route (setting the game's own animator parameters: Speed, MoveX, MoveZ, Aiming, Grounded, and the
    /// stance), and can fire a shot sound for the range.
    /// </summary>
    public sealed class Drill : MonoBehaviour
    {
        Animator animator;
        Stance stance;
        Weapon weapon;
        WeaponHolder holder;
        DrillPose pose;
        bool started;

        public Transform faceTarget;
        readonly List<Vector3> route = new List<Vector3>();
        int next;
        bool loop;
        float speed, curSpeed;
        float turnSpeed = 240f;

        public bool Moving { get { return route.Count > 0 && next < route.Count; } }

        // Lying down: the prone clips hold the body up at standing root height (the player's Stance component
        // lowers it; these copies have none), so the model is let down until the hips are just off the ground.
        Transform model, hips, footL, footR;
        Vector3 modelRest;
        float groundY, lift;
        const float ProneHips = 0.15f, CrouchFoot = 0.09f;

        /// <summary>No weapon in the hands (the casualty on the stretcher).</summary>
        public void Unarmed()
        {
            if (weapon == null) return;
            foreach (Renderer r in weapon.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }

        public void Init(Weapon w, DrillPose p)
        {
            weapon = w;
            animator = GetComponentInChildren<Animator>();
            stance = GetComponentInChildren<Stance>();
            holder = GetComponentInChildren<WeaponHolder>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                model = animator.transform;
                if (animator.isHuman)
                {
                    hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                    footL = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                    footR = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                }
            }
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if (hips == null && (n.Contains("hips") || n.Contains("pelvis"))) hips = t;
                if (n.Contains("foot") && !n.Contains("end"))
                {
                    bool left = n.Contains("left") || n.Contains(".l") || n.Contains("_l") || n.EndsWith("l");
                    if (left && footL == null) footL = t;
                    else if (!left && footR == null) footR = t;
                }
            }
            if (model == null) model = transform;
            modelRest = model.localPosition;
            groundY = transform.position.y;
            Pose(p);
        }

        /// <summary>Height of the hips above the ground the soldier stands on (for checks); NaN if unknown.</summary>
        public float HipsHeight
        {
            get { return hips == null ? float.NaN : hips.position.y - (model == transform ? groundY : transform.position.y); }
        }

        void LateUpdate()
        {
            if (hips == null || model == null) return;
            bool prone = pose == DrillPose.Prone || pose == DrillPose.ProneAim;
            bool crouch = (pose == DrillPose.Crouch || pose == DrillPose.CrouchAim) && footL != null && footR != null && !Moving;
            float floor = model == transform ? groundY : transform.position.y;
            float want = 0f;
            if (prone) want = lift + (ProneHips - (hips.position.y - floor));
            else if (crouch) want = Mathf.Min(0f, lift + (CrouchFoot - (Mathf.Min(footL.position.y, footR.position.y) - floor)));
            want = Mathf.Clamp(want, -1.2f, 0.2f);
            if (Mathf.Abs(want - lift) < 0.002f) return;
            lift = Mathf.MoveTowards(lift, want, 3f * Time.deltaTime + (Mathf.Abs(want - lift) > 0.3f ? 1f : 0f));
            if (model == transform) transform.position = new Vector3(transform.position.x, groundY + lift, transform.position.z);
            else model.localPosition = modelRest + Vector3.up * lift;
        }

        void Start()
        {
            started = true;
            TrainingKit.Hold(holder, weapon);
            Pose(pose);
            if (animator != null) animator.Update(UnityEngine.Random.value * 2f);   // not all in step
        }

        public void Pose(DrillPose p)
        {
            pose = p;
            if (!started) return;
            StanceKind k = p == DrillPose.Crouch || p == DrillPose.CrouchAim ? StanceKind.Crouch : p == DrillPose.Prone || p == DrillPose.ProneAim ? StanceKind.Prone : StanceKind.Stand;
            if (stance != null) stance.Set(k);
        }

        /// <summary>Walk (or jog, with a higher speed) through the points; <paramref name="loop"/> goes round for ever.</summary>
        public void Go(IList<Vector3> points, float metresPerSecond, bool loopRoute)
        {
            route.Clear();
            route.AddRange(points);
            next = 0;
            speed = metresPerSecond;
            loop = loopRoute;
            if (pose != DrillPose.Stand && pose != DrillPose.Aim) Pose(DrillPose.Stand);
        }

        public void Stop() { route.Clear(); next = 0; }

        public void Warp(Vector3 world, float yaw)
        {
            Stop();
            transform.SetPositionAndRotation(world, Quaternion.Euler(0f, yaw, 0f));
            groundY = world.y;
        }

        void Update()
        {
            float target = 0f;
            if (Moving)
            {
                Vector3 to = route[next] - transform.position;
                to.y = 0f;
                float d = to.magnitude;
                if (d < 0.3f)
                {
                    next++;
                    if (next >= route.Count && loop) next = 0;
                }
                else
                {
                    target = speed;
                    Quaternion want = Quaternion.LookRotation(to);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * Time.deltaTime);
                    float step = Mathf.Min(d, curSpeed * Time.deltaTime);
                    Vector3 p = transform.position + transform.forward * step;
                    groundY = Mathf.Lerp(groundY, route[Mathf.Min(next, route.Count - 1)].y, 0.2f);
                    p.y = groundY + (model == transform ? lift : 0f);
                    transform.position = p;
                }
            }
            else if (faceTarget != null)
            {
                Vector3 to = faceTarget.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.5f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 90f * Time.deltaTime);
            }
            curSpeed = Mathf.MoveTowards(curSpeed, target, 6f * Time.deltaTime);
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.SetFloat("Speed", curSpeed);
                animator.SetFloat("MoveX", 0f);
                animator.SetFloat("MoveZ", curSpeed);
                bool aim = pose == DrillPose.Aim || pose == DrillPose.CrouchAim || pose == DrillPose.ProneAim;
                animator.SetBool("Aiming", aim && curSpeed < 0.1f);
                animator.SetBool("Grounded", true);
                if (stance == null)
                {
                    animator.SetBool("Crouch", pose == DrillPose.Crouch || pose == DrillPose.CrouchAim);
                    animator.SetBool("Prone", pose == DrillPose.Prone || pose == DrillPose.ProneAim);
                }
            }
        }

        /// <summary>The display name of the weapon in this soldier's hands (for its sound), or null.</summary>
        public string WeaponName { get { return weapon != null ? weapon.displayName : null; } }

        /// <summary>A shot sound from this soldier's weapon (no bullet).</summary>
        public void ShotSound(string weaponName)
        {
            Vector3 p = transform.position + Vector3.up * (pose == DrillPose.ProneAim || pose == DrillPose.Prone ? 0.3f : 1.4f) + transform.forward * 0.8f;
            Sfx.Shot(weaponName, p);
        }
    }
}
