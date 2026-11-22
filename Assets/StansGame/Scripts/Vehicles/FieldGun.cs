using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame
{
    /// <summary>
    /// A towed anti-tank gun on its trails (5 cm PaK 38): the upper carriage traverses on the lower carriage, the barrel
    /// elevates in its cradle and runs back on recoil.
    ///
    /// Player: walk up behind the shield and press E to man it. The mouse lays the gun (traverse limited to the
    /// carriage, as on the real gun), the right mouse button looks through the 3x sight, the left mouse button fires,
    /// E leaves. Wally Parr's party turned the captured gun at the bridge round on the château; you can too.
    ///
    /// AI: while one of its crew is alive and close by and no player is on it, the gun engages enemy vehicles
    /// (tanks, half-tracks) in its arc of fire.
    /// </summary>
    public class FieldGun : MonoBehaviour
    {
        public static readonly List<FieldGun> All = new List<FieldGun>();

        public string displayName = "5 cm PaK 38";
        [Header("Moving parts (set up by the level builder)")]
        public Transform traverse;   // turns left/right about Y
        public Transform elevate;    // tilts about X (child of traverse)
        public Transform barrel;     // child of elevate; slides back on recoil
        public Transform muzzle;
        public Transform sight;      // the gunner's eye at the sight
        [Header("Laying")]
        public float traverseLimit = 32f;            // PaK 38: 65 degrees in all
        public float minElevation = -8f, maxElevation = 27f;
        public float traverseSpeed = 30f, elevateSpeed = 15f;
        [Header("Firing")]
        public Shell shellPrefab;
        public float shellSpeed = 823f;              // Pzgr 39
        public float reloadTime = 3.5f;
        public float recoilLength = 0.35f;
        [Header("AI crew")]
        public List<Health> crew = new List<Health>();
        public float aiRange = 500f;
        public float mouseSensitivity = 0.08f;
        [Header("Machine gun on a tripod (instead of a shell)")]
        [Tooltip("Set for a mounted machine gun (Vickers, Browning .30): holding the left button fires it like a hand-held gun, endless belts")]
        public Weapon gun;
        [Tooltip("The gunner's view, behind and above the traverse (third person)")]
        public Vector3 eyeOffset = new Vector3(-0.55f, 1.35f, -2.4f);

        public bool Manned => gunner != null;
        public float ReloadProgress => Mathf.Clamp01(1f - (nextShot - Time.time) / reloadTime);
        /// <summary>Where the gunner stands: behind the shield, by the breech.</summary>
        public Vector3 GunnerSpot => (traverse ? traverse.position : transform.position) - Flat(RestForward) * 1.0f;

        SoldierController gunner;
        Camera cam;
        float yaw, pitch, aimYaw, aimPitch, nextShot, recoil, mannedAt;
        Vector3 barrelRest;
        Quaternion traverseRest, elevateRest;
        AudioSource audioSrc;
        float nextScan;
        Transform aiTarget;

        Vector3 RestForward => traverse && traverse.parent ? traverse.parent.rotation * traverseRest * Vector3.forward : transform.forward;
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

        void Awake()
        {
            if (traverse) traverseRest = traverse.localRotation;
            if (elevate) elevateRest = elevate.localRotation;
            if (barrel) barrelRest = barrel.localPosition;
            audioSrc = gameObject.AddComponent<AudioSource>();
            audioSrc.spatialBlend = 1f; audioSrc.minDistance = 8f; audioSrc.maxDistance = 1500f; audioSrc.playOnAwake = false; audioSrc.dopplerLevel = 0f;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        // ------------------------------------------------------------------ manning

        public bool CanMan(SoldierController s) => !gunner && s && s.enabled;

        public void Man(SoldierController s)
        {
            if (!CanMan(s)) return;
            gunner = s; mannedAt = Time.time;
            cam = s.cam ? s.cam : Camera.main;
            s.SetControlActive(false);
            aimYaw = yaw; aimPitch = pitch;
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        }

        public void Leave()
        {
            if (!gunner) return;
            var g = gunner; gunner = null;
            Vector3 exit = GunnerSpot - Flat(RestForward) * 0.4f + (traverse ? traverse.right : transform.right) * -0.6f;
            g.SetControlActive(true, exit + Vector3.up * 0.1f);
            if (cam) cam.fieldOfView = 60f;
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            if (gunner)
            {
                var gh = gunner.GetComponent<Health>();
                if (gh && gh.IsDead) { Leave(); return; }
                var kb = Keyboard.current; var mouse = Mouse.current;
                if (kb != null && mouse != null)
                {
                    if (kb.eKey.wasPressedThisFrame && Time.time - mannedAt > 0.3f) { Leave(); return; }
                    float zoom = mouse.rightButton.isPressed ? 0.35f : 1f;   // finer laying through the sight
                    Vector2 look = mouse.delta.ReadValue() * mouseSensitivity * zoom;
                    aimYaw = Mathf.Clamp(aimYaw + look.x, -traverseLimit, traverseLimit);
                    aimPitch = Mathf.Clamp(aimPitch + look.y, minElevation, maxElevation);
                    if (gun) { if (mouse.leftButton.isPressed) FireGun(); }
                    else if (mouse.leftButton.wasPressedThisFrame) Fire();
                }
            }
            else AiCrew();

            yaw = Mathf.MoveTowards(yaw, aimYaw, traverseSpeed * Time.deltaTime);
            pitch = Mathf.MoveTowards(pitch, aimPitch, elevateSpeed * Time.deltaTime);
            if (traverse) traverse.localRotation = traverseRest * Quaternion.Euler(0f, yaw, 0f);
            if (elevate) elevate.localRotation = elevateRest * Quaternion.Euler(-pitch, 0f, 0f);
            // the barrel runs back fast and comes forward again over a second
            recoil = Mathf.MoveTowards(recoil, 0f, Time.deltaTime * recoilLength * 1.2f);
            if (barrel) barrel.localPosition = barrelRest + Vector3.back * recoil;
        }

        void LateUpdate()
        {
            if (!gunner || !cam || !elevate) return;
            bool sightView = Mouse.current != null && Mouse.current.rightButton.isPressed;
            Quaternion look = elevate.rotation;
            Vector3 eye = sightView && sight ? sight.position
                        : (traverse ? traverse.position : transform.position) + (traverse ? traverse : transform).rotation * eyeOffset;
            cam.transform.SetPositionAndRotation(eye, sightView ? look : Quaternion.Euler(look.eulerAngles.x + 6f, look.eulerAngles.y, 0f));
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, sightView ? 18f : 55f, Time.deltaTime * 10f);
        }

        // ------------------------------------------------------------------ firing

        /// <summary>The mounted machine gun: one round (its own rate of fire, tracer, sound), the belt never runs out.</summary>
        public bool FireGun()
        {
            if (!gun || !muzzle || !gunner) return false;
            gun.Init();
            if (gun.Ammo <= 0) { gun.SetAmmo(gun.magazineSize, 9999); return false; }   // a fresh belt
            Vector3 dir = elevate ? elevate.forward : muzzle.forward;
            if (!gun.Fire(new Ray(muzzle.position, dir), true, gunner.gameObject, 0.6f)) return false;
            recoil = recoilLength;
            NoiseEvents.Report(transform.position, 300f, gameObject);
            return true;
        }

        public bool Fire()
        {
            if (Time.time < nextShot || !muzzle || !shellPrefab) return false;
            nextShot = Time.time + reloadTime;
            Vector3 dir = elevate ? elevate.forward : muzzle.forward;
            var shell = Instantiate(shellPrefab, muzzle.position, Quaternion.LookRotation(dir));
            Vector3 vel = dir * shellSpeed;
            shell.Launch(vel, gameObject);
            NetHooks.ProjectileLaunched?.Invoke(shellPrefab, muzzle.position, vel, gameObject);
            recoil = recoilLength;
            var clip = Sfx.GunClip("tank");
            if (clip) audioSrc.PlayOneShot(clip, 1f);
            MuzzleBlast(muzzle.position, dir);
            NoiseEvents.Report(transform.position, 450f, gameObject);
            return true;
        }

        static void MuzzleBlast(Vector3 p, Vector3 dir)
        {
            var go = new GameObject("Muzzle blast");
            go.transform.position = p + dir * 0.3f;
            var flash = new GameObject("Flash"); flash.transform.SetParent(go.transform, false);
            var l = flash.AddComponent<Light>(); l.type = LightType.Point; l.range = 18f; l.intensity = 14f; l.color = new Color(1f, 0.72f, 0.4f);
            // a puff of dust and smoke kicked up in front of the gun
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.duration = 0.2f; main.loop = false; main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 7f); main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            main.startColor = new Color(0.55f, 0.52f, 0.47f, 0.55f); main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = -0.05f;
            var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 26) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f; sh.radius = 0.1f;
            go.transform.rotation = Quaternion.LookRotation(dir);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                                             new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = SmokeMaterial();
            ps.Play();
            Object.Destroy(flash, 0.1f);
            Object.Destroy(go, 3f);
        }

        static Material smoke;
        static Material SmokeMaterial()
        {
            if (smoke) return smoke;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            smoke = new Material(sh) { name = "Muzzle smoke" };
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 32f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d)));
                }
            tex.Apply();
            smoke.mainTexture = tex;
            if (smoke.HasProperty("_BaseMap")) smoke.SetTexture("_BaseMap", tex);
            if (smoke.HasProperty("_Surface")) { smoke.SetFloat("_Surface", 1f); smoke.SetFloat("_Blend", 0f); }
            smoke.SetOverrideTag("RenderType", "Transparent");
            smoke.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            smoke.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            smoke.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            smoke.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            smoke.SetInt("_ZWrite", 0);
            return smoke;
        }

        // ------------------------------------------------------------------ AI crew

        Faction? CrewFaction()
        {
            foreach (var h in crew) if (h) return h.faction;
            return null;
        }

        bool CrewAtTheGun()
        {
            if (Net.IsRemoteClient) return false;   // the host's copy does the shooting
            foreach (var h in crew)
                if (h && !h.IsDead && Vector3.Distance(h.transform.position, transform.position) < 5f) return true;
            return false;
        }

        void AiCrew()
        {
            if (!CrewAtTheGun()) { aiTarget = null; return; }
            if (Time.time >= nextScan)
            {
                nextScan = Time.time + 1f;
                aiTarget = null;
                var mine = CrewFaction();
                float best = aiRange;
                foreach (var tank in FindObjectsByType<TankController>(FindObjectsInactive.Exclude))
                {
                    var h = tank.GetComponent<Health>();
                    if (!h || h.IsDead || (mine.HasValue && h.faction == mine.Value)) continue;
                    Vector3 to = tank.transform.position - transform.position;
                    float d = to.magnitude;
                    if (d > best) continue;
                    if (Mathf.Abs(Vector3.SignedAngle(Flat(RestForward), Flat(to), Vector3.up)) > traverseLimit) continue;
                    Vector3 from = muzzle ? muzzle.position : transform.position + Vector3.up;
                    Vector3 aimAt = tank.transform.position + Vector3.up * 1.2f;
                    if (Physics.Linecast(from, aimAt, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(tank.transform)) continue;
                    best = d; aiTarget = tank.transform;
                }
            }
            if (!aiTarget) return;
            Vector3 target = aiTarget.position + Vector3.up * 1.2f;
            Vector3 toT = target - (elevate ? elevate.position : transform.position);
            aimYaw = Mathf.Clamp(Vector3.SignedAngle(Flat(RestForward), Flat(toT), Vector3.up), -traverseLimit, traverseLimit);
            float flat = new Vector2(toT.x, toT.z).magnitude;
            aimPitch = Mathf.Clamp(Mathf.Atan2(toT.y, flat) * Mathf.Rad2Deg, minElevation, maxElevation);
            if (Mathf.Abs(yaw - aimYaw) < 1f && Mathf.Abs(pitch - aimPitch) < 1f) Fire();
        }

        // ------------------------------------------------------------------ gunner's view and prompts

        static Texture2D px;

        void OnGUI()
        {
            if (!gunner) return;
            if (!px) { px = new Texture2D(1, 1); px.SetPixel(0, 0, Color.white); px.Apply(); }
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            bool sightView = Mouse.current != null && Mouse.current.rightButton.isPressed;
            GUI.color = new Color(0.05f, 0.05f, 0.05f, 0.9f);
            if (sightView)
            {
                // ZF 3x8: a pointed post in the middle, chevrons either side and range marks below
                float s = Screen.height / 540f;
                GUI.DrawTexture(new Rect(cx - 1.5f * s, cy, 3f * s, 60f * s), px);
                for (int i = 1; i <= 3; i++)
                {
                    float dx = i * 40f * s;
                    foreach (float sgn in new[] { -1f, 1f })
                    {
                        GUI.DrawTexture(new Rect(cx + sgn * dx - 1.5f * s, cy, 3f * s, 16f * s), px);
                        GUI.DrawTexture(new Rect(cx + sgn * dx - 8f * s, cy + 16f * s, 16f * s, 2f * s), px);
                    }
                }
                for (int i = 1; i <= 4; i++) GUI.DrawTexture(new Rect(cx - 6f * s, cy + 70f * s + i * 18f * s, 12f * s, 2f * s), px);
                // the edge of the eyepiece
                GUI.color = new Color(0f, 0f, 0f, 0.85f);
                float r = Screen.height * 0.46f;
                GUI.DrawTexture(new Rect(0, 0, cx - r, Screen.height), px);
                GUI.DrawTexture(new Rect(cx + r, 0, Screen.width - cx - r, Screen.height), px);
            }
            else
            {
                GUI.color = new Color(1f, 1f, 1f, 0.85f);
                GUI.DrawTexture(new Rect(cx - 9, cy - 1, 18, 2), px); GUI.DrawTexture(new Rect(cx - 1, cy - 9, 2, 18), px);
            }
            GUI.color = Color.white;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.LowerCenter, richText = true };
            st.normal.textColor = new Color(0.95f, 0.9f, 0.78f);
            string state = Time.time < nextShot ? $"loading... {ReloadProgress * 100f:F0}%" : "ready";
            GUI.Label(new Rect(0, Screen.height - 70, Screen.width, 28), $"<b>{displayName}</b>   {state}   elevation {pitch:+0;-0}°  traverse {yaw:+0;-0}°", st);
            GUI.Label(new Rect(0, Screen.height - 44, Screen.width, 28), "Mouse: lay the gun    Right mouse: sight    Left mouse: fire    E: leave the gun", st);
        }
    }
}
