using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>Stan's Game > Debug > Sky: move the mission clock on (play mode), to check the sky at other hours.</summary>
    public static class SkyClockDebug
    {
        static void Jump(float hours)
        {
            if (!Application.isPlaying) { Debug.LogWarning("Stan's Game: start the game first"); return; }
            if (SainteMereMission.Instance) { SainteMereMission.Instance.Minutes += hours * 60f; Debug.Log("Stan's Game: clock " + SainteMereMission.Instance.ClockText); }
            else if (MissionDirector.Instance) { MissionDirector.Instance.Minutes += hours * 60f; Debug.Log("Stan's Game: clock " + MissionDirector.Instance.ClockText); }
        }
        [MenuItem("Stan's Game/Debug/Sky: clock +1 hour (play mode) %#k", priority = 60)] static void Plus1() => Jump(1f);
        [MenuItem("Stan's Game/Debug/Sky: clock +3 hours (play mode)", priority = 61)] static void Plus3() => Jump(3f);
    }
}
