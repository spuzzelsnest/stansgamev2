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
            if (w != lastWeapon) { ads = 0f; lastWeapon = w; }   // a new weapon starts at the hip
            IronSights irons = w != null && w.muzzle != null ? IronSights.For(w) : null;

            bool want = soldier.IsAiming && w != null && w.muzzle != null;
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
                if (ads > 0f) BringUp(w, irons, cam.transform, k);
            }
            else if (ads > 0f && w != null && w.muzzle != null && SightEye(w, cam.transform, out sightEye))
            {
                cam.transform.position = Vector3.Lerp(eye, sightEye, k);
            }
            HideCrosshair(ads > 0.5f);
            HideBody(ads > 0.5f && w != null && irons == null, w);   // only the PIAT view needs the body out of the way
        }

        Weapon lastWeapon;

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
        /// Moves the weapon from the game's hold towards the aimed pose, where the sight line (rear sight
        /// to front sight, which can sit at a small angle to the bore) runs along the view and the eye
        /// point behind the rear sight is at the camera. On the way up it dips and
        /// cants a little, as a weapon being shouldered does.
        /// </summary>
        static void BringUp(Weapon w, IronSights irons, Transform view, float k)
        {
            WeaponHolder holder = w.GetComponentInParent<WeaponHolder>();
            if (holder == null) return;
            Transform ht = holder.transform, wt = w.transform;
            if (wt.parent != ht) return;   // only the plain holder > weapon set-up

            float arc = Mathf.Sin(k * Mathf.PI);
            Quaternion wRot = Quaternion.AngleAxis(-arc * 6f, view.forward) * view.rotation * irons.SightTurn;
            Vector3 wPos = view.position - wRot * Vector3.Scale(irons.EyeLocal, wt.lossyScale) - view.up * (arc * 0.015f);

            Quaternion hRot = wRot * Quaternion.Inverse(wt.localRotation);
            Vector3 hPos = wPos - hRot * Vector3.Scale(wt.localPosition, ht.lossyScale);
            WeaponPose.SetHolder(holder, Vector3.Lerp(ht.position, hPos, k), Quaternion.Slerp(ht.rotation, hRot, k));
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
