using System.Collections.Generic;
using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Something lying on the ground that a soldier can pick up with E: a weapon (with whatever ammunition is
    /// left in it), a hand grenade or a field dressing.
    /// </summary>
    public class Pickup : MonoBehaviour
    {
        public enum Kind { Weapon, Grenade, Dressing }

        public Kind kind = Kind.Weapon;
        public Weapon weapon;
        [Tooltip("For a grenade: the grenade that gets thrown.")]
        public Shell grenade;
        public int count = 1;

        public static readonly List<Pickup> All = new List<Pickup>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public Vector3 Position => weapon ? weapon.transform.position : transform.position;

        public string Label => kind switch
        {
            Kind.Weapon => weapon ? $"{weapon.displayName}  ({weapon.Ammo + weapon.SpareRounds} rounds)" : "weapon",
            Kind.Grenade => grenade ? (string.IsNullOrEmpty(grenade.displayName) ? "grenade" : grenade.displayName) : "grenade",
            _ => "field dressing",
        };

        /// <summary>Turns a weapon into something lying on the ground at pos (dropped by a soldier).</summary>
        public static Pickup DropWeapon(Weapon w, Vector3 pos, Quaternion rot)
        {
            var go = w.gameObject;
            go.transform.SetParent(null, true);
            go.SetActive(true);
            go.transform.SetPositionAndRotation(pos, rot);
            if (!go.TryGetComponent<Rigidbody>(out var rb)) rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = false; rb.mass = 4f;
            if (!go.TryGetComponent<BoxCollider>(out var col)) col = go.AddComponent<BoxCollider>();
            col.enabled = true;
            bool pistol = w.slot == WeaponSlot.Sidearm;
            col.size = pistol ? new Vector3(0.05f, 0.14f, 0.24f) : new Vector3(0.07f, 0.14f, 0.9f);
            col.center = pistol ? Vector3.zero : new Vector3(0f, 0f, 0.2f);
            if (!go.TryGetComponent<Pickup>(out var p)) p = go.AddComponent<Pickup>();
            p.kind = Kind.Weapon; p.weapon = w; p.enabled = true;
            return p;
        }

        /// <summary>A grenade or a field dressing lying on the ground (made from simple shapes).</summary>
        public static Pickup SpawnItem(Kind kind, Shell grenade, Vector3 pos)
        {
            var go = new GameObject(kind == Kind.Grenade ? "Grenade (pick up)" : "Field dressing (pick up)");
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, Random.value * 360f, 90f));
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            bool stick = grenade && grenade.displayName.ToLowerInvariant().Contains("stiel");
            if (kind == Kind.Grenade && grenade && grenade.pickupVisual)
            {
                var v = Instantiate(grenade.pickupVisual, go.transform, false);
                v.transform.localPosition = Vector3.zero;
            }
            else if (kind == Kind.Dressing)
            {
                mat.SetColor("_BaseColor", new Color(0.78f, 0.74f, 0.62f));
                Part(PrimitiveType.Cube, go.transform, Vector3.zero, new Vector3(0.12f, 0.05f, 0.08f), mat);
            }
            else if (stick)
            {
                mat.SetColor("_BaseColor", new Color(0.36f, 0.30f, 0.22f));
                var steel = new Material(mat); steel.SetColor("_BaseColor", new Color(0.22f, 0.24f, 0.21f));
                Part(PrimitiveType.Cylinder, go.transform, new Vector3(0f, -0.06f, 0f), new Vector3(0.035f, 0.14f, 0.035f), mat);   // wooden handle
                Part(PrimitiveType.Cylinder, go.transform, new Vector3(0f, 0.11f, 0f), new Vector3(0.07f, 0.05f, 0.07f), steel);   // the charge head
            }
            else
            {
                mat.SetColor("_BaseColor", new Color(0.24f, 0.26f, 0.20f));
                Part(PrimitiveType.Sphere, go.transform, Vector3.zero, new Vector3(0.06f, 0.09f, 0.06f), mat);
            }
            var rb = go.AddComponent<Rigidbody>(); rb.mass = 0.6f;
            var col = go.AddComponent<BoxCollider>(); col.size = kind == Kind.Dressing ? new Vector3(0.12f, 0.05f, 0.08f) : new Vector3(0.07f, stick ? 0.36f : 0.1f, 0.07f);
            var p = go.AddComponent<Pickup>(); p.kind = kind; p.grenade = grenade;
            return p;
        }

        static void Part(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var g = GameObject.CreatePrimitive(t);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
        }
    }
}
