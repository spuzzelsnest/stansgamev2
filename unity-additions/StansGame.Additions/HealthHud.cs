using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame.Additions
{
    /// <summary>
    /// Health panel for the local player, bottom left: colour by health, a light segment that shows the
    /// damage just taken, the numbers, stance or reloading, and a key hint line with the player's own
    /// keyboard labels (key caps, can be turned off in the settings). It covers the game's own health and
    /// weapon text lines, so health and ammo each show once (ammo is in the kit bar next to it).
    /// Drawn in the house style (<see cref="Ui"/>) and scaled with the HUD size setting.
    /// </summary>
    public sealed class HealthHud : MonoBehaviour
    {
        SoldierController soldier;
        Health health;
        float trail = -1f;
        float trailHoldUntil;

        /// <summary>Where the panel was drawn last (for the kit bar next to it); zero when hidden.</summary>
        public static Rect PanelRect { get; private set; }

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

        void OnGUI()
        {
            GUI.depth = -10;   // over the game's own HUD; set on every event, Unity sorts on Layout
            if (Event.current.type != EventType.Repaint) return;
            if (soldier == null || health == null || AdditionsBootstrap.LocalPlayer() != soldier) return;
            if (CinematicDirector.Playing || Net.InMenu) { PanelRect = new Rect(); return; }
            Ui.ApplyGameFont();

            float hs = Ui.HudScale, m = Ui.HudMargin;
            bool hints = GameSettings.KeyHints;
            float panelW = 330f * hs, panelH = (hints ? 92f : 66f) * hs;
            Rect panel = new Rect(m, Screen.height - m - panelH, panelW, panelH);
            // The game's own lines (MissionDirector.OnGUI: weapon at 20, H-60 and health at 20, H-34,
            // fixed size, "Z prone" hard-coded) must stay covered at every HUD size.
            Rect cover = new Rect(8f, Screen.height - 64f, 330f, 58f);
            float xMin = Mathf.Min(panel.xMin, cover.xMin), yMin = Mathf.Min(panel.yMin, cover.yMin);
            panel = Rect.MinMaxRect(xMin, yMin, Mathf.Max(panel.xMax, cover.xMax), Mathf.Max(panel.yMax, cover.yMax));
            PanelRect = panel;
            Ui.Fill(panel, Ui.A(Ui.Night, 1f));   // opaque, or the game's lines show through
            Ui.Fill(new Rect(panel.x, panel.y, panel.width, Mathf.Max(2f, 2f * hs)), Ui.A(Ui.Brass, 0.85f));

            float pad = 12f * hs;
            float x = panel.x + pad, w = panel.width - 2f * pad;
            float y = panel.y + 8f * hs;
            float rowH = 26f * hs;

            float f = Fraction();
            Color fill = f > 0.6f ? Ui.Green : f > 0.3f ? Ui.Amber : Ui.Red;
            if (health.IsDead)
                Ui.Text(new Rect(x, y, w, rowH), "DOWN", UiFont.Body, 22f * hs, Ui.Red, TextAnchor.MiddleLeft, false, false, true);
            else
            {
                float nw = Ui.Width(Mathf.CeilToInt(health.Current).ToString(), UiFont.Body, 26f * hs, true);
                Ui.Text(new Rect(x, y, nw + 4f, rowH), Mathf.CeilToInt(health.Current).ToString(), UiFont.Body, 26f * hs, Ui.Paper, TextAnchor.MiddleLeft, false, false, true);
                Ui.Text(new Rect(x + nw + 6f * hs, y + 3f * hs, 200f * hs, rowH), "/ " + Mathf.RoundToInt(health.maxHealth) + "   HEALTH",
                        UiFont.Body, 14f * hs, Ui.Dim, TextAnchor.MiddleLeft, false, false, true);
            }

            Weapon wpn = soldier.CurrentWeapon;
            bool reloading = wpn != null && wpn.IsReloading;
            string state = reloading ? "RELOADING" : soldier.IsProne ? "PRONE" : soldier.IsCrouching ? "KNEELING" : "STANDING";
            Ui.Text(new Rect(x, y + 3f * hs, w, rowH), state, UiFont.Body, 14f * hs, reloading ? Ui.Amber : Ui.A(Ui.Paper, 0.85f), TextAnchor.MiddleRight, false, false, true);

            float bh = 12f * hs;
            float by = y + rowH + 6f * hs;
            Ui.Fill(new Rect(x, by, w, bh), Ui.A(Ui.Paper, 0.12f));
            if (trail > f) Ui.Fill(new Rect(x + w * f, by, w * (trail - f), bh), new Color(1f, 0.92f, 0.75f, 0.85f));
            if (f <= 0.3f && !health.IsDead) fill.a = 0.65f + 0.35f * Mathf.Sin(Time.time * 8f);
            Ui.Fill(new Rect(x, by, w * f, bh), fill);
            for (int i = 1; i < 4; i++) Ui.Fill(new Rect(x + w * i / 4f - 1f, by, 2f, bh), new Color(0f, 0f, 0f, 0.45f));

            if (hints)
            {
                float hy = by + bh + 9f * hs, hh = 20f * hs;
                Ui.Hints(x, hy, hh, new string[]
                {
                    KeyLabel.Move, "move", KeyLabel.Of(Key.C), "kneel", KeyLabel.Of(Key.Z), "prone", KeyLabel.Of(Key.V), "view",
                }, 0.85f);
            }
        }
    }
}
