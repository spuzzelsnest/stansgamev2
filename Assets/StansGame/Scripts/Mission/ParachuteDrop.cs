using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame
{
    /// <summary>
    /// A paratrooper on his way down: a second or two of static-line fall, then the T-5 canopy opens and he drifts
    /// down at about 5 m/s. The player steers (A/D turn, W run with the wind, S slip/brake); AI paratroopers drift
    /// towards their landing point with a little randomness. On touchdown the soldier's own controls or AI take over.
    /// Landing on a roof hangs you up (Ken Russell on the church roof): press E to cut yourself free.
    /// </summary>
    public class ParachuteDrop : MonoBehaviour, IDisableOnDeath
    {
        public enum State { Waiting, Falling, Canopy, HungUp, Landed }

        public float freefallSeconds = 1.3f;       // static line: the canopy is out within a couple of seconds
        public float descentSpeed = 5.2f;          // T-5 parachute, ~5 m/s with full kit
        public float steerTurnRate = 40f;
        public float driftSpeed = 2.2f, runSpeed = 4f, brakeSpeed = 0.8f;
        public Vector3 wind = new Vector3(1.6f, 0f, 0.4f);   // light westerly that night
        public Vector3 aiLandingPoint;
        public float hangUpAbove = 3.5f;           // a touchdown this high above the street is a roof
        public GameObject canopyPrefab;
        public Vector3 lookAtOnJump = Vector3.zero;   // the player starts his descent facing this point

        public State Current { get; private set; } = State.Waiting;
        public event Action<ParachuteDrop> Landed;

        SoldierController player;
        SoldierAI ai;
        CharacterController cc;
        Transform canopy;
        CanopyCloth cloth;
        Vector3 velocity;
        float stateStart, yaw;
        Camera cam;

        void Awake()
        {
            player = GetComponent<SoldierController>();
            ai = GetComponent<SoldierAI>();
            cc = GetComponent<CharacterController>();
        }

        /// <summary>Out of the door: start falling with the aircraft's speed.</summary>
        public void Jump(Vector3 aircraftVelocity)
        {
            gameObject.SetActive(true);   // the stick waits inactive in the aircraft; Awake runs here
            if (!cc) Awake();
            if (player) { player.SetControlActive(false); foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = true; cam = player.cam ? player.cam : Camera.main; }
            if (ai) ai.enabled = false;
            if (cc) cc.enabled = false;
            velocity = aircraftVelocity;
            yaw = transform.eulerAngles.y;
            if (player)
            {
                // face the town: the fire and the church tower are the landmarks on the way down
                Vector3 to = lookAtOnJump - transform.position; to.y = 0f;
                if (to.sqrMagnitude > 1f) yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            }
            Current = State.Falling; stateStart = Time.time;
        }

        void Update()
        {
            switch (Current)
            {
                case State.Falling: Fall(); break;
                case State.Canopy: Descend(); break;
                case State.HungUp: HangUp(); break;
            }
        }

        void LateUpdate()
        {
            if (!cam || !(Current == State.Falling || Current == State.Canopy || Current == State.HungUp)) return;
            Quaternion flat = Quaternion.Euler(0f, yaw, 0f);
            Vector3 eye = transform.position + flat * new Vector3(0f, 4.5f, -9f);
            Vector3 look = transform.position + flat * Vector3.forward * 30f - Vector3.up * 12f;
            cam.transform.SetPositionAndRotation(Vector3.Lerp(cam.transform.position, eye, Time.deltaTime * 5f),
                Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(look - eye), Time.deltaTime * 5f));
        }

        void Fall()
        {
            velocity += Vector3.down * 9.81f * Time.deltaTime;
            velocity.x *= 1f - Time.deltaTime * 0.6f; velocity.z *= 1f - Time.deltaTime * 0.6f;   // drag sheds the aircraft's speed
            Move(velocity * Time.deltaTime);
            if (Time.time - stateStart >= freefallSeconds) OpenCanopy();
        }

        void OpenCanopy()
        {
            Current = State.Canopy; stateStart = Time.time;
            canopy = canopyPrefab ? Instantiate(canopyPrefab, transform).transform : BuildCanopy(transform);
            canopy.localPosition = new Vector3(0f, 1.6f, 0f);   // the model's origin is where the risers meet
            cloth = canopy.GetComponent<CanopyCloth>();
            if (!cloth) cloth = canopy.gameObject.AddComponent<CanopyCloth>();   // breathes in the air, collapses like silk
            Sfx.At(Sfx.Wind, transform.position, 0.6f, 1.6f, 120f);   // the crack of the canopy opening
        }

        void Descend()
        {
            Vector3 horiz = wind;
            if (player)
            {
                var kb = Keyboard.current;
                float turn = 0f, fwd = driftSpeed;
                bool braking = false;
                if (kb != null)
                {
                    if (kb.aKey.isPressed) turn -= 1f;
                    if (kb.dKey.isPressed) turn += 1f;
                    if (kb.wKey.isPressed) fwd = runSpeed;
                    if (kb.sKey.isPressed) { fwd = brakeSpeed; braking = true; }
                }
                yaw += turn * steerTurnRate * Time.deltaTime;
                horiz += Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * fwd;
                if (braking) horiz *= 0.6f;
            }
            else
            {
                Vector3 to = aiLandingPoint - transform.position; to.y = 0f;
                float wanted = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                yaw = Mathf.MoveTowardsAngle(yaw, wanted, steerTurnRate * Time.deltaTime);
                horiz = Vector3.Lerp(horiz, Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * driftSpeed + wind * 0.3f, 0.8f);
            }
            float t = Time.time - stateStart;
            float down = Mathf.Lerp(Mathf.Abs(velocity.y), descentSpeed, Mathf.Clamp01(t * 2f));   // the opening shock
            velocity = new Vector3(horiz.x, -down, horiz.z);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (canopy) canopy.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 1.3f) * 6f, 0f, Mathf.Cos(Time.time * 0.9f) * 5f);
            Move(velocity * Time.deltaTime);
        }

        void Move(Vector3 delta)
        {
            // touchdown: anything solid under the boots this frame (ground, roof, tree)
            Vector3 from = transform.position + Vector3.up * 0.5f;
            float dist = -delta.y + 0.6f;
            if (delta.y < 0f && Physics.Raycast(from, Vector3.down, out var hit, dist, ~0, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(transform))
            {
                transform.position = new Vector3(transform.position.x + delta.x, hit.point.y, transform.position.z + delta.z);
                Touchdown(hit);
                return;
            }
            transform.position += delta;
        }

        void Touchdown(RaycastHit hit)
        {
            if (player && hit.point.y > hangUpAbove && hit.normal.y < 0.98f)
            {
                Current = State.HungUp; stateStart = Time.time;
                if (MissionText.Show != null) MissionText.Show("Hung up on a roof! Press E to cut your rigging lines.", 5f);
                return;
            }
            Land();
        }

        void HangUp()
        {
            var kb = Keyboard.current;
            if (canopy) canopy.localRotation = Quaternion.Euler(70f, 0f, 0f);
            if (kb != null && kb.eKey.wasPressedThisFrame || Time.time - stateStart > 12f)
            {
                // slide off the roof and drop into the street on the nearest open side
                Vector3 best = transform.position;
                for (int i = 0; i < 16; i++)
                {
                    var dir = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward;
                    for (float r = 4f; r < 25f; r += 2f)
                    {
                        var p = transform.position + dir * r;
                        if (Physics.Raycast(new Vector3(p.x, transform.position.y + 2f, p.z), Vector3.down, out var h, 60f) && h.point.y < 1.5f && h.normal.y > 0.9f)
                        { best = h.point; i = 99; break; }
                    }
                }
                transform.position = best;
                Land();
            }
        }

        void Land()
        {
            Current = State.Landed;
            if (canopy)
            {
                // the air goes out of the canopy and it settles downwind like silk, the lines stretched back to him
                foreach (var c in canopy.GetComponentsInChildren<Collider>()) c.enabled = false;
                if (cloth) cloth.Collapse(transform.position, wind, yaw);
                else canopy.SetParent(null, true);
            }
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var h = GetComponent<Health>();
            if (h && h.IsDead) { enabled = false; return; }
            if (player) player.SetControlActive(true, transform.position + Vector3.up * 0.1f);
            else if (cc) cc.enabled = true;
            if (ai) ai.enabled = Net.Simulates;
            Sfx.Footstep(transform.position, 0.9f);
            Landed?.Invoke(this);
            enabled = false;
        }

        /// <summary>The T-5 canopy in olive-drab nylon, its rigging lines and risers.</summary>
        public static Transform BuildCanopy(Transform parent)
        {
            var root = new GameObject("T-5 canopy").transform;
            root.SetParent(parent, false);
            var cloth = RuntimeMaterials.Lit(new Color(0.55f, 0.53f, 0.42f));
            var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "Canopy"; dome.transform.SetParent(root, false);
            dome.transform.localPosition = new Vector3(0f, 6.0f, 0f);
            dome.transform.localScale = new Vector3(8.5f, 3.2f, 8.5f);
            dome.GetComponent<Renderer>().sharedMaterial = cloth;
            Destroy(dome.GetComponent<Collider>());
            var line = cloth ? new Material(cloth) { color = new Color(0.75f, 0.73f, 0.62f) } : null;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                var rim = new Vector3(Mathf.Cos(a) * 3.9f, 5.4f, Mathf.Sin(a) * 3.9f);
                var harness = Vector3.zero;
                var l = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                l.name = "Rigging line"; l.transform.SetParent(root, false);
                l.transform.localPosition = (rim + harness) * 0.5f;
                l.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (rim - harness).normalized);
                l.transform.localScale = new Vector3(0.02f, (rim - harness).magnitude * 0.5f, 0.02f);
                l.GetComponent<Renderer>().sharedMaterial = line;
                Destroy(l.GetComponent<Collider>());
            }
            return root;
        }
    }

    /// <summary>Lets gameplay pieces put a line on the mission HUD without knowing which chapter is running.</summary>
    public static class MissionText
    {
        public static Action<string, float> Show;
    }
}
