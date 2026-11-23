using UnityEngine;

namespace StansGame
{
    public enum WeaponSlot { Primary, Sidearm }

    /// <summary>How the weapon is carried: decides where the gun sits against the body and where the hands go.</summary>
    public enum HoldStyle { Rifle, SMG, Heavy, Pistol, Launcher }

    /// <summary>
    /// Hitscan firearm: Sten, Bren, Lee-Enfield, Kar98k, MP 40, MG 42, Luger P08, M1911 (and the PIAT, which fires a
    /// projectile). Fire() is called by the player or AI.
    /// </summary>
    public class Weapon : MonoBehaviour
    {
        public string displayName = "Sten Mk II";
        [Tooltip("Which inventory slot it goes in. Pistols are side arms; everything else is a main weapon.")]
        public WeaponSlot slot = WeaponSlot.Primary;
        public float damage = 34f;
        public float roundsPerMinute = 500f;
        public bool automatic = true;
        public int magazineSize = 32;
        public int spareMagazines = 6;
        public float reloadTime = 2.4f;
        public float range = 200f;
        [Tooltip("Spread cone in degrees when hip firing / aiming.")]
        public float hipSpread = 3.5f, aimSpread = 0.8f;
        public float impactForce = 60f;
        public Transform muzzle;
        public ParticleSystem muzzleFlash;
        public Light muzzleLight;
        public AudioSource audioSource;
        public AudioClip fireClip, reloadClip, emptyClip;
        [Tooltip("Played with the last round (the M1 Garand's clip pinging out).")]
        public AudioClip lastRoundClip;
        public GameObject impactPrefab;
        public LayerMask hitMask = ~0;
        [Header("How it is held (set by name in ApplySpecs)")]
        public HoldStyle hold = HoldStyle.Rifle;
        [Tooltip("The middle of what rests in the left palm, in the gun's own space (metres from the model's pivot; +Z is towards the muzzle).")]
        public Vector3 supportHand = new Vector3(0f, -0.03f, 0.22f);
        [Tooltip("Where the left hand goes while reloading (the magazine, or the top of the action for stripper clips).")]
        public Vector3 magazinePoint = new Vector3(0f, -0.12f, 0.06f);
        [Tooltip("The middle of the grip the right palm closes round, in the gun's own space (metres from the model's pivot).")]
        public Vector3 handOffset = Vector3.zero;
        [Header("Reloading with a loose magazine (Sten, MP 40)")]
        [Tooltip("The magazine as its own part (cut from the model by the level builder); none = the hand just goes to the magazine.")]
        public Transform magazine;
        [Tooltip("Which way, and how far, the magazine comes out (gun space).")]
        public Vector3 magazinePull = new Vector3(0f, -0.2f, 0f);
        [Tooltip("The cocking handle the left hand pulls back at the end of a reload (gun space); zero = none.")]
        public Vector3 cockingPoint = Vector3.zero;
        [Header("Projectile weapons (PIAT)")]
        public Shell projectile;
        public float projectileSpeed = 77f;

        public int Ammo { get; private set; }
        public int SpareRounds { get; private set; }
        public bool IsReloading { get; private set; }

        float nextShot;
        float reloadDone;
        /// <summary>How far through the reload (0 to 1), 0 when not reloading.</summary>
        public float ReloadProgress => IsReloading && reloadTime > 0f ? Mathf.Clamp01(1f - (reloadDone - Time.time) / reloadTime) : 0f;

        // ------------------------------------------------------------------ the loose magazine during a reload
        // The reload in steps (fractions of the reload time); HandIK moves the left hand along the same steps.
        public const float MagGrab = 0.12f, MagOut = 0.28f, MagPouch = 0.48f, MagFresh = 0.56f, MagBack = 0.72f, MagIn = 0.82f, CockAt = 0.86f, CockBack = 0.92f;
        Vector3 magRestPos; Quaternion magRestRot; bool magRest, magDropped;

