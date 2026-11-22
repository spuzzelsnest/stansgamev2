#if STANS_NET
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace StansGame
{
    /// <summary>Mirrors the host's MissionDirector state (phase, clock, gliders, groups, messages) to the clients.</summary>
    [RequireComponent(typeof(MissionDirector))]
    public class MissionNet : NetworkBehaviour
    {
        readonly NetworkVariable<int> phase = new NetworkVariable<int>();
        readonly NetworkVariable<int> landed = new NetworkVariable<int>();
        readonly NetworkVariable<float> minutes = new NetworkVariable<float>();
        readonly NetworkVariable<bool> tanks = new NetworkVariable<bool>();
        readonly NetworkVariable<bool> paras = new NetworkVariable<bool>();
        readonly NetworkVariable<int> serial = new NetworkVariable<int>();
        readonly NetworkVariable<int> waves = new NetworkVariable<int>();
        readonly NetworkVariable<bool> snipers = new NetworkVariable<bool>();
        readonly NetworkVariable<int> signals = new NetworkVariable<int>();
        readonly NetworkVariable<float> seconds = new NetworkVariable<float>();
        readonly NetworkVariable<bool> bridgeUp = new NetworkVariable<bool>();
        readonly NetworkVariable<bool> glidersDown = new NetworkVariable<bool>();   // the night's state, for the scenery
        readonly NetworkVariable<bool> alarm = new NetworkVariable<bool>();         // (lorries stop, windows light, dog barks)
        readonly NetworkVariable<FixedString512Bytes> message = new NetworkVariable<FixedString512Bytes>();

        MissionDirector md;
        int seen;
        float clockTimer;

        void Awake() => md = GetComponent<MissionDirector>();

        public override void OnNetworkSpawn()
        {
            seen = serial.Value;
            if (!IsServer) { NetHooks.RequestSignal = () => SignalRpc(); NetHooks.RequestBridge = up => BridgeRpc(up); }
        }

        public override void OnNetworkDespawn() { NetHooks.RequestSignal = null; NetHooks.RequestBridge = null; }

        /// <summary>A client's player worked the bridge controller: the host decides (it won't lift with men on it).</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void BridgeRpc(bool up)
        {
            var lift = BridgeLift.Instance;
            if (lift && !lift.Request(up, out string why) && md) md.Say(why, 4f);
        }

        /// <summary>A client's player sent "Ham and Jam": count it on the host.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void SignalRpc() { if (md) md.ReportSignal(); }

        void Update()
        {
            if (!IsSpawned || !md) return;
            if (IsServer)
            {
                phase.Value = md.CurrentPhase;
                landed.Value = md.LandedMask;
                tanks.Value = md.TanksLive;
                paras.Value = md.ParasLive;
                waves.Value = md.WavesMask;
                snipers.Value = md.SnipersLive;
                signals.Value = md.SignalsSent;
                if (BridgeLift.Instance) bridgeUp.Value = BridgeLift.Instance.Raised;
                glidersDown.Value = SoldierAI.GlidersDown;
                alarm.Value = SoldierAI.Alarm;
                clockTimer -= Time.deltaTime;
                if (clockTimer <= 0f) { clockTimer = 0.5f; minutes.Value = md.Minutes; }
                if (serial.Value != md.MessageSerial)
                {
                    string m = md.Message ?? "";
                    if (m.Length > 160) m = m.Substring(0, 160);
                    message.Value = new FixedString512Bytes(m);
                    seconds.Value = md.MessageSeconds;
                    serial.Value = md.MessageSerial;
                }
            }
            else
            {
                md.CurrentPhase = phase.Value;
                md.LandedMask = landed.Value;
                md.TanksLive = tanks.Value;
                md.ParasLive = paras.Value;
                md.WavesMask = waves.Value;
                md.SnipersLive = snipers.Value;
                md.SignalsSent = signals.Value;
                if (BridgeLift.Instance) BridgeLift.Instance.SetTarget(bridgeUp.Value);
                SoldierAI.SetFromHost(glidersDown.Value, alarm.Value);
                md.Minutes = minutes.Value;
                if (serial.Value != seen)
                {
                    seen = serial.Value;
                    md.ShowLocal(message.Value.ToString(), seconds.Value);
                }
            }
        }
    }
}
#endif
