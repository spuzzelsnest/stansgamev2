#if STANS_NET
using Unity.Netcode;
using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// A player's soldier in multiplayer. The owner drives it (input, camera, HUD); on everyone else's screen
    /// it is a puppet moved by its OwnerNetworkTransform, carrying the weapon the owner has selected.
    /// The host respawns it a few seconds after it is killed.
    /// </summary>
    public class NetPlayer : NetworkBehaviour
    {
        readonly NetworkVariable<int> weapon = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        /// <summary>The owner is driving a lorry: hide the puppet (it rides along in the cab).</summary>
        readonly NetworkVariable<bool> inVehicle = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        SoldierController sc;

        public override void OnNetworkSpawn()
        {
            sc = GetComponent<SoldierController>();
            if (IsOwner)
            {
                name = "Player (you)";
                sc.cam = Camera.main;
                sc.enabled = true;
                if (MissionDirector.Instance) MissionDirector.Instance.SetLocalPlayer(sc);
                if (SainteMereMission.Instance) SainteMereMission.Instance.SetLocalPlayer(sc);   // into the C-47's door, or on the ground
                if (BenouvilleMission.Instance) BenouvilleMission.Instance.SetLocalPlayer(sc);
            }
            else
            {
                name = $"Player {OwnerClientId}";
                sc.enabled = false;
                var entry = GetComponent<VehicleEntry>();
                if (entry) entry.enabled = false;
                var chute = GetComponent<ParachuteDrop>();
                if (chute) chute.enabled = false;   // the owner flies it; NetChute shows the canopy here
                sc.SelectWeapon(weapon.Value);
                weapon.OnValueChanged += OnWeaponChanged;
                inVehicle.OnValueChanged += OnInVehicleChanged;
                if (inVehicle.Value) OnInVehicleChanged(false, true);
            }
            if (IsServer)
            {
                var h = GetComponent<Health>();
                if (h) h.Died += (hh, info) => { if (NetSession.Instance) NetSession.Instance.RespawnLater(OwnerClientId, NetworkObject); };
            }
        }

        public override void OnNetworkDespawn() { weapon.OnValueChanged -= OnWeaponChanged; inVehicle.OnValueChanged -= OnInVehicleChanged; }

        readonly System.Collections.Generic.List<Renderer> hidden = new System.Collections.Generic.List<Renderer>();

        void OnInVehicleChanged(bool before, bool now)
        {
            var hp = GetComponent<Health>();
            if (hp) hp.Sheltered = now;   // the host decides the hits: a client's driver is safe in the cab there too
            if (now)
            {
                hidden.Clear();   // in the cab he stays in sight (seen through the windscreen); only his rifle is put away
                foreach (var r in GetComponentsInChildren<Renderer>()) if (r.enabled && r.GetComponentInParent<Weapon>()) { r.enabled = false; hidden.Add(r); }
            }
            else
            {
                foreach (var r in hidden) if (r) r.enabled = true;
                hidden.Clear();
            }
        }

        void OnWeaponChanged(int before, int now) { if (sc) sc.SelectWeapon(now); }

        void Update()
        {
            if (IsSpawned && IsOwner && sc && weapon.Value != sc.WeaponIndex) weapon.Value = sc.WeaponIndex;
            if (IsSpawned && IsOwner && sc && inVehicle.Value != sc.InVehicle) inVehicle.Value = sc.InVehicle;
        }
    }
}
#endif
