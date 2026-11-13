using UnityEngine.InputSystem;

namespace StansGame.Additions
{
    /// <summary>
    /// The game reads keys by physical position (Keyboard.current.wKey is the key where W sits on a
    /// US keyboard). These helpers return the label printed on that key in the player's own layout,
    /// so prompts read "ZQSD" on AZERTY and "WASD" on QWERTY.
    /// The number row is the exception: on AZERTY its unshifted characters are symbols (the 3 key
    /// gives "), but the number is printed on the key too, so the number is shown.
    /// </summary>
    public static class KeyLabel
    {
        public static string Of(Key key)
        {
            if (key >= Key.Digit1 && key <= Key.Digit9) return ((int)(key - Key.Digit1) + 1).ToString();
            if (key == Key.Digit0) return "0";
            switch (key)
            {
                case Key.Space: return "SPACE";
                case Key.Escape: return "ESC";
                case Key.Tab: return "TAB";
                case Key.Enter: return "ENTER";
                case Key.LeftShift:
                case Key.RightShift: return "SHIFT";
                case Key.LeftCtrl:
                case Key.RightCtrl: return "CTRL";
                case Key.LeftAlt: return "ALT";
                case Key.Backspace: return "BACKSPACE";
                case Key.UpArrow: return "↑";
                case Key.DownArrow: return "↓";
                case Key.LeftArrow: return "←";
                case Key.RightArrow: return "→";
            }
            Keyboard kb = Keyboard.current;
            if (kb == null) return key.ToString().ToUpperInvariant();
            string name = kb[key].displayName;
            return string.IsNullOrEmpty(name) ? key.ToString().ToUpperInvariant() : name.ToUpperInvariant();
        }

        /// <summary>The four movement keys, for example "WASD" or "ZQSD".</summary>
        public static string Move
        {
            get { return Of(Key.W) + Of(Key.A) + Of(Key.S) + Of(Key.D); }
        }

        /// <summary>The keyboard layout name the system reports, for example "French", or null.</summary>
        public static string Layout
        {
            get
            {
                Keyboard kb = Keyboard.current;
                return kb != null && !string.IsNullOrEmpty(kb.keyboardLayout) ? kb.keyboardLayout : null;
            }
        }
    }
}
