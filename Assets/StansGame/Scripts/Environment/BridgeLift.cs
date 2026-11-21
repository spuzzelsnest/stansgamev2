using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// The 1934 Bénouville bridge is a Scherzer rolling-lift bascule: the two curved segments under the tower roll
    /// back along their tracks while the leaf tips up, the counterweight on the tower swinging down behind them.
    /// So the leaf turns about the centre of the segments, and that centre travels towards the bank by
    /// radius x angle (in radians) - no fixed hinge.
    /// Worked from the controller in the bridge-keeper's hut (see BridgeControls on the player). The host (or the
    /// offline game) decides; clients follow the target through MissionNet and animate it themselves.
    /// It will not open with anyone on the span or the tower.
    /// </summary>
    public class BridgeLift : MonoBehaviour
    {
        public static BridgeLift Instance { get; private set; }

        [Header("Set by the level builder")]
        public Transform leaf;                     // origin = centre of the rolling segments
        public Vector3 restPosition;               // world, bridge down
        public Quaternion restRotation = Quaternion.identity;
        public Vector3 east = Vector3.right;       // the way the segments roll as the bridge opens
        public float radius = 5f;                  // rolling radius of the segments
        public float maxAngle = 66f;               // the counterweight just clears the pit at this angle
        public float secondsToOpen = 32f;
        public Transform panel;                    // the controller in the keeper's hut
        public Transform lever;
        public Vector3 checkCentre, checkSize;     // span and tower, bridge down (world, axis aligned)

        public float Angle { get; private set; }
        /// <summary>Where the bridge is going: true = up (open to the canal), false = down (open to the road).</summary>
        public bool Raised { get; private set; }
        public bool Moving => !Mathf.Approximately(Angle, Raised ? maxAngle : 0f);

        Rigidbody rb;
        AudioSource motor;
        float leverAngle;

        void Awake()
        {
            Instance = this;
            if (leaf) rb = leaf.GetComponent<Rigidbody>();
            leverAngle = 35f;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Water.BridgeOpen = false;
        }

        /// <summary>Someone pulled the lever (offline, or on the host). Returns false with the reason if it can't.</summary>
        public bool Request(bool up, out string why)
        {
            why = null;
            if (up == Raised) return true;
            if (up && Occupied())
            {
                why = "The bridge won't lift with men on it: clear the span and the tower first.";
                return false;
            }
            Raised = up;
            Bell();
            return true;
        }

        /// <summary>Debug menu: work the lever even with men on the span.</summary>
        public void DebugForce(bool up) { Raised = up; Bell(); }

        /// <summary>Clients: follow the host's lever.</summary>
        public void SetTarget(bool up)
        {
            if (up == Raised) return;
            Raised = up;
            Bell();
        }

        bool Occupied()
        {
            foreach (var c in Physics.OverlapBox(checkCentre, checkSize * 0.5f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            {
                if (leaf && c.transform.IsChildOf(leaf)) continue;
                var h = c.GetComponentInParent<Health>();
                if (!h || h.IsDead) continue;
                if (h.GetComponent<SoldierController>() || h.GetComponent<SoldierAI>() || h.GetComponent<TankController>()) return true;
            }
            return false;
        }

        void FixedUpdate()
        {
            if (!leaf) return;
            float target = Raised ? maxAngle : 0f;
            float prev = Angle;
            // the drive starts and stops gently
            float t = Mathf.Clamp01(Angle / maxAngle);
            float ease = 0.3f + 0.7f * Mathf.Sin(Mathf.PI * Mathf.Lerp(0.08f, 0.92f, t));
            Angle = Mathf.MoveTowards(Angle, target, maxAngle / secondsToOpen * ease * Time.fixedDeltaTime);
            Water.BridgeOpen = Raised || Angle > 0.2f;
            bool moved = !Mathf.Approximately(prev, Angle);
            if (moved) Apply(rb);
            Motor(moved);
        }

        void Update()
        {
            if (!lever) return;
            leverAngle = Mathf.MoveTowards(leverAngle, Raised ? -35f : 35f, 140f * Time.deltaTime);
            lever.localRotation = Quaternion.Euler(0f, 0f, leverAngle);
        }

        /// <summary>The leaf's pose at an angle: turn about the segment centre, which has rolled radius x angle.</summary>
        public void Pose(float angle, out Vector3 pos, out Quaternion rot)
        {
            Vector3 axis = Vector3.Cross(Vector3.up, east).normalized;     // turns the free (west) end upwards
            pos = restPosition + east.normalized * (radius * angle * Mathf.Deg2Rad);
            rot = Quaternion.AngleAxis(angle, axis) * restRotation;
        }

        void Apply(Rigidbody body)
        {
            Pose(Angle, out var pos, out var rot);
            if (body && Application.isPlaying) { body.MovePosition(pos); body.MoveRotation(rot); }
            else leaf.SetPositionAndRotation(pos, rot);
        }

        /// <summary>Editor/preview use: put the leaf at an angle without playing.</summary>
        public void PoseNow(float angle)
        {
            if (!leaf) return;
            Pose(angle, out var pos, out var rot);
            leaf.SetPositionAndRotation(pos, rot);
        }

        // ------------------------------------------------------------------ sound: a warning bell, then the motor

        void Bell()
        {
            if (!leaf) return;
            var src = new GameObject("Bridge bell").AddComponent<AudioSource>();
            src.transform.position = panel ? panel.position : leaf.position;
            src.clip = BellClip(); src.spatialBlend = 1f; src.maxDistance = 220f; src.rolloffMode = AudioRolloffMode.Linear;
            src.volume = 0.8f; src.Play();
            Destroy(src.gameObject, src.clip.length + 0.2f);
        }

        void Motor(bool on)
        {
            if (on && !motor)
            {
                motor = leaf.gameObject.AddComponent<AudioSource>();
                motor.clip = MotorClip(); motor.loop = true; motor.spatialBlend = 1f; motor.maxDistance = 160f;
                motor.rolloffMode = AudioRolloffMode.Linear; motor.volume = 0.55f;
            }
            if (!motor) return;
            if (on && !motor.isPlaying) motor.Play();
            if (!on && motor.isPlaying) motor.Stop();
        }

        static AudioClip bellClip, motorClip;

        static AudioClip BellClip()
        {
            if (bellClip) return bellClip;
            const int rate = 22050; int n = rate * 3;
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate, s = 0f;
                for (int k = 0; k < 4; k++)            // four strokes of the bell
                {
                    float u = t - k * 0.55f; if (u < 0f) continue;
                    float env = Mathf.Exp(-u * 3.2f);
                    s += env * (Mathf.Sin(2f * Mathf.PI * 1180f * u) * 0.5f + Mathf.Sin(2f * Mathf.PI * 2950f * u) * 0.22f + Mathf.Sin(2f * Mathf.PI * 590f * u) * 0.3f);
                }
                d[i] = s * 0.35f;
            }
            bellClip = AudioClip.Create("bridge bell", n, 1, rate, false); bellClip.SetData(d, 0);
            return bellClip;
        }

        static AudioClip MotorClip()
        {
            if (motorClip) return motorClip;
            const int rate = 22050; int n = rate * 2;
            var d = new float[n];
            var rnd = new System.Random(1934);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                lp = Mathf.Lerp(lp, (float)rnd.NextDouble() * 2f - 1f, 0.08f);
                // electric motor hum, the gear train whining, the segments' teeth clicking into the track
                float hum = Mathf.Sin(2f * Mathf.PI * 50f * t) * 0.35f + Mathf.Sin(2f * Mathf.PI * 100f * t) * 0.2f;
                float whine = Mathf.Sin(2f * Mathf.PI * 410f * t + Mathf.Sin(2f * Mathf.PI * 3f * t)) * 0.08f;
                float click = Mathf.Repeat(t * 2f, 1f) < 0.015f ? 0.5f : 0f;
                d[i] = (hum + whine + lp * 0.5f + click * lp) * 0.6f;
            }
            motorClip = AudioClip.Create("bridge motor", n, 1, rate, false); motorClip.SetData(d, 0);
            return motorClip;
        }
    }
}