        /// <summary>
        /// Puts the magazine where the left hand has it: <paramref name="hand"/> is the hand's point in gun space; the
        /// magazine keeps the same offset from it as when seated. Called by HandIK each frame of a reload.
        /// </summary>
        public void HoldMagazine(Vector3 hand)
        {
            if (!magazine) return;
            KeepMagRest();
            float p = ReloadProgress;
            var r = magazine.GetComponent<Renderer>();
            bool carried = (p >= MagGrab && p < MagOut) || (p >= MagFresh && p < MagIn);
            if (p >= MagOut && !magDropped) { magDropped = true; DropMagazine(); }
            if (r) r.enabled = !(p >= MagOut && p < MagFresh);
            Vector3 seat = magazine.parent.TransformPoint(magRestPos);
            magazine.rotation = magazine.parent.rotation * magRestRot;
            magazine.position = carried ? seat + transform.TransformVector(hand - magazinePoint) : seat;
        }

        void KeepMagRest()
        {
            if (magRest || !magazine) return;
            magRest = true; magRestPos = magazine.localPosition; magRestRot = magazine.localRotation;
        }

        void SeatMagazine()
        {
            if (!magazine) return;
            KeepMagRest();
            magazine.localPosition = magRestPos; magazine.localRotation = magRestRot;
            var r = magazine.GetComponent<Renderer>(); if (r) r.enabled = true;
            magDropped = false;
        }

        /// <summary>The empty magazine falls to the ground (a copy, gone after a while).</summary>
        void DropMagazine()
        {
            if (!magazine || !magazine.gameObject.activeInHierarchy) return;
            var copy = Instantiate(magazine.gameObject, magazine.position, magazine.rotation);
            copy.name = "Empty magazine";
            copy.transform.localScale = magazine.lossyScale;
            var mf = copy.GetComponent<MeshFilter>();
            var box = copy.AddComponent<BoxCollider>();
            if (mf && mf.sharedMesh) { box.center = mf.sharedMesh.bounds.center; box.size = Vector3.Max(mf.sharedMesh.bounds.size, Vector3.one * 0.01f); }
            var rb = copy.AddComponent<Rigidbody>();
            rb.mass = 0.3f;
            rb.linearVelocity = transform.TransformDirection(magazinePull.normalized) * 1.2f;
            rb.angularVelocity = Random.insideUnitSphere * 4f;
            Destroy(copy, 20f);
        }
        float lightOff;

