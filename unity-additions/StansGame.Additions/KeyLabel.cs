using UnityEngine.InputSystem;

namespace StansGame.Additions
{
    /// <summary>
    /// The game reads keys by physical position (Keyboard.current.wKey is the key where W sits on a
    /// US keyboard). These helpers return the label printed on that key in the player's own layout,
    /// so prompts read "ZQSD" on AZERTY and "WASD" on QWERTY.
    /// </summary>
    public static class KeyLabel
    {
        public static string Of(Key key)
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return key.ToString();
            string name = kb[key].displayName;
            return string.IsNullOrEmpty(name) ? key.ToString() : name.ToUpperInvariant();
        }

        /// <summary>The four movement keys, for example "WASD" or "ZQSD".</summary>
        public static string Move
        {
            get { return Of(Key.W) + Of(Key.A) + Of(Key.S) + Of(Key.D); }
        }
    }
}
