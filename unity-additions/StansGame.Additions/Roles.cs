using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame.Additions
{
    /// <summary>The role the player picks before a level, named as in a 1944 airborne rifle platoon.</summary>
    public enum Role { SectionCommander = 0, StretcherBearer = 1, BrenNumberTwo = 2 }

    /// <summary>
    /// The three roles: names, the text on the role cards and the chosen role (kept between sessions).
    /// Period names for D Company, 2nd Oxfordshire and Buckinghamshire Light Infantry (glider-borne),
    /// June 1944: the section commander led a rifle section with a Sten, stretcher-bearers were riflemen
    /// trained in first aid, and the Bren No. 2 was the Bren gunner's mate who carried the spare magazines.
    /// </summary>
    public static class Roles
    {
        const string Pref = "StansGame.Role";

        public sealed class Card
        {
            public string Name, Tag, History, Ability;
            public string[] Kit;
        }

        public static readonly Card[] Cards =
        {
            new Card
            {
                Name = "SECTION COMMANDER", Tag = "ASSAULT",
                History = "The corporal who leads the rifle section. He carries the Sten and goes in first.",
                Kit = new[] { "Sten Mk II, 10 magazines", "M1911 pistol", "4 Mills bombs", "1 field dressing" },
                Ability = "His calls for a stretcher-bearer or ammunition are answered sooner.",
            },
            new Card
            {
                Name = "STRETCHER-BEARER", Tag = "MEDIC",
                History = "A rifleman trained in first aid who goes out to the wounded under fire.",
                Kit = new[] { "Sten Mk II, 5 magazines", "M1911 pistol", "1 Mills bomb", "8 field dressings, put on faster" },
                Ability = "G: dress the wounds of a soldier next to you. Recovers faster himself.",
            },
            new Card
            {
                Name = "BREN No. 2", Tag = "AMMUNITION",
                History = "The Bren gunner's mate. He carries the section's spare magazines and keeps the guns fed.",
                Kit = new[] { "Lee-Enfield No. 4 rifle, 100 rounds", "M1911 pistol", "2 Mills bombs", "4 bandoliers of ammunition" },
                Ability = "G: open a bandolier and fill up the weapon in your hands.",
            },
        };

        public static Role Chosen
        {
            get { return (Role)Mathf.Clamp(PlayerPrefs.GetInt(Pref, 0), 0, Cards.Length - 1); }
            set { PlayerPrefs.SetInt(Pref, (int)value); PlayerPrefs.Save(); }
        }

        public static string NameOf(Role r) { return Cards[(int)r].Name; }
    }

    /// <summary>
    /// Gives the local player the kit of the chosen role once control starts (after the intro and the
    /// glider exit), and again after a respawn. Also runs the role's own action on G:
    /// the stretcher-bearer dresses a wounded soldier next to him, the Bren No. 2 opens a bandolier.
    /// Uses the game's own kit (Loadout, Weapon.SetAmmo); the Bren No. 2's rifle is taken from the
    /// rifleman prefab through the game's own weapon pickup, as if he had picked it up.
    /// </summary>
    public sealed class RoleKit : MonoBehaviour
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly MethodInfo take = typeof(Loadout).GetMethod("Take", Any);
        static readonly MethodInfo say = typeof(Loadout).GetMethod("Say", Any);

        public const float TreatRange = 3f;

        /// <summary>The role whose kit this player carries.</summary>
        public Role Role { get; private set; }
        public bool Applied { get; private set; }
        public int Bandoliers { get; private set; }

        SoldierController soldier;
        Loadout loadout;
        Health health;
        bool wasDead;

        void Awake()
        {
            soldier = GetComponent<SoldierController>();
            loadout = GetComponent<Loadout>();
            if (loadout == null) loadout = GetComponentInChildren<Loadout>();
            health = GetComponent<Health>();
        }

        static bool InControl(SoldierController p)
        {
            return p != null && p.enabled && !CinematicDirector.Playing && !GliderExit.Exiting && !Net.InMenu;
        }

        void Update()
        {
            if (soldier == null || loadout == null || AdditionsBootstrap.LocalPlayer() != soldier) return;
            bool dead = health != null && health.IsDead;
            if (dead) { wasDead = true; return; }
            if (wasDead) { wasDead = false; Applied = false; }   // respawned: the game hands out its own kit again
            if (!Applied)
            {
                if (InControl(soldier)) Apply(Roles.Chosen);
                return;
            }

            Keyboard k = Keyboard.current;
            if (k != null && k.gKey.wasPressedThisFrame && !MainMenu.Showing && !SettingsPanel.Open) Act();
        }

        public void Apply(Role role)
        {
            Role = role;
            Applied = true;
            Weapon primary = loadout.primary, sidearm = loadout.sidearm;
            switch (role)
            {
                case Role.SectionCommander:
                    Fill(primary, 9);
                    Grenades(4, 5);
                    Dressings(1, 3);
                    break;
                case Role.StretcherBearer:
                    Fill(primary, 4);
                    Grenades(1, 2);
                    Dressings(8, 10);
                    loadout.bandageTime = 2f;
                    loadout.dressingHeal = 100f;
                    if (health != null) { health.regenPerSecond = 8f; health.regenDelay = 4f; }
                    break;
                case Role.BrenNumberTwo:
                    Weapon rifle = GiveRifle();
                    Fill(rifle != null ? rifle : loadout.primary, 9);
                    Fill(sidearm, 3);
                    Grenades(2, 4);
                    Dressings(1, 3);
                    Bandoliers = 4;
                    break;
            }
            Say(Roles.NameOf(role) + ": kit drawn");
            Debug.Log("Stan's Game: kit for the " + Roles.NameOf(role) + " handed out (primary " + (loadout.primary != null ? loadout.primary.displayName : "none") + ")");
        }

        /// <summary>A full magazine loaded and this many spare magazines.</summary>
        static void Fill(Weapon w, int spareMags)
        {
            if (w == null) return;
            if (w.spareMagazines < spareMags) w.spareMagazines = spareMags;
            w.SetAmmo(w.magazineSize, w.magazineSize * spareMags);
        }

        void Grenades(int count, int max)
        {
            List<Shell> g = loadout.grenades;
            loadout.maxGrenades = Mathf.Max(max, count);
            if (g == null || g.Count == 0) return;
            Shell kind = g[0];
            while (g.Count < count) g.Add(kind);
            while (g.Count > count) g.RemoveAt(g.Count - 1);
        }

        void Dressings(int count, int max)
        {
            loadout.maxDressings = Mathf.Max(max, count);
            loadout.dressings = count;
        }

        /// <summary>Swaps the Sten for the rifleman's Lee-Enfield through the game's own pickup.</summary>
        Weapon GiveRifle()
        {
            if (loadout.primary != null && (loadout.primary.displayName ?? "").Contains("Enfield")) return loadout.primary;
            MissionDirector md = MissionDirector.Instance;
            if (md == null || md.britishRiflePrefab == null || take == null) return null;
            Weapon source = null;
            foreach (Weapon w in md.britishRiflePrefab.GetComponentsInChildren<Weapon>(true))
                if (w.slot == WeaponSlot.Primary) { source = w; break; }
            if (source == null) return null;

            Weapon old = loadout.primary;
            GameObject copy = Instantiate(source.gameObject);
            copy.name = source.gameObject.name;
            Weapon rifle = copy.GetComponent<Weapon>();
            Pickup p = Pickup.DropWeapon(rifle, transform.position + Vector3.up * 0.5f, transform.rotation);
            if (p == null) { Destroy(copy); return null; }
            take.Invoke(loadout, new object[] { p });
            // The Sten he had is dropped at his feet by the swap; he hands it in rather than leaving it there.
            if (old != null)
                foreach (Pickup q in new List<Pickup>(Pickup.All))
                    if (q != null && q.weapon == old) Destroy(q.gameObject);
            if (loadout.primary != rifle) return null;
            soldier.SelectWeapon(0);
            return rifle;
        }

        void Act()
        {
            if (loadout.Bandaging) return;
            switch (Role)
            {
                case Role.StretcherBearer: Treat(); break;
                case Role.BrenNumberTwo: OpenBandolier(); break;
                default: Say("Section commander: call for help with " + KeyLabel.Of(Key.T) + " (medic) or " + KeyLabel.Of(Key.B) + " (ammunition)"); break;
            }
        }

        /// <summary>The most wounded British soldier within reach, not the player, or null.</summary>
        public Health WoundedNearby()
        {
            Health best = null;
            float bestMissing = 1f;
            foreach (Health h in Health.All)
            {
                if (h == null || h == health || h.IsDead || h.faction != Faction.British) continue;
                if (Vector3.Distance(h.transform.position, transform.position) > TreatRange) continue;
                float missing = h.maxHealth - h.Current;
                if (missing > bestMissing) { bestMissing = missing; best = h; }
            }
            return best;
        }

        void Treat()
        {
            Health h = WoundedNearby();
            if (h == null) { Say("No wounded soldier next to you"); return; }
            if (loadout.dressings <= 0) { Say("No field dressings left"); return; }
            loadout.dressings--;
            h.Heal(loadout.dressingHeal);
            Say("Dressed his wounds  (" + loadout.dressings + " dressings left)");
        }

        void OpenBandolier()
        {
            Weapon w = soldier.CurrentWeapon;
            if (w == null || w.hold == HoldStyle.Launcher) { Say("Take up a rifle, Sten or pistol first"); return; }
            if (Bandoliers <= 0) { Say("No bandoliers left"); return; }
            if (w.SpareRounds >= w.MaxSpare) { Say("Your pouches are full"); return; }
            int got = w.AddSpareRounds(w.magazineSize * 3);
            if (got <= 0) { Say("Your pouches are full"); return; }
            Bandoliers--;
            Say("Opened a bandolier: +" + got + " rounds for the " + w.displayName + "  (" + Bandoliers + " left)");
        }

        public void Say(string text)
        {
            if (say != null && loadout != null) say.Invoke(loadout, new object[] { text });
        }
    }
}
