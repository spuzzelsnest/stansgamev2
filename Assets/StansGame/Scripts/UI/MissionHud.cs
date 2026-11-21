using UnityEngine;
using UnityEngine.SceneManagement;

namespace StansGame
{
    /// <summary>
    /// One HUD for every level, laid out as Pegasus Bridge's (MissionDirector.OnGUI): the clock, date and phase top left
    /// with the objective under it, messages centred a fifth of the way down, the weapon line and health bar bottom left,
    /// the E prompts under the crosshair; the Esc menu; and the films in "The Longest Day" style (CinematicDirector):
    /// letterbox, time stamp and caption in the bars, "Space: skip".
    /// </summary>
    public static class MissionHud
    {
        public static void TopLine(string clock, string title, string objective)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true };
            GUI.Label(new Rect(20, 16, 900, 30), $"<b>{clock}</b>  6 June 1944   —   {title}", style);
            style.fontSize = 16;
            GUI.Label(new Rect(20, 44, 900, 26), objective, style);
        }

        public static void Message(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            var big = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter, richText = true, wordWrap = true };
            GUI.Label(new Rect(Screen.width * 0.08f, Screen.height * 0.22f, Screen.width * 0.84f, 70), $"<b>{message}</b>", big);
        }

        /// <summary>Weapon line, health bar and the E prompts (as Pegasus Bridge).</summary>
        public static void Player(SoldierController player)
        {
            if (!player || !player.enabled) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true };
            var w = player.CurrentWeapon;
            var hs = player.GetComponent<Health>();
            var kit = player.GetComponent<Loadout>();
            string held = w ? $"{w.displayName}   {w.Ammo} / {w.SpareRounds}{(w.IsReloading ? "  reloading..." : "")}"
                        : kit && kit.grenades.Count > 0 ? $"{kit.grenades[0].displayName}   × {kit.grenades.Count}" : "";
            GUI.Label(new Rect(20, Screen.height - 10 - Hud.HealthHeight - 30, 600, 26), held, style);
            string pose = player.IsProne ? "prone" : player.IsCrouching ? "kneeling" : "standing";
            if (hs) Hud.HealthBar(hs, 20, Screen.height - 10, pose);
            var entry = player.GetComponent<VehicleEntry>();
            if (entry && (entry.Nearby || entry.NearbyGun || entry.NearbyTruck) && !(kit && kit.NearbyPickup))
                GUI.Label(new Rect(Screen.width / 2 - 80, Screen.height / 2 + 40, 400, 26),
                          entry.NearbyGun ? $"Press E to man the {entry.NearbyGun.displayName}"
                          : entry.NearbyTruck ? $"Press E to drive the {entry.NearbyTruck.displayName}" : "Press E to get in", style);
        }

        public static void Dead(string text)
        {
            var big = new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleCenter, richText = true };
            GUI.Label(new Rect(0, Screen.height * 0.5f, Screen.width, 40), $"<b>{text}</b>", big);
        }

        /// <summary>The Esc menu; returns the new open state.</summary>
        public static bool Menu(bool open)
        {
            if (!open) return false;
            GUILayout.BeginArea(new Rect(Screen.width / 2 - 160, Screen.height / 2 - 90, 320, 180), GUI.skin.box);
            GUILayout.Label("<b>Stan's Game</b>", new GUIStyle(GUI.skin.label) { richText = true });
            if (GUILayout.Button("Back to the fight", GUILayout.Height(32))) { open = false; Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            if (GUILayout.Button("Choose another mission", GUILayout.Height(32))) SceneManager.LoadScene(0);
            if (GUILayout.Button("Quit the game", GUILayout.Height(32))) Application.Quit();
            GUILayout.EndArea();
            return open;
        }

        public static void Fade(float fade)
        {
            if (fade <= 0f) return;
            GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01(fade));
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        /// <summary>A film frame as Pegasus Bridge's: letterbox (0..1 slid in), time stamp, caption, the skip hint.</summary>
        public static void Film(float letterbox, string stamp, string caption, float alpha)
        {
            GUI.depth = -10;
            float bar = Screen.height * 0.14f * letterbox;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, bar), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, Screen.height - bar, Screen.width, bar), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.026f), richText = true };
            if (!string.IsNullOrEmpty(caption)) CinematicDirector.DrawCaption(stamp, caption, alpha, bar, style);
            var hint = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.LowerRight };
            hint.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
            GUI.Label(new Rect(0, Screen.height - 22, Screen.width - 12, 20), "Space: skip", hint);
        }
    }
}
