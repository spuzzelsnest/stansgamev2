using System.Collections.Generic;
using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// What the player feels when hit: the camera shakes (harder for bigger hits) and blood spatters on the screen,
    /// biased towards the edges, then runs a little and fades. Also a red edge glow for a moment.
    /// Watches the health the HUD shows (Hud.HealthBar calls Watch), so it works the same for the host and for
    /// clients, whose hit points arrive from the host. Creates itself; nothing to put in the scene.
    /// </summary>
    [DefaultExecutionOrder(10000)]   // after the soldier camera has been placed for the frame
    public class HitFeedback : MonoBehaviour
    {
        static HitFeedback instance;
        Health watched;
        float lastValue = -1f, lastWatchFrame;

        // shake
        float trauma;
        Camera shaken;
        Vector3 shakePos; Quaternion shakeRot = Quaternion.identity;

        // splatter
        class Splat { public Vector2 pos; public float size, angle, born, life, drip; public int tex; }
        readonly List<Splat> splats = new List<Splat>();
        static Texture2D[] splatTex;
        static Texture2D vignette;
        float glowAt = -99f, glowStrength;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (instance) return;
            var go = new GameObject("Hit feedback");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<HitFeedback>();
        }

        /// <summary>The local player's health (called by the HUD every frame it is shown).</summary>
        public static void Watch(Health h)
        {
            if (!instance) Create();
            if (instance.watched != h) { instance.watched = h; instance.lastValue = h ? h.Current : -1f; }
            instance.lastWatchFrame = Time.frameCount;
        }

        void Update()
        {
            // take last frame's shake off again (the soldier camera may or may not set itself absolutely)
            if (shaken)
            {
                shaken.transform.localRotation = shaken.transform.localRotation * Quaternion.Inverse(shakeRot);
                shaken.transform.position -= shakePos;
                shaken = null; shakeRot = Quaternion.identity; shakePos = Vector3.zero;
            }
            if (watched && Time.frameCount - lastWatchFrame < 5)
            {
                float v = watched.Current;
                if (lastValue >= 0f && v < lastValue - 0.01f) Hit((lastValue - v) / Mathf.Max(1f, watched.maxHealth));
                lastValue = v;
            }
            else { watched = null; lastValue = -1f; }
            trauma = Mathf.Max(0f, trauma - Time.unscaledDeltaTime * 1.4f);
            splats.RemoveAll(s => Time.unscaledTime - s.born > s.life);
        }

        void Hit(float frac)
        {
            trauma = Mathf.Min(1f, trauma + 0.35f + frac * 2.2f);
            glowAt = Time.unscaledTime; glowStrength = Mathf.Clamp01(0.35f + frac * 3f);
            int n = Mathf.Clamp(1 + Mathf.RoundToInt(frac * 8f), 1, 4);
            for (int i = 0; i < n; i++)
            {
                // towards the edges and corners, rarely across the middle of the view
                Vector2 p = new Vector2(Random.value, Random.value);
                if (Random.value < 0.8f)
                {
                    if (Random.value < 0.5f) p.x = Random.value < 0.5f ? Random.Range(0f, 0.22f) : Random.Range(0.78f, 1f);
                    else p.y = Random.value < 0.5f ? Random.Range(0f, 0.22f) : Random.Range(0.78f, 1f);
                }
                splats.Add(new Splat
                {
                    pos = p, size = Random.Range(0.16f, 0.28f) * (0.8f + frac * 2.5f), angle = Random.Range(0f, 360f),
                    born = Time.unscaledTime, life = Random.Range(3.0f, 4.2f), drip = Random.Range(0.02f, 0.06f),
                    tex = Random.Range(0, 4),
                });
            }
            if (splats.Count > 14) splats.RemoveRange(0, splats.Count - 14);
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (!cam || trauma <= 0.001f) return;
            float t = Time.unscaledTime * 22f, k = trauma * trauma;
            float pitch = (Mathf.PerlinNoise(t, 0.1f) - 0.5f) * 2f * 2.8f * k;
            float yawA = (Mathf.PerlinNoise(0.3f, t) - 0.5f) * 2f * 2.2f * k;
            float roll = (Mathf.PerlinNoise(t, 7.7f) - 0.5f) * 2f * 3.5f * k;
            shakeRot = Quaternion.Euler(pitch, yawA, roll);
            shakePos = (cam.transform.right * (Mathf.PerlinNoise(t, 3.3f) - 0.5f) + cam.transform.up * (Mathf.PerlinNoise(5.5f, t) - 0.5f)) * 0.12f * k;
            cam.transform.localRotation = cam.transform.localRotation * shakeRot;
            cam.transform.position += shakePos;
            shaken = cam;
        }

        void OnGUI()
        {
            if (splats.Count == 0 && Time.unscaledTime - glowAt > 1f) return;
            GUI.depth = 10;   // behind the HUD text and bars
            EnsureTextures();
            float now = Time.unscaledTime, W = Screen.width, H = Screen.height, m = Mathf.Max(W, H);
            var old = GUI.color; var oldMatrix = GUI.matrix;

            // red glow round the edges
            float g = 1f - (now - glowAt) / 0.9f;
            if (g > 0f) { GUI.color = new Color(0.75f, 0.02f, 0.02f, 0.55f * g * glowStrength); GUI.DrawTexture(new Rect(0, 0, W, H), vignette); }

            foreach (var s in splats)
            {
                float age = (now - s.born) / s.life;
                float a = age < 0.6f ? 1f : 1f - (age - 0.6f) / 0.4f;
                float size = s.size * m * (age < 0.04f ? Mathf.Lerp(0.6f, 1f, age / 0.04f) : 1f);   // splashes on, not fades in
                var c = new Vector2(s.pos.x * W, s.pos.y * H + s.drip * H * Mathf.SmoothStep(0f, 1f, age));
                GUI.matrix = oldMatrix;
                GUIUtility.RotateAroundPivot(s.angle, c);
                GUI.color = new Color(1f, 1f, 1f, 0.9f * a);
                GUI.DrawTexture(new Rect(c.x - size / 2f, c.y - size / 2f, size, size), splatTex[s.tex]);
            }
            GUI.matrix = oldMatrix; GUI.color = old;
        }

        // ---- procedural textures: blood spatters (a blob with a ragged edge, droplets thrown out, a few runs) and a vignette
        static void EnsureTextures()
        {
            if (splatTex != null && vignette) return;
            splatTex = new Texture2D[4];
            for (int i = 0; i < splatTex.Length; i++) splatTex[i] = MakeSplat(256, 1944 + i * 17);
            const int v = 128;
            vignette = new Texture2D(v, v, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[v * v];
            for (int y = 0; y < v; y++)
                for (int x = 0; x < v; x++)
                {
                    float dx = (x + 0.5f) / v * 2f - 1f, dy = (y + 0.5f) / v * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.41f;
                    px[y * v + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((d - 0.45f) / 0.55f) * Mathf.Clamp01((d - 0.45f) / 0.55f));
                }
            vignette.SetPixels(px); vignette.Apply();
        }

        static Texture2D MakeSplat(int n, int seed)
        {
            var rnd = new System.Random(seed);
            float R() => (float)rnd.NextDouble();
            var alpha = new float[n * n];
            void Disc(float cx, float cy, float r, float strength)
            {
                int x0 = Mathf.Max(0, (int)(cx - r - 2)), x1 = Mathf.Min(n - 1, (int)(cx + r + 2));
                int y0 = Mathf.Max(0, (int)(cy - r - 2)), y1 = Mathf.Min(n - 1, (int)(cy + r + 2));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        float a = Mathf.Clamp01((r - d) / 1.5f) * strength;
                        int k = y * n + x; alpha[k] = Mathf.Max(alpha[k], a);
                    }
            }
            float c = n / 2f, baseR = n * 0.17f;
            // main blob: a ragged ring of overlapping discs
            float p1 = R() * 10f, p2 = R() * 10f;
            for (int i = 0; i < 48; i++)
            {
                float ang = i / 48f * Mathf.PI * 2f;
                float rr = baseR * (0.75f + 0.35f * Mathf.PerlinNoise(p1 + Mathf.Cos(ang) * 1.5f, p2 + Mathf.Sin(ang) * 1.5f));
                Disc(c + Mathf.Cos(ang) * rr * 0.55f, c + Mathf.Sin(ang) * rr * 0.55f, rr * 0.6f, 1f);
            }
            Disc(c, c, baseR * 0.8f, 1f);
            // spikes and thrown droplets
            int spikes = 7 + rnd.Next(6);
            for (int s = 0; s < spikes; s++)
            {
                float ang = R() * Mathf.PI * 2f, len = baseR * (1.1f + R() * 1.6f);
                for (float t = 0.5f; t < 1f; t += 0.04f)
                    Disc(c + Mathf.Cos(ang) * len * t, c + Mathf.Sin(ang) * len * t, baseR * 0.16f * (1.15f - t), 1f);
                int drops = 1 + rnd.Next(3);
                for (int d = 0; d < drops; d++)
                {
                    float dl = len * (1.05f + R() * 0.6f);
                    if (dl > n * 0.47f) dl = n * 0.47f;
                    Disc(c + Mathf.Cos(ang + (R() - 0.5f) * 0.3f) * dl, c + Mathf.Sin(ang + (R() - 0.5f) * 0.3f) * dl, baseR * (0.05f + R() * 0.08f), 0.95f);
                }
            }
            // a couple of runs down the screen (texture y goes up, so "down" is towards y = 0)
            for (int r = 0; r < 2; r++)
            {
                float x = c + (R() - 0.5f) * baseR * 1.2f, w = baseR * (0.08f + R() * 0.07f), len = baseR * (1.2f + R() * 1.4f);
                for (float t = 0f; t < 1f; t += 0.02f) Disc(x + Mathf.Sin(t * 6f) * 1.5f, c - t * len, w * (1f - t * 0.4f), 1f);
                Disc(x, c - len, w * 1.5f, 1f);
            }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int k = 0; k < px.Length; k++)
            {
                int x = k % n, y = k / n;
                float shade = 0.75f + 0.25f * Mathf.PerlinNoise(x * 0.05f + seed, y * 0.05f);
                float a = alpha[k];
                // darker, thicker in the middle of the blob; brighter thin red at the ragged edge
                px[k] = new Color(Mathf.Lerp(0.55f, 0.32f, a) * shade, 0.015f, 0.02f, a * 0.92f);
            }
            tex.SetPixels(px); tex.Apply();
            return tex;
        }
    }
}
