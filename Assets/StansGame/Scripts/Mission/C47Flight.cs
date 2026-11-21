using System.Collections.Generic;
using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// A C-47 Skytrain on its run over the Cotentin: flies a straight line at drop height, red light, then green,
    /// and the stick goes out of the cargo door one man after another. Used for the player's aircraft and for
    /// the rest of the serial flying past (those only drop AI paratroopers or nobody at all).
    /// </summary>
    public class C47Flight : MonoBehaviour
    {
        public Vector3 start, end;
        public float speed = 50f;                 // ~110 mph: the pilots were told to slow for the jump; many did not
        public float greenLightAt = 0.45f;        // share of the run where the green light comes on
        public float jumpInterval = 0.7f;         // seconds between men in the door
        public Transform door;                    // cargo door, port side aft
        public Light jumpLight;
        public List<ParachuteDrop> stick = new List<ParachuteDrop>();
        public int playerSlot = -1;               // which man in the stick is the player (-1 = none)
        public bool startOnAwake = false;
        public float startDelay = 0f;

        public bool GreenLight { get; private set; }
        public bool Flying { get; private set; }
        public float Progress { get; private set; }
        public Vector3 Velocity => (end - start).normalized * speed;

        int next;
        float nextJump, startAt;
        AudioSource drone;
        public System.Action PlayerAtDoor;          // the player's turn: the mission waits for him to jump

        void Awake()
        {
            drone = gameObject.AddComponent<AudioSource>();
            drone.clip = Sfx.AircraftDrone; drone.loop = true; drone.spatialBlend = 1f; drone.minDistance = 20f; drone.maxDistance = 1500f; drone.volume = 0.8f;
            foreach (var p in stick) if (p && p.Current == ParachuteDrop.State.Waiting) p.gameObject.SetActive(false);   // in the aircraft
        }

        void Start()
        {
            if (startOnAwake && !Flying) Launch(startDelay);
            else if (!Flying) SetVisible(false);
        }

        public void Launch(float delay = 0f)
        {
            startAt = Time.time + delay; Flying = true; Progress = 0f; next = 0; GreenLight = false;
            transform.position = start;
            transform.rotation = Quaternion.LookRotation(end - start);
            SetVisible(true);
        }

        void SetVisible(bool v)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = v;
            if (drone) { if (v && !drone.isPlaying) drone.Play(); else if (!v) drone.Stop(); }
        }

        public bool WaitingForPlayer => GreenLight && next == playerSlot;

        void Update()
        {
            if (!Flying || Time.time < startAt) return;
            float len = Vector3.Distance(start, end);
            Progress = Mathf.Min(1f, Progress + speed * Time.deltaTime / len);
            // a little weave: the pilots were jinking away from the flak
            Vector3 p = Vector3.Lerp(start, end, Progress) + Vector3.up * Mathf.Sin(Time.time * 0.7f) * 1.5f;
            transform.position = p;
            transform.rotation = Quaternion.LookRotation(end - start) * Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 0.9f) * 6f);
            if (jumpLight) jumpLight.color = GreenLight ? new Color(0.2f, 1f, 0.3f) : new Color(1f, 0.15f, 0.1f);

            if (!GreenLight && Progress >= greenLightAt) { GreenLight = true; nextJump = Time.time + 0.4f; }
            if (GreenLight && next < stick.Count && Time.time >= nextJump)
            {
                if (next == playerSlot) { PlayerAtDoor?.Invoke(); return; }   // the player stands in the door until he jumps
                OutOfTheDoor(next);
            }
            if (Progress >= 1f) { Flying = false; SetVisible(false); }
        }

        /// <summary>The man in the door jumps (the mission calls this for the player when he presses jump).</summary>
        public void OutOfTheDoor(int i)
        {
            var man = stick[i];
            if (man)
            {
                Transform d = door ? door : transform;
                man.transform.SetPositionAndRotation(d.position - transform.right * 1.2f, Quaternion.LookRotation(-transform.right));
                man.Jump(Velocity * 0.9f - transform.right * 2f);
            }
            next = i + 1; nextJump = Time.time + jumpInterval;
        }
    }
}
