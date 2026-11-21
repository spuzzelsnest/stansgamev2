#if STANS_NET
using Unity.Netcode;
using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Sainte-Mère-Église co-op: mirrors the host's shared mission step (phase from the rally on, counter-attack waves,
    /// the clock, the alarm) to the clients, and carries "a player reached the orchard / the town hall" from a client to the host.
    /// </summary>
    [RequireComponent(typeof(SainteMereMission))]
    public class SmeMissionNet : NetworkBehaviour
    {
        readonly NetworkVariable<int> phase = new NetworkVariable<int>(2);
        readonly NetworkVariable<int> waves = new NetworkVariable<int>(0);
        readonly NetworkVariable<float> minutes = new NetworkVariable<float>(0f);
        readonly NetworkVariable<bool> alarm = new NetworkVariable<bool>();   // for the scenery: windows, the dog

        SainteMereMission m;
        float clockTimer;

        void Awake() => m = GetComponent<SainteMereMission>();

        public override void OnNetworkSpawn()
        {
            if (!IsServer) SainteMereMission.ReachedOnHost = what => ReachedRpc(what);
        }

        public override void OnNetworkDespawn() => SainteMereMission.ReachedOnHost = null;

        [Rpc(SendTo.Server, RequireOwnership = false)]
        void ReachedRpc(int what) { if (m) m.ServerReached(what); }

        void Update()
        {
            if (!IsSpawned || !m) return;
            if (IsServer)
            {
                phase.Value = m.SharedPhase;
                waves.Value = m.SharedWaves;
                alarm.Value = SoldierAI.Alarm;
                clockTimer -= Time.deltaTime;
                if (clockTimer <= 0f) { clockTimer = 1f; minutes.Value = m.Minutes; }
            }
            else
            {
                m.ApplyShared(phase.Value, waves.Value);
                SoldierAI.SetFromHost(true, alarm.Value);   // (the town is awake from the start: GlidersDown is set at Start)
                if (Mathf.Abs(m.Minutes - minutes.Value) > 3f) m.Minutes = minutes.Value;
            }
        }
    }
}
#endif
