using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame.Additions
{
    /// <summary>
    /// First-person view for the local player, plus the field-of-view and mouse-sensitivity settings
    /// in both views. Runs after SoldierController.LateUpdate (which places the shoulder camera) and
    /// moves the camera to the eyes. V switches between first and third person.
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

            soldier.mouseSensitivity = GameSettings.MouseSensitivity;

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
                RestoreCamera(cam);
                return;
            }

            float target = soldier.IsAiming ? GameSettings.AimFieldOfView : GameSettings.FieldOfView;
            fov = Mathf.Lerp(fov, target, 1f - Mathf.Exp(-fovSharpness * Time.deltaTime));
            cam.fieldOfView = fov;

            if (!fp)
            {
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
            cam.transform.position = eye;
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
            SetHeadHidden(false);
            if (soldier != null && soldier.cam != null) RestoreCamera(soldier.cam);
        }
    }
}
