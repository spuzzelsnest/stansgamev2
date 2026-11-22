using System;
using System.Collections.Generic;
using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// The battle sheet of one map: who attacks, who defends, and with what. Each side is a list of groups (a
    /// garrison, a patrol, a stick of paratroopers, a counter-attack) and each group a list of men with where they
    /// start. The level builder reads this sheet and puts the men in the scene, so a map can be changed here, in the
    /// Inspector, without touching code (then rebuild the level). Validate it with Stan's Game > Debug > Validate Battle.
    ///
    /// Names still matter: the German soldier builder gives a man an MG 42 if "MG42" is in his name, an MP 40 for
    /// "MP40", else a Kar 98k; "Sentry" and "NCO" walk a beat; "MG42", "pillbox", "loader" never leave their post.
    /// American "kind": Rifleman (M1 Garand), Thompson, Carbine; "Player" marks the player's place in a stick.
    /// </summary>
    [CreateAssetMenu(menuName = "Stan's Game/Battle Plan", fileName = "BattlePlan")]
    public class BattlePlan : ScriptableObject
    {
        public string level;
        [TextArea(2, 6)] public string notes;
        public Side attacker = new Side { name = "Attacker" };
        public Side defender = new Side { name = "Defender" };

        public enum Arrival
        {
            [Tooltip("In place when the level starts")] AtStart,
            [Tooltip("Jumps from the C-47s; position = the landing point")] ByParachute,
            [Tooltip("Hidden until the mission reaches its phase (see 'phase')")] ByPhase,
        }

        [Serializable]
        public class Side
        {
            public string name;
            public Faction faction;
            [TextArea(1, 4)] public string notes;
            public Commander commander = new Commander();
            public List<Group> groups = new List<Group>();
            public int Men { get { int n = 0; foreach (var g in groups) n += g.units.Count; return n; } }
        }

        /// <summary>The man in charge of a side: who he was (for the loading screen and the cutscenes) and how he
        /// fights the battle (read by BattleCommander).</summary>
        [Serializable]
        public class Commander
        {
            public string name;
            public string rank;
            public string unit;
            [Tooltip("For the loading screen and the cutscenes")] public Texture2D portrait;
            [TextArea(2, 6)] public string bio;

            [Header("How he fights")]
            [Range(0f, 1f), Tooltip("Defender: a group in a fight sends for help when it is down to this share of its men")] public float reinforceBelow = 0.6f;
            [Range(0f, 1f), Tooltip("Defender: a group pulls back to the nearest strong group when down to this share")] public float fallBackBelow = 0.3f;
            [Tooltip("Defender: the most men sent to help in one order")] public int maxSend = 3;
            [Tooltip("Attacker: in contact, half the group gives covering fire while the other half moves up")] public bool bounding = true;
            [Tooltip("Attacker: seconds each half moves before they swap")] public float boundSeconds = 7f;
            [Range(0f, 1f), Tooltip("Attacker: a group stops to regroup when down to this share, until the shooting dies down")] public float regroupBelow = 0.4f;
            [Tooltip("Seconds between his looks at the battle (orders take time to reach the men)")] public float orderInterval = 3f;
        }

        [Serializable]
        public class Group
        {
            [Tooltip("Short id the level builder and the mission use (don't rename)")] public string id;
            public string name;
            [TextArea(1, 4)] public string task;
            public Arrival arrival;
            [Tooltip("For ByPhase: which part of the mission brings them in")] public string phase;
            public SoldierAI.Mode mode = SoldierAI.Mode.HoldPosition;
            [Tooltip("Awake and watching from the start (false: asleep until the alarm)")] public bool awake = true;
            [Range(0f, 1f)] public float skill = 0.32f;
            [Range(0f, 1f), Tooltip("Skill of the MG 42 gunners in the group")] public float mgSkill = 0.35f;
            [Range(0f, 1f), Tooltip("Chance a man carries a smoke grenade")] public float smokeChance = 0.35f;
            [Tooltip("How far a man may move about once fighting (m); 0 = stays at his post")] public float combatRoam = -1f;
            [Tooltip("Name of the marker they advance to (mode Advance)")] public string objective;
            [Tooltip("A beat to walk (patrols): the men follow it in file")] public Vector3[] route;
            public List<Unit> units = new List<Unit>();
        }

        [Serializable]
        public class Unit
        {
            public string name;
            [Tooltip("Americans: Rifleman, Thompson, Carbine, Player. Germans: from the name (MG42, MP40, else rifle)")] public string kind;
            public Vector3 position;
            public float yaw;
        }

        public IEnumerable<(Side side, Group group)> Groups()
        {
            foreach (var g in defender.groups) yield return (defender, g);
            foreach (var g in attacker.groups) yield return (attacker, g);
        }

        public Group Find(string id)
        {
            foreach (var (_, g) in Groups()) if (g.id == id) return g;
            return null;
        }
    }
}
