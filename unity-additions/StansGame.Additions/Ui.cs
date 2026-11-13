using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame.Additions
{
    /// <summary>The house fonts (see <see cref="Ui"/>).</summary>
    public enum UiFont
    {
        /// <summary>Stencil, for big titles and banners only.</summary>
        Display,
        /// <summary>The main text face (Gill Sans, else Bahnschrift), for menus and the HUD.</summary>
        Body,
        /// <summary>Typewriter (Courier New), for orders, briefings and other paperwork.</summary>
        Type,
    }

    /// <summary>
    /// The shared look of every additions screen and HUD element: a 1944 field look (stencil titles,
    /// typed orders on paper, khaki panels, brass rules) that stays readable at night.
    /// <para>Everything is drawn with IMGUI in <c>OnGUI</c>. Use <see cref="MenuScale"/> for full-screen
    /// menus and <see cref="HudScale"/> for HUD elements (it includes the player's HUD size setting), and
    /// draw through the helpers here so fonts, colours and spacing stay the same everywhere.</para>
    /// <para>Fonts are taken from the operating system (no font files are shipped). An OFL or other
    /// freely licensed font can be dropped next to the game as <c>Fonts/Display.ttf</c>,
    /// <c>Fonts/Body.ttf</c> or <c>Fonts/Type.ttf</c> and is used instead; list it in
    /// <see cref="Credits"/> and in the README.</para>
    /// </summary>
    public static class Ui
    {
        // ------------------------------------------------------------------ palette

        /// <summary>Paper white, the main text colour on dark panels.</summary>
        public static readonly Color Paper = new Color(0.93f, 0.89f, 0.79f);
        /// <summary>Paper for card backgrounds (typed orders, briefings).</summary>
        public static readonly Color Card = new Color(0.86f, 0.81f, 0.69f);
        /// <summary>Ink, text on paper.</summary>
        public static readonly Color Ink = new Color(0.13f, 0.12f, 0.09f);
        /// <summary>Brass, the accent: focus, rules, labels.</summary>
        public static readonly Color Brass = new Color(0.88f, 0.72f, 0.42f);
        /// <summary>Khaki, the panel colour.</summary>
        public static readonly Color Khaki = new Color(0.16f, 0.17f, 0.12f);
        /// <summary>Near-black olive for HUD panels.</summary>
        public static readonly Color Night = new Color(0.06f, 0.065f, 0.05f);
        public static readonly Color Red = new Color(0.82f, 0.24f, 0.17f);
        public static readonly Color Amber = new Color(0.95f, 0.70f, 0.26f);
        public static readonly Color Green = new Color(0.55f, 0.74f, 0.38f);
        /// <summary>Muted text on dark panels.</summary>
        public static readonly Color Dim = new Color(0.93f, 0.89f, 0.79f, 0.62f);

        /// <summary>The colour with another alpha.</summary>
        public static Color A(Color c, float a) { return new Color(c.r, c.g, c.b, c.a * a); }

        // ------------------------------------------------------------------ scale

        /// <summary>Scale for full-screen menus: 1 at 1080 lines.</summary>
        public static float MenuScale { get { return Mathf.Max(0.6f, Screen.height / 1080f); } }

        /// <summary>
        /// Smallest HUD scale. The smallest HUD text is the key cap label (11.6 px at scale 1) and the
        /// "LMB throw" tag (13 px); 0.95 keeps both at 11 px or more. One number scales panels and text,
        /// so the layout keeps its proportions.
        /// </summary>
        public const float MinHudScale = 0.95f;

        /// <summary>Width of the bottom HUD row (health panel plus kit bar, with margins) at scale 1, in pixels.</summary>
        const float HudRowWidth = 1110f;

        /// <summary>
        /// Scale for HUD elements: resolution (at least <see cref="MinHudScale"/>) times the player's HUD
        /// size setting, never below <see cref="MinHudScale"/>, and never wider than the screen. At
        /// 1280x720: 0.95 for sizes up to 100 %, 1.16 at 125 % and 150 %. At 1920x1080: 0.95, 1.0, 1.5.
        /// </summary>
        public static float HudScale
        {
            get
            {
                float s = Mathf.Max(MinHudScale, Screen.height / 1080f) * GameSettings.HudScale;
                s = Mathf.Max(MinHudScale, s);
                return Mathf.Min(s, Screen.width / HudRowWidth);
            }
        }

        /// <summary>Distance of HUD elements from the screen edges, in pixels.</summary>
        public static float HudMargin { get { return Mathf.Round(16f * HudScale); } }

        // ------------------------------------------------------------------ fonts

        static readonly string[] DisplayNames = { "Stencil", "Bahnschrift SemiBold Condensed", "Bahnschrift", "Impact", "Arial" };
        static readonly string[] BodyNames = { "Gill Sans MT", "Bahnschrift", "Segoe UI", "Arial" };
        static readonly string[] TypeNames = { "Courier New", "Consolas", "Lucida Console" };

        static Font[] fonts;
        static string[] fontNames;
        static GUIStyle[] styles;
        static Texture2D white, paper, vignette, gradient;
        static bool skinDone;

        static void EnsureFonts()
        {
            if (fonts != null) return;
            fonts = new Font[3];
            fontNames = new string[3];
            Load(UiFont.Display, "Display.ttf", DisplayNames);
            Load(UiFont.Body, "Body.ttf", BodyNames);
            Load(UiFont.Type, "Type.ttf", TypeNames);
            Debug.Log("Stan's Game: UI fonts " + fontNames[0] + " / " + fontNames[1] + " / " + fontNames[2]);
        }

        static void Load(UiFont which, string file, string[] names)
        {
            int i = (int)which;
            try
            {
                string path = Path.Combine(Path.Combine(Path.GetDirectoryName(Application.dataPath), "Fonts"), file);
                if (File.Exists(path))
                {
                    Font f = new Font(path);
                    if (f != null) { fonts[i] = f; fontNames[i] = "Fonts/" + file; return; }
                }
            }
            catch (System.Exception e) { Debug.LogWarning("Stan's Game: font file " + file + " not loaded: " + e.Message); }

            HashSet<string> installed = new HashSet<string>(Font.GetOSInstalledFontNames());
            foreach (string n in names)
                if (installed.Contains(n))
                {
                    fonts[i] = Font.CreateDynamicFontFromOSFont(n, 16);
                    fontNames[i] = n;
                    return;
                }
            fontNames[i] = "built-in";   // null font: the default IMGUI font
        }

        /// <summary>Name of the font in use for a role (for the credits and the log).</summary>
        public static string FontName(UiFont f) { EnsureFonts(); return fontNames[(int)f]; }

        /// <summary>The font in use for a role, or null for the built-in one.</summary>
        public static Font FontOf(UiFont f) { EnsureFonts(); return fonts[(int)f]; }

        /// <summary>Fonts and textures used by the additions' UI, with their source and licence.</summary>
        public static string[] Credits
        {
            get
            {
                return new string[]
                {
                    "Titles: " + Describe(UiFont.Display),
                    "Text: " + Describe(UiFont.Body),
                    "Orders: " + Describe(UiFont.Type),
                    "Paper and panel textures: generated by the game, no image files",
                    "Menu and objective sounds: generated by the game, no sound files",
                };
            }
        }

        static string Describe(UiFont f)
        {
            string n = FontName(f);
            if (n.StartsWith("Fonts/")) return n + " (file next to the game, see Fonts/LICENSE)";
            if (n == "built-in") return "Unity built-in font";
            return n + " (installed with Windows or Office on this PC, not shipped with the game)";
        }

        /// <summary>
        /// The game's own IMGUI text (objective lines, weapon bar, subtitles, prompts) takes its font from
        /// the default skin. Pointing that at the body font gives the whole screen one face. Call from OnGUI.
        /// </summary>
        public static void ApplyGameFont()
        {
            if (skinDone) return;
            skinDone = true;
            EnsureFonts();
            GUISkin skin = GUI.skin;   // the default skin while no script has set its own
            if (!gameFontKept) { gameFont = skin.font; gameFontKept = true; }
            skin.font = GameSettings.PeriodFontForGameText && fonts[(int)UiFont.Body] != null ? fonts[(int)UiFont.Body] : gameFont;
        }

        static Font gameFont;
        static bool gameFontKept;

        /// <summary>Puts the game's own font back or applies the body font again (settings change).</summary>
        public static void RefreshGameFont()
        {
            skinDone = false;
        }

        // ------------------------------------------------------------------ textures

        /// <summary>A 1x1 white texture for tinted fills.</summary>
        public static Texture2D White
        {
            get
            {
                if (white == null)
                {
                    white = new Texture2D(1, 1);
                    white.SetPixel(0, 0, Color.white);
                    white.Apply();
                    white.hideFlags = HideFlags.DontSave;
                }
                return white;
            }
        }

        /// <summary>Paper grain (generated): fibres and blotches, tinted when drawn.</summary>
        public static Texture2D PaperTexture
        {
            get
            {
                if (paper != null) return paper;
                const int n = 256;
                paper = new Texture2D(n, n, TextureFormat.RGBA32, false);
                paper.wrapMode = TextureWrapMode.Repeat;
                paper.hideFlags = HideFlags.DontSave;
                System.Random rng = new System.Random(1944);
                float[] v = new float[n * n];
                for (int i = 0; i < v.Length; i++) v[i] = (float)rng.NextDouble();
                Color[] px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        // Coarse blotches (smoothed noise) plus fine grain and horizontal fibres.
                        float coarse = 0f;
                        for (int k = -3; k <= 3; k++) coarse += v[((y + k * 5 + n) % n) * n + (x * 3 + k * 7 + n) % n];
                        coarse /= 7f;
                        float fibre = v[y * n + (x / 6) % n] * 0.5f + v[((y + 1) % n) * n + (x / 9) % n] * 0.5f;
                        float g = 0.90f + 0.06f * (coarse - 0.5f) + 0.05f * (fibre - 0.5f) + 0.04f * (v[y * n + x] - 0.5f);
                        px[y * n + x] = new Color(g, g, g, 1f);
                    }
                paper.SetPixels(px);
                paper.Apply();
                return paper;
            }
        }

        /// <summary>Soft dark edges for menu backgrounds (generated).</summary>
        public static Texture2D Vignette
        {
            get
            {
                if (vignette != null) return vignette;
                const int n = 128;
                vignette = new Texture2D(n, n, TextureFormat.RGBA32, false);
                vignette.wrapMode = TextureWrapMode.Clamp;
                vignette.hideFlags = HideFlags.DontSave;
                float h = (n - 1) * 0.5f;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x - h) / h, dy = (y - h) / h;
                        float r = Mathf.Sqrt(dx * dx * 0.8f + dy * dy);
                        vignette.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1.35f, r))));
                    }
                vignette.Apply();
                return vignette;
            }
        }

        /// <summary>Left-to-right fade from opaque to clear (menu backdrops).</summary>
        public static Texture2D Gradient
        {
            get
            {
                if (gradient != null) return gradient;
                gradient = new Texture2D(256, 1, TextureFormat.RGBA32, false);
                gradient.wrapMode = TextureWrapMode.Clamp;
                gradient.hideFlags = HideFlags.DontSave;
                for (int x = 0; x < 256; x++)
                    gradient.SetPixel(x, 0, new Color(1f, 1f, 1f, Mathf.Lerp(0.92f, 0.30f, Mathf.SmoothStep(0f, 1f, x / 255f))));
                gradient.Apply();
                return gradient;
            }
        }

        // ------------------------------------------------------------------ drawing

        static bool Repaint { get { return Event.current != null && Event.current.type == EventType.Repaint; } }

        /// <summary>A tinted rectangle.</summary>
        public static void Fill(Rect r, Color c)
        {
            if (!Repaint) return;
            Color keep = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White);
            GUI.color = keep;
        }

        /// <summary>A dark khaki panel with a thin brass rule on top (HUD and menu boxes).</summary>
        public static void Panel(Rect r, float alpha, float s, bool rule = true)
        {
            Fill(r, A(Night, 0.80f * alpha));
            if (rule) Fill(new Rect(r.x, r.y, r.width, Mathf.Max(1f, 2f * s)), A(Brass, 0.85f * alpha));
        }

        /// <summary>A sheet of paper with grain and a darker edge (orders, briefing cards).</summary>
        public static void PaperCard(Rect r, float alpha, float s)
        {
            if (!Repaint) return;
            Color keep = GUI.color;
            GUI.color = A(new Color(0f, 0f, 0f, 0.5f), alpha);
            GUI.DrawTexture(new Rect(r.x + 4f * s, r.y + 5f * s, r.width, r.height), White);   // drop shadow
            GUI.color = A(Card, alpha);
            float tile = 256f * s;
            GUI.DrawTextureWithTexCoords(r, PaperTexture, new Rect(r.x / tile, r.y / tile, r.width / tile, r.height / tile));
            GUI.color = A(new Color(0.45f, 0.38f, 0.25f, 0.35f), alpha);
            float e = Mathf.Max(1f, 1.5f * s);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, e), White);
            GUI.DrawTexture(new Rect(r.x, r.yMax - e, r.width, e), White);
            GUI.DrawTexture(new Rect(r.x, r.y, e, r.height), White);
            GUI.DrawTexture(new Rect(r.xMax - e, r.y, e, r.height), White);
            GUI.color = keep;
        }

        /// <summary>A short brass rule (under headings).</summary>
        public static void Rule(float x, float y, float w, float s, float alpha)
        {
            Fill(new Rect(x, y, w, Mathf.Max(1f, 3f * s)), A(Brass, alpha));
        }

        /// <summary>Full-screen backdrop for menu pages over the level: dark, faded to the right, soft edges.</summary>
        public static void Backdrop(float alpha)
        {
            if (!Repaint) return;
            Color keep = GUI.color;
            Rect all = new Rect(0f, 0f, Screen.width, Screen.height);
            GUI.color = A(new Color(0.03f, 0.035f, 0.025f, 1f), alpha);
            GUI.DrawTexture(all, Gradient);
            GUI.color = A(new Color(0f, 0f, 0f, 0.85f), alpha);
            GUI.DrawTexture(all, Vignette);
            GUI.color = keep;
        }

        /// <summary>The shared style for a font role, set up for one draw.</summary>
        public static GUIStyle Style(UiFont f, float size, TextAnchor anchor, bool wrap = false, bool bold = false)
        {
            EnsureFonts();
            if (styles == null)
            {
                styles = new GUIStyle[3];
                for (int i = 0; i < 3; i++)
                {
                    GUIStyle st = new GUIStyle(GUI.skin.label);
                    st.font = fonts[i];
                    st.richText = false;
                    st.padding = new RectOffset(0, 0, 0, 0);
                    st.margin = new RectOffset(0, 0, 0, 0);
                    st.clipping = TextClipping.Overflow;
                    st.normal.textColor = Color.white;
                    styles[i] = st;
                }
            }
            GUIStyle s = styles[(int)f];
            s.fontSize = Mathf.Max(8, Mathf.RoundToInt(size));
            s.alignment = anchor;
            s.wordWrap = wrap;
            s.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            return s;
        }

        /// <summary>Text in a font role; <paramref name="shadow"/> adds a dark drop shadow for text over the world.</summary>
        public static void Text(Rect r, string text, UiFont f, float size, Color c, TextAnchor anchor, bool shadow = false, bool wrap = false, bool bold = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            GUIStyle st = Style(f, size, anchor, wrap, bold);
            Color keep = GUI.color;
            if (shadow)
            {
                float o = Mathf.Max(1f, size / 14f);
                GUI.color = new Color(0f, 0f, 0f, 0.8f * c.a);
                GUI.Label(new Rect(r.x + o, r.y + o, r.width, r.height), text, st);
            }
            GUI.color = c;
            GUI.Label(r, text, st);
            GUI.color = keep;
        }

        /// <summary>Height of wrapped text in a width.</summary>
        public static float Height(string text, UiFont f, float size, float width, bool bold = false)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            return Style(f, size, TextAnchor.UpperLeft, true, bold).CalcHeight(new GUIContent(text), width);
        }

        /// <summary>Width of one line of text.</summary>
        public static float Width(string text, UiFont f, float size, bool bold = false)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            return Style(f, size, TextAnchor.MiddleLeft, false, bold).CalcSize(new GUIContent(text)).x;
        }

        /// <summary>A key cap with the key's label (for hints and the controls page). Returns its width.</summary>
        public static float KeyCap(float x, float y, float h, string label, float alpha, bool onPaper = false)
        {
            float size = h * 0.58f;
            float w = Mathf.Max(h, Width(label, UiFont.Body, size, true) + h * 0.6f);
            Rect r = new Rect(x, y, w, h);
            Color face = onPaper ? A(Ink, 0.85f * alpha) : A(Paper, 0.14f * alpha);
            Fill(r, face);
            float e = Mathf.Max(1f, h / 16f);
            Color edge = onPaper ? A(Ink, alpha) : A(Paper, 0.55f * alpha);
            Fill(new Rect(r.x, r.yMax - e, r.width, e), edge);
            Fill(new Rect(r.x, r.y, r.width, e), A(edge, 0.5f));
            Fill(new Rect(r.x, r.y, e, r.height), A(edge, 0.5f));
            Fill(new Rect(r.xMax - e, r.y, e, r.height), A(edge, 0.5f));
            Text(r, label, UiFont.Body, size, onPaper ? A(Card, alpha) : A(Paper, alpha), TextAnchor.MiddleCenter, false, false, true);
            return w;
        }

        /// <summary>
        /// A row of "key action" pairs, for example { "Z", "prone", "V", "view" }, with key caps.
        /// Returns the width used.
        /// </summary>
        public static float Hints(float x, float y, float h, string[] pairs, float alpha, TextAnchor side = TextAnchor.MiddleLeft)
        {
            float gap = h * 0.35f, size = h * 0.62f;
            float total = 0f;
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                total += Mathf.Max(h, Width(pairs[i], UiFont.Body, h * 0.58f, true) + h * 0.6f) + gap * 0.6f + Width(pairs[i + 1], UiFont.Body, size);
                if (i + 2 < pairs.Length) total += gap * 2.2f;
            }
            float cx = side == TextAnchor.MiddleRight ? x - total : side == TextAnchor.MiddleCenter ? x - total * 0.5f : x;
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                cx += KeyCap(cx, y, h, pairs[i], alpha) + gap * 0.6f;
                float tw = Width(pairs[i + 1], UiFont.Body, size);
                Text(new Rect(cx, y, tw + 4f, h), pairs[i + 1], UiFont.Body, size, A(Dim, alpha), TextAnchor.MiddleLeft, true);
                cx += tw + gap * 2.2f;
            }
            return total;
        }

        // ------------------------------------------------------------------ input

        static int backFrame = -1, okFrame = -1;

        /// <summary>Esc or gamepad B (or Start, when <paramref name="start"/>) this frame, once: the first caller takes it.</summary>
        public static bool TakeBack(bool start = false)
        {
            if (backFrame == Time.frameCount) return false;
            Keyboard k = Keyboard.current;
            Gamepad g = Gamepad.current;
            bool pressed = (k != null && k.escapeKey.wasPressedThisFrame)
                        || (g != null && (g.buttonEast.wasPressedThisFrame || (start && g.startButton.wasPressedThisFrame)));
            if (!pressed) return false;
            backFrame = Time.frameCount;
            return true;
        }

        /// <summary>Enter, Space or gamepad A this frame, once.</summary>
        public static bool TakeOk()
        {
            if (okFrame == Time.frameCount) return false;
            Keyboard k = Keyboard.current;
            Gamepad g = Gamepad.current;
            bool pressed = (k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame))
                        || (g != null && g.buttonSouth.wasPressedThisFrame);
            if (!pressed) return false;
            okFrame = Time.frameCount;
            return true;
        }

        /// <summary>Menu direction this frame: arrows, the move keys (by position), d-pad or left stick.</summary>
        public static Vector2Int Nav()
        {
            Keyboard k = Keyboard.current;
            Gamepad g = Gamepad.current;
            int x = 0, y = 0;
            if (k != null)
            {
                if (k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame) y--;
                if (k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame) y++;
                if (k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame) x--;
                if (k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame) x++;
            }
            if (g != null)
            {
                if (g.dpad.up.wasPressedThisFrame || g.leftStick.up.wasPressedThisFrame) y--;
                if (g.dpad.down.wasPressedThisFrame || g.leftStick.down.wasPressedThisFrame) y++;
                if (g.dpad.left.wasPressedThisFrame || g.leftStick.left.wasPressedThisFrame) x--;
                if (g.dpad.right.wasPressedThisFrame || g.leftStick.right.wasPressedThisFrame) x++;
            }
            return new Vector2Int(Mathf.Clamp(x, -1, 1), Mathf.Clamp(y, -1, 1));
        }

        /// <summary>Tab change this frame: Q/E (by position), Page Up/Down, or the gamepad shoulder buttons.</summary>
        public static int TabNav()
        {
            Keyboard k = Keyboard.current;
            Gamepad g = Gamepad.current;
            int d = 0;
            if (k != null)
            {
                if (k.qKey.wasPressedThisFrame || k.pageUpKey.wasPressedThisFrame) d--;
                if (k.eKey.wasPressedThisFrame || k.pageDownKey.wasPressedThisFrame || k.tabKey.wasPressedThisFrame) d++;
            }
            if (g != null)
            {
                if (g.leftShoulder.wasPressedThisFrame) d--;
                if (g.rightShoulder.wasPressedThisFrame) d++;
            }
            return Mathf.Clamp(d, -1, 1);
        }

        static bool padSeen;

        /// <summary>Call once per frame from one place (the settings panel does it) to track the last device.</summary>
        public static void TrackDevice()
        {
            Gamepad g = Gamepad.current;
            if (g != null && (g.buttonSouth.wasPressedThisFrame || g.buttonEast.wasPressedThisFrame || g.dpad.ReadValue().sqrMagnitude > 0.2f || g.leftStick.ReadValue().sqrMagnitude > 0.3f || g.startButton.wasPressedThisFrame))
                padSeen = true;
            Keyboard k = Keyboard.current;
            Mouse m = Mouse.current;
            if ((k != null && k.anyKey.wasPressedThisFrame) || (m != null && (m.leftButton.wasPressedThisFrame || m.delta.ReadValue().sqrMagnitude > 4f)))
                padSeen = false;
        }

        /// <summary>The prompt for a menu action on the device in use, for example "Esc" or "B".</summary>
        public static string Prompt(string keyboard, string pad)
        {
            return padSeen && Gamepad.current != null ? pad : keyboard;
        }

        // ------------------------------------------------------------------ sound

        static AudioSource sfx;
        static AudioClip tick, choose, back;

        /// <summary>The menu sounds (generated): a tick on focus, a click on choose, a lower one on back.</summary>
        public static void PlayTick() { Play(ref tick, "ui tick", 1400f, 1400f, 0.035f); }
        public static void PlayChoose() { Play(ref choose, "ui choose", 660f, 990f, 0.12f); }
        public static void PlayBack() { Play(ref back, "ui back", 700f, 440f, 0.10f); }

        static void Play(ref AudioClip clip, string name, float f0, float f1, float seconds)
        {
            if (sfx == null)
            {
                GameObject go = new GameObject("StansGame.UiSound");
                Object.DontDestroyOnLoad(go);
                sfx = go.AddComponent<AudioSource>();
                sfx.playOnAwake = false;
                sfx.spatialBlend = 0f;
                sfx.volume = 0.35f;
                sfx.ignoreListenerPause = true;   // menu sounds play while the game is paused
            }
            if (clip == null) clip = Tone(name, f0, f1, seconds);
            sfx.PlayOneShot(clip);
        }

        static AudioClip Tone(string name, float f0, float f1, float seconds)
        {
            int rate = 44100, n = Mathf.CeilToInt(rate * seconds);
            float[] d = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                phase += 2f * Mathf.PI * Mathf.Lerp(f0, f1, t) / rate;
                d[i] = Mathf.Sin(phase) * Mathf.Exp(-5f * t) * Mathf.Min(1f, i / 60f) * 0.6f;
            }
            AudioClip c = AudioClip.Create(name, n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }
    }
}
