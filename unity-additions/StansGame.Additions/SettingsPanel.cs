using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame.Additions
{
    /// <summary>
    /// F10 opens a small settings window: view, field of view, aim field of view, mouse sensitivity,
    /// hit effects, heartbeat and volume. While it is open the local player's controller is paused
    /// (no shooting or turning when you click a slider) and the cursor is free. Co-op keeps running.
    /// The main menu opens it too (<see cref="Show"/>); there Esc also closes it.
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        /// <summary>True while the window is open.</summary>
        public static bool Open { get; private set; }

        /// <summary>The player whose controller the window paused, or null.</summary>
        public static SoldierController PausedPlayer { get; private set; }

        Rect window;
        static GUIStyle title;

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            // F10 only: Esc already opens the game's own menu (NetSession).
            if (kb[Key.F10].wasPressedThisFrame) SetOpen(!Open);
            else if (Open && Net.InMenu && kb.escapeKey.wasPressedThisFrame) SetOpen(false);

            // The player died or respawned while the window was open: let go of the old one.
            if (Open && PausedPlayer != null && AdditionsBootstrap.LocalPlayer() != PausedPlayer) Release();
        }

        /// <summary>Opens or closes the window (used by the main menu).</summary>
        public static void Show(bool open)
        {
            SettingsPanel panel = FindAnyObjectByType<SettingsPanel>();
            if (panel != null) panel.SetOpen(open);
        }

        void SetOpen(bool open)
        {
            if (open == Open) return;
            if (open && CinematicDirector.Playing) return;
            Open = open;

            if (open)
            {
                SoldierController p = AdditionsBootstrap.LocalPlayer();
                if (p != null && p.enabled)
                {
                    p.enabled = false;
                    PausedPlayer = p;
                }
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                GameSettings.Save();
                Release();
                if (!Net.InMenu)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
        }

        void Release()
        {
            if (PausedPlayer != null) PausedPlayer.enabled = true;
            PausedPlayer = null;
        }

        void OnDisable()
        {
            if (Open) SetOpen(false);
        }

        void OnGUI()
        {
            if (!Open) return;
            GUI.depth = -200;   // over the main menu
            float s = Screen.height / 1080f;
            float w = 460f * s, h = 640f * s;
            window = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

            Matrix4x4 keep = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
            Rect r = new Rect(window.x / s, window.y / s, 460f, 640f);
            GUI.Box(r, "");
            GUILayout.BeginArea(new Rect(r.x + 20f, r.y + 16f, r.width - 40f, r.height - 32f));

            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label);
                title.fontSize = 20;
                title.fontStyle = FontStyle.Bold;
            }
            GUILayout.Label("Settings", title);
            GUILayout.Space(8f);

            bool fp = GUILayout.Toggle(GameSettings.FirstPerson, "  First-person view  (" + KeyLabel.Of(Key.V) + " switches in game)");
            if (fp != GameSettings.FirstPerson) GameSettings.FirstPerson = fp;
            GUILayout.Space(6f);

            GameSettings.FieldOfView = Slider("Field of view", GameSettings.FieldOfView, GameSettings.MinFieldOfView, GameSettings.MaxFieldOfView, "0");
            GameSettings.AimFieldOfView = Slider("Field of view when aiming", GameSettings.AimFieldOfView, GameSettings.MinAimFieldOfView, GameSettings.MaxAimFieldOfView, "0");
            GameSettings.MouseSensitivity = Slider("Mouse sensitivity", GameSettings.MouseSensitivity, GameSettings.MinSensitivity, GameSettings.MaxSensitivity, "0.00");
            GameSettings.AimSensitivity = Slider("Aim sensitivity (1.00 = matched to the zoom)", GameSettings.AimSensitivity, GameSettings.MinAimSensitivity, GameSettings.MaxAimSensitivity, "0.00");
            GameSettings.HitEffects = Slider("Hit effects (red flash, flinch)", GameSettings.HitEffects, 0f, 1f, "0%");
            GameSettings.WeaponMotion = Slider("Weapon motion (kick, sway, walk bob)", GameSettings.WeaponMotion, 0f, 1f, "0%");
            GameSettings.MasterVolume = Slider("Volume", GameSettings.MasterVolume, 0f, 1f, "0%");

            bool hb = GUILayout.Toggle(GameSettings.Heartbeat, "  Heartbeat at low health");
            if (hb != GameSettings.Heartbeat) GameSettings.Heartbeat = hb;
            bool el = GUILayout.Toggle(GameSettings.EnhancedLighting, "  Enhanced night lighting");
            if (el != GameSettings.EnhancedLighting) GameSettings.EnhancedLighting = el;
            bool se = GUILayout.Toggle(GameSettings.SmoothEdges, "  Smooth edges (anti-aliasing, softer leaves)");
            if (se != GameSettings.SmoothEdges) GameSettings.SmoothEdges = se;

            GUILayout.FlexibleSpace();
            GUILayout.Label("Keys on this keyboard: " + KeyLabel.Move + " move, " + KeyLabel.Of(Key.C) + " kneel, " + KeyLabel.Of(Key.Z) + " prone.");
            if (GUILayout.Button("Close  (F10)", GUILayout.Height(30f))) SetOpen(false);

            GUILayout.EndArea();
            GUI.matrix = keep;
        }

        static float Slider(string name, float value, float min, float max, string format)
        {
            GUILayout.Label(name + ":  " + value.ToString(format));
            float v = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.Space(10f);
            return v;
        }
    }
}
