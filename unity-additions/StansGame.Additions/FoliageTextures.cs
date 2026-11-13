using UnityEngine;

namespace StansGame.Additions
{
    /// <summary>
    /// Texture work for <see cref="Foliage"/>: mipmaps that keep alpha-clipped leaves and grass as full in
    /// the distance as up close, and generated grass textures.
    /// <para>Ordinary mipmaps average the alpha, so a leaf crown that is 50 % covered at full size drops
    /// below the clip threshold a few mip levels down and breaks up into speckle; the black texels round
    /// the leaves also bleed into the colour and darken the edges. Here each level is averaged weighted by
    /// alpha (no dark fringe), and its alpha is scaled so the share of texels that pass the clip threshold
    /// stays the same as at full size (what Unity's "Mip Maps Preserve Coverage" import option does).</para>
    /// </summary>
    public static class FoliageTextures
    {
        /// <summary>Reads any texture (readable or not) back from the GPU, in its own colour space.</summary>
        public static Color32[] Read(Texture src)
        {
            RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D c = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, false);
            c.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0, false);
            c.Apply(false, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Color32[] px = c.GetPixels32();
            Object.Destroy(c);
            return px;
        }

        /// <summary>A copy of <paramref name="src"/> with coverage-preserving, fringe-free mipmaps (DXT5).</summary>
        public static Texture2D WithCoverageMips(Texture src, float cutoff)
        {
            Texture2D t = Build(Read(src), src.width, src.height, cutoff, src.name + " (coverage mips)", true);
            t.wrapMode = src.wrapMode;
            return t;
        }

