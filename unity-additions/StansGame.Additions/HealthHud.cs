using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame.Additions
{
    /// <summary>
    /// Health bar for the local player, bottom left: colour by health, a light segment that shows the
    /// damage just taken, the numbers, and a key hint line with the player's own keyboard labels.
    /// Styles are created once, not on every OnGUI call.
    /// </summary>
    public sealed class HealthHud : MonoBehaviour
    {
        SoldierController soldier;
        Health health;
        float trail = -1f;
        float trailHoldUntil;

        static GUIStyle label;
        static GUIStyle hint;
        static Texture2D white;

        void Awake()
        {
            soldier = GetComponent<SoldierController>();
            health = GetComponent<Health>();
        }

        float Fraction()
        {
            if (health == null) return 0f;
            return Mathf.Clamp01(health.Current / Mathf.Max(1f, health.maxHealth));
        }

        void Update()
        {
            float f = Fraction();
            if (trail < 0f || f > trail)
            {
                trail = f;
                trailHoldUntil = Time.time + 0.5f;
            }
            else if (f < trail && Time.time > trailHoldUntil)
            {
                trail = Mathf.MoveTowards(trail, f, Time.deltaTime * 0.6f);
            }
        }

        static void EnsureStyles(float scale)
        {
            if (white == null)
            {
                white = new Texture2D(1, 1);
                white.SetPixel(0, 0, Color.white);
                white.Apply();
            }
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label);
                label.fontStyle = FontStyle.Bold;
                label.alignment = TextAnchor.MiddleLeft;
                label.normal.textColor = Color.white;
                hint = new GUIStyle(label);
                hint.fontStyle = FontStyle.Normal;
                hint.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
            }
            label.fontSize = Mathf.RoundToInt(16f * scale);
            hint.fontSize = Mathf.RoundToInt(13f * scale);
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (soldier == null || health == null || AdditionsBootstrap.LocalPlayer() != soldier) return;
            if (CinematicDirector.Playing || Net.InMenu) return;

            float s = Mathf.Max(0.85f, Screen.height / 1080f);   // readable on small windows too
            EnsureStyles(s);

            float f = Fraction();
            float x = 28f * s, w = 300f * s, h = 16f * s;
            // Sits above the game's own bottom-left weapon and stance text.
            float y = Screen.height - 138f * s;

            Color old = GUI.color;

            // Frame and background
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(x - 3f * s, y - 3f * s, w + 6f * s, h + 6f * s), white);

            // Damage just taken
            if (trail > f)
            {
                GUI.color = new Color(1f, 0.92f, 0.75f, 0.85f);
                GUI.DrawTexture(new Rect(x + w * f, y, w * (trail - f), h), white);
            }

            // Health
            Color fill = f > 0.6f ? new Color(0.36f, 0.78f, 0.34f)
                       : f > 0.3f ? new Color(0.95f, 0.70f, 0.18f)
                       : new Color(0.90f, 0.18f, 0.14f);
            if (f <= 0.3f && !health.IsDead) fill.a = 0.65f + 0.35f * Mathf.Sin(Time.time * 8f);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(x, y, w * f, h), white);

            // Ticks every 25 %
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            for (int i = 1; i < 4; i++) GUI.DrawTexture(new Rect(x + w * i / 4f - 1f, y, 2f, h), white);

            GUI.color = Color.white;
            string text = health.IsDead
                ? "DOWN"
                : string.Format("HEALTH   {0} / {1}", Mathf.CeilToInt(health.Current), Mathf.RoundToInt(health.maxHealth));
            GUI.Label(new Rect(x, y - 24f * s, w, 22f * s), text, label);

            string keys = string.Format("{0} move    {1} kneel    {2} prone    {3} view    F10 settings",
                KeyLabel.Move, KeyLabel.Of(Key.C), KeyLabel.Of(Key.Z), KeyLabel.Of(Key.V));
            GUI.Label(new Rect(x, y + h + 4f * s, w * 2f, 20f * s), keys, hint);

            GUI.color = old;
        }
    }
}
