using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
#if STANS_NET
using Unity.Netcode;
#endif

namespace StansGame.EditorTools
{
    /// <summary>
    /// Sainte-Mère-Église co-op: network identities for every soldier the host runs (garrison, the 04:00 defenders,
    /// Krause's battalion, both counter-attacks), the mission's network object (SmeMissionNet + NetCombat) and the
    /// US paratrooper every player gets (Prefabs/NetPlayer_US.prefab, registered by the Pegasus Bridge level's NetSession:
    /// build Sainte-Mère-Église first, then Pegasus Bridge).
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        const string UsNetPlayerPath = PrefabDir + "/NetPlayer_US.prefab";

        static void SmeNetworking(SainteMereMission mission, Shell mk2, GameObject canopy, params Transform[] soldierGroups)
        {
#if STANS_NET
            int n = 0;
            foreach (var g in soldierGroups)
            {
                if (!g) continue;
                foreach (var ai in g.GetComponentsInChildren<SoldierAI>(true))
                {
                    var go = ai.gameObject;
                    var outer = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
                    if (outer) PrefabUtility.UnpackPrefabInstance(outer, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    AddNet(go); n++;
                }
            }
            mission.gameObject.AddComponent<NetworkObject>().AutoObjectParentSync = false;
            mission.gameObject.AddComponent<SmeMissionNet>();
            var combat = mission.gameObject.AddComponent<NetCombat>();
            var list = new List<Shell>();
            if (mk2) list.Add(mk2);
            if (stickGrenade) list.Add(stickGrenade);
            foreach (var sm in new[] { smokeNo77, smokeNb39, smokeM8 }) if (sm) list.Add(sm);
            combat.projectilePrefabs = list.ToArray();
            BuildUsNetPlayerPrefab(mk2, canopy);
            Debug.Log($"Stan's Game: Sainte-Mère-Église co-op - {n} networked soldiers");
#endif
        }

#if STANS_NET
        /// <summary>The co-op soldier for Sainte-Mère-Église: the single-player US paratrooper with a T-5, driven by its owner.</summary>
        static GameObject BuildUsNetPlayerPrefab(Shell mk2, GameObject canopy)
        {
            var tmp = new GameObject("tmp");
            var pc = BuildUsPlayer(tmp.transform, mk2);
            var go = pc.gameObject;
            go.transform.SetParent(null);
            go.name = "NetPlayer_US";
            go.transform.position = Vector3.zero;
            pc.cam = null; pc.enabled = false;
            var chute = go.AddComponent<ParachuteDrop>();
            chute.canopyPrefab = canopy;
            go.AddComponent<NetworkObject>().AutoObjectParentSync = false;
            var nt = go.AddComponent<OwnerNetworkTransform>();
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            go.AddComponent<NetEntity>();
            go.AddComponent<NetPlayer>();
            go.AddComponent<NetChute>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, UsNetPlayerPath);
            Object.DestroyImmediate(go); Object.DestroyImmediate(tmp);
            return prefab;
        }
#endif
    }
}
