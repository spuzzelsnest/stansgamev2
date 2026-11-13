using System.Collections.Generic;
using UnityEngine;

namespace StansGame.Additions
{
    /// <summary>
    /// Player settings, saved in PlayerPrefs under "stans.*". Read them anywhere; call Save() after changing.
    /// </summary>
    public static class GameSettings
    {
        const string Prefix = "stans.";

        public const float MinFieldOfView = 60f, MaxFieldOfView = 100f;
        public const float MinAimFieldOfView = 30f, MaxAimFieldOfView = 70f;
        public const float MinSensitivity = 0.02f, MaxSensitivity = 0.40f;

        /// <summary>First-person view (true) or the original over-the-shoulder view (false).</summary>
        public static bool FirstPerson
        {
            get { return PlayerPrefs.GetInt(Prefix + "firstPerson", 1) == 1; }
            set { PlayerPrefs.SetInt(Prefix + "firstPerson", value ? 1 : 0); }
        }

        /// <summary>Vertical field of view in degrees when not aiming.</summary>
        public static float FieldOfView
        {
            get { return Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "fov", 70f), MinFieldOfView, MaxFieldOfView); }
            set { PlayerPrefs.SetFloat(Prefix + "fov", Mathf.Clamp(value, MinFieldOfView, MaxFieldOfView)); }
        }

        /// <summary>Vertical field of view in degrees while aiming (right mouse).</summary>
        public static float AimFieldOfView
        {
            get { return Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "aimFov", 50f), MinAimFieldOfView, MaxAimFieldOfView); }
            set { PlayerPrefs.SetFloat(Prefix + "aimFov", Mathf.Clamp(value, MinAimFieldOfView, MaxAimFieldOfView)); }
        }

        /// <summary>Degrees of turn per mouse count; the game's original value is 0.12.</summary>
        public static float MouseSensitivity
        {
            get { return Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "sensitivity", 0.12f), MinSensitivity, MaxSensitivity); }
            set { PlayerPrefs.SetFloat(Prefix + "sensitivity", Mathf.Clamp(value, MinSensitivity, MaxSensitivity)); }
        }

        public const float MinAimSensitivity = 0.5f, MaxAimSensitivity = 1.5f;

        /// <summary>
        /// Aiming turn speed relative to the zoom-matched speed (1 = the same mouse move covers the same
        /// part of the screen as at the hip).
        /// </summary>
        public static float AimSensitivity
        {
            get { return Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "aimSensitivity", 1f), MinAimSensitivity, MaxAimSensitivity); }
            set { PlayerPrefs.SetFloat(Prefix + "aimSensitivity", Mathf.Clamp(value, MinAimSensitivity, MaxAimSensitivity)); }
        }

        /// <summary>Strength of the red flash and camera flinch when hit, 0 to 1.</summary>
        public static float HitEffects
        {
            get { return Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "hitEffects", 1f)); }
            set { PlayerPrefs.SetFloat(Prefix + "hitEffects", Mathf.Clamp01(value)); }
        }

        /// <summary>Strength of the weapon kick, sway and walk bob, 0 to 1 (WeaponFeel).</summary>
        public static float WeaponMotion
        {
            get { return Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "weaponMotion", 1f)); }
            set { PlayerPrefs.SetFloat(Prefix + "weaponMotion", Mathf.Clamp01(value)); }
        }

        /// <summary>Heartbeat sound at low health.</summary>
        public static bool Heartbeat
        {
            get { return PlayerPrefs.GetInt(Prefix + "heartbeat", 1) == 1; }
            set { PlayerPrefs.SetInt(Prefix + "heartbeat", value ? 1 : 0); }
        }

        /// <summary>Night lighting and post-processing from the additions (Atmosphere.cs).</summary>
        public static bool EnhancedLighting
        {
            get { return PlayerPrefs.GetInt(Prefix + "enhancedLighting", 1) == 1; }
            set { PlayerPrefs.SetInt(Prefix + "enhancedLighting", value ? 1 : 0); }
        }

        /// <summary>Smooth edges: 4x MSAA with alpha to coverage on leaves and grass (Atmosphere.cs).</summary>
        public static bool SmoothEdges
        {
            get { return PlayerPrefs.GetInt(Prefix + "smoothEdges", 1) == 1; }
            set { PlayerPrefs.SetInt(Prefix + "smoothEdges", value ? 1 : 0); }
        }

        /// <summary>Master volume, 0 to 1 (AudioListener.volume).</summary>
        public static float MasterVolume
        {
            get { return Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "volume", 1f)); }
            set
            {
                PlayerPrefs.SetFloat(Prefix + "volume", Mathf.Clamp01(value));
                AudioListener.volume = Mathf.Clamp01(value);
            }
        }

        public const float MinHudScale = 0.75f, MaxHudScale = 1.5f;

        /// <summary>Size of the HUD on top of the resolution scaling (1 = normal).</summary>
        public static float HudScale
        {
            get { return Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "hudScale", 1f), MinHudScale, MaxHudScale); }
            set { PlayerPrefs.SetFloat(Prefix + "hudScale", Mathf.Clamp(value, MinHudScale, MaxHudScale)); }
        }

        /// <summary>Key hints in the HUD (the line under the health bar).</summary>
        public static bool KeyHints
        {
            get { return PlayerPrefs.GetInt(Prefix + "keyHints", 1) == 1; }
            set { PlayerPrefs.SetInt(Prefix + "keyHints", value ? 1 : 0); }
        }

        /// <summary>The game's own text (objective lines, weapon bar, subtitles) in the menus' font.</summary>
        public static bool PeriodFontForGameText
        {
            get { return PlayerPrefs.GetInt(Prefix + "periodFont", 1) == 1; }
            set { PlayerPrefs.SetInt(Prefix + "periodFont", value ? 1 : 0); }
        }

        /// <summary>Vertical sync: one frame per display refresh (QualitySettings.vSyncCount 1) or off (0).</summary>
        public static bool VSync
        {
            get { return PlayerPrefs.GetInt(Prefix + "vsync", 1) == 1; }
            set
            {
                PlayerPrefs.SetInt(Prefix + "vsync", value ? 1 : 0);
                QualitySettings.vSyncCount = value ? 1 : 0;
            }
        }

        /// <summary>Applies the saved VSync choice. Called once by AdditionsBootstrap at start (no
        /// RuntimeInitializeOnLoadMethod here, so the player's RuntimeInitializeOnLoads list needs no new entry).</summary>
        public static void ApplyAtStart()
        {
            QualitySettings.vSyncCount = VSync ? 1 : 0;
        }

        // ------------------------------------------------------------------ resolution and display mode
        // Unity's player already remembers the resolution and window mode between runs, so nothing is
        // saved here. A choice made in the settings window is held as pending and applied when the
        // window closes (ApplyDisplay), so pressing the arrows does not flicker through modes.

        static readonly string[] modeNames = { "Fullscreen window", "Exclusive fullscreen", "Windowed" };
        static List<Vector2Int> resList;
        static int pendingRes = -1, pendingMode = -1;

        /// <summary>The display modes offered, in the order of <see cref="DisplayModeIndex"/>.</summary>
        public static string[] DisplayModeNames { get { return modeNames; } }

        /// <summary>Distinct width x height pairs of the display, smallest first, largest last.</summary>
        public static List<Vector2Int> Resolutions()
        {
            if (resList == null)
            {
                resList = new List<Vector2Int>();
                Resolution[] all = Screen.resolutions;
                for (int i = 0; i < all.Length; i++)
                {
                    Vector2Int v = new Vector2Int(all[i].width, all[i].height);
                    if (!resList.Contains(v)) resList.Add(v);
                }
                resList.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            }
            // The running size (for example a window dragged to an odd size) is listed too, so the row never shows a wrong value.
            Vector2Int cur = new Vector2Int(Screen.width, Screen.height);
            if (!resList.Contains(cur))
            {
                resList.Add(cur);
                resList.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            }
            return resList;
        }

        /// <summary>Labels for <see cref="Resolutions"/>, like "1920 x 1080".</summary>
        public static string[] ResolutionLabels()
        {
            List<Vector2Int> list = Resolutions();
            string[] s = new string[list.Count];
            for (int i = 0; i < s.Length; i++) s[i] = list[i].x + " x " + list[i].y;
            return s;
        }

        /// <summary>Index into <see cref="Resolutions"/>: the pending choice, else the running size.</summary>
        public static int ResolutionIndex
        {
            get
            {
                List<Vector2Int> list = Resolutions();
                if (pendingRes >= 0 && pendingRes < list.Count) return pendingRes;
                return Mathf.Max(0, list.IndexOf(new Vector2Int(Screen.width, Screen.height)));
            }
            set { pendingRes = Mathf.Clamp(value, 0, Resolutions().Count - 1); }
        }

        /// <summary>0 fullscreen window, 1 exclusive fullscreen, 2 windowed: the pending choice, else the running mode.</summary>
        public static int DisplayModeIndex
        {
            get
            {
                if (pendingMode >= 0) return pendingMode;
                switch (Screen.fullScreenMode)
                {
                    case FullScreenMode.ExclusiveFullScreen: return 1;
                    case FullScreenMode.FullScreenWindow: return 0;
                    default: return 2;
                }
            }
            set { pendingMode = Mathf.Clamp(value, 0, 2); }
        }

        /// <summary>True when the pending resolution would be used (a fullscreen window always takes the desktop size).</summary>
        public static bool ResolutionApplies { get { return DisplayModeIndex != 0; } }

        /// <summary>Applies the pending resolution and display mode, if any. Called when the settings window closes.</summary>
        public static void ApplyDisplay()
        {
            if (pendingRes < 0 && pendingMode < 0) return;
            List<Vector2Int> list = Resolutions();
            Vector2Int r = list[ResolutionIndex];
            int m = DisplayModeIndex;
            pendingRes = -1;
            pendingMode = -1;
            FullScreenMode mode = m == 1 ? FullScreenMode.ExclusiveFullScreen : m == 0 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (mode == FullScreenMode.FullScreenWindow) r = new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height);
            if (r.x == Screen.width && r.y == Screen.height && mode == Screen.fullScreenMode) return;
            Screen.SetResolution(r.x, r.y, mode, Screen.currentResolution.refreshRateRatio);
        }

        public static void Save()
        {
            PlayerPrefs.Save();
        }
    }
}
