using System;
using System.Collections.Generic;
using UnityEngine;

namespace StansGame
{
    public enum Faction { British, German }

    /// <summary>Hit points for soldiers and vehicles. Raises Died once.</summary>
    public class Health : MonoBehaviour
    {
        public Faction faction = Faction.British;
        public float maxHealth = 100f;
        [Tooltip("Vehicles ignore small-arms damage below this value per hit.")]
        public float armour = 0f;

        [Tooltip("Hit points regained per second after regenDelay seconds without damage (player only).")]
        public float regenPerSecond = 0f;
        public float regenDelay = 6f;
        float lastDamage = -999f;

        public float Current { get; private set; }
        /// <summary>Out of reach in a vehicle's cab (driving a lorry): bullets, shells and blasts don't touch him and
        /// the enemy doesn't pick him as a target.</summary>
        public bool Sheltered { get; set; }
        public bool IsDead { get; private set; }

        public event Action<Health, DamageInfo> Damaged;
        public event Action<Health, DamageInfo> Died;

        /// <summary>Every live Health in the scene (soldiers, player, vehicles) for AI targeting.</summary>
        public static readonly List<Health> All = new List<Health>();

        void Awake() => Current = maxHealth;
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public void TakeDamage(DamageInfo info)
        {
            if (IsDead || Sheltered) return;
            float amount = info.amount - armour;
            if (amount <= 0f) return;
            Current = Mathf.Max(0f, Current - amount);
            lastDamage = Time.time;
            Damaged?.Invoke(this, info);
            if (GetComponent<CharacterController>()) Sfx.Hurt(transform.position + Vector3.up * 1.5f);
            if (Current <= 0f)
            {
                IsDead = true;
                Died?.Invoke(this, info);
            }
        }

        void Update()
        {
            if (regenPerSecond > 0f && !IsDead && Current < maxHealth && Time.time - lastDamage > regenDelay)
                Current = Mathf.Min(maxHealth, Current + regenPerSecond * Time.deltaTime);
        }

        /// <summary>Multiplayer: the host's hit points for this object (clients only display them).</summary>
        public void ApplyRemoteState(float current)
        {
            if (IsDead) return;
            if (current < Current) lastDamage = Time.time;
            Current = current;
        }

        /// <summary>Multiplayer: the host says this one died; play the death (ragdoll etc.) here too.</summary>
        public void ApplyRemoteDeath(DamageInfo info)
        {
            if (IsDead) return;
            Current = 0f; IsDead = true;
            Died?.Invoke(this, info);
        }

        public void Heal(float amount)
        {
            if (IsDead) return;
            Current = Mathf.Min(maxHealth, Current + amount);
        }
    }

    public struct DamageInfo
    {
        public float amount;
        public Vector3 point;
        public Vector3 direction;
        public float force;
        public GameObject source;
        public Rigidbody hitBody;
    }
}
