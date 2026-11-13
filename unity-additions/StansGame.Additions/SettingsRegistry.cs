using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame.Additions
{
    /// <summary>
    /// The rows of the settings window (F10, or Settings in the main and pause menus), grouped in tabs.
    /// Any script can add its own rows; the window draws them, handles mouse, keyboard and gamepad, and
    /// saves PlayerPrefs when it closes. Registering again with the same id replaces the row, so it is
    /// safe to call from <c>Awake</c> of a component that is created more than once.
    /// <example><code>
    /// SettingsRegistry.AddToggle(SettingsRegistry.Video, "fog", "Ground fog",
    ///     () =&gt; MySettings.Fog, v =&gt; MySettings.Fog = v, "Low mist over the canal.");
    /// SettingsRegistry.AddSlider(SettingsRegistry.Audio, "music", "Music volume",
    ///     () =&gt; MySettings.Music, v =&gt; MySettings.Music = v, 0f, 1f, SettingsRegistry.Percent);
    /// </code></example>
    /// Rows are shown in <c>order</c> (lower first, then in the order they were added). The built-in rows
    /// use orders 10 to 90 in steps of 10, so new rows can go between them.
    /// </summary>
    public static class SettingsRegistry
    {
        public const string Video = "VIDEO", Audio = "AUDIO", Gameplay = "GAMEPLAY", Controls = "CONTROLS";

        public enum Kind { Toggle, Slider, Choice, Button }

        public sealed class Row
        {
            public string Tab, Id, Label, Help;
            public int Order, Seq;
            public Kind Kind;
            public Func<bool> GetBool;
            public Action<bool> SetBool;
            public Func<float> GetFloat;
            public Action<float> SetFloat;
            public float Min, Max, Step;
            public Func<float, string> Format;
            public string[] Options;
            public Func<int> GetInt;
            public Action<int> SetInt;
            public Action Press;
            /// <summary>Optional: the row is shown but greyed out while this returns false.</summary>
            public Func<bool> Enabled;
        }

        static readonly List<string> tabs = new List<string> { Video, Audio, Gameplay, Controls };
        static readonly List<Row> rows = new List<Row>();
        static int seq;
        static bool builtIns;

        /// <summary>Formats 0..1 as "75 %".</summary>
        public static string Percent(float v) { return Mathf.RoundToInt(v * 100f) + " %"; }
        /// <summary>Formats a whole number.</summary>
        public static string Whole(float v) { return Mathf.RoundToInt(v).ToString(); }
        /// <summary>Formats with two decimals.</summary>
        public static string TwoDecimals(float v) { return v.ToString("0.00"); }

        /// <summary>Adds a tab after the existing ones (if it is not there yet).</summary>
        public static void AddTab(string name)
        {
            if (!tabs.Contains(name)) tabs.Add(name);
        }

        public static void AddToggle(string tab, string id, string label, Func<bool> get, Action<bool> set, string help = null, int order = 100)
        {
            Add(new Row { Tab = tab, Id = id, Label = label, Help = help, Order = order, Kind = Kind.Toggle, GetBool = get, SetBool = set });
        }

        /// <summary>A slider; <paramref name="step"/> 0 moves 1/20 of the range per key press.</summary>
        public static void AddSlider(string tab, string id, string label, Func<float> get, Action<float> set, float min, float max,
                                     Func<float, string> format = null, string help = null, int order = 100, float step = 0f)
        {
            Add(new Row { Tab = tab, Id = id, Label = label, Help = help, Order = order, Kind = Kind.Slider, GetFloat = get, SetFloat = set,
                          Min = min, Max = max, Step = step > 0f ? step : (max - min) / 20f, Format = format ?? TwoDecimals });
        }

        public static void AddChoice(string tab, string id, string label, string[] options, Func<int> get, Action<int> set, string help = null, int order = 100)
        {
            Add(new Row { Tab = tab, Id = id, Label = label, Help = help, Order = order, Kind = Kind.Choice, Options = options, GetInt = get, SetInt = set });
        }

        public static void AddButton(string tab, string id, string label, Action press, string help = null, int order = 100)
        {
            Add(new Row { Tab = tab, Id = id, Label = label, Help = help, Order = order, Kind = Kind.Button, Press = press });
        }

        /// <summary>Adds a row built by hand (for <see cref="Row.Enabled"/>).</summary>
        public static void Add(Row row)
        {
            EnsureBuiltIns();
            if (row == null || string.IsNullOrEmpty(row.Id)) return;
            AddTab(row.Tab);
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Id == row.Id) { row.Seq = rows[i].Seq; rows[i] = row; return; }
            row.Seq = seq++;
            rows.Add(row);
        }

        public static void Remove(string id)
        {
            rows.RemoveAll(r => r.Id == id);
        }

        public static int TabCount() { EnsureBuiltIns(); return tabs.Count; }
        public static string TabName(int i) { EnsureBuiltIns(); return tabs[Mathf.Clamp(i, 0, tabs.Count - 1)]; }

        /// <summary>The rows of a tab in display order.</summary>
        public static List<Row> RowsOf(string tab)
        {
            EnsureBuiltIns();
            List<Row> list = rows.FindAll(r => r.Tab == tab);
            list.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Seq.CompareTo(b.Seq));
            return list;
        }

        static void EnsureBuiltIns()
        {
            if (builtIns) return;
            builtIns = true;

            AddToggle(Video, "firstPerson", "First-person view", () => GameSettings.FirstPerson, v => GameSettings.FirstPerson = v,
                "Off: the original over-the-shoulder view. " + "The view key switches in the field.", 10);
            AddSlider(Video, "fov", "Field of view", () => GameSettings.FieldOfView, v => GameSettings.FieldOfView = v,
                GameSettings.MinFieldOfView, GameSettings.MaxFieldOfView, v => Mathf.RoundToInt(v) + "°", "Vertical, in degrees, at the hip.", 20, 1f);
            AddSlider(Video, "aimFov", "Field of view when aiming", () => GameSettings.AimFieldOfView, v => GameSettings.AimFieldOfView = v,
                GameSettings.MinAimFieldOfView, GameSettings.MaxAimFieldOfView, v => Mathf.RoundToInt(v) + "°", "Looking down the sights (right mouse).", 30, 1f);
            AddToggle(Video, "enhancedLighting", "Enhanced night lighting", () => GameSettings.EnhancedLighting, v => GameSettings.EnhancedLighting = v,
                "Moonlit sky and dark ground light, soft bloom on flashes and fires.", 40);
            AddToggle(Video, "smoothEdges", "Smooth edges", () => GameSettings.SmoothEdges, v => GameSettings.SmoothEdges = v,
                "Anti-aliasing and softer leaves and grass.", 50);
            Add(new Row { Tab = Video, Id = "resolution", Label = "Resolution", Order = 60, Kind = Kind.Choice,
                Options = GameSettings.ResolutionLabels(), GetInt = () => GameSettings.ResolutionIndex, SetInt = v => GameSettings.ResolutionIndex = v,
                Help = "Applied when this window closes. A fullscreen window always uses the desktop size.",
                Enabled = () => GameSettings.ResolutionApplies });
            AddChoice(Video, "displayMode", "Display mode", GameSettings.DisplayModeNames, () => GameSettings.DisplayModeIndex, v => GameSettings.DisplayModeIndex = v,
                "Applied when this window closes.", 70);
            AddToggle(Video, "vsync", "VSync", () => GameSettings.VSync, v => GameSettings.VSync = v,
                "One frame per screen refresh: no tearing, a steadier pace.", 80);

            AddSlider(Audio, "volume", "Master volume", () => GameSettings.MasterVolume, v => GameSettings.MasterVolume = v, 0f, 1f, Percent, null, 10);
            AddToggle(Audio, "heartbeat", "Heartbeat at low health", () => GameSettings.Heartbeat, v => GameSettings.Heartbeat = v, null, 20);

            AddSlider(Gameplay, "hudScale", "HUD size", () => GameSettings.HudScale, v => GameSettings.HudScale = v,
                GameSettings.MinHudScale, GameSettings.MaxHudScale, Percent, "Health, kit, orders and markers. They also grow with the resolution.", 10, 0.05f);
            AddToggle(Gameplay, "keyHints", "Key hints in the HUD", () => GameSettings.KeyHints, v => GameSettings.KeyHints = v,
                "The keys for stance and view under the health bar.", 20);
            AddToggle(Gameplay, "periodFont", "Period font for the game's own text", () => GameSettings.PeriodFontForGameText,
                v => { GameSettings.PeriodFontForGameText = v; Ui.RefreshGameFont(); }, "Objective lines, weapon bar and subtitles in the menus' font.", 30);
            AddSlider(Gameplay, "hitEffects", "Hit effects", () => GameSettings.HitEffects, v => GameSettings.HitEffects = v, 0f, 1f, Percent,
                "Red flash, flinch, the screen edge darkening near misses.", 40);
            AddSlider(Gameplay, "weaponMotion", "Weapon motion", () => GameSettings.WeaponMotion, v => GameSettings.WeaponMotion = v, 0f, 1f, Percent,
                "Kick, sway and walk bob.", 50);

            AddSlider(Controls, "sensitivity", "Mouse sensitivity", () => GameSettings.MouseSensitivity, v => GameSettings.MouseSensitivity = v,
                GameSettings.MinSensitivity, GameSettings.MaxSensitivity, TwoDecimals, "Degrees of turn per mouse count (the game's own value is 0.12).", 10, 0.01f);
            AddSlider(Controls, "aimSensitivity", "Aim sensitivity", () => GameSettings.AimSensitivity, v => GameSettings.AimSensitivity = v,
                GameSettings.MinAimSensitivity, GameSettings.MaxAimSensitivity, TwoDecimals, "1.00 is matched to the zoom.", 20, 0.05f);
            AddButton(Controls, "keys", "Show all keys", () => ControlsPage.Show(true), "Every key on this keyboard's layout.", 30);
        }
    }
}
