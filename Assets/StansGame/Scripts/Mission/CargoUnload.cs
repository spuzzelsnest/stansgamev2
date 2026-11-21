using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Kit that comes in with a glider (the 2-inch mortar of chalk 91 at Pegasus Bridge). The kit stays hidden while
    /// the glider flies in; once the glider has come back to its landing area and stopped, the kit appears on the ground
    /// at an offset from the glider, as if the section had just unloaded it.
    /// The glider is watched by reference only, so a glider that breaks up or is replaced by network copies is fine.
    /// </summary>
    public class CargoUnload : MonoBehaviour
    {
        public Transform glider;
        public GameObject kit;                                 // child of this object, inactive until unloaded
        public Vector3 offset = new Vector3(-2.5f, 0f, 11f);   // in the glider's frame: ahead of the nose, port side
        public float settleSeconds = 2.5f;

        Vector3 home, last, lastSeen;
        Quaternion lastRot;
        GliderApproach approach;   // the glider's own approach script: its landing is what counts
        bool away, done;
        float still;

        void Start()
        {
            if (kit) kit.SetActive(false);
            if (!glider) { enabled = false; return; }
            approach = glider.GetComponent<GliderApproach>();
            home = transform.position;   // the builder put this object on the glider's landing spot
            last = lastSeen = glider.position;
            lastRot = glider.rotation;
        }

        void Update()
        {
            if (done) return;
            if (!glider) { Unload(lastSeen, lastRot); return; }   // the glider broke up: drop the kit where it was
            var p = glider.position;
            float fromHome = Vector3.Distance(p, home);
            if (fromHome > 60f) away = true;                      // it has gone off to start its approach
            float speed = (p - last).magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
            last = p; lastSeen = p; lastRot = glider.rotation;
            bool landed = approach && approach.HasLanded;
            if (!landed && approach && MissionDirector.Instance && MissionDirector.Instance.gliders != null)
            {   // on a client the host's landing arrives as a bit in the mission's landed mask
                int i = System.Array.IndexOf(MissionDirector.Instance.gliders, approach);
                landed = i >= 0 && (MissionDirector.Instance.LandedMask & (1 << i)) != 0;
            }
            if (approach) { still = landed ? still + Time.deltaTime : 0f; }
            else
            {
                if (!away) return;
                still = speed < 0.3f && fromHome < 150f ? still + Time.deltaTime : 0f;
            }
            if (still >= settleSeconds) Unload(p, glider.rotation);
        }

        void Unload(Vector3 at, Quaternion rot)
        {
            done = true; enabled = false;
            var flat = Quaternion.Euler(0f, rot.eulerAngles.y, 0f);
            var pos = at + flat * offset;
            float best = float.MaxValue;
            foreach (var h in Physics.RaycastAll(pos + Vector3.up * 8f, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (glider && h.collider.transform.IsChildOf(glider)) continue;
                if (h.distance < best) { best = h.distance; pos.y = h.point.y; }
            }
            transform.SetPositionAndRotation(pos, flat);
            if (kit) kit.SetActive(true);
        }
    }
}
