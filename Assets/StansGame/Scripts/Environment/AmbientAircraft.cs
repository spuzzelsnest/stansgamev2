using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Bombers droning over in the night: a vic of aircraft flies a straight pass over the map, again and again,
    /// each pass on the next of the given lines. Between passes it waits far below the map.
    /// Multiplayer: the host flies it (a network object); the other machines see it where the host has it and play
    /// the engine drone whenever it is up.
    /// </summary>
    public class AmbientAircraft : MonoBehaviour
    {
        [System.Serializable] public struct Pass { public Vector3 from, to; }
        public Pass[] passes;
        public float speed = 70f;            // ~250 km/h
        public float firstPassAt = 25f;
        public Vector2 gap = new Vector2(90f, 160f);
        public float bank = 0f;

        const float HiddenY = -2000f;
        AudioSource drone;
        int index = -1;
        float t, length, clock, nextAt;
        bool flying, started;
        readonly System.Random rng = new System.Random(644);

        [Range(0f, 1f)] public float volume = 0.9f;

        void Awake()
        {
            drone = gameObject.AddComponent<AudioSource>();
            drone.clip = Sfx.AircraftDrone; drone.loop = true; drone.spatialBlend = 1f;
            drone.minDistance = 120f; drone.maxDistance = 3500f; drone.rolloffMode = AudioRolloffMode.Linear;
            drone.volume = volume; drone.dopplerLevel = 0.6f; drone.pitch = 0.82f;   // four Hercules: deeper than a Dakota
        }

        void Update()
        {
            bool up = transform.position.y > HiddenY * 0.5f;
            if (up && !drone.isPlaying) drone.Play(); else if (!up && drone.isPlaying) drone.Stop();
            if (!Net.Simulates || passes == null || passes.Length == 0) return;   // the host flies it
            if (!started) { started = true; nextAt = firstPassAt; Place(new Vector3(0f, HiddenY, 0f), Quaternion.identity, true); }
            clock += Time.deltaTime;
            if (!flying)
            {
                if (clock < nextAt) return;
                index = (index + 1) % passes.Length;
                t = 0f; length = Vector3.Distance(passes[index].from, passes[index].to);
                flying = true;
                Place(passes[index].from, Quaternion.LookRotation((passes[index].to - passes[index].from).normalized), true);
                return;
            }
            t += speed * Time.deltaTime;
            var p = passes[index];
            Vector3 dir = (p.to - p.from).normalized;
            Place(Vector3.Lerp(p.from, p.to, t / length), Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, 0f, bank), false);
            if (t >= length)
            {
                flying = false;
                nextAt = clock + gap.x + (float)rng.NextDouble() * (gap.y - gap.x);
                Place(new Vector3(0f, HiddenY, 0f), transform.rotation, true);
            }
        }

        void Place(Vector3 p, Quaternion r, bool jump)
        {
#if STANS_NET
            if (jump && Net.Active)
            {
                var nt = GetComponent<Unity.Netcode.Components.NetworkTransform>();
                if (nt && nt.IsSpawned) { nt.Teleport(p, r, transform.localScale); return; }
            }
#endif
            transform.SetPositionAndRotation(p, r);
        }
    }
}
