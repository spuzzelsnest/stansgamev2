#if STANS_NET
using Unity.Netcode;
using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// On every networked world object (soldiers, players, gliders, tanks).
    /// The host owns the truth: it runs the AI and physics, takes the damage, and tells everyone the hit points
    /// and the moment of death (so ragdolls fall and tanks burn on every screen). On clients this switches off
    /// the simulation scripts; the object is moved by its NetworkTransform instead.
    /// </summary>
    public class NetEntity : NetworkBehaviour
    {
        readonly NetworkVariable<float> hp = new NetworkVariable<float>(100f);
        Health health;
        float pushTimer;

        public override void OnNetworkSpawn()
        {
            health = GetComponent<Health>();
            if (!IsServer)
            {
                foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true))
                    if (mb is SoldierAI || mb is TankAI || mb is TankController || mb is GliderApproach) mb.enabled = false;
                var rb = GetComponent<Rigidbody>();
                if (rb) rb.isKinematic = true;
                if (health)
                {
                    health.regenPerSecond = 0f;   // the host regenerates; we just show its number
                    hp.OnValueChanged += OnHpChanged;
                    if (hp.Value <= 0f) health.ApplyRemoteDeath(new DamageInfo { direction = Vector3.down });
                    else health.ApplyRemoteState(hp.Value);
                }
            }
            else if (health)
            {
                hp.Value = health.Current;
                health.Died += OnDiedOnHost;
            }
        }

        public override void OnNetworkDespawn()
        {
            hp.OnValueChanged -= OnHpChanged;
            if (health) health.Died -= OnDiedOnHost;
        }

        void OnHpChanged(float before, float now)
        {
            if (!health) return;
            if (now <= 0f) health.ApplyRemoteDeath(new DamageInfo { direction = Vector3.down });
            else health.ApplyRemoteState(now);
        }

        void Update()
        {
            if (!IsServer || !IsSpawned || !health) return;
            pushTimer -= Time.deltaTime;
            if (pushTimer > 0f) return;
            pushTimer = 0.15f;
            if (Mathf.Abs(hp.Value - health.Current) > 0.01f) hp.Value = health.Current;
        }

        void OnDiedOnHost(Health h, DamageInfo info)
        {
            hp.Value = 0f;
            DiedRpc(info.point, info.direction, info.force);
        }

        [Rpc(SendTo.NotServer)]
        void DiedRpc(Vector3 point, Vector3 direction, float force)
        {
            if (health) health.ApplyRemoteDeath(new DamageInfo { point = point, direction = direction, force = force });
        }

        /// <summary>A client bandaged himself: the host restores the hit points.</summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void HealRpc(float amount) { if (health) health.Heal(amount); }

        /// <summary>A client hit this object: the host applies the damage.</summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void HitRpc(float amount, Vector3 point, Vector3 direction, float force, ulong shooterId, bool hasShooter)
        {
            if (!health) return;
            GameObject src = null;
            if (hasShooter && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(shooterId, out var no)) src = no.gameObject;
            health.TakeDamage(new DamageInfo { amount = amount, point = point, direction = direction, force = force, source = src });
        }
    }
}
#endif
