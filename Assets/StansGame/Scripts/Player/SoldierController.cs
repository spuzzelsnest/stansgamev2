using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame
{
    /// <summary>
    /// Third-person over-the-shoulder soldier (V switches to first person and back). WASD move, Shift sprint, C crouch,
    /// Space jump, right mouse aim, left mouse fire, R reload, E use (pick up, enter vehicles).
    /// The kit and the slot keys 1-4 are handled by the Loadout component.
    /// Works with a capsule placeholder; drives Animator parameters if an Animator is present.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class SoldierController : MonoBehaviour, IDisableOnDeath
    {
        [Header("Movement")]
        [Tooltip("m/s. A man in battle order: walking (aiming) ~1.5, a loaded run ~3.4, flat-out sprint ~5.6, kneeling ~1.1, crawling ~0.4.")]
        public float walkSpeed = 1.5f, runSpeed = 3.4f, sprintSpeed = 5.6f, crouchSpeed = 1.1f, proneSpeed = 0.4f;
        [Tooltip("How fast he gets up to speed and stops (m/s per second): about a second to reach a run, half that to stop.")]
        public float acceleration = 5f, deceleration = 9f;
        Vector3 groundVel;   // eased horizontal velocity
        public float jumpHeight = 0.9f, gravity = -18f, turnSpeed = 720f;

        [Header("Camera")]
        public Transform cameraRig;   // created automatically if empty
        public Camera cam;
        public Vector3 shoulderOffset = new Vector3(0.55f, 1.6f, -2.6f);
        public Vector3 aimOffset = new Vector3(0.6f, 1.55f, -1.3f);
        public float mouseSensitivity = 0.12f;
        public float pitchMin = -60f, pitchMax = 70f;
        public LayerMask cameraCollision = ~0;

        /// <summary>First-person view (V toggles). Kept between missions and respawns.</summary>
        public static bool FirstPerson
        {
            get { if (fpLoaded < 0) fpLoaded = PlayerPrefs.GetInt("stans.firstPerson", 0); return fpLoaded == 1; }
            set { fpLoaded = value ? 1 : 0; PlayerPrefs.SetInt("stans.firstPerson", fpLoaded); }
        }
        static int fpLoaded = -1;

        [Header("Weapons")]
        public Weapon[] weapons;
        public Animator animator;

        public bool IsAiming { get; private set; }
        public bool IsCrouching => stance && stance.Current == StanceKind.Crouch;
        public bool IsProne => stance && stance.Current == StanceKind.Prone;
        public Stance Stance => stance;
        Stance stance;
        public Weapon CurrentWeapon => weapons != null && weaponIndex < weapons.Length ? weapons[weaponIndex] : null;   // null: grenade in hand

        CharacterController cc;
        Loadout loadout;
        WeaponHolder holder;
        float yaw, pitch, verticalVel, camDistance;
        int weaponIndex;
        float standHeight;
        Vector3 currentCamOffset;

        void Awake()
        {
            // older prefabs still carry the first, too-quick speeds
            if (Mathf.Approximately(runSpeed, 4.2f)) { walkSpeed = 1.5f; runSpeed = 3.4f; sprintSpeed = 5.6f; crouchSpeed = 1.1f; proneSpeed = 0.4f; }
            cc = GetComponent<CharacterController>();
            loadout = GetComponent<Loadout>();
            standHeight = cc.height;
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (!cam) cam = Camera.main;
            if (!cameraRig)
            {
                cameraRig = new GameObject("CameraRig").transform;
            }
            yaw = transform.eulerAngles.y;
            currentCamOffset = shoulderOffset;
            SelectWeapon(0);
        }

        void OnEnable() { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        void OnDisable() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; ShowHead(true); }

        float stepTimer;
        Crosshair crosshair;
        RadioSet radio;
        BridgeControls bridgeControls;

        void Update()
        {
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            // look
            Vector2 look = GameInput.LookDelta * mouseSensitivity;   // nothing while a menu has the mouse
            bool mouseOn = GameInput.MouseActive;
            yaw += look.x;
            pitch = Mathf.Clamp(pitch - look.y, pitchMin, pitchMax);

            IsAiming = mouseOn && mouse.rightButton.isPressed;
            if (kb.vKey.wasPressedThisFrame) { FirstPerson = !FirstPerson; viewNoticeUntil = Time.unscaledTime + 2.5f; }
            // C: crouch (kneel), Z: go prone; the same key again, or Space, to stand up
            if (!stance && !TryGetComponent(out stance)) { stance = gameObject.AddComponent<Stance>(); stance.animator = animator; }
            if (kb.cKey.wasPressedThisFrame) stance.Set(IsCrouching ? StanceKind.Stand : StanceKind.Crouch);
            if (kb.zKey.wasPressedThisFrame) stance.Set(IsProne ? StanceKind.Crouch : StanceKind.Prone);
            if (!crosshair && !TryGetComponent(out crosshair)) crosshair = gameObject.AddComponent<Crosshair>();
            if (!radio && !TryGetComponent(out radio)) radio = gameObject.AddComponent<RadioSet>();
            if (!bridgeControls && !TryGetComponent(out bridgeControls)) bridgeControls = gameObject.AddComponent<BridgeControls>();

            // move
            Vector2 input = Vector2.zero;
            if (kb.wKey.isPressed) input.y += 1; if (kb.sKey.isPressed) input.y -= 1;
            if (kb.dKey.isPressed) input.x += 1; if (kb.aKey.isPressed) input.x -= 1;
            input = Vector2.ClampMagnitude(input, 1f);

            // sprinting gets him up off his knees (not off his belly: that takes a deliberate Space)
            if (kb.leftShiftKey.isPressed && IsCrouching && input.y > 0.1f) stance.Set(StanceKind.Stand);
            bool sprint = kb.leftShiftKey.isPressed && !IsAiming && stance.Current == StanceKind.Stand && input.y > 0.1f;
            float speed = IsProne ? proneSpeed : IsCrouching ? crouchSpeed : IsAiming ? walkSpeed : sprint ? sprintSpeed : runSpeed;
            if (stance.Changing) speed *= 0.3f;

            Quaternion camYaw = Quaternion.Euler(0f, yaw, 0f);
            Vector3 move = camYaw * new Vector3(input.x, 0f, input.y) * speed;

            // the soldier faces where the camera looks (he strafes and walks backwards with his weapon up);
            // only when sprinting does he turn to run the way he is going
            {
                // swimming, he turns the way he swims (the crawl only goes forwards)
                bool inWater = Water.Swimming(transform.position, out _);
                Quaternion target = (sprint || inWater) && move.sqrMagnitude > 0.001f ? Quaternion.LookRotation(move) : camYaw;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
            }

            // in the water: float with head and shoulders out, swim slowly, climb out at a quay wall or bank
            bool swimming = false;
            if (climbing) { ClimbStep(); move = Vector3.zero; }
            else if (Water.Swimming(transform.position, out float surface))
            {
                swimming = true;
                if (stance.Current != StanceKind.Stand) stance.Set(StanceKind.Stand);
                move = Vector3.ClampMagnitude(move, 1.5f);
                if (move.sqrMagnitude > 0.1f && Water.FindClimb(transform.position, move, surface, transform, out Vector3 top))
                    StartClimb(top + new Vector3(move.x, 0f, move.z).normalized * 0.2f);
                verticalVel = Mathf.Clamp((Water.FloatFeet(surface) - transform.position.y) * 2.5f, -3f, 2f);
            }
            if (!swimming && !climbing)
            {
                if (cc.isGrounded)
                {
                    verticalVel = -2f;
                    if (kb.spaceKey.wasPressedThisFrame)
                    {
                        if (stance.Current != StanceKind.Stand) stance.Set(StanceKind.Stand);   // Space: stand up first
                        else verticalVel = Mathf.Sqrt(jumpHeight * -2f * gravity);
                    }
                }
                verticalVel += gravity * Time.deltaTime;
            }
            // a body has weight: it takes a moment to get going and to stop (less control in the air)
            {
                Vector3 wantFlat = new Vector3(move.x, 0f, move.z);
                float rate = (wantFlat.sqrMagnitude > groundVel.sqrMagnitude ? acceleration : deceleration) * (cc.isGrounded || swimming ? 1f : 0.25f);
                groundVel = Vector3.MoveTowards(groundVel, wantFlat, rate * Time.deltaTime);
                if (climbing) groundVel = Vector3.zero;
                move.x = groundVel.x; move.z = groundVel.z;
            }
            move.y = verticalVel;
            if (!climbing) cc.Move(move * Time.deltaTime);
            Vector3 flatMove = new Vector3(move.x, 0f, move.z);
            if (cc.isGrounded && flatMove.sqrMagnitude > 0.5f)
            {
                stepTimer -= Time.deltaTime * flatMove.magnitude;
                if (stepTimer <= 0f) { stepTimer = 1.6f; Sfx.Footstep(transform.position, IsProne ? 0.06f : IsCrouching ? 0.15f : 0.35f); }
            }

            // the gun in his hands points where the camera looks
            if (!holder) holder = GetComponentInChildren<WeaponHolder>();
            if (holder && cam && holder.aimSource != cam.transform) holder.aimSource = cam.transform;

            // weapons (slot keys: see Loadout)
            var w = CurrentWeapon;
            if (w && !(loadout && loadout.Bandaging))
            {
                // (a quick tap on an automatic still fires: the button can go down and up between two frames)
                bool trigger = mouseOn && (w.automatic ? mouse.leftButton.isPressed || mouse.leftButton.wasPressedThisFrame : mouse.leftButton.wasPressedThisFrame);
                if (trigger && !sprint && !stance.Changing && !swimming && !climbing)
                {
                    // a steadier stance means a tighter group and less kick
                    if (w.Fire(AimRay(), IsAiming, gameObject, stance.SpreadScale * (flatMove.sqrMagnitude > 0.5f ? 1.5f : 1f)))
                        pitch -= (IsAiming ? 0.4f : 0.8f) * Mathf.Lerp(1f, stance.SpreadScale, 0.6f);
                }
                if (kb.rKey.wasPressedThisFrame) w.Reload();
            }

            if (animator)
            {
                Vector3 local = transform.InverseTransformDirection(new Vector3(groundVel.x, 0f, groundVel.z));
                Locomotion.Set(animator, local, 0.1f, stance.Current == StanceKind.Stand && !Water.Swimming(transform.position, out _));
                animator.SetBool("Aiming", IsAiming);
                animator.SetBool("Grounded", cc.isGrounded);
            }
        }

        void LateUpdate()
        {
            if (!cam) return;
            if (FirstPerson) { FirstPersonCamera(); return; }
            ShowHead(true);
            if (cam.nearClipPlane < 0.1f) cam.nearClipPlane = 0.1f;
            currentCamOffset = Vector3.Lerp(currentCamOffset, IsAiming ? aimOffset : shoulderOffset, Time.deltaTime * 12f);
            Vector3 pivot = transform.position + Vector3.up * (cc.height * 0.9f);
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 desired = transform.position + Quaternion.Euler(0f, yaw, 0f) * new Vector3(currentCamOffset.x, currentCamOffset.y * (cc.height / standHeight), 0f)
                              + rot * new Vector3(0f, 0f, currentCamOffset.z);
            // keep camera out of walls
            Vector3 dir = desired - pivot;
            if (Physics.SphereCast(pivot, 0.2f, dir.normalized, out RaycastHit hit, dir.magnitude, cameraCollision, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(transform))
                desired = pivot + dir.normalized * Mathf.Max(0.3f, hit.distance);
            cameraRig.SetPositionAndRotation(desired, rot);
            cam.transform.SetPositionAndRotation(desired, rot);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, IsAiming ? 45f : 60f, Time.deltaTime * 10f);
        }

        // ------------------------------------------------------------------ first-person view

        Transform head; Vector3 headScale = Vector3.one; bool headHidden;
        float adsBlend; Vector3 fpEye; bool haveFpEye;
        float viewNoticeUntil;

        void FirstPersonCamera()
        {
            if (!head && animator && animator.isHuman) { head = animator.GetBoneTransform(HumanBodyBones.Head); if (head) headScale = head.localScale; }
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 flat = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

            // the eyes: just in front of and above the head bone (which sits at the top of the neck) as animated,
            // so crouching, going prone, swimming and climbing all carry the view with them
            Vector3 eye = head ? head.position + transform.up * 0.09f + flat * 0.11f
                               : transform.position + Vector3.up * (stance ? stance.EyeHeight : 1.6f) + flat * 0.1f;
            // take the edge off the walk cycle's head bob
            fpEye = haveFpEye ? Vector3.Lerp(fpEye, eye, 1f - Mathf.Exp(-30f * Time.deltaTime)) : eye;
            haveFpEye = true;
            Vector3 pos = fpEye;

            // aiming: put the eye behind the sights (just above the bore line, at the stock)
            var w = CurrentWeapon;
            if (!holder) holder = GetComponentInChildren<WeaponHolder>();
            bool ads = IsAiming && w && holder && holder.Active == w && w.muzzle && !(loadout && loadout.Bandaging);
            adsBlend = Mathf.MoveTowards(adsBlend, ads ? 1f : 0f, Time.deltaTime * 6f);
            if (adsBlend > 0f && w && holder && w.muzzle)
            {
                Transform g = holder.transform;
                Vector3 m = g.InverseTransformPoint(w.muzzle.position);
                bool pistol = w.hold == HoldStyle.Pistol;
                Vector3 sight = g.TransformPoint(new Vector3(m.x, m.y + (pistol ? 0.045f : 0.06f), pistol ? -0.28f : -0.12f));
                pos = Vector3.Lerp(fpEye, sight, Mathf.SmoothStep(0f, 1f, adsBlend));
            }

            cameraRig.SetPositionAndRotation(pos, rot);
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.nearClipPlane = 0.03f;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, IsAiming ? 50f : 70f, Time.deltaTime * 10f);
            ShowHead(false);   // after reading the head position: shrink the head (and helmet) so it doesn't fill the view
        }

        void ShowHead(bool show)
        {
            if (!head || headHidden == !show) return;
            headHidden = !show;
            head.localScale = show ? headScale : headScale * 0.001f;
            if (show) haveFpEye = false;
        }

        void OnGUI()
        {
            if (Time.unscaledTime > viewNoticeUntil) return;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            st.normal.textColor = new Color(0.95f, 0.9f, 0.75f);
            GUI.Label(new Rect(0f, Screen.height * 0.62f, Screen.width, 30f), FirstPerson ? "First-person view   (V)" : "Third-person view   (V)", st);
        }

        // ------------------------------------------------------------------ climbing out of the water

        bool climbing; Vector3 climbFrom, climbTo; float climbT;

        void StartClimb(Vector3 top)
        {
            climbing = true; climbFrom = transform.position; climbTo = top; climbT = 0f;
            cc.enabled = false; verticalVel = 0f;
        }

        void ClimbStep()
        {
            climbT += Time.deltaTime / 1.0f;
            float k = Mathf.SmoothStep(0f, 1f, climbT);
            Vector3 up = new Vector3(climbFrom.x, Mathf.Lerp(climbFrom.y, climbTo.y + 0.3f, Mathf.Clamp01(k * 1.6f)), climbFrom.z);
            transform.position = Vector3.Lerp(up, climbTo, Mathf.Clamp01((k - 0.5f) * 2f));
            if (climbT >= 1f) { climbing = false; transform.position = climbTo; cc.enabled = true; }
        }

        public Ray AimRay()
        {
            Ray centre = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            // start the ray in front of the player so walls behind the camera are ignored
            float skip = Vector3.Dot(transform.position - centre.origin, centre.direction);
            centre.origin += centre.direction * Mathf.Max(0f, skip);
            return centre;
        }

        public int WeaponIndex => weaponIndex;

        public void SelectWeapon(int i)
        {
            if (weapons == null) return;
            weaponIndex = Mathf.Clamp(i, 0, 2);   // 0 main weapon, 1 side arm, 2 grenade in hand
            for (int k = 0; k < weapons.Length; k++) if (weapons[k]) weapons[k].gameObject.SetActive(k == weaponIndex);
        }

        /// <summary>Called by vehicles when the soldier gets in/out.</summary>
        /// <summary>Riding in a vehicle (out of sight in the cab): the other players' machines hide this soldier too.</summary>
        public bool InVehicle
        {
            get => inVehicle;
            set { inVehicle = value; var h = GetComponent<Health>(); if (h) h.Sheltered = value; }   // safe in the cab
        }
        bool inVehicle;

        public void SetControlActive(bool active, Vector3? exitPosition = null)
        {
            if (!active)
            {
                enabled = false;
                foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
                cc.enabled = false;
            }
            else
            {
                if (exitPosition.HasValue) transform.position = exitPosition.Value;
                cc.enabled = true;
                foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = true;
                enabled = true;
                yaw = transform.eulerAngles.y;
            }
        }
    }
}
