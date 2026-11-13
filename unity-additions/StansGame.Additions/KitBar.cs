using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame.Additions
{
    /// <summary>
    /// The weapon and kit bar for the local player, bottom left next to the health panel, in the house
    /// style and scaled with the HUD: main weapon, side arm, grenades and field dressings with their
    /// keys (1 to 4) and ammunition. It is drawn over the game's own bar (<c>Loadout.OnGUI</c>, fixed
    /// 190 x 58 pixel slots at x = 330, y = height - 68) on an opaque backing that covers that bar, so the
    /// game's bar is not seen. <c>Loadout</c> is left untouched: its pickup prompt and notices (the notice
    /// line sits 38 pixels above the bar, y = height - 106 to height - 78) and the crosshair's grenade
    /// ring keep working. At large HUD sizes the row reaches above y = height - 78 and covers part of the
    /// notice line.
    /// </summary>
    public sealed class KitBar : MonoBehaviour
    {
        SoldierController soldier;
        Loadout loadout;

        void Awake()
        {
            soldier = GetComponent<SoldierController>();
            loadout = GetComponent<Loadout>();
        }

        /// <summary>True when the bar is drawn for this player this frame.</summary>
        public bool Showing
        {
            get
            {
                return soldier != null && loadout != null && AdditionsBootstrap.LocalPlayer() == soldier
                    && (soldier.enabled || GamePause.PausedPlayer == soldier) && !Net.InMenu && !CinematicDirector.Playing;
            }
        }

        static string AmmoText(Weapon w)
        {
            if (w == null) return "";
            if (w.IsReloading) return "RELOADING";
            return w.Ammo + " / " + w.SpareRounds;
        }

        void OnGUI()
        {
            GUI.depth = -10;
            if (Event.current.type != EventType.Repaint || !Showing) return;
            float hs = Ui.HudScale, m = Ui.HudMargin;
            Rect health = HealthHud.PanelRect;
            float slotW = 176f * hs, slotH = 66f * hs, gap = 6f * hs;
            float x = health.width > 0f ? health.xMax + 10f * hs : m + 340f * hs;
            float y = Screen.height - m - slotH;
            // The game's own bar: 4 slots of 190 x 58 at x = 330 + i * 198, y = height - 68.
            Rect game = new Rect(330f, Screen.height - 68f, 3f * 198f + 190f, 58f);
            // Stretch the slots when the scaled row is narrower than the game's bar, so no plate shows empty.
            slotW = Mathf.Max(slotW, (game.xMax - x - 3f * gap) / 4f);
            Rect row = new Rect(x, y, 4f * slotW + 3f * gap, slotH);
            Ui.Fill(game, Ui.A(Ui.Night, 1f));
            Ui.Fill(row, Ui.A(Ui.Night, 1f));
            int sel = loadout.Selected;
            bool bandaging = loadout.Bandaging;
            if (bandaging) sel = 3;

            for (int i = 0; i < 4; i++)
            {
                Rect r = new Rect(x + i * (slotW + gap), y, slotW, slotH);
                bool on = i == sel;
                string name, info;
                float progress = -1f;
                Color infoColor = Ui.Paper;
                bool empty = false;
                switch (i)
                {
                    case 0:
                        empty = loadout.primary == null;
                        name = empty ? "Main weapon" : loadout.primary.displayName;
                        info = empty ? "empty" : AmmoText(loadout.primary);
                        if (!empty && loadout.primary.IsReloading) infoColor = Ui.Amber;
                        else if (!empty && loadout.primary.Ammo == 0) infoColor = Ui.Red;
                        break;
                    case 1:
                        empty = loadout.sidearm == null;
                        name = empty ? "Side arm" : loadout.sidearm.displayName;
                        info = empty ? "empty" : AmmoText(loadout.sidearm);
                        if (!empty && loadout.sidearm.IsReloading) infoColor = Ui.Amber;
                        else if (!empty && loadout.sidearm.Ammo == 0) infoColor = Ui.Red;
                        break;
                    case 2:
                        int g = loadout.grenades != null ? loadout.grenades.Count : 0;
                        empty = g == 0;
                        name = empty ? "Grenades" : loadout.grenades[0].displayName;
                        info = "× " + g;
                        break;
                    default:
                        name = "Field dressing";
                        empty = loadout.dressings <= 0 && !bandaging;
                        info = bandaging ? "DRESSING WOUNDS" : "× " + loadout.dressings;
                        if (bandaging) { progress = loadout.BandageProgress; infoColor = Ui.Green; }
                        break;
                }

                float a = on ? 1f : 0.72f;
                Ui.Fill(r, Ui.A(Ui.Night, on ? 0.86f : 0.66f));
                if (on)
                {
                    Ui.Fill(new Rect(r.x, r.y, r.width, Mathf.Max(2f, 3f * hs)), Ui.Brass);
                    Ui.Fill(r, Ui.A(Ui.Paper, 0.05f));
                }
                float pad = 9f * hs;
                float capH = 20f * hs;
                Ui.KeyCap(r.x + pad, r.y + pad, capH, KeyLabel.Of(Key.Digit1 + i), on ? 1f : 0.6f);
                Ui.Text(new Rect(r.x + pad + capH + 8f * hs, r.y + pad - 2f * hs, r.width - 2f * pad - capH - 8f * hs, capH + 4f * hs),
                        empty ? name.ToUpperInvariant() : name.ToUpperInvariant(), UiFont.Body, 14f * hs, Ui.A(on ? Ui.Brass : Ui.Dim, empty ? 0.6f : a), TextAnchor.MiddleLeft, true, false, true);
                Rect infoR = new Rect(r.x + pad, r.y + pad + capH + 2f * hs, r.width - 2f * pad, r.height - 2f * pad - capH - 2f * hs);
                if (progress >= 0f)
                {
                    Rect bar = new Rect(infoR.x, infoR.yMax - 6f * hs, infoR.width, 5f * hs);
                    Ui.Fill(bar, Ui.A(Ui.Paper, 0.15f));
                    Ui.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(progress), bar.height), Ui.Green);
                    infoR.height -= 8f * hs;
                }
                float infoSize = info.Length > 12 ? 15f * hs : 24f * hs;
                Ui.Text(infoR, empty && i < 2 ? "—" : info, UiFont.Body, infoSize, Ui.A(infoColor, empty ? 0.5f : a), TextAnchor.MiddleLeft, true, false, true);
                if (on && i == 2 && !empty)
                    Ui.Text(infoR, "LMB throw", UiFont.Body, 13f * hs, Ui.A(Ui.Dim, 0.9f), TextAnchor.MiddleRight, true);
            }
            Ui.ApplyGameFont();
        }
    }
}
