using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame
{
    /// <summary>
    /// Whether the game may use the mouse this frame. Not while the cursor is free (a menu is open, or the window has
    /// lost focus), and not straight after a menu closes until the buttons have been let go - so the click on
    /// "Back to the fight" doesn't also fire the gun, and moving the mouse over a menu doesn't turn the view.
    /// </summary>
    public static class GameInput
    {
        static bool waitRelease;
        static int checkedFrame = -1, lockedSince = -1;

        public static bool MouseActive
        {
            get
            {
                if (checkedFrame != Time.frameCount)
                {
                    checkedFrame = Time.frameCount;
                    var m = Mouse.current;
                    if (Cursor.lockState != CursorLockMode.Locked || !Application.isFocused) { waitRelease = true; lockedSince = -1; }
                    else if (waitRelease)
                    {
                        // the menu's click can reach the game a frame after the menu has closed: give it two frames
                        // to pass, then wait for the buttons to be up
                        if (lockedSince < 0) lockedSince = Time.frameCount;
                        if (Time.frameCount - lockedSince >= 2 && (m == null || (!m.leftButton.isPressed && !m.rightButton.isPressed))) waitRelease = false;
                    }
                }
                return !waitRelease;
            }
        }

        /// <summary>Mouse movement for looking around (zero while the mouse belongs to a menu).</summary>
        public static Vector2 LookDelta => MouseActive && Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
    }
}
