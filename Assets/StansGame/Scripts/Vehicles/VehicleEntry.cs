using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame
{
    /// <summary>Put on the player. Press E near a vehicle (within range) to get in, behind a field gun to man it, or by a
    /// lorry's cab to drive it (lorries: in multiplayer too, for every player).</summary>
    [RequireComponent(typeof(SoldierController))]
    public class VehicleEntry : MonoBehaviour, IDisableOnDeath
    {
        public float range = 4f;
        public TankController Nearby { get; private set; }
        /// <summary>A field gun (PaK 38) close enough to man: stand behind its shield.</summary>
        public FieldGun NearbyGun { get; private set; }
        /// <summary>A lorry close enough to climb into.</summary>
        public TruckController NearbyTruck { get; private set; }

        SoldierController soldier;
        float cooldown;
        bool hadControl;

        void Awake() => soldier = GetComponent<SoldierController>();

        void Update()
        {
            Nearby = null; NearbyGun = null; NearbyTruck = null;
            cooldown -= Time.deltaTime;
            // just got out of something: the E that got him out must not put him straight back in
            if (soldier.enabled && !hadControl) cooldown = Mathf.Max(cooldown, 0.5f);
            hadControl = soldier.enabled;
            float best = range;
            foreach (var t in TruckController.All)
            {
                if (!t || !(t.CanEnter || t.CanRide)) continue;
                var c = t.GetComponent<Collider>();
                float d = Vector3.Distance(transform.position, c ? c.ClosestPoint(transform.position) : t.transform.position);
                if (d < best) { best = d; NearbyTruck = t; }
            }
            if (Net.IsRemoteClient)   // multiplayer stage 1: only the host can drive tanks and man guns; anyone can take a lorry
            {
                TruckOnE();
                return;
            }
            foreach (var t in FindObjectsByType<TankController>())
            {
                if (t.HasDriver) continue;
                var h = t.GetComponent<Health>();
                if (h && h.IsDead) continue;
                float d = Vector3.Distance(transform.position, t.GetComponent<Collider>() ? t.GetComponent<Collider>().ClosestPoint(transform.position) : t.transform.position);
                if (d < best) { best = d; Nearby = t; }
            }
            float bestGun = 1.8f;
            foreach (var g in FieldGun.All)
            {
                if (!g || g.Manned) continue;
                float d = Vector3.Distance(transform.position, g.GunnerSpot);
                if (d < bestGun && d < best) { bestGun = d; NearbyGun = g; }
            }
            if (NearbyGun) { Nearby = null; NearbyTruck = null; }
            if (Nearby) NearbyTruck = null;
            var loadout = GetComponent<Loadout>();
            if (loadout && loadout.NearbyPickup) return;   // E picks things up first
            if (NearbyTruck) { TruckOnE(); return; }
            if (Nearby && cooldown <= 0f && soldier.enabled && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                cooldown = 0.5f;
                Nearby.Enter(soldier);
            }
            else if (NearbyGun && cooldown <= 0f && soldier.enabled && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                cooldown = 0.5f;
                NearbyGun.Man(soldier);
            }
        }
    
        void TruckOnE()
        {
            if (!NearbyTruck || cooldown > 0f || !soldier.enabled || Keyboard.current == null || !Keyboard.current.eKey.wasPressedThisFrame) return;
            var loadout = GetComponent<Loadout>();
            if (loadout && loadout.NearbyPickup) return;   // E picks things up first
            cooldown = 0.5f;
            if (NearbyTruck.CanEnter) NearbyTruck.Enter(soldier);   // the wheel if it's free, else a passenger seat
            else NearbyTruck.Ride(soldier);
        }

        // the prompt is drawn here, so it shows in every level (not only where a mission script draws it)
        GUIStyle promptStyle;
        void OnGUI()
        {
            if (!soldier || !soldier.enabled || !(Nearby || NearbyGun || NearbyTruck) || cooldown > 0f) return;
            var kit = GetComponent<Loadout>();
            if (kit && kit.NearbyPickup) return;   // Loadout shows "E: pick up" instead
            if (promptStyle == null)
            {
                promptStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                promptStyle.normal.textColor = Color.white;
            }
            string text = NearbyGun ? $"Press E to man the {NearbyGun.displayName}"
                        : NearbyTruck ? (NearbyTruck.CanEnter ? $"Press E to drive the {NearbyTruck.displayName}" : $"Press E to climb aboard the {NearbyTruck.displayName}")
                        : "Press E to get in";
            GUI.Label(new Rect(0, Screen.height / 2 + 40, Screen.width, 28), text, promptStyle);
        }
    }
}
