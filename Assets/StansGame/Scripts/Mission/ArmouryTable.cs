using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// The Proving Ground's gun table: every weapon and grenade laid out to pick up with E. Take one and, a few
    /// seconds later, a fresh one (full magazine) is back in its place, so the table never empties.
    /// </summary>
    public class ArmouryTable : MonoBehaviour
    {
        [System.Serializable]
        public class Slot
        {
            public GameObject prefab;
            public Vector3 position;
            public Quaternion rotation = Quaternion.identity;
            [System.NonSerialized] public GameObject current;
            [System.NonSerialized] public float emptySince = -1f;
        }
        public Slot[] slots = new Slot[0];
        public float refillAfter = 3f;

        void Start() { foreach (var s in slots) Lay(s); }

        void Update()
        {
            foreach (var s in slots)
            {
                bool there = s.current && s.current.transform.parent == transform && (s.current.transform.position - s.position).sqrMagnitude < 1f;
                if (there) { s.emptySince = -1f; continue; }
                if (s.emptySince < 0f) { s.emptySince = Time.time; s.current = null; continue; }
                if (Time.time - s.emptySince > refillAfter) { Lay(s); s.emptySince = -1f; }
            }
        }

        void Lay(Slot s)
        {
            if (!s.prefab) return;
            var go = Instantiate(s.prefab, s.position, s.rotation);
            go.name = s.prefab.name;
            var w = go.GetComponent<Weapon>();
            if (w)
            {
                var p = Pickup.DropWeapon(w, s.position, s.rotation);
                var rb = p.GetComponent<Rigidbody>(); if (rb) rb.isKinematic = true;   // lies still on the table
            }
            go.transform.SetParent(transform, true);
            s.current = go;
        }
    }
}
