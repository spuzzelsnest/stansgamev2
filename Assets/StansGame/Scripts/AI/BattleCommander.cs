using System.Collections.Generic;
using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// The commander of one side, above the soldiers' own AI (SoldierAI fights each man: cover, aim, spacing; the
    /// commander moves groups). Reads his side's men from the battle sheet tags (BattleUnit) and how he fights from
    /// the sheet (BattlePlan.Commander). Every few seconds he looks at his groups:
    /// Both sides do both (by what each group is doing, not which side it is on):
    ///   Holding ground: a group in a fight that is losing men sends for help, and the nearest group not in the fight
    ///   sends a few men; a group nearly wiped out pulls its survivors back to the nearest strong group.
    ///   On the move: a group advancing under fire moves in bounds, half covering while the other half moves up; a
    ///   group that has lost too many men stops and regroups until the shooting dies down, then goes on.
    /// The mission's own orders come first: he never touches a group the mission has not let into play, nor men
    /// following the player, nor men dug in at a weapon (MG nest, gun pit). Host only in multiplayer.
    /// </summary>
    public class BattleCommander : MonoBehaviour
    {
        public BattlePlan plan;
        public bool attacker;

        public BattlePlan.Side Side => plan ? (attacker ? plan.attacker : plan.defender) : null;
        public BattlePlan.Commander Cmd => Side != null ? Side.commander : null;
        public string Title
        {
            get
            {
                var c = Cmd;
                string who = c != null && !string.IsNullOrEmpty(c.name) ? $"{c.rank} {c.name}".Trim() : "Commander";
                return $"{who} ({(attacker ? "attacking" : "defending")})";
            }
        }

        public static readonly List<BattleCommander> All = new List<BattleCommander>();
        /// <summary>His latest orders, newest last (for the F10 overlay and the log).</summary>
        public readonly List<string> Orders = new List<string>();
        /// <summary>What each group (by id) has been told to do now.</summary>
        public readonly Dictionary<string, string> GroupOrder = new Dictionary<string, string>();

        class Group
        {
            public string id, name;
            public readonly List<SoldierAI> men = new List<SoldierAI>();
            public int total, alive;
            public bool live, engaged;
            public Vector3 centre, enemy;
            public float lastContact = -999f, helpSentAt = -999f;
            public bool fellBack, regrouping;
            // bounding
            public float boundSwapAt; public bool firstHalfMoving = true;
            public Transform objective;
        }
        readonly Dictionary<string, Group> groups = new Dictionary<string, Group>();
        readonly Dictionary<SoldierAI, Transform> heldFrom = new Dictionary<SoldierAI, Transform>();   // advance target to give back after holding
        float nextLook;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Start()
        {
            foreach (var bu in FindObjectsByType<BattleUnit>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (bu.attacker != attacker) continue;
                var ai = bu.GetComponent<SoldierAI>(); if (!ai) continue;
                if (!groups.TryGetValue(bu.group, out var g)) groups[bu.group] = g = new Group { id = bu.group, name = bu.groupName };
                g.men.Add(ai); g.total++;
            }
            nextLook = Time.time + 5f;
        }

        void Update()
        {
            if (!plan || !Net.Simulates || Time.time < nextLook) return;
            var c = Cmd;
            nextLook = Time.time + (c != null ? Mathf.Max(1f, c.orderInterval) : 3f);
            Survey();
            // both sides do both: groups holding ground defend (the 3rd Battalion once it holds the square too),
            // groups on the move attack (the German counter-attacks too)
            Defend(c); Attack(c);
        }

        // ------------------------------------------------------------------ looking at the battle

        static bool Usable(SoldierAI ai)
        {
            if (!ai || !ai.isActiveAndEnabled) return false;
            var h = ai.GetComponent<Health>();
            return !(h && h.IsDead);
        }

        static bool DugIn(SoldierAI ai) => ai.mode == SoldierAI.Mode.HoldPosition && ai.combatRoam <= 0f && ai.patrolRoute == null;

        void Survey()
        {
            foreach (var g in groups.Values)
            {
                g.alive = 0; g.live = false; g.engaged = false;
                Vector3 sum = Vector3.zero, foe = Vector3.zero; int foes = 0;
                foreach (var ai in g.men)
                {
                    if (!ai || !ai.gameObject.activeInHierarchy) continue;
                    g.live = true;
                    if (!Usable(ai)) continue;
                    g.alive++; sum += ai.transform.position;
                    if (ai.Target) { foe += ai.Target.transform.position; foes++; }
                    if (ai.advanceTarget) g.objective = ai.advanceTarget;
                }
                if (g.alive > 0) g.centre = sum / g.alive;
                if (foes > 0) { g.engaged = true; g.enemy = foe / foes; g.lastContact = Time.time; }
                else if (Time.time - g.lastContact < 8f) g.engaged = true;   // still under fire a moment after losing sight
            }
        }

        float Strength(Group g) => g.total > 0 ? (float)g.alive / g.total : 0f;

        void Say(Group g, string order)
        {
            if (g != null) GroupOrder[g.id] = order;
            string line = $"{System.DateTime.Now:HH:mm:ss}  {(g != null ? g.name + ": " : "")}{order}";
            Orders.Add(line); if (Orders.Count > 8) Orders.RemoveAt(0);
            Debug.Log($"Stan's Game - {Title}: {line}");
        }

        // ------------------------------------------------------------------ defending

        void Defend(BattlePlan.Commander c)
        {
            if (c == null) return;
            foreach (var g in groups.Values)
            {
                if (!g.live || g.alive == 0 || Advancing(g) || g.regrouping) continue;
                float s = Strength(g);

                // nearly wiped out: the survivors pull back to the nearest strong group
                if (g.engaged && !g.fellBack && s <= c.fallBackBelow)
                {
                    var to = Nearest(g, x => x.live && !x.fellBack && Strength(x) > 0.5f && !Advancing(x));
                    if (to != null)
                    {
                        Vector3 watch = g.enemy - to.centre;
                        int i = 0;
                        foreach (var ai in g.men)
                            if (Usable(ai) && ai.mode != SoldierAI.Mode.FollowPlayer && !DugIn(ai))
                                ai.Defend(to.centre + Spread(i++, 3f), watch, 5f);
                        g.fellBack = true;
                        Say(g, $"down to {g.alive} of {g.total} - fall back on {to.name}");
                        continue;
                    }
                }

                // losing men in a fight: send for help (once a minute at most)
                if (g.engaged && s <= c.reinforceBelow && Time.time - g.helpSentAt > 60f)
                {
                    var from = Nearest(g, x => x.live && !x.engaged && x.alive > 1 && x != g && !Advancing(x));
                    if (from == null) continue;
                    int sent = 0;
                    Vector3 watch = g.enemy - g.centre;
                    foreach (var ai in from.men)
                    {
                        if (sent >= Mathf.Max(1, c.maxSend) || from.alive - sent <= 1) break;
                        if (!Usable(ai) || DugIn(ai) || ai.mode == SoldierAI.Mode.FollowPlayer) continue;
                        ai.Defend(g.centre + Spread(sent, 4f) - watch.normalized * 3f, watch, 6f);
                        sent++;
                    }
                    if (sent > 0)
                    {
                        g.helpSentAt = Time.time;
                        Say(g, $"under attack, {g.alive} of {g.total} left - {from.name} sends {sent} men");
                        GroupOrder[from.id] = $"sent {sent} men to {g.name}";
                    }
                }
            }
        }

        // ------------------------------------------------------------------ attacking

        void Attack(BattlePlan.Commander c)
        {
            if (c == null) return;
            foreach (var g in groups.Values)
            {
                if (!g.live || g.alive == 0) continue;
                float s = Strength(g);

                // too many lost: stop, get down and regroup until it goes quiet, then carry on
                if (!g.regrouping && g.engaged && s <= c.regroupBelow && Advancing(g))
                {
                    foreach (var ai in g.men) if (Usable(ai) && ai.mode == SoldierAI.Mode.Advance) Hold(ai, g);
                    g.regrouping = true;
                    Say(g, $"heavy losses ({g.alive} of {g.total}) - halt and regroup");
                    continue;
                }
                if (g.regrouping)
                {
                    if (Time.time - g.lastContact > 20f) { ResumeAll(g); g.regrouping = false; Say(g, "quiet again - move on"); }
                    continue;
                }

                // in contact on the move: bound, half covering while the other half moves up
                if (c.bounding && (Advancing(g) || AnyHeld(g)))
                {
                    if (g.engaged)
                    {
                        if (Time.time >= g.boundSwapAt)
                        {
                            g.boundSwapAt = Time.time + Mathf.Max(3f, c.boundSeconds);
                            g.firstHalfMoving = !g.firstHalfMoving;
                            int i = 0;
                            foreach (var ai in g.men)
                            {
                                if (!Usable(ai) || ai.mode == SoldierAI.Mode.FollowPlayer) continue;
                                bool firstHalf = (i++ % 2) == 0;
                                if (firstHalf == g.firstHalfMoving) Resume(ai, g); else Hold(ai, g);
                            }
                            GroupOrder[g.id] = $"bounding: {(g.firstHalfMoving ? "first" : "second")} half moves, the others cover";
                        }
                    }
                    else if (AnyHeld(g)) { ResumeAll(g); Say(g, "contact broken - all move up"); }
                }
            }
        }

        bool Advancing(Group g) { foreach (var ai in g.men) if (Usable(ai) && ai.mode == SoldierAI.Mode.Advance) return true; return false; }
        bool AnyHeld(Group g) { foreach (var ai in g.men) if (ai && heldFrom.ContainsKey(ai)) return true; return false; }

        void Hold(SoldierAI ai, Group g)
        {
            if (heldFrom.ContainsKey(ai) || ai.mode != SoldierAI.Mode.Advance) return;
            heldFrom[ai] = ai.advanceTarget;
            Vector3 watch = g.engaged ? g.enemy - ai.transform.position : ai.transform.forward;
            ai.Defend(ai.transform.position, watch, 2f);   // get down where he is and give covering fire
        }

        void Resume(SoldierAI ai, Group g)
        {
            if (!heldFrom.TryGetValue(ai, out var target)) return;
            heldFrom.Remove(ai);
            // the mission may have changed the objective meanwhile: take the group's latest one
            if (ai.mode != SoldierAI.Mode.HoldPosition || ai.advanceTarget) return;   // re-ordered by the mission: leave him be
            ai.mode = SoldierAI.Mode.Advance; ai.advanceTarget = g.objective ? g.objective : target; ai.startAlert = true;
        }

        void ResumeAll(Group g) { foreach (var ai in g.men) if (ai) Resume(ai, g); }

        Group Nearest(Group from, System.Func<Group, bool> ok)
        {
            Group best = null; float bd = float.MaxValue;
            foreach (var x in groups.Values)
            {
                if (x == from || x.alive == 0 || !ok(x)) continue;
                float d = (x.centre - from.centre).sqrMagnitude;
                if (d < bd) { bd = d; best = x; }
            }
            return best;
        }

        static Vector3 Spread(int i, float r)
        {
            float a = i * 2.39996f;   // the golden angle: men spread round the spot, not on top of each other
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r * (0.6f + 0.2f * (i % 3));
        }
    }
}
