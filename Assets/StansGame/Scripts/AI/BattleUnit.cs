using System.Collections.Generic;
using UnityEngine;

namespace StansGame
{
    /// <summary>On every man the battle sheet put in the scene: which side and group he belongs to (for the
    /// validator and the F10 overlay).</summary>
    public class BattleUnit : MonoBehaviour
    {
        public string side, group, groupName;
        [TextArea] public string task;
        public bool attacker;

        public static readonly List<BattleUnit> All = new List<BattleUnit>();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDestroy() => All.Remove(this);
    }
}
