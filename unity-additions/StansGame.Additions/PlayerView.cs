using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace StansGame.Additions
{
    /// <summary>
    /// First-person view for the local player, plus the field-of-view and mouse-sensitivity settings
    /// in both views. Runs after SoldierController.LateUpdate (which places the shoulder camera) and
    /// moves the camera to the eyes. V switches between first and third person.
    /// Aiming in first person looks through the iron sights (<see cref="IronSights"/>): the eye moves
    /// behind the rear sight so the front post sits on the aim point, and the hip crosshair is hidden.
    /// The PIAT has no iron sights here and gets a raised view with a small centre dot.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class PlayerView : MonoBehaviour
    {
        [Tooltip("Eye position relative to the head bone, in body space (x right, y up, z forward).")]
        public Vector3 eyeOffset = new Vector3(0f, 0.07f, 0.11f);

        [Tooltip("Camera near clip plane in first person, so the camera does not cut through the arms and weapon.")]
        public float nearClip = 0.05f;

        [Tooltip("How fast the field of view follows aiming (higher is snappier).")]
        public float fovSharpness = 14f;

        SoldierController soldier;
        CharacterController cc;
        Transform head;
        Vector3 headScale = Vector3.one;
        bool headHidden;
        float defaultNear = -1f;
        float fov;
        float ads;            // 0 hip, 1 looking down the sights
        Crosshair crosshair;
        bool crosshairHidden;
        static Texture2D dot;

        /// <summary>How far the view is into the sights (0 to 1), for tests.</summary>
        public float SightsBlend { get { return ads; } }

        void Awake()
        {
            soldier = GetComponent<SoldierController>();
            cc = GetComponent<CharacterController>();
            fov = GameSettings.FieldOfView;
        }

        void Start()
        {
            Animator a = soldier != null ? soldier.animator : null;
            if (a == null) a = GetComponentInChildren<Animator>();
            if (a != null && a.isHuman) head = a.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) headScale = head.localScale;
        }

        bool IsLocal()
        {
            return soldier != null && AdditionsBootstrap.LocalPlayer() == soldier;
        }

        /// <summary>True while the camera should sit at the eyes.</summary>
        bool FirstPersonNow()
        {
            if (!GameSettings.FirstPerson || !IsLocal() || CinematicDirector.Playing) return false;
            // The controller is disabled while dead, in a tank or in a cutscene; it is also
            // disabled on purpose while the settings panel is open, and then the view stays.
            return soldier.enabled || SettingsPanel.PausedPlayer == soldier;
        }

        void Update()
        {
            if (!IsLocal()) return;

            // While aiming, turn speed follows the zoom, so the same mouse move covers the same part of
            // the screen in both views (scaled by tan of half the field of view), times the player's
            // aim sensitivity setting.
            float sens = GameSettings.MouseSensitivity;
            if (soldier.IsAiming)
            {
                float hip = Mathf.Tan(GameSettings.FieldOfView * 0.5f * Mathf.Deg2Rad);
                float aim = Mathf.Tan(GameSettings.AimFieldOfView * 0.5f * Mathf.Deg2Rad);
                sens *= Mathf.Lerp(1f, aim / hip, Mathf.SmoothStep(0f, 1f, ads)) * GameSettings.AimSensitivity;
            }
            soldier.mouseSensitivity = sens;

            Keyboard kb = Keyboard.current;
            if (kb != null && !SettingsPanel.Open && soldier.enabled && kb[Key.V].wasPressedThisFrame)
            {
                GameSettings.FirstPerson = !GameSettings.FirstPerson;
                GameSettings.Save();
            }
        }

        void LateUpdate()
        {
            if (soldier == null || soldier.cam == null) return;
            Camera cam = soldier.cam;
            if (defaultNear < 0f) defaultNear = cam.nearClipPlane;

            bool fp = FirstPersonNow();
            SetHeadHidden(fp);

            if (!IsLocal() || CinematicDirector.Playing || !soldier.enabled && !fp)
            {
                ads = 0f;
                HideCrosshair(false);
                HideBody(false, null);
                RestoreCamera(cam);
                return;
            }

            float target = soldier.IsAiming ? GameSettings.AimFieldOfView : GameSettings.FieldOfView;
            fov = Mathf.Lerp(fov, target, 1f - Mathf.Exp(-fovSharpness * Time.deltaTime));
            cam.fieldOfView = fov;

            if (!fp)
            {
                ads = 0f;
                HideCrosshair(false);
                HideBody(false, null);
                RestoreCamera(cam);
                return;
            }

            // The body always faces where the player looks; movement already follows the camera yaw.
            Quaternion yaw = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f);
            transform.rotation = yaw;

            Vector3 eye;
            if (head != null)
            {
                eye = head.position + yaw * eyeOffset;
            }
            else
            {
                float h = soldier.Stance != null ? soldier.Stance.EyeHeight : 1.6f;
                if (cc != null) h = Mathf.Min(h, cc.center.y + cc.height * 0.5f - 0.1f);
                eye = transform.position + Vector3.up * h + yaw * new Vector3(0f, 0f, eyeOffset.z);
            }

            cam.nearClipPlane = nearClip;

            Weapon w = soldier.enabled ? soldier.CurrentWeapon : null;
            if (w != lastWeapon) { ads = 0f; raise = 0f; lastWeapon = w; }   // a new weapon comes up from below, at the hip
            IronSights irons = w != null && w.muzzle != null ? IronSights.For(w) : null;

            // Reload: the weapon comes down from the eye, tilts to show the magazine well, jerks down as
            // the empty magazine comes out and up as the full one goes in, then comes back.
            bool reloading = w != null && w.IsReloading;
            if (reloading) { if (reloadStart < 0f) reloadStart = Time.time; }
            else reloadStart = -1f;
            float rp = reloading ? Mathf.Clamp01((Time.time - reloadStart) / Mathf.Max(0.3f, w.reloadTime)) : 1f;
            reloadPose = Mathf.MoveTowards(reloadPose, reloading && rp < 0.86f ? 1f : 0f, Time.deltaTime / 0.2f);
            float bump = reloading ? Pulse(rp, 0.32f) * -1f + Pulse(rp, 0.64f) : 0f;
            ReloadProgress = reloading ? rp : 0f;

            bool want = soldier.IsAiming && w != null && w.muzzle != null && !reloading;
            ads = Mathf.MoveTowards(ads, want ? 1f : 0f, Time.deltaTime / AdsSeconds(w));
            float k = Mathf.SmoothStep(0f, 1f, ads);

            // Zoom in step with the weapon coming up.
            if (irons != null || ads > 0f)
            {
                fov = Mathf.Lerp(GameSettings.FieldOfView, GameSettings.AimFieldOfView, k);
                cam.fieldOfView = fov;
            }

            // The camera stays at the eyes. With iron sights the weapon is brought up to the eye: the
            // rear sight centre and the front post land on the centre of the screen, the hands follow
            // through the game's hand IK. The PIAT (no iron sights) keeps the raised view.
            cam.transform.position = eye;
            Vector3 sightEye;
            if (irons != null)
            {
                raise = Mathf.MoveTowards(raise, 1f, Time.deltaTime / RaiseSeconds);
                bool sprinting = cc != null && cc.isGrounded && new Vector2(cc.velocity.x, cc.velocity.z).magnitude > soldier.runSpeed + 0.5f && !want;
                sprint = Mathf.MoveTowards(sprint, sprinting ? 1f : 0f, Time.deltaTime / (sprinting ? 0.25f : 0.18f));
                Hold(w, irons, cam.transform, k, Mathf.SmoothStep(0f, 1f, raise), Mathf.SmoothStep(0f, 1f, sprint), Mathf.SmoothStep(0f, 1f, reloadPose), bump);
            }
            else if (ads > 0f && w != null && w.muzzle != null && SightEye(w, cam.transform, out sightEye))
            {
                cam.transform.position = Vector3.Lerp(eye, sightEye, k);
            }
            HideCrosshair(ads > 0.5f);
            HideBody(ads > 0.5f && w != null && irons == null, w);   // only the PIAT view needs the body out of the way
        }

        Weapon lastWeapon;
        float raise = 1f, sprint;
        const float RaiseSeconds = 0.3f;
        float reloadStart = -1f, reloadPose;

        /// <summary>For tests: how far the current reload is (0 to 1), 0 when not reloading.</summary>
        public float ReloadProgress { get; private set; }

        /// <summary>A short bump around <paramref name="centre"/> (0 to 1 progress), 1 at its peak.</summary>
        static float Pulse(float t, float centre)
        {
            float d = (t - centre) / 0.05f;
            return Mathf.Exp(-d * d);
        }

        /// <summary>
        /// Where the weapon sits at the hip in first person, in view space (metres, x right, y up, z
        /// forward): the point behind the rear sight goes here. The game's own hold is made for the
        /// third-person body (the pistol, for one, is held against the chest and cannot be seen from
        /// the eyes), so first person gets its own hold, low and to the right, pointing where you look.
        /// </summary>
        static Vector3 HipOffset(Weapon w)
        {
            switch (w.hold)
            {
                // Its eye point is 0.45 m behind the rear sight: held out low in both hands (low ready),
                // the aimed grip lowered, so the hands wrap it as they do when aiming.
                case HoldStyle.Pistol: return new Vector3(0.05f, -0.13f, -0.03f);
                case HoldStyle.SMG: return new Vector3(0.13f, -0.15f, 0.09f);
                case HoldStyle.Rifle: return new Vector3(0.13f, -0.15f, 0.06f);
                case HoldStyle.Heavy: return new Vector3(0.15f, -0.19f, 0.08f);
            }
            return new Vector3(0.13f, -0.15f, 0.08f);
        }

        /// <summary>
        /// Places the weapon for first person every frame: the hip pose, blended into the aimed pose by
        /// <paramref name="k"/>, lowered while a newly selected weapon comes up (<paramref name="up"/>)
        /// and turned down and across the chest while sprinting (<paramref name="run"/>).
        /// </summary>
        static void Hold(Weapon w, IronSights irons, Transform view, float k, float up, float run, float reload, float bump)
        {
            WeaponHolder holder = w.GetComponentInParent<WeaponHolder>();
            if (holder == null) return;
            Transform ht = holder.transform, wt = w.transform;
            if (wt.parent != ht) return;   // only the plain holder > weapon set-up
            Vector3 eyePoint = Vector3.Scale(irons.EyeLocal, wt.lossyScale);
            bool pistol = w.hold == HoldStyle.Pistol;

            // Hip: low right, toed in a little so the muzzle points at the middle of the screen.
            Vector3 off = HipOffset(w);
            off += new Vector3(-0.02f, -0.05f, -0.04f) * run + new Vector3(0f, -0.22f, -0.05f) * (1f - up);
            Quaternion hipTurn = Quaternion.Euler(28f * run + 35f * (1f - up) + (pistol ? 4f : 0f), (pistol ? 0f : -2f) - 32f * run, (pistol ? 0f : -6f) - 18f * run);

            // Reload: a pistol's muzzle tips up beside the face. A long arm is pushed out to the middle
            // of the view, muzzle up and to the left, canted so the action and magazine well face the
            // eyes (at the hip only the barrel is in view, the action sits below the screen).
            if (reload > 0f || bump != 0f)
            {
                off += (pistol ? new Vector3(-0.02f, -0.03f, -0.02f) : new Vector3(-0.10f, 0.035f, 0.17f)) * reload + new Vector3(0f, 0.014f, 0f) * bump;
                hipTurn = hipTurn * Quaternion.Euler((pistol ? -22f : -14f) * reload - 4f * bump, (pistol ? -8f : -28f) * reload, (pistol ? 15f : 32f) * reload);
            }
            Quaternion hipRot = view.rotation * hipTurn * irons.SightTurn;
            Vector3 hipPos = view.position + view.rotation * off - hipRot * eyePoint;

            // Aimed: the sight line along the view, the eye point at the camera. On the way up the
            // weapon dips and cants a little, as a weapon being shouldered does.
            float arc = Mathf.Sin(k * Mathf.PI);
            Quaternion aimRot = Quaternion.AngleAxis(-arc * 6f, view.forward) * view.rotation * irons.SightTurn;
            Vector3 aimPos = view.position - aimRot * eyePoint - view.up * (arc * 0.015f);

            Quaternion wRot = Quaternion.Slerp(hipRot, aimRot, k);
            Vector3 wPos = Vector3.Lerp(hipPos, aimPos, k);
            Quaternion hRot = wRot * Quaternion.Inverse(wt.localRotation);
            Vector3 hPos = wPos - hRot * Vector3.Scale(wt.localPosition, ht.lossyScale);
            WeaponPose.SetHolder(holder, hPos, hRot);
        }

        /// <summary>Seconds to bring each kind of weapon up to the eye (eased).</summary>
        static float AdsSeconds(Weapon w)
        {
            if (w == null) return 0.2f;
            switch (w.hold)
            {
                case HoldStyle.Pistol: return 0.18f;
                case HoldStyle.SMG: return 0.22f;
                case HoldStyle.Rifle: return 0.28f;
                case HoldStyle.Heavy: return 0.40f;
                case HoldStyle.Launcher: return 0.35f;
            }
            return 0.25f;
        }

        /// <summary>
        /// Where the eye goes to look down this weapon's sights, in the weapon's own space (the game keeps
        /// the weapon parallel to the view while aiming): on the bore line raised to the sight line, the
        /// cheek on the stock for long arms, the pistol held out in front.
        /// </summary>
        static bool SightEye(Weapon w, Transform view, out Vector3 eye)
        {
            eye = Vector3.zero;
            Transform wt = w.transform;
            if (Vector3.Angle(wt.forward, view.forward) > 20f) return false;   // not shouldered (yet)
            IronSights irons = IronSights.For(w);
            if (irons != null)
            {
                eye = wt.TransformPoint(irons.EyeLocal);   // through the rear sight, front post on the aim point
                return true;
            }
            Vector3 muzzle = wt.InverseTransformPoint(w.muzzle.position);
            float minZ = float.MaxValue, maxY = float.MinValue;
            foreach (MeshFilter mf in w.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Bounds b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    Vector3 l = wt.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (l.z < minZ) minZ = l.z;
                    if (l.y > maxY) maxY = l.y;
                }
            }
            if (minZ == float.MaxValue) { minZ = muzzle.z - 0.7f; maxY = muzzle.y + 0.04f; }
            bool pistol = w.hold == HoldStyle.Pistol;
            float sightY = Mathf.Min(muzzle.y + (pistol ? 0.06f : 0.045f), maxY + 0.035f);
            float eyeZ = pistol ? minZ - 0.20f : w.hold == HoldStyle.Launcher ? minZ + 0.30f : minZ + 0.18f;
            eye = wt.TransformPoint(new Vector3(muzzle.x, sightY, eyeZ));
            return true;
        }

        void OnGUI()
        {
            // Small centre dot while aiming a weapon without iron sights (the PIAT); the hip crosshair is hidden then.
            if (ads < 0.9f || Event.current.type != EventType.Repaint || !IsLocal()) return;
            Weapon w = soldier.CurrentWeapon;
            if (w != null && w.GetComponent<IronSights>() != null) return;   // the sights do the job
            if (dot == null)
            {
                dot = new Texture2D(1, 1);
                dot.SetPixel(0, 0, Color.white);
                dot.Apply();
            }
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            Color keep = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(cx - 2.5f, cy - 2.5f, 5f, 5f), dot);
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            GUI.DrawTexture(new Rect(cx - 1.5f, cy - 1.5f, 3f, 3f), dot);
            GUI.color = keep;
        }

        readonly List<Renderer> hiddenBody = new List<Renderer>();

        void HideBody(bool hide, Weapon w)
        {
            if (hide && hiddenBody.Count == 0)
            {
                foreach (Renderer r in GetComponentsInChildren<Renderer>())
                {
                    if (w != null && r.transform.IsChildOf(w.transform)) continue;
                    if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly || !r.enabled) continue;
                    if (r.GetComponentInParent<Weapon>() != null) continue;   // other carried weapons stay as they are
                    r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                    hiddenBody.Add(r);
                }
            }
            else if (!hide && hiddenBody.Count > 0)
            {
                foreach (Renderer r in hiddenBody)
                    if (r != null) r.shadowCastingMode = ShadowCastingMode.On;
                hiddenBody.Clear();
            }
        }

        void HideCrosshair(bool hide)
        {
            if (crosshair == null) crosshair = FindAnyObjectByType<Crosshair>();
            if (crosshair == null) return;
            if (hide && crosshair.enabled) { crosshair.enabled = false; crosshairHidden = true; }
            else if (!hide && crosshairHidden) { crosshair.enabled = true; crosshairHidden = false; }
        }

        void RestoreCamera(Camera cam)
        {
            if (defaultNear > 0f && !Mathf.Approximately(cam.nearClipPlane, defaultNear)) cam.nearClipPlane = defaultNear;
        }

        void SetHeadHidden(bool hide)
        {
            if (head == null) return;
            if (hide)
            {
                // Scale after the animation pass so the camera does not look out through the inside of the head.
                head.localScale = headScale * 0.001f;
                headHidden = true;
            }
            else if (headHidden)
            {
                head.localScale = headScale;
                headHidden = false;
            }
        }

        void OnDisable()
        {
            HideCrosshair(false);
            HideBody(false, null);
            SetHeadHidden(false);
            if (soldier != null && soldier.cam != null) RestoreCamera(soldier.cam);
        }
    }
}
