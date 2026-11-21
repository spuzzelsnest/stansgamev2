using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// What a soldier leaves behind when he falls, besides the weapon in his hands: his pistol (MG gunners and
    /// NCOs carried a P08), sometimes a stick grenade or his field dressing. The same on every machine.
    /// </summary>
    public class LootOnDeath : MonoBehaviour
    {
        [Tooltip("A holstered side arm (inactive child) that is dropped when he dies.")]
        public Weapon sidearm;
        public Shell grenade;
        [Range(0f, 1f)] public float grenadeChance = 0.4f;
        [Range(0f, 1f)] public float dressingChance = 0.3f;

        void Start()
        {
            var h = GetComponent<Health>();
            if (h) h.Died += OnDied;
        }

        void OnDied(Health h, DamageInfo info)
        {
            // decided by his name so every player sees the same things on the ground
            var rnd = new System.Random(name.GetHashCode() ^ Mathf.RoundToInt(transform.position.x * 10f));
            Vector3 at = transform.position + Vector3.up * 0.4f;
            if (sidearm)
            {
                sidearm.SetAmmo(sidearm.magazineSize, sidearm.magazineSize);
                Pickup.DropWeapon(sidearm, at + transform.right * 0.5f, Quaternion.Euler(0f, transform.eulerAngles.y + 70f, 90f));
            }
            if (grenade && rnd.NextDouble() < grenadeChance) Pickup.SpawnItem(Pickup.Kind.Grenade, grenade, at - transform.right * 0.5f);
            if (rnd.NextDouble() < dressingChance) Pickup.SpawnItem(Pickup.Kind.Dressing, null, at + transform.forward * 0.5f);
        }
    }
}