        public static Texture2D Build(Color32[] px, int w, int h, float cutoff, string name, bool gpuOnly)
        {
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, true, false);
            t.name = name;
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 4;
            byte cut = (byte)Mathf.Clamp(Mathf.RoundToInt(cutoff * 255f), 1, 254);
            Bleed(px, w, h);
            float cov0 = Coverage(px, cut);
            t.SetPixels32(px, 0);
            Color32[] cur = px;
            int cw = w, ch = h;
            for (int m = 1; m < t.mipmapCount; m++)
            {
                int nw = Mathf.Max(1, cw / 2), nh = Mathf.Max(1, ch / 2);
                Color32[] next = Down(cur, cw, ch, nw, nh);
                t.SetPixels32(Preserve(next, cut, cov0), m);
                cur = next; cw = nw; ch = nh;
            }
            t.Apply(false, false);
            if (gpuOnly)
            {
                t.Compress(true);
                t.Apply(false, true);   // drop the CPU copy
            }
            return t;
        }

        /// <summary>Fully transparent texels take the average colour of the opaque ones (no black fringe in bilinear filtering).</summary>
        static void Bleed(Color32[] px, int w, int h)
        {
            long r = 0, g = 0, b = 0, n = 0;
            for (int i = 0; i < px.Length; i++)
                if (px[i].a > 128) { r += px[i].r; g += px[i].g; b += px[i].b; n++; }
            if (n == 0) return;
            Color32 avg = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 0);
            for (int i = 0; i < px.Length; i++)
                if (px[i].a == 0) px[i] = avg;
        }

        static float Coverage(Color32[] px, byte cut)
        {
            int n = 0;
            for (int i = 0; i < px.Length; i++) if (px[i].a >= cut) n++;
            return (float)n / px.Length;
        }

        /// <summary>2x2 box filter, colour weighted by alpha.</summary>
        static Color32[] Down(Color32[] src, int sw, int sh, int dw, int dh)
        {
            Color32[] d = new Color32[dw * dh];
            for (int y = 0; y < dh; y++)
            {
                int y0 = Mathf.Min(y * 2, sh - 1), y1 = Mathf.Min(y * 2 + 1, sh - 1);
                for (int x = 0; x < dw; x++)
                {
                    int x0 = Mathf.Min(x * 2, sw - 1), x1 = Mathf.Min(x * 2 + 1, sw - 1);
                    Color32 a = src[y0 * sw + x0], b = src[y0 * sw + x1], c = src[y1 * sw + x0], e = src[y1 * sw + x1];
                    int wa = a.a + 1, wb = b.a + 1, wc = c.a + 1, we = e.a + 1;   // +1: fully clear blocks still average
                    int ws = wa + wb + wc + we;
                    d[y * dw + x] = new Color32(
                        (byte)((a.r * wa + b.r * wb + c.r * wc + e.r * we) / ws),
                        (byte)((a.g * wa + b.g * wb + c.g * wc + e.g * we) / ws),
                        (byte)((a.b * wa + b.b * wb + c.b * wc + e.b * we) / ws),
                        (byte)((a.a + b.a + c.a + e.a + 2) / 4));
                }
            }
            return d;
        }

        /// <summary>A copy with alpha scaled so the share of texels at or over the cutoff matches <paramref name="target"/>.</summary>
        static Color32[] Preserve(Color32[] px, byte cut, float target)
        {
            int[] hist = new int[256];
            for (int i = 0; i < px.Length; i++) hist[px[i].a]++;
            int want = Mathf.RoundToInt(target * px.Length), sum = 0, threshold = cut;
            for (int a = 255; a >= 1; a--)
            {
                sum += hist[a];
                if (sum >= want) { threshold = a; break; }
            }
            float scale = Mathf.Clamp((float)cut / Mathf.Max(1, threshold), 0.5f, 4f);
            Color32[] o = new Color32[px.Length];
            for (int i = 0; i < px.Length; i++)
            {
                Color32 c = px[i];
                c.a = (byte)Mathf.Min(255, Mathf.RoundToInt(c.a * scale));
                o[i] = c;
            }
            return o;
        }

        /// <summary>
        /// A grass tuft texture: many thin tapered blades, darker at the root, with gaps between them so the
        /// billboard does not read as a card. Near-white so the terrain's healthy and dry colours give the hue.
        /// </summary>
        public static Texture2D Grass(int seed, int blades, float minHeight, float maxLean, float dryShare, string name)
        {
            const int s = 256;
            Random.State keep = Random.state;
            Random.InitState(seed);
            float[] alpha = new float[s * s];
            Color[] col = new Color[s * s];
            for (int k = 0; k < blades; k++)
            {
                float x0 = Random.Range(0.08f, 0.92f) * s;
                float height = Random.Range(minHeight, 0.98f) * s;
                float width = Random.Range(3.2f, 6.5f);
                float lean = Random.Range(-maxLean, maxLean) * s;
                float shade = Random.Range(0.80f, 1.0f);
                bool dry = Random.value < dryShare;
                Color tint = dry ? new Color(1.08f, 0.98f, 0.72f) : new Color(Random.Range(0.92f, 1f), 1f, Random.Range(0.85f, 0.95f));
                for (int y = 0; y < (int)height; y++)
                {
                    float t = y / height;
                    float cx = x0 + lean * t * t;
                    float hw = width * 0.5f * Mathf.Pow(1f - t, 0.75f) + 0.35f;
                    int xa = Mathf.Max(0, Mathf.FloorToInt(cx - hw - 1f)), xb = Mathf.Min(s - 1, Mathf.CeilToInt(cx + hw + 1f));
                    float lum = Mathf.Lerp(0.42f, 1f, Mathf.Pow(t, 0.6f)) * shade;
                    for (int x = xa; x <= xb; x++)
                    {
                        float a = Mathf.Clamp01(hw - Mathf.Abs(x + 0.5f - cx) + 0.5f);
                        if (a <= 0f) continue;
                        int i = y * s + x;
                        // Blade side: a little lighter on one edge.
                        float side = 1f + 0.08f * Mathf.Sign(x + 0.5f - cx);
                        Color c = tint * (lum * side);
                        if (a >= alpha[i]) col[i] = c;
                        alpha[i] = Mathf.Max(alpha[i], a);
                    }
                }
            }
            Random.state = keep;
            Color32[] px = new Color32[s * s];
            for (int i = 0; i < px.Length; i++)
            {
                Color c = col[i];
                px[i] = new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f), (byte)(Mathf.Clamp01(c.b) * 255f), (byte)(alpha[i] * 255f));
            }
            Texture2D tex = Build(px, s, s, 0.5f, name, false);   // the terrain packs detail textures into an atlas, so keep it readable
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }
    }
}