        /// <summary>
        /// What a soldier really carried for each weapon: magazine (or clip, drum) size, how many spare ones, how long
        /// a reload takes. Applied by name so every copy of a weapon (player, AI, picked up) behaves the same.
        /// </summary>
        void ApplySpecs()
        {
            string n = displayName.ToLowerInvariant();
            void Set(int mag, int spare, float reload) { magazineSize = mag; spareMagazines = spare; reloadTime = reload; }
            // Where the hands go, in the gun's own space (x right, y up, z to the muzzle; metres from the model's pivot),
            // measured off each model from the side (Stan's Game > Debug > Capture Weapon Grips, 5 cm grid):
            //   palm    - the middle of the grip the right hand closes round (pistol grip, or the wrist of the stock)
            //   support - the middle of what rests in the left palm (barrel, forestock, sleeve, jacket; a pistol: the right fist)
            //   mag     - where the left hand goes while reloading
            // the cocking handle: on the barrel's line, to one side (+1 right, -1 left), this far ahead of the palm
            void Cock(float side, float ahead)
            {
                Vector3 bore = muzzle ? transform.InverseTransformPoint(muzzle.position) : new Vector3(0f, 0.08f, 0f);
                cockingPoint = new Vector3(bore.x + side * 0.035f, bore.y, handOffset.z + ahead);
            }
            void Hold(HoldStyle h, Vector3 palm, Vector3 support, Vector3 mag)
            {
                hold = h; handOffset = palm; supportHand = support; magazinePoint = mag;
            }
            if (n.Contains("sten"))
            {   Set(32, 7, 2.6f);                                            // 8 magazines in the airborne webbing
                Hold(HoldStyle.SMG, new Vector3(0f, 0.055f, 0.03f), new Vector3(0f, 0.09f, 0.38f), new Vector3(-0.13f, 0.09f, 0.297f));
                magazinePull = new Vector3(-0.2f, 0f, 0f); Cock(+1f, 0.02f); }   // the magazine slides out to the left; the cocking handle is in the slot on the right   // right hand round the trigger housing, the barrel sleeve in the left palm (never the side magazine)
            else if (n.Contains("bren"))
            {   Set(28, 8, 3.2f);                                            // 30-round magazine, loaded with 28 to stop jams
                Hold(HoldStyle.Heavy, new Vector3(0f, 0.03f, -0.07f), new Vector3(0f, 0.10f, 0.36f), new Vector3(0f, 0.2f, 0.05f)); }  // the pistol grip; from the hip, the gas cylinder under the barrel in the left palm; magazine on top
            else if (n.Contains("lee") || n.Contains("enfield"))
            {   Set(10, 5, 4.0f);                                            // 10 in the magazine, 50 in the bandolier, refilled with 5-round chargers
                Hold(HoldStyle.Rifle, new Vector3(0f, 0.015f, -0.03f), new Vector3(0f, 0.07f, 0.30f), new Vector3(0f, 0.09f, 0.02f)); }  // the wrist of the stock; the forestock in the left palm; chargers pushed in from the top
            else if (n.Contains("kar"))
            {   Set(5, 12, 3.2f);                                            // 5-round stripper clips, 60 rounds in the two pouches
                Hold(HoldStyle.Rifle, new Vector3(0f, 0.015f, -0.04f), new Vector3(0f, 0.075f, 0.30f), new Vector3(0f, 0.09f, 0.02f)); }
            else if (n.Contains("mp40") || n.Contains("mp 40"))
            {   Set(32, 5, 2.6f);                                            // 6 magazines in the two pouches
                Hold(HoldStyle.SMG, new Vector3(0f, 0.06f, -0.04f), new Vector3(0f, 0.11f, 0.33f), new Vector3(0f, -0.12f, 0.27f));
                magazinePull = new Vector3(0f, -0.2f, 0.02f); Cock(-1f, 0.12f); }   // the magazine drops out underneath; the bolt handle is on the left // the raked pistol grip; the barrel in the left palm, ahead of the magazine
            else if (n.Contains("mg42") || n.Contains("mg 42"))
            {   Set(50, 4, 6.5f);                                            // 50-round drum; the team carried the 250-round belts
                Hold(HoldStyle.Heavy, new Vector3(0f, -0.05f, -0.01f), new Vector3(0f, 0.07f, 0.35f), new Vector3(-0.09f, 0f, 0.05f)); } // the pistol grip; from the hip on the move, the jacket on the left palm; drum on the left
            else if (n.Contains("luger") || n.Contains("p08"))
            {   Set(8, 2, 1.8f);
                Hold(HoldStyle.Pistol, new Vector3(0f, 0.025f, -0.015f), new Vector3(0f, 0f, -0.01f), new Vector3(0f, -0.06f, -0.01f)); } // left hand cupped under and round the right fist
            else if (n.Contains("1911"))
            {   Set(7, 2, 1.8f);
                Hold(HoldStyle.Pistol, new Vector3(0f, 0.025f, -0.015f), new Vector3(0f, 0f, -0.01f), new Vector3(0f, -0.06f, -0.01f)); }
            else if (n.Contains("piat"))
            {   Set(1, 2, 5f);                                               // bombs came in 3-round carriers
                Hold(HoldStyle.Launcher, Vector3.zero, new Vector3(0f, -0.05f, 0.2f), new Vector3(0f, 0.05f, 0.3f)); }
            else if (n.Contains("garand"))
            {   Set(8, 10, 2.2f);                                            // 8-round en-bloc clips: 10 in the cartridge belt and bandoliers
                Hold(HoldStyle.Rifle, new Vector3(0f, 0.015f, -0.03f), new Vector3(0f, 0.07f, 0.30f), new Vector3(0f, 0.09f, 0.02f)); }  // the wrist of the stock; the forestock in the left palm; the clip goes in from the top
            else if (n.Contains("carbine"))
            {   Set(15, 6, 2.4f);                                            // 15-round magazines, two in the stock pouch
                Hold(HoldStyle.Rifle, new Vector3(0f, 0.005f, -0.005f), new Vector3(0f, 0.085f, 0.27f), new Vector3(0f, -0.06f, 0.13f)); } // the M1A1's pistol grip; the forestock in the left palm
            else if (n.Contains("thompson"))
            {   Set(30, 5, 3.0f);                                            // 20- and 30-round box magazines
                Hold(HoldStyle.SMG, Vector3.zero, new Vector3(0f, 0.075f, 0.27f), new Vector3(0f, -0.06f, 0.12f)); } // the pistol grip; the forend in the left palm
            else if (n.Contains("bazooka"))
            {   Set(1, 3, 5f);                                               // the loader carried the rockets
                Hold(HoldStyle.Launcher, Vector3.zero, new Vector3(0f, -0.02f, 0.21f), new Vector3(0f, 0.05f, -0.6f)); } // the trigger grip; the left hand on the front grip; loaded from the rear
            else if (n.Contains("pistol") || n.Contains("revolver"))
                Hold(HoldStyle.Pistol, new Vector3(0f, 0.025f, -0.015f), new Vector3(0f, 0f, -0.01f), new Vector3(0f, -0.06f, -0.01f));
        }

