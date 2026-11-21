using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame
{
    /// <summary>
    /// The player's kit, shown as four slots at the top of the screen:
    ///   1 main weapon, 2 side arm, 3 hand grenades (throw with the left mouse button), 4 field dressings (press 4 to use),
    ///   5 smoke grenades (press 5 to throw one where you aim: a smoke screen to cross open ground behind).
    /// E picks up what is lying nearby: a weapon goes into its slot (the one you had is dropped), the same weapon
    /// only gives you its ammunition, grenades and dressings are added to what you carry.
    /// </summary>
    [RequireComponent(typeof(SoldierController))]
    public class Loadout : MonoBehaviour, IDisableOnDeath
    {
        public Weapon primary, sidearm;
        [Tooltip("Grenades carried, thrown in this order.")]
        public List<Shell> grenades = new List<Shell>();
        public int dressings = 1;
        [Tooltip("Smoke grenades carried (No. 77, Nebelhandgranate 39, AN-M8): key 5 throws one.")]
        public List<Shell> smokes = new List<Shell>();

        public int maxGrenades = 4, maxDressings = 3, maxSmokes = 3;
        public float dressingHeal = 80f, bandageTime = 3f;
        public float pickupRange = 2.2f;
        public float throwSpeed = 15f;

        [Tooltip("Where weapons are held: the WeaponHolder that follows the right hand (or the soldier himself).")]
        public Transform weaponMount;

        public Pickup NearbyPickup { get; private set; }
        public bool Bandaging => bandageEnd > 0f;
        public float BandageProgress => Bandaging ? 1f - (bandageEnd - Time.time) / bandageTime : 0f;
        public int Selected => soldier ? soldier.WeaponIndex : 0;
        public string Notice { get; private set; }
        /// <summary>When the last grenade left his hand (for the throwing arm's follow-through).</summary>
        public float LastThrow { get; private set; } = -10f;
        /// <summary>The grenade he would throw next (null when he has none).</summary>
        public Shell NextGrenade => grenades.Count > 0 ? grenades[0] : null;

        SoldierController soldier;
        Health health;
        float bandageEnd = -1f, nextThrow, noticeUntil;

        void Awake()
        {
            soldier = GetComponent<SoldierController>();
            health = GetComponent<Health>();
            if (primary) primary.Init();
            if (sidearm) sidearm.Init();
            if (!weaponMount) { var wh = GetComponentInChildren<WeaponHolder>(true); weaponMount = wh ? wh.transform : transform; }
            SyncWeapons();
        }

        void SyncWeapons()
        {
            soldier.weapons = new[] { primary, sidearm };
            soldier.SelectWeapon(soldier.WeaponIndex);
        }

        void Say(string text) { Notice = text; noticeUntil = Time.time + 2.5f; }

        void Update()
        {
            if (Time.time > noticeUntil) Notice = null;
            NearbyPickup = null;
            if (!soldier.enabled) return;   // a puppet (another player's soldier), in a vehicle or in a film scene
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            // choose a slot: 1 2 3, or the mouse wheel
            if (kb.digit1Key.wasPressedThisFrame) Choose(0);
            if (kb.digit2Key.wasPressedThisFrame) Choose(1);
            if (kb.digit3Key.wasPressedThisFrame) Choose(2);
            float wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.1f) Cycle(wheel < 0f ? 1 : -1);
            // Tab: next item in the inventory (Shift+Tab: back)
            if (kb.tabKey.wasPressedThisFrame) Cycle(kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed ? -1 : 1);

            // 4: put on a field dressing
            if (kb.digit4Key.wasPressedThisFrame && !Bandaging)
            {
                if (dressings <= 0) Say("No field dressings left");
                else if (health && health.Current >= health.maxHealth) Say("You are not wounded");
                else { bandageEnd = Time.time + bandageTime; Sfx.Reload(transform.position); }
            }
            if (Bandaging && Time.time >= bandageEnd)
            {
                bandageEnd = -1f; dressings--;
                if (health)
                {
                    if (Net.IsRemoteClient && NetHooks.RequestHeal != null) NetHooks.RequestHeal(health, dressingHeal);
                    else health.Heal(dressingHeal);
                }
                Say("Wound dressed");
            }

            // 5: throw a smoke grenade where you're aiming
            if (kb.digit5Key.wasPressedThisFrame && !Bandaging)
            {
                if (smokes.Count == 0) Say("No smoke grenades");
                else if (Time.time > nextThrow) { ThrowShell(smokes[0], smokes, 0.85f); Say("Smoke!"); }
            }

            // grenade in hand: left mouse throws it
            if (Selected == 2 && GameInput.MouseActive && mouse.leftButton.wasPressedThisFrame && Time.time > nextThrow && !Bandaging) Throw();

            // E: pick up the nearest thing on the ground
            float best = pickupRange;
            foreach (var p in Pickup.All)
            {
                if (!p) continue;
                float d = Vector3.Distance(transform.position + Vector3.up * 0.5f, p.Position);
                if (d < best) { best = d; NearbyPickup = p; }
            }
            if (NearbyPickup && kb.eKey.wasPressedThisFrame) Take(NearbyPickup);
        }

        void Choose(int slot)
        {
            if (slot == 0 && !primary) { Say("No main weapon"); return; }
            if (slot == 1 && !sidearm) { Say("No side arm"); return; }
            if (slot == 2 && grenades.Count == 0) { Say("No grenades"); return; }
            soldier.SelectWeapon(slot);
        }

        void Cycle(int dir)
        {
            for (int i = 1; i <= 3; i++)
            {
                int s = (Selected + dir * i + 3) % 3;
                if ((s == 0 && primary) || (s == 1 && sidearm) || (s == 2 && grenades.Count > 0)) { soldier.SelectWeapon(s); return; }
            }
        }

        void Throw()
        {
            if (grenades.Count == 0) return;
            ThrowShell(grenades[0], grenades, 1f);
            if (grenades.Count == 0) soldier.SelectWeapon(primary ? 0 : 1);
        }

        void ThrowShell(Shell prefab, List<Shell> from_, float speedScale)
        {
            from_.RemoveAt(0);
            nextThrow = Time.time + 1.2f;
            LastThrow = Time.time;
            Ray aim = soldier.AimRay();
            Vector3 from = transform.position + Vector3.up * 1.7f + transform.right * 0.25f + aim.direction * 0.4f;
            Vector3 vel = aim.direction * throwSpeed * speedScale + Vector3.up * 3.5f;
            var g = Instantiate(prefab, from, Random.rotation);
            g.visualOnly = Net.IsRemoteClient;   // on a client the host's copy does the damage
            g.Launch(vel, gameObject);
            NetHooks.ProjectileLaunched?.Invoke(prefab, from, vel, gameObject);
        }

        void Take(Pickup p)
        {
            switch (p.kind)
            {
                case Pickup.Kind.Grenade when p.grenade && p.grenade.smoke:
                    if (smokes.Count >= maxSmokes) { Say("You can't carry more smoke grenades"); return; }
                    smokes.Add(p.grenade);
                    Say($"Picked up a {p.Label}");
                    Destroy(p.gameObject);
                    return;
                case Pickup.Kind.Grenade:
                    if (grenades.Count >= maxGrenades) { Say("You can't carry more grenades"); return; }
                    grenades.Add(p.grenade);
                    Say($"Picked up a {p.Label}");
                    Destroy(p.gameObject);
                    return;
                case Pickup.Kind.Dressing:
                    if (dressings >= maxDressings) { Say("You can't carry more field dressings"); return; }
                    dressings++;
                    Say("Picked up a field dressing");
                    Destroy(p.gameObject);
                    return;
            }

            var w = p.weapon;
            if (!w) return;
            var current = w.slot == WeaponSlot.Sidearm ? sidearm : primary;
            if (current && current.displayName == w.displayName)
            {
                // the same weapon: just empty his magazines into your pouches
                int rounds = w.Ammo + w.SpareRounds;
                int took = current.AddSpareRounds(rounds);
                if (took <= 0) { Say($"Your pouches are full ({current.displayName})"); return; }
                w.SetAmmo(0, rounds - took);
                Say($"Took {took} rounds for the {current.displayName}");
                if (w.Ammo + w.SpareRounds <= 0) Destroy(p);
                return;
            }

            // a different weapon: drop yours, take his
            if (current) Pickup.DropWeapon(current, transform.position + transform.forward * 0.7f + Vector3.up * 0.4f,
                Quaternion.Euler(0f, transform.eulerAngles.y + 80f, 90f));
            GameObject holder = p.gameObject != w.gameObject ? p.gameObject : null;   // a dead soldier's gun sits in a holder
            Destroy(p);
            if (w.TryGetComponent<Rigidbody>(out var rb)) Destroy(rb);
            if (w.TryGetComponent<BoxCollider>(out var col)) Destroy(col);
            w.transform.SetParent(weaponMount ? weaponMount : transform, false);
            w.transform.localPosition = weaponMount && weaponMount != transform ? Vector3.zero
                : w.slot == WeaponSlot.Sidearm ? new Vector3(0.3f, 1.3f, 0.35f) : new Vector3(0.25f, 1.35f, 0.35f);
            w.transform.localRotation = Quaternion.identity;
            if (holder) Destroy(holder);
            if (w.slot == WeaponSlot.Sidearm) sidearm = w; else primary = w;
            SyncWeapons();
            soldier.SelectWeapon(w.slot == WeaponSlot.Sidearm ? 1 : 0);
            Say($"Picked up the {w.displayName}");
        }

        // ------------------------------------------------------------------ the five slots along the bottom of the screen

        void OnGUI()
        {
            if (!soldier || !soldier.enabled || Net.InMenu || CinematicDirector.Playing) return;
            const float w = 172f, h = 58f, gap = 6f;
            float x0 = 330f, y = Screen.height - 68f;   // along the bottom, next to the weapon and health readout
            var name = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true, alignment = TextAnchor.UpperLeft };
            var small = new GUIStyle(name) { fontSize = 12 };
            name.normal.textColor = small.normal.textColor = new Color(0.92f, 0.9f, 0.82f);
            for (int i = 0; i < 5; i++)
            {
                var r = new Rect(x0 + i * (w + gap), y, w, h);
                bool sel = i == Selected || (i == 3 && Bandaging);
                GUI.color = sel ? new Color(0.85f, 0.75f, 0.35f, 0.85f) : new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), Texture2D.whiteTexture);
                GUI.color = Color.white;
                string title, line;
                switch (i)
                {
                    case 0: title = primary ? primary.displayName : "<i>empty</i>"; line = primary ? Ammo(primary) : "main weapon"; break;
                    case 1: title = sidearm ? sidearm.displayName : "<i>empty</i>"; line = sidearm ? Ammo(sidearm) : "side arm"; break;
                    case 2:
                        title = grenades.Count > 0 ? grenades[0].displayName : "<i>no grenades</i>";
                        line = grenades.Count > 0 ? $"× {grenades.Count}   (left mouse: throw)" : "grenades";
                        // a mixed bag (Mills bombs and a Gammon bomb): say what comes after the next one
                        if (grenades.Count > 1 && grenades.Exists(g => g && grenades[0] && g.displayName != grenades[0].displayName))
                        {
                            int same = grenades.FindAll(g => g && g.displayName == grenades[0].displayName).Count;
                            var other = grenades.Find(g => g && g.displayName != grenades[0].displayName);
                            line = $"× {same}  + {grenades.Count - same} {other.displayName}";
                        }
                        break;
                    case 4:
                        title = smokes.Count > 0 ? smokes[0].displayName : "<i>no smoke</i>";
                        line = smokes.Count > 0 ? $"× {smokes.Count}   (press 5: throw)" : "smoke grenades";
                        break;
                    default:
                        title = "Field dressing";
                        line = Bandaging ? $"bandaging... {Mathf.RoundToInt(BandageProgress * 100f)}%" : $"× {dressings}   (press 4)";
                        break;
                }
                GUI.Label(new Rect(r.x + 8, r.y + 4, 20, 20), $"<b>{i + 1}</b>", name);
                GUI.Label(new Rect(r.x + 26, r.y + 4, w - 30, 22), $"<b>{title}</b>", name);
                GUI.Label(new Rect(r.x + 26, r.y + 28, w - 30, 22), line, small);
            }

            // what can be picked up, and short messages
            var mid = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, richText = true };
            mid.normal.textColor = new Color(0.95f, 0.92f, 0.8f);
            if (NearbyPickup) GUI.Label(new Rect(0, Screen.height / 2 + 40, Screen.width, 28), $"E: pick up {NearbyPickup.Label}", mid);
            if (!string.IsNullOrEmpty(Notice)) GUI.Label(new Rect(0, y - 38, Screen.width, 28), Notice, mid);
        }

        static string Ammo(Weapon w) => w.IsReloading ? "reloading..." : $"{w.Ammo} / {w.SpareRounds}";
    }
}
