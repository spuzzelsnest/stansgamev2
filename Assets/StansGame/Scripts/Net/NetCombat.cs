#if STANS_NET
using Unity.Netcode;
using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Carries combat between machines (one per scene, on the MissionDirector):
    ///   - a client's hits are sent to the host, which applies the damage;
    ///   - tracers are shown on every screen;
    ///   - PIAT bombs and tank shells: the host flies the real one, everyone else sees a harmless copy;
    ///   - soldiers the host creates at runtime (glider sections) get a network identity.
    /// </summary>
    public class NetCombat : NetworkBehaviour
    {
        [Tooltip("Every projectile prefab that can be fired (PIAT bomb, KwK 40 shell). Same list on every machine.")]
        public Shell[] projectilePrefabs;

        public override void OnNetworkSpawn()
        {
            NetHooks.RequestDamage = RequestDamage;
            NetHooks.ShotFired = OnShotFired;
            NetHooks.ProjectileLaunched = OnProjectileLaunched;
            NetHooks.Spawned = OnSpawned;
            NetHooks.RequestHeal = RequestHeal;
        }

        void RequestHeal(Health h, float amount)
        {
            var ent = h.GetComponentInParent<NetEntity>();
            if (!ent || !ent.IsSpawned) h.Heal(amount); else ent.HealRpc(amount);
        }

        public override void OnNetworkDespawn()
        {
            NetHooks.RequestDamage = null; NetHooks.ShotFired = null; NetHooks.ProjectileLaunched = null; NetHooks.Spawned = null; NetHooks.RequestHeal = null;
        }

        static bool Id(GameObject go, out ulong id)
        {
            id = 0;
            if (!go) return false;
            var no = go.GetComponentInParent<NetworkObject>();
            if (!no || !no.IsSpawned) return false;
            id = no.NetworkObjectId; return true;
        }

        // ---------------------------------------------------------------- damage

        void RequestDamage(Health h, DamageInfo info)
        {
            var ent = h.GetComponentInParent<NetEntity>();
            if (!ent || !ent.IsSpawned) { h.TakeDamage(info); return; }
            bool has = Id(info.source, out ulong sid);
            ent.HitRpc(info.amount, info.point, info.direction, info.force, sid, has);
        }

        // ---------------------------------------------------------------- tracers

        void OnShotFired(Vector3 from, Vector3 to)
        {
            if (!IsSpawned || Random.value > 0.35f) return;   // only the glowing tracer rounds travel
            ShotRpc(from, to, NetworkManager.LocalClientId);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Everyone)]
        void ShotRpc(Vector3 from, Vector3 to, ulong origin)
        {
            if (origin == NetworkManager.LocalClientId) return;   // the shooter already drew it
            Tracer.Spawn(from, to, true);
            Sfx.Shot("remote", from);
        }

        // ---------------------------------------------------------------- projectiles

        void OnProjectileLaunched(Shell prefab, Vector3 from, Vector3 velocity, GameObject shooter)
        {
            if (!IsSpawned) return;
            int index = System.Array.IndexOf(projectilePrefabs, prefab);
            if (index < 0) return;
            bool has = Id(shooter, out ulong sid);
            if (IsServer) ProjectileVisualRpc(index, from, velocity, NetworkManager.LocalClientId);
            else ProjectileFromClientRpc(index, from, velocity, sid, has, NetworkManager.LocalClientId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void ProjectileFromClientRpc(int index, Vector3 from, Vector3 velocity, ulong shooterId, bool hasShooter, ulong origin)
        {
            // the host flies the real bomb (it does the damage) ...
            GameObject shooter = null;
            if (hasShooter && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(shooterId, out var no)) shooter = no.gameObject;
            var shell = Instantiate(projectilePrefabs[index], from, Quaternion.LookRotation(velocity));
            shell.Launch(velocity, shooter ? shooter : gameObject);
            // ... and the other clients get a harmless copy
            ProjectileVisualRpc(index, from, velocity, origin);
        }

        [Rpc(SendTo.NotServer)]
        void ProjectileVisualRpc(int index, Vector3 from, Vector3 velocity, ulong origin)
        {
            if (origin == NetworkManager.LocalClientId) return;
            var shell = Instantiate(projectilePrefabs[index], from, Quaternion.LookRotation(velocity));
            shell.visualOnly = true;
            shell.Launch(velocity, gameObject);
        }

        // ---------------------------------------------------------------- runtime soldiers

        void OnSpawned(GameObject go)
        {
            if (!IsServer || !go) return;
            var no = go.GetComponent<NetworkObject>();
            if (no && !no.IsSpawned) no.Spawn(true);
        }
    }
}
#endif
