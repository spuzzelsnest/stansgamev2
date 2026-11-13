using UnityEngine;
using UnityEngine.Rendering;

namespace StansGame.Additions
{
    /// <summary>
    /// A night sky for the level, which otherwise clears to one flat colour: a dome with a gradient from
    /// the misty horizon (the fog colour, so distant ground fades into it) to a dark zenith, broken
    /// cloud lit from the moon's side, faint stars, a halo round the moon and the moon itself, which
    /// sits where the moonlight comes from. Everything is generated (no assets) and drawn with the
    /// Sprites/Default shader, which is in the build and unlit. The same sky is used as the reflection
    /// cubemap, so glossy surfaces reflect a sky instead of black.
    /// <see cref="Atmosphere"/> shows and hides it with the "Enhanced night lighting" setting.
    /// </summary>
    public sealed class NightSky : MonoBehaviour
    {
        /// <summary>Horizon colour; the fog uses the same colour.</summary>
        public static readonly Color Horizon = new Color(0.105f, 0.125f, 0.17f);
        static readonly Color Zenith = new Color(0.012f, 0.017f, 0.035f);
        static readonly Color Glow = new Color(0.55f, 0.60f, 0.72f);

        const float Radius = 1000f;
        const float LowestDeg = -20f;

        static Texture2D skyTexture, moonTexture;
        static Cubemap reflection;
        static Material skyMaterial, moonMaterial;
        static Mesh dome, quad;

        GameObject domeObject, moonObject;
        Vector3 moonDir = new Vector3(0f, 0.5f, 0.86f);
        bool shown;

        public static bool Shown { get; private set; }
        public static Cubemap Reflection { get { return reflection; } }

        /// <summary>Shows the sky with the moon in the given direction (towards the moon).</summary>
        public void Show(Vector3 towardsMoon)
        {
            if (towardsMoon.sqrMagnitude < 0.01f) towardsMoon = moonDir;
            towardsMoon.Normalize();
            if (skyTexture == null || (towardsMoon - moonDir).sqrMagnitude > 0.0001f)
            {
                moonDir = towardsMoon;
                Build();
            }
            if (domeObject == null) MakeObjects();
            domeObject.SetActive(true);
            moonObject.SetActive(true);
            if (!shown) RenderPipelineManager.beginCameraRendering += Follow;
            shown = true;
            Shown = true;
        }

        public void Hide()
        {
            if (domeObject != null) domeObject.SetActive(false);
            if (moonObject != null) moonObject.SetActive(false);
            if (shown) RenderPipelineManager.beginCameraRendering -= Follow;
            shown = false;
            Shown = false;
        }

        void OnDestroy()
        {
            Hide();
        }

        /// <summary>Keeps the dome centred on whichever camera is drawing, scaled inside its far plane.</summary>
        void Follow(ScriptableRenderContext ctx, Camera cam)
        {
            if (domeObject == null || cam == null) return;
            float scale = Mathf.Min(1f, cam.farClipPlane * 0.9f / Radius);
            Vector3 p = cam.transform.position;
            domeObject.transform.position = p;
            domeObject.transform.localScale = Vector3.one * scale;
            moonObject.transform.position = p + moonDir * Radius * 0.95f * scale;
            moonObject.transform.rotation = Quaternion.LookRotation(moonDir);
            moonObject.transform.localScale = Vector3.one * Radius * 0.95f * scale * 0.032f;   // about 1.8 degrees across, with the halo
        }

        void MakeObjects()
        {
            Shader s = Shader.Find("Sprites/Default");
            if (skyMaterial == null)
            {
                skyMaterial = new Material(s);
                skyMaterial.name = "Night sky (Stan's Game additions)";
                skyMaterial.mainTexture = skyTexture;
                skyMaterial.renderQueue = 1000;   // before all opaque geometry; it writes no depth
            }
            if (moonMaterial == null)
            {
                moonMaterial = new Material(s);
                moonMaterial.name = "Moon (Stan's Game additions)";
                moonMaterial.mainTexture = moonTexture;
                moonMaterial.color = new Color(1.6f, 1.65f, 1.75f, 1f);   // over 1, so the bloom catches it
                moonMaterial.renderQueue = 1001;
            }
            if (dome == null) dome = DomeMesh();
            if (quad == null) quad = QuadMesh();

            domeObject = Part("Night sky dome", dome, skyMaterial);
            moonObject = Part("Moon disc", quad, moonMaterial);
        }

