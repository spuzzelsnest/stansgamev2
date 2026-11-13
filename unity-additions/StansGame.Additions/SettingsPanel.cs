using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame.Additions
{
    /// <summary>
    /// The settings window: <b>F10</b> in the field, or Settings in the main menu and the pause menu.
    /// The rows come from <see cref="SettingsRegistry"/>, grouped in tabs (Video, Audio, Gameplay,
    /// Controls, and any tab another script adds). Mouse, keyboard (arrows or the move keys, Q/E or Tab
    /// for the tabs, Enter, Esc) and gamepad (d-pad, A, B, shoulder buttons) all work.
    /// While it is open in the field the player is paused (<see cref="GamePause"/>). Saved in
    /// <c>PlayerPrefs</c> when it closes.
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        /// <summary>
        /// True while a menu window is open over the field or the menu (settings, pause menu or controls
        /// page): game input such as G, T, B, V and mouse look must wait.
        /// </summary>
        public static bool Open { get { return WindowOpen || PauseMenu.Open || ControlsPage.Open; } }

        /// <summary>True while the settings window itself is open.</summary>
        public static bool WindowOpen { get; private set; }

        /// <summary>The player whose controller is switched off by a menu window, or null.</summary>
        public static SoldierController PausedPlayer { get { return GamePause.PausedPlayer; } }

        int tab;
        int row;
        int scroll;
        int dragging = -1;
        float openedAt;
        Vector2 lastMouse = new Vector2(-1f, -1f);
        List<SettingsRegistry.Row> rows = new List<SettingsRegistry.Row>();

        void Update()
        {
            Ui.TrackDevice();
            Keyboard kb = Keyboard.current;
            if (kb != null && kb[Key.F10].wasPressedThisFrame && !ControlsPage.Open) { SetOpen(!WindowOpen); return; }
            if (!WindowOpen || ControlsPage.Open) return;

            rows = SettingsRegistry.RowsOf(SettingsRegistry.TabName(tab));
            if (Ui.TakeBack()) { Ui.PlayBack(); SetOpen(false); return; }
            int t = Ui.TabNav();
            if (t != 0) { SetTab(tab + t); return; }
            if (rows.Count == 0) return;
            row = Mathf.Clamp(row, 0, rows.Count - 1);
            Vector2Int nav = Ui.Nav();
            if (nav.y != 0) { row = (row + nav.y + rows.Count) % rows.Count; Ui.PlayTick(); }
            SettingsRegistry.Row r = rows[row];
            if (!RowEnabled(r)) return;
            if (nav.x != 0) Change(r, nav.x);
            else if (Ui.TakeOk()) Press(r);
        }

        void SetTab(int t)
        {
            int n = SettingsRegistry.TabCount();
            tab = (t + n) % n;
            row = 0;
            scroll = 0;
            Ui.PlayTick();
        }

        static bool RowEnabled(SettingsRegistry.Row r) { return r.Enabled == null || r.Enabled(); }

        static void Change(SettingsRegistry.Row r, int dir)
        {
            switch (r.Kind)
            {
                case SettingsRegistry.Kind.Toggle: r.SetBool(!r.GetBool()); Ui.PlayTick(); break;
                case SettingsRegistry.Kind.Slider:
                    float v = Mathf.Clamp(r.GetFloat() + dir * r.Step, r.Min, r.Max);
                    r.SetFloat(Mathf.Round((v - r.Min) / r.Step) * r.Step + r.Min);
                    Ui.PlayTick();
                    break;
                case SettingsRegistry.Kind.Choice:
                    int n = r.Options.Length;
                    r.SetInt((r.GetInt() + dir + n) % n);
                    Ui.PlayTick();
                    break;
            }
        }

        static void Press(SettingsRegistry.Row r)
        {
            switch (r.Kind)
            {
                case SettingsRegistry.Kind.Toggle: r.SetBool(!r.GetBool()); Ui.PlayChoose(); break;
                case SettingsRegistry.Kind.Choice: Change(r, 1); break;
                case SettingsRegistry.Kind.Button: Ui.PlayChoose(); r.Press(); break;
            }
        }

        /// <summary>Opens or closes the window (used by the main menu and the pause menu).</summary>
        public static void Show(bool open)
        {
            SettingsPanel panel = FindAnyObjectByType<SettingsPanel>();
            if (panel != null) panel.SetOpen(open);
        }

        void SetOpen(bool open)
        {
            if (open == WindowOpen) return;
            if (open && CinematicDirector.Playing) return;
            WindowOpen = open;
            if (open) { openedAt = Time.unscaledTime; Ui.PlayChoose(); }
            else { GameSettings.Save(); GameSettings.ApplyDisplay(); }
            if (!Net.InMenu) GamePause.Set("settings", open);
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        void OnDisable()
        {
            if (WindowOpen) SetOpen(false);
        }

        // ------------------------------------------------------------------ drawing

        void OnGUI()
        {
            if (!WindowOpen) return;
            GUI.depth = -200;   // over the main menu and the pause menu
            Ui.ApplyGameFont();
            float s = Ui.MenuScale;
            Event e = Event.current;
            float fade = Mathf.Clamp01((Time.unscaledTime - openedAt) / 0.15f);
            if (!PauseMenu.Open && !Net.InMenu) Ui.Backdrop(0.85f * fade);
            if (Net.InMenu) Ui.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.45f * fade));

            float w = Mathf.Min(1040f * s, Screen.width - 40f), h = Mathf.Min(760f * s, Screen.height - 40f);
            Rect win = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            // Opaque, so the main menu or the pause menu behind it does not show through.
            Ui.Fill(win, Ui.A(Ui.Night, fade));
            Ui.Panel(win, fade, s);
            float pad = 40f * s;
            float x = win.x + pad, iw = w - 2f * pad;

            Ui.Text(new Rect(x, win.y + 22f * s, iw, 60f * s), "SETTINGS", UiFont.Display, 48f * s, Ui.A(Ui.Paper, fade), TextAnchor.MiddleLeft);
            // Close box top right.
            Rect close = new Rect(win.xMax - 64f * s, win.y + 26f * s, 40f * s, 40f * s);
            bool overClose = close.Contains(e.mousePosition);
            Ui.Text(close, "×", UiFont.Body, 28f * s, Ui.A(overClose ? Ui.Brass : Ui.Dim, fade), TextAnchor.MiddleCenter);
            if (e.type == EventType.MouseDown && e.button == 0 && overClose) { e.Use(); Ui.PlayBack(); SetOpen(false); return; }

            // Tabs
            float ty = win.y + 96f * s, th = 44f * s;
            int n = SettingsRegistry.TabCount();
            float tx = x;
            float capH = 26f * s;
            tx += Ui.KeyCap(tx, ty + (th - capH) * 0.5f, capH, Ui.Prompt(KeyLabel.Of(Key.Q), "LB"), 0.7f * fade) + 18f * s;
            for (int i = 0; i < n; i++)
            {
                string name = SettingsRegistry.TabName(i);
                float tw = Ui.Width(name, UiFont.Body, 22f * s, true) + 36f * s;
                Rect tr = new Rect(tx, ty, tw, th);
                bool on = i == tab;
                if (e.type == EventType.MouseDown && e.button == 0 && tr.Contains(e.mousePosition)) { e.Use(); if (!on) SetTab(i); }
                if (on) Ui.Fill(tr, Ui.A(Ui.Paper, 0.08f * fade));
                if (on) Ui.Fill(new Rect(tr.x, tr.yMax - 3f * s, tr.width, 3f * s), Ui.A(Ui.Brass, fade));
                Ui.Text(tr, name, UiFont.Body, 22f * s, Ui.A(on ? Ui.Paper : Ui.Dim, fade), TextAnchor.MiddleCenter, false, false, true);
                tx += tw + 6f * s;
            }
            Ui.KeyCap(tx + 12f * s, ty + (th - capH) * 0.5f, capH, Ui.Prompt(KeyLabel.Of(Key.E), "RB"), 0.7f * fade);
            Ui.Fill(new Rect(x, ty + th, iw, Mathf.Max(1f, s)), Ui.A(Ui.Paper, 0.18f * fade));

            // Rows
            rows = SettingsRegistry.RowsOf(SettingsRegistry.TabName(tab));
            float top = ty + th + 18f * s, rh = 56f * s;
            float helpH = 70f * s, footH = 50f * s;
            int visible = Mathf.Max(1, Mathf.FloorToInt((win.yMax - top - helpH - footH) / rh));
            if (rows.Count > 0) row = Mathf.Clamp(row, 0, rows.Count - 1);
            if (row < scroll) scroll = row;
            if (row >= scroll + visible) scroll = row - visible + 1;
            scroll = Mathf.Clamp(scroll, 0, Mathf.Max(0, rows.Count - visible));
            float valueW = Mathf.Min(380f * s, iw * 0.45f);
            for (int vi = 0; vi < visible && scroll + vi < rows.Count; vi++)
            {
                int i = scroll + vi;
                SettingsRegistry.Row r = rows[i];
                Rect rr = new Rect(x - 12f * s, top + vi * rh, iw + 24f * s, rh - 6f * s);
                bool enabled = RowEnabled(r);
                if (e.type == EventType.Repaint && rr.Contains(e.mousePosition) && (e.mousePosition - lastMouse).sqrMagnitude > 1f && row != i && dragging < 0) { row = i; Ui.PlayTick(); }
                bool on = row == i;
                if (on)
                {
                    Ui.Fill(rr, Ui.A(Ui.Paper, 0.07f * fade));
                    Ui.Fill(new Rect(rr.x, rr.y + 8f * s, 4f * s, rr.height - 16f * s), Ui.A(Ui.Brass, fade));
                }
                float a = (enabled ? 1f : 0.4f) * fade;
                Ui.Text(new Rect(x + 6f * s, rr.y, iw - valueW - 20f * s, rr.height), r.Label, UiFont.Body, 23f * s, Ui.A(on ? Ui.Paper : new Color(0.93f, 0.89f, 0.79f, 0.8f), a), TextAnchor.MiddleLeft);
                Rect vr = new Rect(x + iw - valueW, rr.y + 6f * s, valueW, rr.height - 12f * s);
                DrawValue(r, i, vr, on, a, s, e, enabled);
            }
            if (rows.Count > visible)
            {
                float barH = visible * rh, knob = barH * visible / rows.Count;
                float kx = win.xMax - 16f * s;
                Ui.Fill(new Rect(kx, top, 3f * s, barH), Ui.A(Ui.Paper, 0.12f * fade));
                Ui.Fill(new Rect(kx, top + (barH - knob) * scroll / Mathf.Max(1, rows.Count - visible), 3f * s, knob), Ui.A(Ui.Brass, 0.8f * fade));
                if (e.type == EventType.ScrollWheel && win.Contains(e.mousePosition))
                {
                    row = Mathf.Clamp(row + (e.delta.y > 0f ? 1 : -1), 0, rows.Count - 1);
                    e.Use();
                }
            }
            if (rows.Count == 0)
                Ui.Text(new Rect(x, top, iw, rh), "Nothing to set here yet.", UiFont.Body, 21f * s, Ui.A(Ui.Dim, fade), TextAnchor.MiddleLeft);
            if (e.type == EventType.MouseUp) dragging = -1;
            if (e.type == EventType.Repaint) lastMouse = e.mousePosition;

            // Help for the focused row
            float hy = win.yMax - footH - helpH;
            Ui.Fill(new Rect(x, hy, iw, Mathf.Max(1f, s)), Ui.A(Ui.Paper, 0.18f * fade));
            if (rows.Count > 0 && !string.IsNullOrEmpty(rows[row].Help))
                Ui.Text(new Rect(x, hy + 12f * s, iw, helpH - 16f * s), rows[row].Help, UiFont.Body, 19f * s, Ui.A(Ui.Dim, fade), TextAnchor.UpperLeft, false, true);

            Ui.Hints(x, win.yMax - footH + 6f * s, 28f * s, new string[]
            {
                Ui.Prompt("↑ ↓", "D-PAD"), "select", Ui.Prompt("← →", "‹ ›"), "change", Ui.Prompt("ENTER", "A"), "toggle",
                Ui.Prompt("ESC", "B"), "close", "F10", "in the field",
            }, 0.8f * fade);
        }

        void DrawValue(SettingsRegistry.Row r, int i, Rect vr, bool on, float a, float s, Event e, bool enabled)
        {
            bool click = enabled && e.type == EventType.MouseDown && e.button == 0 && vr.Contains(e.mousePosition);
            switch (r.Kind)
            {
                case SettingsRegistry.Kind.Toggle:
                {
                    bool v = r.GetBool();
                    float bw = vr.width * 0.5f - 4f * s;
                    Rect offR = new Rect(vr.x, vr.y, bw, vr.height), onR = new Rect(vr.x + bw + 8f * s, vr.y, bw, vr.height);
                    Ui.Fill(offR, !v ? Ui.A(Ui.Paper, 0.85f * a) : Ui.A(Ui.Paper, 0.08f * a));
                    Ui.Fill(onR, v ? Ui.A(Ui.Brass, 0.95f * a) : Ui.A(Ui.Paper, 0.08f * a));
                    Ui.Text(offR, "OFF", UiFont.Body, 19f * s, !v ? Ui.A(Ui.Ink, a) : Ui.A(Ui.Dim, a), TextAnchor.MiddleCenter, false, false, true);
                    Ui.Text(onR, "ON", UiFont.Body, 19f * s, v ? Ui.A(Ui.Ink, a) : Ui.A(Ui.Dim, a), TextAnchor.MiddleCenter, false, false, true);
                    if (click) { e.Use(); row = i; bool want = onR.Contains(e.mousePosition); if (want != v) { r.SetBool(want); Ui.PlayTick(); } }
                    break;
                }
                case SettingsRegistry.Kind.Slider:
                {
                    float v = r.GetFloat();
                    string txt = r.Format(v);
                    float textW = 90f * s;
                    Rect track = new Rect(vr.x, vr.center.y - 4f * s, vr.width - textW - 12f * s, 8f * s);
                    float f = Mathf.InverseLerp(r.Min, r.Max, v);
                    Ui.Fill(track, Ui.A(Ui.Paper, 0.14f * a));
                    Ui.Fill(new Rect(track.x, track.y, track.width * f, track.height), Ui.A(on ? Ui.Brass : new Color(0.75f, 0.62f, 0.38f), a));
                    float kx = track.x + track.width * f;
                    Ui.Fill(new Rect(kx - 5f * s, track.center.y - 13f * s, 10f * s, 26f * s), Ui.A(Ui.Paper, a));
                    Ui.Text(new Rect(track.xMax + 12f * s, vr.y, textW, vr.height), txt, UiFont.Body, 21f * s, Ui.A(Ui.Paper, a), TextAnchor.MiddleRight, false, false, true);
                    Rect hit = new Rect(track.x - 8f * s, vr.y, track.width + 16f * s, vr.height);
                    if (enabled && e.type == EventType.MouseDown && e.button == 0 && hit.Contains(e.mousePosition)) { dragging = i; row = i; e.Use(); }
                    if (dragging == i && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
                    {
                        float nv = Mathf.Lerp(r.Min, r.Max, Mathf.Clamp01((e.mousePosition.x - track.x) / track.width));
                        r.SetFloat(Mathf.Clamp(Mathf.Round((nv - r.Min) / r.Step) * r.Step + r.Min, r.Min, r.Max));
                        e.Use();
                    }
                    break;
                }
                case SettingsRegistry.Kind.Choice:
                {
                    int v = Mathf.Clamp(r.GetInt(), 0, r.Options.Length - 1);
                    float aw = 40f * s;
                    Rect lr = new Rect(vr.x, vr.y, aw, vr.height), rr = new Rect(vr.xMax - aw, vr.y, aw, vr.height);
                    Ui.Fill(vr, Ui.A(Ui.Paper, 0.08f * a));
                    Ui.Text(lr, "‹", UiFont.Body, 30f * s, Ui.A(on ? Ui.Brass : Ui.Dim, a), TextAnchor.MiddleCenter, false, false, true);
                    Ui.Text(rr, "›", UiFont.Body, 30f * s, Ui.A(on ? Ui.Brass : Ui.Dim, a), TextAnchor.MiddleCenter, false, false, true);
                    Ui.Text(vr, r.Options[v], UiFont.Body, 20f * s, Ui.A(Ui.Paper, a), TextAnchor.MiddleCenter, false, false, true);
                    if (click) { e.Use(); row = i; Change(r, lr.Contains(e.mousePosition) ? -1 : 1); }
                    break;
                }
                case SettingsRegistry.Kind.Button:
                {
                    Ui.Fill(vr, on ? Ui.A(Ui.Brass, 0.95f * a) : Ui.A(Ui.Paper, 0.10f * a));
                    Ui.Text(vr, "OPEN", UiFont.Body, 19f * s, on ? Ui.A(Ui.Ink, a) : Ui.A(Ui.Paper, a), TextAnchor.MiddleCenter, false, false, true);
                    if (click) { e.Use(); row = i; Press(r); }
                    break;
                }
            }
        }
    }
}
