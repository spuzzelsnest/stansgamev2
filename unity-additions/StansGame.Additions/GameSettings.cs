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

        public static void Save()
        {
            PlayerPrefs.Save();
        }
    }
}
