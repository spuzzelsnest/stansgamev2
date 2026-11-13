using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame.Additions
{
    /// <summary>
    /// Shared obstacle test for climbing over low things (wreckage, crates, wire, walls up to chest height).
    /// </summary>
    public static class Obstacle
    {
        public const float MinHeight = 0.45f;   // below this the CharacterController steps up by itself
        public const float MaxHeight = 1.7f;

        /// <summary>True for any part of a glider.</summary>
        public static bool IsGlider(Collider c)
        {
            return c != null && c.GetComponentInParent<GliderApproach>() != null;
        }

        /// <summary>
        /// Looks for a low obstacle straight ahead. Returns true with the point on top of it to climb to
        /// when there is one and the space on top is free. <paramref name="soldierCollider"/> is
        /// true when the thing in the way is another soldier (never climb over people).
        /// </summary>
        public static bool FindLedge(CharacterController cc, Vector3 dir, float reach, out Vector3 top, out bool soldierCollider)
        {
            top = Vector3.zero;
            soldierCollider = false;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return false;
            dir.Normalize();

            Transform t = cc.transform;
            Vector3 feet = t.position + cc.center - Vector3.up * (cc.height * 0.5f);
            float r = cc.radius * 0.9f;
            Vector3 p1 = feet + Vector3.up * (cc.stepOffset + r + 0.05f);
            Vector3 p2 = feet + Vector3.up * (cc.height - r);
            RaycastHit hit;
            if (!Physics.CapsuleCast(p1, p2, r, dir, out hit, reach, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (hit.collider.transform.IsChildOf(t)) return false;
            if (hit.collider is CharacterController || hit.collider.GetComponentInParent<SoldierAI>() != null
                || hit.collider.GetComponentInParent<SoldierController>() != null)
            {
                soldierCollider = true;
                return false;
            }

            // Find the top surface a little way past the face we hit.
            Vector3 over = hit.point + dir * (r + 0.3f);
            over.y = feet.y + MaxHeight + 0.4f;
            RaycastHit down;
            if (!Physics.Raycast(over, Vector3.down, out down, MaxHeight + 0.6f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (down.collider.transform.IsChildOf(t)) return false;
            if (IsGlider(down.collider)) return false;   // walk round a glider, never onto it (soldiers wedge between its parts)
            float rise = down.point.y - feet.y;
            if (rise < MinHeight || rise > MaxHeight) return false;
            if (Vector3.Angle(down.normal, Vector3.up) > 40f) return false;   // not a flat enough top

            // Room to stand (or at least crouch) on top.
            Vector3 c1 = down.point + Vector3.up * (r + 0.05f);
            Vector3 c2 = down.point + Vector3.up * Mathf.Max(r + 0.06f, cc.height * 0.75f - r);
            if (Physics.CheckCapsule(c1, c2, r, ~0, QueryTriggerInteraction.Ignore)) return false;

            top = down.point;
            return true;
        }
    }

    /// <summary>
    /// Lets the local player climb over low obstacles: press Space against one, or keep walking into it
    /// for a moment, in any direction. Uses the game's own climb movement (the one used for the canal bank), so the camera,
    /// animation and co-op sync behave as they already do there.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class Mantle : MonoBehaviour
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly MethodInfo startClimb = typeof(SoldierController).GetMethod("StartClimb", Any);
        static readonly FieldInfo climbingField = typeof(SoldierController).GetField("climbing", Any);

        SoldierController soldier;
        CharacterController cc;
        float pushing;

        void Awake()
        {
            soldier = GetComponent<SoldierController>();
            cc = GetComponent<CharacterController>();
        }

        bool Climbing()
        {
            return climbingField != null && (bool)climbingField.GetValue(soldier);
        }

        void Update()
        {
            if (soldier == null || cc == null || startClimb == null || !soldier.enabled) return;
            if (AdditionsBootstrap.LocalPlayer() != soldier || CinematicDirector.Playing || SettingsPanel.Open) return;
            if (Climbing() || !cc.enabled || !cc.isGrounded || soldier.IsProne) { pushing = 0f; return; }

            Keyboard k = Keyboard.current;
            if (k == null) return;
            // Physical key positions, the same keys the game reads (ZQSD on AZERTY).
            Vector3 input = Vector3.zero;
            if (k.wKey.isPressed) input += Vector3.forward;
            if (k.sKey.isPressed) input += Vector3.back;
            if (k.dKey.isPressed) input += Vector3.right;
            if (k.aKey.isPressed) input += Vector3.left;
            if (input.sqrMagnitude < 0.01f) { pushing = 0f; return; }

            // The game moves the player relative to the camera, so test in that direction (any direction:
            // stepping sideways or backwards onto wreckage counts too).
            float yaw = soldier.cam != null ? soldier.cam.transform.eulerAngles.y : transform.eulerAngles.y;
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * input.normalized;
            Vector3 top;
            bool person;
            if (!Obstacle.FindLedge(cc, dir, 0.6f, out top, out person)) { pushing = 0f; return; }

            pushing += Time.deltaTime;
            if (k.spaceKey.wasPressedThisFrame || pushing > 0.35f)
            {
                pushing = 0f;
                if (soldier.IsCrouching) soldier.Stance.Set(StanceKind.Stand);
                startClimb.Invoke(soldier, new object[] { top + Vector3.up * 0.05f });
            }
        }
    }
}
