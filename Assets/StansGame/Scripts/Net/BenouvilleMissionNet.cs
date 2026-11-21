#if STANS_NET
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Château de Bénouville co-op: mirrors the host's mission (phase, counter-attack waves, MacDonald's platoon and the
    /// Warwicks, the clock, the messages) to the clients; BenouvilleMission.ApplyShared sets each client's world to match
    /// and plays the same films there.
    /// </summary>
    [RequireComponent(typeof(BenouvilleMission))]
    public class BenouvilleMissionNet : NetworkBehaviour
    {
        readonly NetworkVariable<int> phase = new NetworkVariable<int>();
        readonly NetworkVariable<int> waves = new NetworkVariable<int>();
        readonly NetworkVariable<bool> macdonald = new NetworkVariable<bool>();
        readonly NetworkVariable<bool> warwicks = new NetworkVariable<bool>();
        readonly NetworkVariable<float> minutes = new NetworkVariable<float>();
        readonly NetworkVariable<int> serial = new NetworkVariable<int>();
        readonly NetworkVariable<float> seconds = new NetworkVariable<float>();
        readonly NetworkVariable<FixedString512Bytes> message = new NetworkVariable<FixedString512Bytes>();

        BenouvilleMission m;
        float clockTimer;
        int seen;

        void Awake() => m = GetComponent<BenouvilleMission>();

        public override void OnNetworkSpawn() => seen = serial.Value;

        void Update()
        {
            if (!IsSpawned || !m) return;
            if (IsServer)
            {
                phase.Value = m.CurrentPhase;
                waves.Value = m.WavesLaunched;
                macdonald.Value = m.MacdonaldIn;
                warwicks.Value = m.WarwicksIn;
                clockTimer -= Time.deltaTime;
                if (clockTimer <= 0f) { clockTimer = 0.5f; minutes.Value = m.Minutes; }
                if (serial.Value != m.MessageSerial)
                {
                    string s = m.LastMessage ?? "";
                    if (s.Length > 160) s = s.Substring(0, 160);
                    message.Value = new FixedString512Bytes(s);
                    seconds.Value = m.LastMessageSeconds;
                    serial.Value = m.MessageSerial;
                }
            }
            else
            {
                m.ApplyShared(phase.Value, waves.Value, macdonald.Value, warwicks.Value);
                if (Mathf.Abs(m.Minutes - minutes.Value) > 2f) m.Minutes = minutes.Value;
                if (serial.Value != seen) { seen = serial.Value; m.ShowShared(message.Value.ToString(), seconds.Value); }
            }
        }
    }
}
#endif
