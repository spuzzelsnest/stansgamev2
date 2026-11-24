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

        void Awake() { Current = maxHealth; FaceVariety.Apply(gameObject); }   // a complexion of his own (FaceVariety)
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public void TakeDamage(DamageInfo info)
        {
            if (IsDead || Sheltered) return;
            float amount = info.amount - armour;
            if (GetComponent<SoldierController>()) amount *= GameSettings.DamageToPlayer;   // Easy / Normal / Hard (Esc menu)
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
            // badly hurt (under 40%): the animator's "Injured" gait - limping walk and run, clutching the wound when still
            if (injuredHash == 0)
            {
                injuredHash = -1;
                anim = GetComponentInChildren<Animator>();
                if (anim) foreach (var p in anim.parameters) if (p.name == "Injured") injuredHash = p.nameHash;
            }
            if (injuredHash != -1 && anim)
            {
                bool hurt = !IsDead && Current < maxHealth * 0.4f;
                if (anim.GetBool(injuredHash) != hurt) anim.SetBool(injuredHash, hurt);
            }
        }
        Animator anim; int injuredHash;

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

        /// <summary>Back to full health and alive again (a practice target repaired).</summary>
        public void Revive()
        {
            IsDead = false; Current = maxHealth; lastDamage = -999f;
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