        void Awake() => Init();

        void OnDisable() => SeatMagazine();   // put away mid-reload: the magazine goes back in

        bool initialised;

        /// <summary>Sets up the magazine and pouches; safe to call more than once (a holstered weapon is inactive, so its Awake has not run yet).</summary>
        public void Init()
        {
            if (initialised) return;
            initialised = true;
            ApplySpecs();
            // the carrying sling (WeaponSling): every long weapon had one in 1944; pistols had a lanyard
            if (hold != HoldStyle.Pistol && !GetComponent<WeaponSling>()) gameObject.AddComponent<WeaponSling>();
            Ammo = magazineSize;
            SpareRounds = magazineSize * spareMagazines;
            string n = displayName.ToLowerInvariant();
            if (n.Contains("luger") || n.Contains("p08") || n.Contains("1911") || n.Contains("pistol") || n.Contains("revolver")) slot = WeaponSlot.Sidearm;
            if (!audioSource) audioSource = GetComponent<AudioSource>();
            Sfx.SetupWeapon(this);
            if (muzzleLight) muzzleLight.enabled = false;
        }

        void Update()
        {
            if (IsReloading && Time.time >= reloadDone)
            {
                int need = magazineSize - Ammo;
                int take = Mathf.Min(need, SpareRounds);
                Ammo += take; SpareRounds -= take;
                IsReloading = false;
                SeatMagazine();
            }
            if (muzzleLight && muzzleLight.enabled && Time.time > lightOff) muzzleLight.enabled = false;
        }

        /// <summary>Attempts a shot along aimRay. Returns true if a round was fired.</summary>
        /// <summary>Someone's shot hit a soldier: (shooter, victim, killed). The crosshair shows a hit marker for the player's own hits.</summary>
        public static event System.Action<GameObject, Health, bool> HitConfirmed;
        /// <summary>A bullet went from here to there (hit or miss): soldiers near its path feel the fire and get down.</summary>
        public static event System.Action<Vector3, Vector3, GameObject> ShotPassed;

        float bloom;          // extra spread from firing quickly (degrees), settles in about half a second
        float lastShot;
        /// <summary>The spread a shot would have right now (degrees), for the crosshair.</summary>
        public float CurrentSpread(bool aiming, float scale) => ((aiming ? aimSpread : hipSpread) + Bloom) * scale;
        float Bloom => Mathf.Max(0f, bloom - (Time.time - lastShot) * (automatic ? 6f : 4f));

        public bool Fire(Ray aimRay, bool aiming, GameObject shooter) => Fire(aimRay, aiming, shooter, 1f);