        GameObject Part(string name, Mesh mesh, Material mat)
        {
            GameObject go = new GameObject(name + " (Stan's Game additions)");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.allowOcclusionWhenDynamic = false;
            return go;
        }

        static Mesh DomeMesh()
        {
            const int seg = 64, rings = 28;
            Vector3[] v = new Vector3[(seg + 1) * (rings + 1)];
            Vector2[] uv = new Vector2[v.Length];
            for (int r = 0; r <= rings; r++)
            {
                float vv = (float)r / rings;
                float el = Mathf.Lerp(LowestDeg, 90f, vv) * Mathf.Deg2Rad;
                for (int i = 0; i <= seg; i++)
                {
                    float uu = (float)i / seg;
                    float az = uu * Mathf.PI * 2f;
                    int k = r * (seg + 1) + i;
                    v[k] = new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az)) * Radius;
                    uv[k] = new Vector2(uu, vv);
                }
            }
            int[] tri = new int[seg * rings * 6];
            int n = 0;
            for (int r = 0; r < rings; r++)
                for (int i = 0; i < seg; i++)
                {
                    int a = r * (seg + 1) + i, b = a + 1, c = a + seg + 1, d = c + 1;
                    tri[n++] = a; tri[n++] = c; tri[n++] = b;
                    tri[n++] = b; tri[n++] = c; tri[n++] = d;
                }
            Mesh m = new Mesh();
            m.name = "Night sky dome";
            m.vertices = v;
            m.uv = uv;
            m.triangles = tri;
            m.bounds = new Bounds(Vector3.zero, Vector3.one * Radius * 2f);
            return m;
        }

        static Mesh QuadMesh()
        {
            Mesh m = new Mesh();
            m.name = "Moon quad";
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Sky colour (gamma space) in a direction.</summary>
        Color Sky(Vector3 d, bool stars)
        {
            float el = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (el <= 0f) return Horizon;
            float t = Mathf.Pow(Mathf.Clamp01(el / 90f), 0.42f);
            Color c = Color.Lerp(Horizon, Zenith, t);

            float m = Mathf.Max(0f, Vector3.Dot(d, moonDir));
            float halo = Mathf.Pow(m, 90f) * 0.30f + Mathf.Pow(m, 12f) * 0.07f + Mathf.Pow(m, 3f) * 0.025f;
            c += Glow * halo;

            // Broken cloud on a flat layer: darker than the sky, with silver edges towards the moon.
            float k = 1f / Mathf.Max(0.06f, d.y);
            float x = d.x * k * 1.3f, z = d.z * k * 1.3f;
            float n = Fbm(x + 13.1f, z + 7.7f);
            float cover = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.50f, 0.74f, n));
            cover *= Mathf.Clamp01(el / 4f);
            float edge = Mathf.Clamp01(1f - Mathf.Abs(cover - 0.45f) * 2.2f);
            Color cloud = Color.Lerp(Horizon * 0.62f, Horizon * 0.95f, Mathf.Clamp01(1f - el / 70f));
            cloud += Glow * (edge * (Mathf.Pow(m, 4f) * 0.20f + 0.015f)) + Glow * (Mathf.Pow(m, 20f) * 0.18f * cover);
            c = Color.Lerp(c, cloud, cover * 0.85f);

            if (stars && cover < 0.25f)
            {
                float s = Hash(Mathf.Floor(x * 160f), Mathf.Floor(z * 160f));
                if (s > 0.9965f)
                {
                    float b = (s - 0.9965f) / 0.0035f * 0.55f * Mathf.Clamp01((el - 8f) / 30f) * (1f - cover * 4f) * (1f - halo * 6f);
                    if (b > 0f) c += new Color(b, b, b * 1.05f);
                }
            }
            c.a = 1f;
            return c;
        }

        static float Hash(float x, float y)
        {
            float h = Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        static float Fbm(float x, float y)
        {
            float a = 0.5f, s = 0f, f = 1f;
            for (int i = 0; i < 5; i++)
            {
                s += a * Mathf.PerlinNoise(x * f, y * f);
                f *= 2.03f;
                a *= 0.5f;
            }
            return s / 0.97f;
        }

        void Build()
        {
            const int w = 2048, h = 512;
            if (skyTexture == null)
            {
                skyTexture = new Texture2D(w, h, TextureFormat.RGBA32, true, false);
                skyTexture.name = "Night sky";
                skyTexture.wrapModeU = TextureWrapMode.Repeat;
                skyTexture.wrapModeV = TextureWrapMode.Clamp;
                skyTexture.filterMode = FilterMode.Trilinear;
                skyTexture.anisoLevel = 4;
            }
            Color32[] px = new Color32[w * h];
            for (int j = 0; j < h; j++)
            {
                float el = Mathf.Lerp(LowestDeg, 90f, (j + 0.5f) / h) * Mathf.Deg2Rad;
                for (int i = 0; i < w; i++)
                {
                    float az = (i + 0.5f) / w * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
                    px[j * w + i] = Sky(d, true);
                }
            }
            skyTexture.SetPixels32(px);
            skyTexture.Apply(true, false);
            if (skyMaterial != null) skyMaterial.mainTexture = skyTexture;

            if (moonTexture == null)
            {
                const int ms = 128;
                moonTexture = new Texture2D(ms, ms, TextureFormat.RGBA32, true, false);
                moonTexture.name = "Moon";
                moonTexture.wrapMode = TextureWrapMode.Clamp;
                Color32[] mp = new Color32[ms * ms];
                const float disc = 0.28f;   // disc radius in the quad; the rest is a soft halo
                for (int j = 0; j < ms; j++)
                    for (int i = 0; i < ms; i++)
                    {
                        float u = (i + 0.5f) / ms - 0.5f, v = (j + 0.5f) / ms - 0.5f;
                        float r = Mathf.Sqrt(u * u + v * v);
                        float inside = Mathf.Clamp01((disc - r) * ms * 0.9f);
                        float maria = 0.78f + 0.22f * Fbm(u * 9f + 3f, v * 9f + 5f);
                        float limb = Mathf.Sqrt(Mathf.Clamp01(1f - (r / disc) * (r / disc)));
                        float lum = inside * maria * (0.75f + 0.25f * limb);
                        float halo = (1f - inside) * Mathf.Pow(Mathf.Clamp01(1f - (r - disc) / (0.5f - disc)), 3f) * 0.18f;
                        float a = Mathf.Clamp01(inside + halo);
                        // Straight alpha (Sprites/Default multiplies by alpha itself): moon grey inside, pale blue halo outside.
                        Color moonFace = new Color(lum, lum, lum * 1.02f);
                        Color mist = new Color(0.80f, 0.85f, 1f);
                        Color c = Color.Lerp(mist, moonFace, inside);
                        c.a = a;
                        mp[j * ms + i] = c;
                    }
                moonTexture.SetPixels32(mp);
                moonTexture.Apply(true, false);
            }

            // Reflection cubemap from the same sky (no stars, they would only sparkle).
            if (reflection == null)
            {
                reflection = new Cubemap(64, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB, UnityEngine.Experimental.Rendering.TextureCreationFlags.MipChain);
                reflection.name = "Night sky reflection";
            }
            const int cs = 64;
            Color[] face = new Color[cs * cs];
            for (int f = 0; f < 6; f++)
            {
                for (int j = 0; j < cs; j++)
                    for (int i = 0; i < cs; i++)
                    {
                        float a = 2f * (i + 0.5f) / cs - 1f, b = 2f * (j + 0.5f) / cs - 1f;
                        Vector3 d;
                        switch (f)
                        {
                            case 0: d = new Vector3(1f, -b, -a); break;    // +X
                            case 1: d = new Vector3(-1f, -b, a); break;    // -X
                            case 2: d = new Vector3(a, 1f, b); break;      // +Y
                            case 3: d = new Vector3(a, -1f, -b); break;    // -Y
                            case 4: d = new Vector3(a, -b, 1f); break;     // +Z
                            default: d = new Vector3(-a, -b, -1f); break;  // -Z
                        }
                        // Brighter than the sky itself: it stands in for the moonlit haze and clouds that glossy surfaces pick up.
                        Color c = d.y < 0f ? Horizon * 0.6f : Sky(d.normalized, false) * 1.8f;
                        c.a = 1f;
                        face[j * cs + i] = c;
                    }
                reflection.SetPixels(face, (CubemapFace)f);
            }
            reflection.Apply(true, false);
        }
    }
}
