using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// The relief of the bridges, 6 June 1944 (Pegasus Bridge rework, level template 1 and 4b): No. 1 Special Service
    /// Brigade, landed on Sword Beach, came down through Bénouville and over the canal bridge to the 6th Airborne east of
    /// the Orne - Brigadier Lord Lovat at the head, his piper Bill Millin playing them across (Lovat had told him to,
    /// against the rule that pipers no longer played in battle). The commandos wear the green beret; Millin his kilt
    /// and pipes, and no weapon but his sgian-dubh.
    /// Bodies sketched in Blender from Joan's British_Infantry (Models/Source~/commando_variant.py):
    /// Models/Characters/British_Commando and British_Piper. Out of play until the last phase (MissionDirector.commandos).
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        static GameObject BuildCommandos(Transform parent)
        {
            var g = Group("No. 1 Special Service Brigade (Lord Lovat, Piper Bill Millin) - the relief", parent);
            GameObject Fresh(string n, System.Func<GameObject> make) { AssetDatabase.DeleteAsset($"{PrefabDir}/{n}.prefab"); return make(); }
            var green = new Color(0.40f, 0.37f, 0.26f);
            var rifle = Fresh("CommandoRifleman", () => BuildSoldierPrefab("CommandoRifleman", Faction.British, green, "Lee-Enfield", 70f, 30f, false, 10, false, "British_Commando"));
            var tommy = Fresh("CommandoThompson", () => BuildSoldierPrefab("CommandoThompson", Faction.British, green, "Thompson M1A1", 34f, 700f, true, 30, false, "British_Commando"));
            var piper = Fresh("CommandoPiper", () => BuildSoldierPrefab("CommandoPiper", Faction.British, green, "M1911", 20f, 120f, false, 7, false, "British_Piper"));
            if (!rifle) return g.gameObject;
            // in a file down the road from Bénouville to the bridge, Lovat and the piper in front
            Vector3 from = BenouvilleCrossroads + new Vector3(30f, 0f, -2f), to = new Vector3(CanalHalfWidth + 70f, 0f, 2f);
            Vector3 dir = (to - from); dir.y = 0f; dir.Normalize();
            float yaw = Quaternion.LookRotation(dir).eulerAngles.y;
            var side = Vector3.Cross(Vector3.up, dir);
            void Man(GameObject prefab, string name, int i, bool armedLook = true)
            {
                if (!prefab) prefab = rifle;
                var s = (GameObject)PrefabUtility.InstantiatePrefab(prefab, g);
                s.name = name;
                s.transform.SetPositionAndRotation(from - dir * (i * 2.4f) + side * (i % 2 == 0 ? -1.2f : 1.2f) + Vector3.up * 0.2f, Quaternion.Euler(0f, yaw, 0f));
                var ai = s.GetComponent<SoldierAI>();
                if (ai)
                {
                    ai.mode = SoldierAI.Mode.Advance; ai.startAlert = true; ai.sleepsUntilAlarm = false;
                    var target = new GameObject($"{name} - over the bridge").transform; target.SetParent(g);
                    target.position = to - dir * (i * 2.4f) + side * (i % 2 == 0 ? -1.2f : 1.2f);
                    ai.advanceTarget = target;
                }
                if (!armedLook)   // the piper: no rifle to be seen
                    foreach (var w in s.GetComponentsInChildren<Weapon>(true)) foreach (var r in w.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            }
            Man(rifle, "Brigadier Lord Lovat (No. 1 Special Service Brigade)", 0);
            Man(piper, "Piper Bill Millin", 1, false);
            var pipes = g.Find("Piper Bill Millin"); if (pipes) pipes.gameObject.AddComponent<PiperMusic>();
            for (int i = 2; i < 14; i++) Man(i % 3 == 0 ? tommy : rifle, $"Commando ({(i < 8 ? "No. 6" : "No. 3")} Commando)", i);
            return g.gameObject;
        }

        /// <summary>Objective markers for the phases of the Pegasus Bridge mission (MissionDirector.phases).</summary>
        static Transform[] PegasusPhaseTargets(Transform parent, Transform bridgeEnd, Health panzer)
        {
            var g = Group("Objective markers", parent);
            Transform At(string name, Vector3 p) { var t = new GameObject(name).transform; t.SetParent(g); t.position = p; return t; }
            var pillbox = At("Objective: the pillbox and trenches (east bank)", new Vector3(CanalHalfWidth + 12f, 0f, 14f));
            var bridge = At("Objective: the bridge", Vector3.zero);
            return new[]
            {
                pillbox,                                  // 0 coup de main
                bridgeEnd ? bridgeEnd : bridge,           // 1 over the bridge, the west bank by the café
                null,                                     // 2 Ham and Jam (anywhere: hold H)
                bridge,                                   // 3 hold the bridge
                panzer ? panzer.transform : bridge,       // 4 the lead vehicle
                bridge,                                   // 5 hold until relieved
                null,                                     // 6 bridge held
            };
        }
    }
}
