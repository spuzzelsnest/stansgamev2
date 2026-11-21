using System.Collections;
using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// The second half of the coup de main: three Horsas were sent for the Ranville bridge over the Orne ("Euston II").
    /// Two came down on landing zone Y between the canal and the river - Lt Fox's 17 Platoon close to the bridge,
    /// Lt Sweeney's 23 Platoon well short of it - and the third (Capt Priday with Lt Hooper's 22 Platoon) was cast off
    /// over the Dives and landed miles away. The platoons climb out, go for the bridge and take it from its guard;
    /// then the wireless can report "Jam" (MissionDirector waits for Taken).
    /// Multiplayer: the host spawns the platoons and decides when the bridge is taken.
    /// </summary>
    public class OrneAssault : MonoBehaviour
    {
        public GliderApproach[] gliders;          // [0] Fox (chalk 96), [1] Sweeney (chalk 95)
        public Transform[] exits;                 // where each platoon climbs out
        public string[] landedMessages;
        public GameObject rifle, bren, sten;      // the platoon's men
        [Tooltip("The way to the bridge, in order: onto the Ranville road west of the river, the west end of the bridge, then over it (the last point).")]
        public Transform[] route;
        public Health[] guard;                    // the bridge's German guard
        public int platoonSize = 7;
        public float lostGliderAfter = 75f;       // seconds after the first Orne glider lands: no sign of the third

        /// <summary>The Orne bridge is in British hands (host).</summary>
        public static bool Taken { get; private set; }

        bool reported, lostSaid;
        readonly System.Collections.Generic.Dictionary<SoldierAI, int> men = new System.Collections.Generic.Dictionary<SoldierAI, int>();
        float firstLandedAt = -1f;

        void Awake() => Taken = false;

        void Start()
        {
            for (int i = 0; i < gliders.Length; i++)
            {
                int idx = i;
                if (gliders[i]) gliders[i].Landed += g => OnLanded(idx);
            }
        }

        void OnLanded(int i)
        {
            if (!Net.Simulates) return;
            if (firstLandedAt < 0f) firstLandedAt = Time.time;
            var md = MissionDirector.Instance;
            if (md && landedMessages != null && i < landedMessages.Length) md.Say(landedMessages[i], 4f);
            var spawn = exits != null && i < exits.Length && exits[i] ? exits[i] : gliders[i].transform;
            for (int k = 0; k < platoonSize; k++)
            {
                var pick = k % 3 == 2 && bren ? bren : k % 3 == 1 && sten ? sten : rifle;
                if (!pick) continue;
                Vector3 p = spawn.position + spawn.right * (k - platoonSize / 2f) * 1.4f - spawn.forward * 2f;
                var s = Instantiate(pick, p, spawn.rotation);
                s.name = $"{(i == 0 ? "17 Platoon (Fox)" : "23 Platoon (Sweeney)")} {k + 1}";
                var ai = s.GetComponent<SoldierAI>();
                if (ai && route != null && route.Length > 0) { ai.mode = SoldierAI.Mode.Advance; ai.advanceTarget = route[0]; ai.startAlert = true; men[ai] = 0; }
                NetHooks.Spawned?.Invoke(s);
            }
        }

        void Update()
        {
            if (!Net.Simulates) return;
            // over the bridge, not through the river: point by point along the route (a point counts as reached when the
            // man is close to it or already past it towards the next one)
            if (route != null && route.Length > 1)
                foreach (var ai in new System.Collections.Generic.List<SoldierAI>(men.Keys))
                {
                    if (!ai) { men.Remove(ai); continue; }
                    int i = men[ai];
                    if (i >= route.Length - 1) continue;
                    Vector3 here = ai.transform.position, wp = route[i].position, next = route[i + 1].position;
                    Vector3 d = here - wp; d.y = 0f; Vector3 ahead = next - wp; ahead.y = 0f;
                    if (d.magnitude < 6f || Vector3.Dot(d, ahead) > 0f)
                    {
                        men[ai] = i + 1; ai.advanceTarget = route[i + 1];
                    }
                }
            if (reported) return;
            var md = MissionDirector.Instance;
            if (!lostSaid && firstLandedAt > 0f && Time.time - firstLandedAt > lostGliderAfter)
            {
                lostSaid = true;
                if (md) md.Say("No sign of glider 94 (Capt Priday, Lt Hooper): it was cast off over the wrong river.", 5f);
            }
            if (firstLandedAt < 0f || guard == null) return;
            foreach (var h in guard) if (h && !h.IsDead) return;
            reported = true; Taken = true;
            if (md) md.Say("Lt Fox at the river bridge: the Orne bridge is taken intact.", 5f);
        }
    }
}
