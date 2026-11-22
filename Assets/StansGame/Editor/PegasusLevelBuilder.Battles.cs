using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Battle sheets for the levels whose men are placed in code round their works (Pegasus Bridge: the pillbox,
    /// the AT gun pit, the trenches; Château de Bénouville). The builder still places them, then "adopts" them:
    ///   first build:  the sheet is written from what was built (each group = the men under one parent object);
    ///   later builds: the sheet is applied back - each man's position and facing, and his group's skill - so
    ///                 changes made on the sheet in the Inspector are kept. New men in code are added to the sheet;
    ///                 men on the sheet that the code no longer builds are reported.
    /// Every man gets his BattleUnit tag and the two commanders are put in the level.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        const string PortraitDir = Root + "/Resources/Commanders";

        static BattlePlan AdoptBattle(string level, Transform root, Faction attackerFaction, System.Action<BattlePlan> commanders)
        {
            if (!AssetDatabase.IsValidFolder(BattleDir)) AssetDatabase.CreateFolder(Root, "Battles");
            string path = $"{BattleDir}/{level}.asset";
            var plan = AssetDatabase.LoadAssetAtPath<BattlePlan>(path);
            bool fresh = !plan;
            if (fresh)
            {
                plan = ScriptableObject.CreateInstance<BattlePlan>();
                plan.level = level;
                plan.attacker.faction = attackerFaction;
                plan.defender.faction = attackerFaction == Faction.German ? Faction.British : Faction.German;
                plan.notes = "Written from the level as the builder placed it. Positions, facing and skill set here are applied on every rebuild.";
            }

            // the men, in the order the builder made them, grouped by the object they sit under
            var men = new List<SoldierAI>();
            foreach (var ai in root.GetComponentsInChildren<SoldierAI>(true)) if (!ai.GetComponent<BattleUnit>()) men.Add(ai);
            var seen = new Dictionary<string, int>();   // group|name -> how many so far (men can share a name)
            int applied = 0, added = 0;
            var used = new HashSet<BattlePlan.Unit>();

            foreach (var ai in men)
            {
                var h = ai.GetComponent<Health>();
                bool att = h && h.faction == attackerFaction;
                var side = att ? plan.attacker : plan.defender;
                var parent = ai.transform.parent ? ai.transform.parent : root;
                string gid = GroupId(parent, root);
                var g = side.groups.Find(x => x.id == gid);
                if (g == null)
                {
                    g = new BattlePlan.Group
                    {
                        id = gid, name = parent.name, task = "",
                        arrival = ai.gameObject.activeInHierarchy ? BattlePlan.Arrival.AtStart : BattlePlan.Arrival.ByPhase,
                        phase = ai.gameObject.activeInHierarchy ? "" : "(brought in by the mission)",
                        mode = ai.mode, awake = ai.startAlert || !ai.sleepsUntilAlarm, skill = -1f, mgSkill = -1f, smokeChance = 0f, combatRoam = -1f,
                        route = ai.patrolRoute != null && ai.patrolRoute.Length > 1 ? (Vector3[])ai.patrolRoute.Clone() : null,
                    };
                    side.groups.Add(g);
                }
                bool mg = ai.name.Contains("MG42");
                if (string.IsNullOrEmpty(g.objective) && ai.advanceTarget) { g.objective = ai.advanceTarget.name; EditorUtility.SetDirty(plan); }
                if (mg && g.mgSkill < 0f) g.mgSkill = ai.skill;
                if (!mg && g.skill < 0f) g.skill = ai.skill;

                string key = gid + "|" + ai.name;
                seen.TryGetValue(key, out int nth); seen[key] = nth + 1;
                var u = NthUnit(g, ai.name, nth);
                if (u == null)
                {
                    g.units.Add(new BattlePlan.Unit { name = ai.name, kind = mg ? "MG42" : ai.name.Contains("MP40") ? "MP40" : "", position = ai.transform.position, yaw = ai.transform.eulerAngles.y });
                    if (!fresh) added++;
                }
                else
                {
                    used.Add(u);
                    if ((ai.transform.position - u.position).sqrMagnitude > 0.0001f || Mathf.Abs(Mathf.DeltaAngle(ai.transform.eulerAngles.y, u.yaw)) > 0.5f)
                    {
                        ai.transform.SetPositionAndRotation(u.position, Quaternion.Euler(0f, u.yaw, 0f));
                        applied++;
                    }
                    float s = mg ? g.mgSkill : g.skill;
                    if (s >= 0f) ai.skill = s;
                }
                TagUnit(ai.gameObject, plan, g);
            }

            if (!fresh)
                foreach (var (_, g) in plan.Groups())
                    foreach (var u in g.units)
                        if (!used.Contains(u))
                        {
                            bool stillBuilt = false;
                            foreach (var ai in men) if (ai.name == u.name) { stillBuilt = true; break; }
                            if (!stillBuilt) Debug.LogWarning($"Stan's Game: battle sheet {level}: '{u.name}' ({g.name}) is on the sheet but the level no longer builds him");
                        }

            bool cmdChanged = FillCommanders(plan, commanders);
            if (fresh) { AssetDatabase.CreateAsset(plan, path); }
            else if (added > 0 || cmdChanged) EditorUtility.SetDirty(plan);
            AssetDatabase.SaveAssets();

            foreach (bool att in new[] { false, true })
            {
                var bc = new GameObject(att ? "Commander (attacker)" : "Commander (defender)").AddComponent<BattleCommander>();
                bc.transform.SetParent(root); bc.plan = plan; bc.attacker = att;
            }
            Debug.Log($"Stan's Game: battle sheet {path} - {plan.defender.Men} defending, {plan.attacker.Men} attacking" +
                      (fresh ? " (written from the level)" : $"; {applied} men moved as on the sheet, {added} new men added"));
            return plan;
        }

        static string GroupId(Transform parent, Transform root)
        {
            string s = parent.name;
            for (var p = parent.parent; p && p != root; p = p.parent) s = p.name + "/" + s;
            return s.ToLowerInvariant().Replace(' ', '-');
        }

        static BattlePlan.Unit NthUnit(BattlePlan.Group g, string name, int nth)
        {
            foreach (var u in g.units) if (u.name == name && nth-- == 0) return u;
            return null;
        }

        /// <summary>Fills in who commands each side where the sheet has no name yet (true if anything changed).</summary>
        static bool FillCommanders(BattlePlan plan, System.Action<BattlePlan> commanders)
        {
            string before = plan.attacker.commander.name + "|" + plan.defender.commander.name + "|" + (plan.attacker.commander.portrait ? 1 : 0) + (plan.defender.commander.portrait ? 1 : 0);
            commanders?.Invoke(plan);
            string after = plan.attacker.commander.name + "|" + plan.defender.commander.name + "|" + (plan.attacker.commander.portrait ? 1 : 0) + (plan.defender.commander.portrait ? 1 : 0);
            return before != after;
        }

        static void Commander(BattlePlan.Commander c, string name, string rank, string unit, string portrait, string bio)
        {
            if (string.IsNullOrEmpty(c.name) && string.IsNullOrEmpty(c.unit)) { c.name = name; c.rank = rank; c.unit = unit; c.bio = bio; }
            if (!c.portrait) c.portrait = AssetDatabase.LoadAssetAtPath<Texture2D>($"{PortraitDir}/{portrait}.png");
        }

        static void PegasusCommanders(BattlePlan plan)
        {
            plan.attacker.name = string.IsNullOrEmpty(plan.attacker.name) || plan.attacker.name == "Attacker" ? "D Company, 2nd Ox and Bucks" : plan.attacker.name;
            plan.defender.name = string.IsNullOrEmpty(plan.defender.name) || plan.defender.name == "Defender" ? "Bridge garrison, 736th Grenadier Regiment" : plan.defender.name;
            Commander(plan.attacker.commander, "John Howard", "Major", "D Company, 2nd Oxfordshire and Buckinghamshire Light Infantry (glider-borne)", "Howard",
                "Led the six Horsa gliders of D Company, with sappers of the Royal Engineers, onto the bridges over the Caen Canal and the Orne just after midnight on 6 June, and held them until relieved.");
            Commander(plan.defender.commander, "Hans Schmidt", "Major", "Garrison of the Bénouville and Ranville bridges, 736th Grenadier Regiment, 716th Infantry Division", "Schmidt",
                "Commanded the understrength company guarding the two bridges. He was away in Ranville when the gliders landed, and drove to the bridge afterwards, where he was wounded and taken prisoner.");
        }

        static void BenouvilleCommanders(BattlePlan plan)
        {
            plan.attacker.name = string.IsNullOrEmpty(plan.attacker.name) || plan.attacker.name == "Attacker" ? "Kampfgruppe Rauch, 21st Panzer Division" : plan.attacker.name;
            plan.defender.name = string.IsNullOrEmpty(plan.defender.name) || plan.defender.name == "Defender" ? "A Company, 7th Parachute Battalion" : plan.defender.name;
            Commander(plan.attacker.commander, "Josef Rauch", "Oberst", "Panzergrenadier-Regiment 192 (Kampfgruppe Rauch), 21st Panzer Division", "Rauch",
                "His regiment's second battalion was sent against Bénouville in the night of 5/6 June; through the day his men attacked A Company again and again, mostly from the west and north-west.");
            Commander(plan.defender.commander, "Nigel Taylor", "Major", "A Company, 7th Parachute Battalion, 5th Parachute Brigade", "Taylor",
                "Held the south and west of Bénouville for about 17 hours, cut off, against attack after attack. Badly wounded in the leg early on, he went on directing the fight from a stretcher.");
        }
    }
}
