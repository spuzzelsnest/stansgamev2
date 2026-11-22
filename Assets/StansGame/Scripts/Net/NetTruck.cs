#if STANS_NET
using Unity.Netcode;
using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Multiplayer side of a drivable lorry (TruckController). The host owns the seat and moves the lorry; a client
    /// who wants to drive asks for the seat, then sends his keys about 30 times a second. Everyone gets who is
    /// driving, whether the players have taken the lorry over from its ambient route, and the front wheels' angle.
    /// If the driver leaves the game, the seat is freed and the lorry stops.
    /// </summary>
    public class NetTruck : NetworkBehaviour, ITruckLink
    {
        const ulong NoDriver = ulong.MaxValue;
        readonly NetworkVariable<ulong> driver = new NetworkVariable<ulong>(NoDriver);
        readonly NetworkVariable<bool> taken = new NetworkVariable<bool>(false);
        readonly NetworkVariable<float> steer = new NetworkVariable<float>(0f);
        TruckController truck;

        public override void OnNetworkSpawn()
        {
            truck = GetComponent<TruckController>();
            if (truck) truck.link = this;
        }

        public override void OnNetworkDespawn()
        {
            if (truck && ReferenceEquals(truck.link, this)) truck.link = null;
        }

        public bool Occupied => IsSpawned && driver.Value != NoDriver;
        public bool LocalIsDriver => IsSpawned && driver.Value == NetworkManager.LocalClientId;
        public bool TakenOver => IsSpawned && taken.Value;
        public float Steer => steer.Value;

        public bool ServerClaim(ulong clientId)
        {
            if (!IsServer || driver.Value != NoDriver) return false;
            driver.Value = clientId;
            taken.Value = true;
            if (truck) truck.ServerTakeOver();
            return true;
        }

        public void ServerRelease()
        {
            if (!IsServer) return;
            driver.Value = NoDriver;
            if (truck) truck.ServerRemoteInput(0f, 0f, true);
        }

        public void RequestEnter() => EnterRpc();
        public void RequestExit() => ExitRpc();
        public void SendInput(float throttle, float steerIn, bool handbrake) => InputRpc(throttle, steerIn, handbrake);
        public void SendHorn() => HornRpc();

        /// <summary>The driver pressed H: the host checks it is him and has every machine sound the horn.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void HornRpc(RpcParams rpcParams = default)
        {
            if (driver.Value == rpcParams.Receive.SenderClientId) HornAllRpc();
        }

        [Rpc(SendTo.Everyone)]
        void HornAllRpc() { if (truck) truck.PlayHorn(); }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void EnterRpc(RpcParams rpcParams = default) => ServerClaim(rpcParams.Receive.SenderClientId);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void ExitRpc(RpcParams rpcParams = default)
        {
            if (driver.Value == rpcParams.Receive.SenderClientId) ServerRelease();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone, Delivery = RpcDelivery.Unreliable)]
        void InputRpc(float throttle, float steerIn, bool handbrake, RpcParams rpcParams = default)
        {
            if (driver.Value == rpcParams.Receive.SenderClientId && truck) truck.ServerRemoteInput(throttle, steerIn, handbrake);
        }

        void Update()
        {
            if (!IsSpawned || !IsServer || !truck) return;
            if (Mathf.Abs(steer.Value - truck.SteerAngle) > 0.4f) steer.Value = truck.SteerAngle;
            // the driver left the game: free the seat (the host's own seat is freed by the lorry when he gets out)
            ulong d = driver.Value;
            if (d != NoDriver && d != NetworkManager.LocalClientId && !NetworkManager.ConnectedClients.ContainsKey(d)) ServerRelease();
        }
    }
}
#endif