        public bool Fire(Ray aimRay, bool aiming, GameObject shooter, float spreadScale)
        {
            if (IsReloading || Time.time < nextShot) return false;
            if (Ammo <= 0)
            {
                if (emptyClip && audioSource) audioSource.PlayOneShot(emptyClip);
                nextShot = Time.time + 0.3f;
                return false;
            }
            nextShot = Time.time + 60f / roundsPerMinute;
            Ammo--;

            float spread = CurrentSpread(aiming, spreadScale);
            // each shot adds a little: an automatic climbs, a bolt-action or pistol barely
            bloom = Bloom + (automatic ? Mathf.Lerp(0.25f, 0.6f, Mathf.InverseLerp(400f, 1200f, roundsPerMinute)) : hold == HoldStyle.Pistol ? 0.8f : 0.35f) * spreadScale;
            bloom = Mathf.Min(bloom, (aiming ? aimSpread : hipSpread) * 2.5f + 2f);
            lastShot = Time.time;
            Vector3 dir = Quaternion.Euler(Random.Range(-spread, spread) * 0.5f, Random.Range(-spread, spread) * 0.5f, 0f) * aimRay.direction;
            Vector3 origin = aimRay.origin;

            if (projectile)
            {
                Vector3 from = muzzle ? muzzle.position : origin;
                // aim the bomb at whatever the crosshair is on
                Vector3 aimPoint = Physics.Raycast(origin, dir, out RaycastHit ah, range, hitMask, QueryTriggerInteraction.Ignore) ? ah.point : origin + dir * range;
                Vector3 vel = (aimPoint - from).normalized * projectileSpeed;
                var shell = Instantiate(projectile, from, Quaternion.LookRotation(vel));
                shell.visualOnly = Net.IsRemoteClient;    // on a client the host's copy does the damage
                shell.Launch(vel, shooter);
                NetHooks.ProjectileLaunched?.Invoke(projectile, from, vel, shooter);
            }
            else if (Physics.Raycast(origin, dir, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore))
            {
                var health = hit.collider.GetComponentInParent<Health>();
                if (health && health.gameObject != shooter)
                {
                    float dmg = damage;
                    if (hit.collider.name.ToLower().Contains("head")) dmg *= 3f;
                    var info = new DamageInfo
                    {
                        amount = dmg, point = hit.point, direction = dir, force = impactForce,
                        source = shooter, hitBody = hit.rigidbody
                    };
                    bool wasAlive = !health.IsDead;
                    if (Net.IsRemoteClient && NetHooks.RequestDamage != null) NetHooks.RequestDamage(health, info);
                    else health.TakeDamage(info);
                    HitConfirmed?.Invoke(shooter, health, wasAlive && health.IsDead);
                }
                else if (hit.rigidbody)
                {
                    hit.rigidbody.AddForceAtPosition(dir * impactForce * 0.2f, hit.point, ForceMode.Impulse);
                }
                if (impactPrefab)
                {
                    var fx = Instantiate(impactPrefab, hit.point, Quaternion.LookRotation(hit.normal));
                    Destroy(fx, 3f);
                }
                if (Random.value < 0.5f) Sfx.Impact(hit.point, hit.collider.GetComponentInParent<Health>());
                Tracer.Spawn(muzzle ? muzzle.position : origin, hit.point);
                ShotPassed?.Invoke(origin, hit.point, shooter);
                NetHooks.ShotFired?.Invoke(muzzle ? muzzle.position : origin, hit.point);
            }
            else
            {
                Tracer.Spawn(muzzle ? muzzle.position : origin, origin + dir * range);
                ShotPassed?.Invoke(origin, origin + dir * range, shooter);
                NetHooks.ShotFired?.Invoke(muzzle ? muzzle.position : origin, origin + dir * range);
            }

            if (muzzleFlash) muzzleFlash.Play();
            if (muzzleLight) { muzzleLight.enabled = true; lightOff = Time.time + 0.05f; }
            if (fireClip && audioSource) audioSource.PlayOneShot(fireClip);
            if (Ammo == 0 && lastRoundClip && audioSource) audioSource.PlayOneShot(lastRoundClip, 0.7f);
            NoiseEvents.Report(transform.position, 60f, shooter);
            return true;
        }

        public void Reload()
        {
            if (IsReloading || Ammo == magazineSize || SpareRounds <= 0) return;
            IsReloading = true;
            reloadDone = Time.time + reloadTime;
            SeatMagazine();
            if (reloadClip && audioSource) audioSource.PlayOneShot(reloadClip);
        }

        /// <summary>The most spare rounds a soldier can carry for this weapon (his pouches are only so big).</summary>
        public int MaxSpare => magazineSize * (spareMagazines + 2);

        /// <summary>Adds spare rounds up to what can be carried; returns how many were taken.</summary>
        public int AddSpareRounds(int rounds)
        {
            int take = Mathf.Clamp(MaxSpare - SpareRounds, 0, rounds);
            SpareRounds += take;
            return take;
        }

        public void SetAmmo(int loaded, int spare) { Init(); Ammo = Mathf.Clamp(loaded, 0, magazineSize); SpareRounds = Mathf.Max(0, spare); IsReloading = false; SeatMagazine(); }
    }

    /// <summary>Gunfire is loud: lets AI hear shots within a radius.</summary>
    public static class NoiseEvents
    {
        public static event System.Action<Vector3, float, GameObject> Noise;
        public static void Report(Vector3 pos, float radius, GameObject source) => Noise?.Invoke(pos, radius, source);
    }
}
