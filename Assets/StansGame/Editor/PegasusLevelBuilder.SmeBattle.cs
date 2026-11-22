using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Sainte-Mère-Église's battle sheet (Battles/SainteMereEglise.asset): the German garrison defending, the 82nd
    /// Airborne attacking. The first build writes the sheet from the defaults below (exactly what the level had in
    /// code before); after that the builder reads the sheet, so changes made to it in the Inspector are kept. To go
    /// back to the defaults: Stan's Game > Battles > Reset Sainte-Mère-Église battle sheet.
    /// Not on the sheet yet: the flak battery's crew (they are part of the gun, PegasusLevelBuilder.SainteMere.cs).
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        const string BattleDir = Root + "/Battles";
        const string SmeBattlePath = BattleDir + "/SainteMereEglise.asset";
        static BattlePlan smePlan;

        [MenuItem("Stan's Game/Battles/Reset Sainte-Mère-Église battle sheet", priority = 60)]
        static void ResetSmeBattlePlan()
        {
            if (!EditorUtility.DisplayDialog("Reset battle sheet", "Put Sainte-Mère-Église's battle sheet back to the defaults? Changes made to it are lost. (Rebuild the level afterwards.)", "Reset", "Cancel")) return;
            AssetDatabase.DeleteAsset(SmeBattlePath);
            SmeBattlePlan();
            Selection.activeObject = smePlan;
        }

        [MenuItem("Stan's Game/Battles/Open Sainte-Mère-Église battle sheet", priority = 61)]
        static void OpenSmeBattlePlan()
        {
            var plan = AssetDatabase.LoadAssetAtPath<BattlePlan>(SmeBattlePath);
            if (!plan) { Debug.Log("Stan's Game: no battle sheet yet - build Sainte-Mère-Église once (or Reset) to write it"); return; }
            Selection.activeObject = plan; EditorGUIUtility.PingObject(plan);
        }

        /// <summary>The sheet: read if it is there, else written from the defaults.</summary>
        static BattlePlan SmeBattlePlan()
        {
            smePlan = AssetDatabase.LoadAssetAtPath<BattlePlan>(SmeBattlePath);
            if (smePlan) { if (SmeCommanders(smePlan)) { EditorUtility.SetDirty(smePlan); AssetDatabase.SaveAssets(); } return smePlan; }
            if (!AssetDatabase.IsValidFolder(BattleDir)) AssetDatabase.CreateFolder(Root, "Battles");
            smePlan = SmeDefaultPlan();
            AssetDatabase.CreateAsset(smePlan, SmeBattlePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"Stan's Game: wrote the battle sheet {SmeBattlePath} ({smePlan.defender.Men} defending, {smePlan.attacker.Men} attacking)");
            return smePlan;
        }

        static BattlePlan.Unit U(string name, Vector3 p, float yaw, string kind = "") => new BattlePlan.Unit { name = name, position = p, yaw = yaw, kind = kind };

        static BattlePlan SmeDefaultPlan()
        {
            var plan = ScriptableObject.CreateInstance<BattlePlan>();
            plan.level = "SainteMereEglise";
            plan.notes = "Night of 5/6 June 1944. The 505th PIR drops on and around the town; the German garrison is out in the square with the townspeople fighting the fire at the Pommier house. By 04:00 the 3rd Battalion (Krause) holds the town; the Germans counter-attack from Carentan (south) and Neuville (north) during the day.";

            // ---------------------------------------------------------------- defender: the German garrison
            var d = plan.defender;
            d.name = "German garrison"; d.faction = Faction.German;
            d.notes = "Flak battery crew (north-east field) is built with the gun and is not on this sheet yet.";

            d.groups.Add(new BattlePlan.Group
            {
                id = "night-garrison", name = "Night garrison (in the square)", arrival = BattlePlan.Arrival.AtStart,
                task = "Guard the bucket chain at the fire and the square; awake, the shooting has started.",
                mode = SoldierAI.Mode.HoldPosition, awake = true, skill = 0.32f, mgSkill = 0.35f, smokeChance = 0.35f,
                units = new List<BattlePlan.Unit>
                {
                    U("Sentry by the fire 1", new Vector3(-22f, 0f, -55f), 0f), U("Sentry by the fire 2", new Vector3(-40f, 0f, -54f), 30f),
                    U("Sentry by the pump", SmePump + new Vector3(4f, 0f, 4f), 200f), U("Sentry at the church door", new Vector3(-30f, 0f, 0f), 270f),
                    U("Sentry, square NE", new Vector3(25f, 0f, 14f), 180f), U("Sentry, square E", new Vector3(30f, 0f, -20f), 250f),
                    U("Sentry MG42 by the town hall", SmeTownHall + new Vector3(-12f, 0f, -4f), 270f), U("Sentry MP40 NCO", new Vector3(-10f, 0f, -40f), 0f),
                    U("Sentry, rue General de Gaulle", new Vector3(SmeN13X + 3f, 0f, -150f), 180f), U("Sentry, square W", new Vector3(-84f, 0f, -5f), 90f),
                    U("Leutnant MP40, town hall", SmeTownHall + new Vector3(-9f, 0f, 3f), 250f),
                },
            });

            // the two patrols: two men each, walking a beat in file
            Vector3 G(float x, float z) => OnGround(x, z);
            d.groups.Add(SmePatrolGroup("patrol-n13", "Patrol on the N13 (with the dog)",
                "Up the N13 from the town hall to the street north of the square, along it to the east end, and back.",
                new[] { G(SmeN13X + 3f, -80f), G(SmeN13X + 3f, 18f), G(-60f, 26f), G(20f, 26f), G(-60f, 26f), G(SmeN13X + 3f, 18f) },
                "Sentry patrol 1 (N13)", "Sentry patrol 2 (N13)"));
            d.groups.Add(SmePatrolGroup("patrol-church", "Patrol round the church",
                "Round the church, outside the churchyard wall.",
                new[] { G(-40f, -20f), G(28f, -20f), G(28f, 18f), G(-40f, 18f) },
                "Sentry patrol 3 (church)", "Sentry patrol 4 (church)"));

            d.groups.Add(new BattlePlan.Group
            {
                id = "defenders-04", name = "Defenders at 04:00", arrival = BattlePlan.Arrival.ByPhase, phase = "04:00 - the town",
                task = "Most of the garrison has pulled out; a few posts are still held round the square and the town hall.",
                mode = SoldierAI.Mode.HoldPosition, awake = true, skill = 0.32f, mgSkill = 0.35f, smokeChance = 0.35f,
                units = new List<BattlePlan.Unit>
                {
                    U("MG42 at the town hall", SmeTownHall + new Vector3(-12f, 0f, -4f), 270f), U("Rifleman at the town hall", SmeTownHall + new Vector3(-10f, 0f, 8f), 270f),
                    U("Rifleman behind the churchyard wall", new Vector3(-4f, 0f, -12.5f), 180f), U("MP40 by the church", new Vector3(20f, 0f, -12.5f), 180f),
                    U("Rifleman at the pump", SmePump + new Vector3(2f, 0f, -2f), 270f), U("Rifleman, rue General de Gaulle", new Vector3(SmeN13X + 3f, 0f, -140f), 180f),
                    U("Rifleman, square W", new Vector3(-84f, 0f, -15f), 90f),
                },
            });

            d.groups.Add(SmeCounterAttackGroup("counter-south", "Counter-attack from Carentan (south)", new Vector3(SmeN13X + 4f, 0f, -330f), 10));
            d.groups.Add(SmeCounterAttackGroup("counter-north", "Counter-attack from Neuville (north, N13)", new Vector3(SmeN13X - 4f, 0f, 340f), 8));

            // ---------------------------------------------------------------- attacker: the 82nd Airborne
            var a = plan.attacker;
            a.name = "505th PIR, 82nd Airborne"; a.faction = Faction.British;   // the game's "Allied" faction
            a.notes = "F Company's stick (the player's) jumps into the town; the 3rd Battalion gathers in the orchard west of it.";

            var stick = new BattlePlan.Group
            {
                id = "stick", name = "F Company, 2/505 - the player's stick", arrival = BattlePlan.Arrival.ByParachute,
                task = "Jump from the C-47 over the town. Position = where each man lands (the 'Player' entry is the player's place in the door).",
                mode = SoldierAI.Mode.HoldPosition, awake = true, skill = 0.5f, combatRoam = -1f,
            };
            var drops = new[]
            {
                new Vector3(-120f, 0f, 40f), new Vector3(-70f, 0f, -30f), new Vector3(-25f, 0f, 30f), Vector3.zero,
                new Vector3(5f, 0f, -26f), SmeFireHouse, new Vector3(20f, 0f, 28f), new Vector3(25f, 0f, -40f),
                new Vector3(100f, 0f, -90f), new Vector3(160f, 0f, 40f),
            };
            for (int i = 0; i < drops.Length; i++)
            {
                if (i == 3) { stick.units.Add(U("Player", drops[i], 0f, "Player")); continue; }
                stick.units.Add(U($"F Co. paratrooper {i + 1}", drops[i], 0f, i % 4 == 1 ? "Thompson" : i % 4 == 3 ? "Carbine" : "Rifleman"));
            }
            a.groups.Add(stick);

            var krause = new BattlePlan.Group
            {
                id = "krause", name = "3rd Battalion 505th PIR (Krause)", arrival = BattlePlan.Arrival.ByPhase, phase = "Rally in the orchard",
                task = "Gather in the orchard west of the town, then take it (the mission sends them to the square, the town hall and the Carentan road).",
                mode = SoldierAI.Mode.HoldPosition, awake = true, skill = 0.6f, combatRoam = 10f,
            };
            for (int i = 0; i < 12; i++)
                krause.units.Add(U(i == 0 ? "Lt. Col. Edward Krause" : $"3/505 paratrooper {i}",
                    SmeRally + new Vector3((i % 4) * 3f - 4.5f, 0f, (i / 4) * 3f + 4f), 90f, i % 4 == 1 ? "Thompson" : i % 4 == 3 ? "Carbine" : "Rifleman"));
            a.groups.Add(krause);
            SmeCommanders(plan);
            return plan;
        }

        /// <summary>Who commands each side (filled in where the sheet has no name yet; true if anything changed).</summary>
        static bool SmeCommanders(BattlePlan plan)
        {
            bool changed = false;
            var a = plan.attacker.commander;
            if (string.IsNullOrEmpty(a.name))
            {
                a.name = "Edward C. Krause"; a.rank = "Lieutenant Colonel"; a.unit = "3rd Battalion, 505th Parachute Infantry Regiment, 82nd Airborne";
                a.bio = "Commanded the 3rd Battalion of the 505th. Gathered his scattered men west of the town in the dark and took Sainte-Mère-Église before dawn on 6 June; the American flag went up at the town hall, making it one of the first towns freed in Normandy.";
                a.portrait = AssetDatabase.LoadAssetAtPath<Texture2D>(PortraitDir + "/Krause.png");
                a.bounding = true; a.regroupBelow = 0.4f;
                changed = true;
            }
            if (!a.portrait) { a.portrait = AssetDatabase.LoadAssetAtPath<Texture2D>(PortraitDir + "/Krause.png"); changed |= a.portrait != null; }
            var d = plan.defender.commander;
            if (!d.portrait) { d.portrait = AssetDatabase.LoadAssetAtPath<Texture2D>(PortraitDir + "/SmeGarrison.png"); changed |= d.portrait != null; }
            if (string.IsNullOrEmpty(d.unit))
            {
                d.name = ""; d.rank = ""; d.unit = "German garrison, Sainte-Mère-Église";
                d.bio = "(Name, portrait and story to come.)";
                d.reinforceBelow = 0.6f; d.fallBackBelow = 0.3f; d.maxSend = 3;
                changed = true;
            }
            return changed;
        }

        static BattlePlan.Group SmePatrolGroup(string id, string name, string task, Vector3[] route, string lead, string second)
        {
            Vector3 dir = route[1] - route[0]; dir.y = 0f; dir.Normalize();
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            var g = new BattlePlan.Group { id = id, name = name, task = task, arrival = BattlePlan.Arrival.AtStart, mode = SoldierAI.Mode.HoldPosition, awake = true, skill = 0.32f, mgSkill = 0.35f, smokeChance = 0.35f, combatRoam = 10f, route = route };
            for (int i = 0; i < 2; i++)
            {
                Vector3 p = route[0] - dir * (i * 3f) + Vector3.Cross(Vector3.up, dir) * (i * 0.8f);
                p.y = GroundAt(p);
                g.units.Add(U(i == 0 ? lead : second, p, yaw));
            }
            return g;
        }

        static BattlePlan.Group SmeCounterAttackGroup(string id, string name, Vector3 from, int count)
        {
            var g = new BattlePlan.Group
            {
                id = id, name = name, arrival = BattlePlan.Arrival.ByPhase, phase = "Counter-attacks (day)",
                task = "Advance up the N13 on the church square.", objective = "Objective: church square",
                mode = SoldierAI.Mode.Advance, awake = true, skill = 0.32f, mgSkill = 0.35f, smokeChance = 0.35f, combatRoam = 8f,
            };
            for (int i = 0; i < count; i++)
            {
                var p = from + new Vector3((i % 3 - 1) * 4f + Random.Range(-1f, 1f), 0f, (i / 3) * -5f * Mathf.Sign(from.z));
                string n = i == 0 ? "MG42 team" : i % 4 == 1 ? "MP40 NCO" : "Grenadier";
                g.units.Add(U($"{n} {i + 1}", p, from.z < 0f ? 0f : 180f));
            }
            return g;
        }

        // ---------------------------------------------------------------- putting the sheet in the scene

        static void TagUnit(GameObject go, BattlePlan plan, BattlePlan.Group g)
        {
            if (!go) return;
            bool att = plan.attacker.groups.Contains(g);
            var bu = go.GetComponent<BattleUnit>() ?? go.AddComponent<BattleUnit>();
            bu.side = att ? plan.attacker.name : plan.defender.name; bu.attacker = att;
            bu.group = g.id; bu.groupName = g.name; bu.task = g.task;
        }

        /// <summary>German men of one group (the garrison, the 04:00 posts, a counter-attack).</summary>
        static void SpawnGermans(BattlePlan plan, string id, Transform parent)
        {
            var g = plan.Find(id);
            if (g == null) { Debug.LogWarning($"Stan's Game: battle sheet has no group '{id}'"); return; }
            foreach (var u in g.units)
            {
                var h = SmeGerman(parent, u.name, u.position, u.yaw, g);
                if (h) TagUnit(h.gameObject, plan, g);
            }
        }

        /// <summary>A patrol: the men walk the group's route in file, 3 m apart. The first is returned (the dog's handler).</summary>
        static Health SpawnPatrol(BattlePlan plan, string id, Transform parent)
        {
            var g = plan.Find(id);
            if (g == null || g.route == null || g.route.Length < 2) { Debug.LogWarning($"Stan's Game: battle sheet patrol '{id}' is missing or has no route"); return null; }
            Vector3 dir = g.route[1] - g.route[0]; dir.y = 0f; dir.Normalize();
            Health first = null;
            for (int i = 0; i < g.units.Count; i++)
            {
                var u = g.units[i];
                var h = SmeGerman(parent, u.name, u.position, u.yaw, g);
                if (!h) continue;
                TagUnit(h.gameObject, plan, g);
                if (i == 0) first = h;
                var ai = h.GetComponent<SoldierAI>();
                if (!ai) continue;
                var r = new Vector3[g.route.Length];
                for (int k = 0; k < g.route.Length; k++) r[k] = g.route[k] - dir * (i * 3f);
                ai.patrolRoute = r; ai.patrolPause = 2.5f; ai.walkSpeed = 1.25f;
                ai.startAlert = false; ai.sleepsUntilAlarm = false;
                if (g.combatRoam >= 0f) ai.combatRoam = g.combatRoam;
            }
            return first;
        }

        /// <summary>American paratroopers standing (the 3rd Battalion in the orchard).</summary>
        static void SpawnAmericans(BattlePlan plan, string id, Transform parent, GameObject rifle, GameObject smg, GameObject carbine)
        {
            var g = plan.Find(id);
            if (g == null) { Debug.LogWarning($"Stan's Game: battle sheet has no group '{id}'"); return; }
            foreach (var u in g.units)
            {
                var prefab = UsPrefabFor(u.kind, rifle, smg, carbine);
                var s = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                s.name = u.name;
                s.transform.position = u.position;
                s.transform.rotation = Quaternion.Euler(0f, u.yaw, 0f);
                var ai = s.GetComponent<SoldierAI>();
                if (ai) { ai.mode = g.mode; ai.skill = g.skill; if (g.combatRoam >= 0f) ai.combatRoam = g.combatRoam; }
                TagUnit(s, plan, g);
            }
        }

        static GameObject UsPrefabFor(string kind, GameObject rifle, GameObject smg, GameObject carbine)
            => kind == "Thompson" ? smg : kind == "Carbine" ? carbine : rifle;
    }
}
