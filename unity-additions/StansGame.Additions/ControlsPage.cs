using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame.Additions
{
    /// <summary>
    /// The controls page: every key of the game and the additions, with the label printed on this
    /// keyboard (<see cref="KeyLabel"/>: ZQSD on AZERTY), laid out as a page of the field manual.
    /// Opened from the pause menu, or from Settings &gt; Controls. Esc, Enter, B or a click closes it.
    /// The game itself is played with mouse and keyboard; the menus also take a gamepad.
    /// </summary>
    public sealed class ControlsPage : MonoBehaviour
    {
        public static bool Open { get; private set; }
        static float openedAt;

        /// <summary>Opens or closes the page.</summary>
        public static void Show(bool open)
        {
            if (open == Open) return;
            Open = open;
            if (open) openedAt = Time.unscaledTime;
            if (!Net.InMenu) GamePause.Set("controls", open);
        }

        void Update()
        {
            if (!Open) return;
            if (Ui.TakeBack() || Ui.TakeOk()) { Ui.PlayBack(); Show(false); }
        }

        struct Entry
        {
            public string[] Keys;
            public string Action;
            public Entry(string action, params string[] keys) { Action = action; Keys = keys; }
        }

        static Entry[][] Sections()
        {
            string move = KeyLabel.Move;
            return new Entry[][]
            {
                new Entry[]
                {
                    new Entry("Move", KeyLabel.Of(Key.W), KeyLabel.Of(Key.A), KeyLabel.Of(Key.S), KeyLabel.Of(Key.D)),
                    new Entry("Sprint (hold)", KeyLabel.Of(Key.LeftShift)),
                    new Entry("Jump, climb over a low obstacle", KeyLabel.Of(Key.Space)),
                    new Entry("Kneel", KeyLabel.Of(Key.C)),
                    new Entry("Prone", KeyLabel.Of(Key.Z)),
                    new Entry("Look", "MOUSE"),
                },
                new Entry[]
                {
                    new Entry("Fire, throw a grenade", "LEFT MOUSE"),
                    new Entry("Aim down the sights", "RIGHT MOUSE"),
                    new Entry("Reload", KeyLabel.Of(Key.R)),
                    new Entry("Main weapon, side arm", KeyLabel.Of(Key.Digit1), KeyLabel.Of(Key.Digit2)),
                    new Entry("Grenades, field dressing", KeyLabel.Of(Key.Digit3), KeyLabel.Of(Key.Digit4)),
                    new Entry("Next weapon", KeyLabel.Of(Key.Tab), "WHEEL"),
                },
                new Entry[]
                {
                    new Entry("Call a stretcher-bearer", KeyLabel.Of(Key.T)),
                    new Entry("Call for ammunition", KeyLabel.Of(Key.B)),
                    new Entry("Role: dress wounds or open a bandolier", KeyLabel.Of(Key.G)),
                    new Entry("Pick up, use, get in", KeyLabel.Of(Key.E)),
                    new Entry("Radio: transmit (hold)", KeyLabel.Of(Key.H)),
                },
                new Entry[]
                {
                    new Entry("First / third person", KeyLabel.Of(Key.V)),
                    new Entry("Pause menu", KeyLabel.Of(Key.Escape)),
                    new Entry("Settings", "F10"),
                    new Entry("Skip a film", KeyLabel.Of(Key.Space)),
                },
            };
        }

        static readonly string[] Titles = { "MOVEMENT", "COMBAT", "SECTION AND KIT", "VIEW AND MENUS" };

        void OnGUI()
        {
            if (!Open) return;
            GUI.depth = -250;   // over the settings window
            Ui.ApplyGameFont();
            Event e = Event.current;
            float s = Ui.MenuScale;
            float fade = Mathf.Clamp01((Time.unscaledTime - openedAt) / 0.15f);
            Ui.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.6f * fade));

            float w = Mathf.Min(1500f * s, Screen.width - 40f), h = Mathf.Min(860f * s, Screen.height - 40f);
            Rect card = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            Ui.PaperCard(card, fade, s);
            float pad = 48f * s;
            float x = card.x + pad, y = card.y + 36f * s, iw = w - 2f * pad;
            Ui.Text(new Rect(x, y, iw, 56f * s), "CONTROLS", UiFont.Display, 50f * s, Ui.A(Ui.Ink, fade), TextAnchor.MiddleLeft);
            string layout = KeyLabel.Layout;
            Ui.Text(new Rect(x, y, iw, 56f * s), "Keys as printed on this keyboard" + (layout != null ? "  (" + layout + ")" : ""),
                    UiFont.Type, 18f * s, Ui.A(Ui.Ink, 0.7f * fade), TextAnchor.MiddleRight);
            y += 66f * s;
            Ui.Fill(new Rect(x, y, iw, Mathf.Max(1f, 2f * s)), Ui.A(Ui.Ink, 0.6f * fade));
            y += 24f * s;

            Entry[][] sec = Sections();
            float colGap = 56f * s, colW = (iw - colGap) * 0.5f;
            float capH = 34f * s, rowH = 46f * s;
            float[] colY = { y, y };
            for (int si = 0; si < sec.Length; si++)
            {
                int col = si % 2;
                float cx = x + col * (colW + colGap), cy = colY[col];
                Ui.Text(new Rect(cx, cy, colW, 30f * s), Titles[si], UiFont.Type, 20f * s, Ui.A(new Color(0.42f, 0.14f, 0.08f), fade), TextAnchor.MiddleLeft, false, false, true);
                cy += 38f * s;
                foreach (Entry en in sec[si])
                {
                    float kx = cx;
                    for (int k = 0; k < en.Keys.Length; k++)
                        kx += Ui.KeyCap(kx, cy + (rowH - capH) * 0.5f, capH, en.Keys[k], fade, true) + 6f * s;
                    float ax = Mathf.Max(cx + 230f * s, kx + 12f * s);
                    Ui.Text(new Rect(ax, cy, cx + colW - ax, rowH), en.Action, UiFont.Type, 20f * s, Ui.A(Ui.Ink, fade), TextAnchor.MiddleLeft, false, false, true);
                    cy += rowH;
                }
                colY[col] = cy + 26f * s;
            }

            float fy = card.yMax - 58f * s;
            Ui.Fill(new Rect(x, fy - 10f * s, iw, Mathf.Max(1f, s)), Ui.A(Ui.Ink, 0.4f * fade));
            Ui.Text(new Rect(x, fy, iw, 40f * s),
                "Menus also take a gamepad: d-pad or stick to move, A to choose, B to go back, LB / RB for the settings tabs, Start to pause.",
                UiFont.Type, 17f * s, Ui.A(Ui.Ink, 0.75f * fade), TextAnchor.MiddleLeft, false, true);
            Ui.Text(new Rect(x, fy, iw, 40f * s), Ui.Prompt("ESC", "B") + "  close", UiFont.Body, 19f * s, Ui.A(Ui.Ink, 0.85f * fade), TextAnchor.MiddleRight, false, false, true);

            if (e.type == EventType.MouseDown && e.button == 0) { e.Use(); Ui.PlayBack(); Show(false); }
        }
    }
}
