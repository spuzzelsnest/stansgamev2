using System.Collections.Generic;
using UnityEngine;

namespace StansGame.Additions
{
    /// <summary>
    /// The local player and British soldiers no longer block each other's movement (bullets still hit as
    /// before: this only affects CharacterController movement). Next to a glider a section of five can
    /// otherwise box the player in against the wreckage, and soldiers jam each other in narrow gaps.
    /// </summary>
    public static class FriendlyPassThrough
    {
        static readonly List<CharacterController> allies = new List<CharacterController>();
        static CharacterController player;

        /// <summary>Called by AdditionsBootstrap for every new AI soldier.</summary>
        public static void AddSoldier(SoldierAI ai)
        {
            Health h = ai.GetComponent<Health>();
            CharacterController cc = ai.GetComponent<CharacterController>();
            if (h == null || cc == null || h.faction != Faction.British) return;
            allies.RemoveAll(c => c == null);
            foreach (CharacterController other in allies) Physics.IgnoreCollision(cc, other, true);
            if (player != null) Physics.IgnoreCollision(cc, player, true);
            allies.Add(cc);
        }

        /// <summary>Called by AdditionsBootstrap with the local player (new after every respawn).</summary>
        public static void SetPlayer(SoldierController p)
        {
            CharacterController cc = p != null ? p.GetComponent<CharacterController>() : null;
            if (cc == null || cc == player) return;
            player = cc;
            allies.RemoveAll(c => c == null);
            foreach (CharacterController other in allies) Physics.IgnoreCollision(cc, other, true);
        }
    }
}
