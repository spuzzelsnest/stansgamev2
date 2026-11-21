using System.Collections.Generic;
using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// HUD pieces drawn with IMGUI, shared by the chapters. The health bar: "HEALTH 200 / 200" with the stance on the
    /// right, and a bar in four segments under it that goes from green to amber to red, with a pale strip that shows
    /// the damage just taken and drains away after it, and a slow red pulse when you are badly hurt.
    /// </summary>
    public static class Hud
    {
        static Texture2D white;
        static readonly Dictionary<Health, float> trail = new Dictionary<Health, float>();
        static readonly Dictionary<Health, float> lastSeen = new Dictionary<Health, float>();
        static readonly Dictionary<Health, float> hitAt = new Dictionary<Health, float>();

        public static readonly Color Green = new Color(0.36f, 0.78f, 0.33f), Amber = new Color(0.92f, 0.66f, 0.18f),
                                     Red = new Color(0.86f, 0.20f, 0.16f), Grey = new Color(0.68f, 0.68f, 0.68f);

        public static void Fill(Rect r, Color c)
        {
            if (!white) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
            var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = old;
        }

        static GUIStyle Style(float size, Color c, bool bold, TextAnchor a = TextAnchor.MiddleLeft) => new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Max(8, Mathf.RoundToInt(size)), fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
            alignment = a, richText = true, normal = { textColor = c }, clipping = TextClipping.Overflow,
        };

        /// <summary>Height of the health block for a given screen (to stack other lines above it).</summary>
        public static float HealthHeight => 58f;   // same height as the weapon slots (Loadout), which start at x = 330

        /// <summary>The health block with its bottom-left corner at (x, bottom). pose: "standing" etc. or null.</summary>
        public static void HealthBar(Health h, float x, float bottom, string pose)
        {
            if (!h) return;
            HitFeedback.Watch(h);                 // screen shake and blood when this health drops
            const float s = 1f;                   // pixel sizes, like the weapon slots next to it
            float w = 300f, hgt = HealthHeight;
            var box = new Rect(x, bottom - hgt, w, hgt);
            float max = Mathf.Max(1f, h.maxHealth), cur = Mathf.Clamp(h.Current, 0f, max), frac = cur / max;

            // the damage trail: jumps to the old value when hit, then drains down after a short pause
            if (!trail.TryGetValue(h, out float tr)) tr = frac;
            if (!lastSeen.TryGetValue(h, out float last)) last = frac;
            if (frac < last - 0.0001f) hitAt[h] = Time.unscaledTime;
            float since = hitAt.TryGetValue(h, out float ht) ? Time.unscaledTime - ht : 99f;
            if (frac >= tr) tr = frac;
            else if (since > 0.45f) tr = Mathf.MoveTowards(tr, frac, Time.unscaledDeltaTime * 0.6f);
            trail[h] = tr; lastSeen[h] = frac;

            // panel
            Fill(box, new Color(0f, 0f, 0f, 0.45f));
            if (since < 0.25f) Fill(box, new Color(0.8f, 0.1f, 0.05f, 0.25f * (1f - since / 0.25f)));   // hit flash

            // text line
            float pad = 10f;
            GUI.Label(new Rect(x + pad, box.y + 6f, 90f, 24f), "HEALTH", Style(16f, Color.white, true));
            Color numC = frac > 0.5f ? Color.white : frac > 0.25f ? Amber : Red;
            GUI.Label(new Rect(x + pad + 78f, box.y + 6f, 140f, 24f),
                      $"{Mathf.CeilToInt(cur)} <color=#9a9a9a>/ {Mathf.RoundToInt(max)}</color>", Style(16f, numC, true));
            if (!string.IsNullOrEmpty(pose))
                GUI.Label(new Rect(x + w - pad - 160f, box.y + 6f, 160f, 24f), pose.ToUpperInvariant(), Style(11f, Grey, false, TextAnchor.MiddleRight));

            // bar: track, damage trail, health, four segments
            var bar = new Rect(x + pad, box.y + 36f, w - 2f * pad, 11f);
            Fill(bar, new Color(1f, 1f, 1f, 0.10f));
            Color col = frac > 0.5f ? Color.Lerp(Amber, Green, (frac - 0.5f) / 0.5f) : frac > 0.25f ? Color.Lerp(Red, Amber, (frac - 0.25f) / 0.25f) : Red;
            if (frac <= 0.25f && frac > 0f) col = Color.Lerp(col, new Color(1f, 0.55f, 0.5f), 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f)));
            if (tr > frac) Fill(new Rect(bar.x + bar.width * frac, bar.y, bar.width * (tr - frac), bar.height), new Color(1f, 0.92f, 0.85f, 0.75f));
            Fill(new Rect(bar.x, bar.y, bar.width * frac, bar.height), col);
            Fill(new Rect(bar.x, bar.y, bar.width * frac, bar.height * 0.35f), new Color(1f, 1f, 1f, 0.18f));   // a little shine on top
            const int segments = 4;
            for (int i = 1; i < segments; i++)
                Fill(new Rect(bar.x + bar.width * i / segments - 1f * s, bar.y, 2f * s, bar.height), new Color(0f, 0f, 0f, 0.55f));
        }
    }
}
