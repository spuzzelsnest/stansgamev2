using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame.Additions
{
    /// <summary>
    /// Health panel for the local player, bottom left: colour by health, a light segment that shows the
    /// damage just taken, the numbers, stance or reloading, and a key hint line with the player's own
    /// keyboard labels. It covers the game's own health and weapon text lines, so health and ammo
    /// each show once (ammo stays in the game's weapon bar).
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
            GUI.depth = -10;   // over the game's own HUD; set on every event, Unity sorts on Layout
            if (Event.current.type != EventType.Repaint) return;
            if (soldier == null || health == null || AdditionsBootstrap.LocalPlayer() != soldier) return;
            if (CinematicDirector.Playing || Net.InMenu) return;

            float s = Mathf.Clamp(Screen.height / 1080f, 0.85f, 1.25f);
            EnsureStyles(s);
            Color old = GUI.color;

            // One panel over the spot where MissionDirector.OnGUI writes its own weapon line
            // (20, H-60) and health line (20, H-34, with fixed "Z prone"). Ammo stays in the weapon
            // bar next to it, so health and ammo each show once.
            float panelW = 330f, panelH = Mathf.Max(66f, 84f * s);
            Rect panel = new Rect(8f, Screen.height - 6f - panelH, panelW, panelH);
            GUI.color = new Color(0.05f, 0.05f, 0.06f, 1f);   // opaque, or the game's lines show through
            GUI.DrawTexture(panel, white);

            float pad = 12f * s;
            float x = panel.x + pad, w = panel.width - 2f * pad, h = 12f * s;
            float rowH = 20f * s;
            float y0 = panel.y + 8f * s;

            GUI.color = Color.white;
            string text = health.IsDead
                ? "DOWN"
                : string.Format("HEALTH   {0} / {1}", Mathf.CeilToInt(health.Current), Mathf.RoundToInt(health.maxHealth));
            label.alignment = TextAnchor.MiddleLeft;
            GUI.Label(new Rect(x, y0, w, rowH), text, label);

            Weapon wpn = soldier.CurrentWeapon;
            string state = wpn != null && wpn.IsReloading ? "RELOADING"
                         : soldier.IsProne ? "PRONE" : soldier.IsCrouching ? "KNEELING" : "STANDING";
            hint.alignment = TextAnchor.MiddleRight;
            GUI.color = state == "RELOADING" ? new Color(0.95f, 0.75f, 0.3f) : Color.white;
            GUI.Label(new Rect(x, y0, w, rowH), state, hint);
            hint.alignment = TextAnchor.MiddleLeft;

            float f = Fraction();
            float y = y0 + rowH + 4f * s;
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(new Rect(x, y, w, h), white);
            if (trail > f)
            {
                GUI.color = new Color(1f, 0.92f, 0.75f, 0.85f);
                GUI.DrawTexture(new Rect(x + w * f, y, w * (trail - f), h), white);
            }
            Color fill = f > 0.6f ? new Color(0.36f, 0.78f, 0.34f)
                       : f > 0.3f ? new Color(0.95f, 0.70f, 0.18f)
                       : new Color(0.90f, 0.18f, 0.14f);
            if (f <= 0.3f && !health.IsDead) fill.a = 0.65f + 0.35f * Mathf.Sin(Time.time * 8f);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(x, y, w * f, h), white);
            GUI.color = new Color(0f, 0f, 0f, 0.4f);
            for (int i = 1; i < 4; i++) GUI.DrawTexture(new Rect(x + w * i / 4f - 1f, y, 2f, h), white);

            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            string keys = string.Format("{0} move   {1} kneel   {2} prone   {3} view   F10",
                KeyLabel.Move, KeyLabel.Of(Key.C), KeyLabel.Of(Key.Z), KeyLabel.Of(Key.V));
            GUI.Label(new Rect(x, y + h + 4f * s, w, rowH), keys, hint);

            GUI.color = old;
        }
    }
}
