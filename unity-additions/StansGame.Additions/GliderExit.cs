using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StansGame.Additions
{
    /// <summary>
    /// Gets the player and the glider sections out on the open side of each glider, facing the fight.
    /// In the build the section spawn points sit behind the gliders, against the wreckage colliders.
    /// When a glider has landed and settled, this looks for clear, level ground around the real wreck,
    /// preferring the side that faces the objective, and moves that glider's spawn point there
    /// (rotated towards the objective). Soldiers the landing just spawned are moved with it.
    /// When the player gets control after the Intro, a short exit plays: the screen opens from black
    /// with the door going, and the player jumps down from the glider door onto that ground.
    /// Also stops a respawn during "Over the bridge" from completing that phase (the game respawns
    /// the player at the bridge objective; see the level 1 review, finding 2).
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class GliderExit : MonoBehaviour
    {
        const float SettleSeconds = 0.4f;
        const float ExitSeconds = 1.15f;

        MissionDirector watched;
        float[] landedAt;
        bool[] done;
        Vector3[] spots;
        bool exitPlayed;
        bool exiting;
        float blackUntil, fadeUntil;
        SoldierController lastPlayer;

        AudioSource sfx;
        AudioClip doorClip, landClip;
        static Texture2D white;

        /// <summary>True while the scripted exit runs.</summary>
        public static bool Exiting { get; private set; }

        /// <summary>Times a spawn point was moved / soldiers moved with it (for tests).</summary>
        public static int SpawnsMoved, SoldiersMoved;

        void Awake()
        {
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 0f;
            doorClip = Thump("glider door", 0.45f, 55f, 0.9f);
            landClip = Thump("boots landing", 0.18f, 90f, 0.6f);
        }

        void Update()
        {
            MissionDirector md = MissionDirector.Instance;
            if (md != watched)
            {
                watched = md;
                int n = md != null && md.gliders != null ? md.gliders.Length : 0;
                landedAt = new float[n];
                done = new bool[n];
                spots = new Vector3[n];
                for (int i = 0; i < n; i++) landedAt[i] = -1f;
                exitPlayed = false;
                lastPlayer = null;
            }
            if (md == null || Net.InMenu || md.gliders == null) return;

            for (int i = 0; i < md.gliders.Length; i++)
            {
                GliderApproach g = md.gliders[i];
                if (done[i] || g == null || !g.HasLanded) continue;
                if (landedAt[i] < 0f) { landedAt[i] = Time.time; continue; }
                if (Time.time - landedAt[i] < SettleSeconds) continue;
                done[i] = true;
                Relocate(md, i, g);
            }

            SoldierController p = AdditionsBootstrap.LocalPlayer();
            GuardBridgeRespawn(md, p);

            if (!exitPlayed && p != null && p.enabled && !CinematicDirector.Playing && md.CurrentPhase == 0
                && done.Length > 0 && done[0])
            {
                exitPlayed = true;
                if (Flat(p.transform.position - spots[0]) < 25f) StartCoroutine(Exit(md, p));
            }
        }

        // ------------------------------------------------------------------ spawn points

        void Relocate(MissionDirector md, int i, GliderApproach g)
        {
            if (md.gliderSectionSpawns == null || i >= md.gliderSectionSpawns.Length || md.gliderSectionSpawns[i] == null) return;
            Transform sp = md.gliderSectionSpawns[i];
            Vector3 objective = ObjectiveHud.PhaseZeroTarget(md, g.transform.position);
            Vector3 spot;
            if (!FindExitSpot(g, objective, out spot))
            {
                spots[i] = sp.position;
                Debug.LogWarning("Stan's Game: no clear ground found around glider " + (i + 1) + " (rejected: ground " + rejGround + ", blocked " + rejClear + ", no room " + rejRoom + ", no view " + rejView + "); spawn left as it was");
                return;
            }

            Vector3 old = sp.position;
            Quaternion face = Face(spot, objective);
            spots[i] = spot;
            // The game puts the player at spawn + right*(id%6 - 2.5)*1.6 + forward*1.5; centre client 0 on the spot.
            sp.SetPositionAndRotation(spot + face * new Vector3(2.5f * 1.6f, 0f, -1.5f), face);
            SpawnsMoved++;

            if (!Net.Simulates) return;
            int k = 0;
            foreach (SoldierAI ai in FindObjectsByType<SoldierAI>())
            {
                Health h = ai.GetComponent<Health>();
                if (h == null || h.faction != Faction.British || h.IsDead) continue;
                if (Flat(ai.transform.position - old) > 9f) continue;
                Vector3 slot = spot + face * new Vector3(((k % 3) - 1) * 1.3f, 0f, -(k / 3) * 1.4f - 1.2f);
                Vector3 free;
                if (!Clear(slot, g)) slot = LocalPath.FindFreeSpot(ai.GetComponent<CharacterController>(), slot, out free) ? free : spot;
                Teleport(ai.transform, slot, face);
                k++;
                SoldiersMoved++;
            }
            Debug.Log("Stan's Game: glider " + (i + 1) + " spawn moved to the open side (" + Flat(spot - old).ToString("0.0") + " m), " + k + " soldiers with it");
        }

        /// <summary>Clear, level ground next to the wreck, preferring the side that faces the objective.</summary>
        static int rejGround, rejClear, rejRoom, rejView;

        static bool FindExitSpot(GliderApproach g, Vector3 objective, out Vector3 best)
        {
            rejGround = rejClear = rejRoom = rejView = 0;
            if (FindExitSpot(g, objective, true, out best)) return true;
            return FindExitSpot(g, objective, false, out best);   // no open view anywhere: take any clear ground
        }

        static bool FindExitSpot(GliderApproach g, Vector3 objective, bool strict, out Vector3 best)
        {
            best = Vector3.zero;
            Bounds b = GliderBounds(g);
            Vector3 c = b.center;
            Vector3 toObj = objective - c; toObj.y = 0f;
            if (toObj.sqrMagnitude < 0.01f) toObj = g.transform.forward;
            toObj.Normalize();
            float bestScore = float.MaxValue;
            for (int a = -180; a < 180; a += 10)
            {
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * toObj;
                for (float d = 2.5f; d <= 18f; d += 0.5f)
                {
                    Vector3 p = c + dir * d;
                    Vector3 ground;
                    if (!Ground(p, g, out ground)) { rejGround++; continue; }
                    if (!Clear(ground, g)) { rejClear++; continue; }
                    if (strict)
                    {
                        // Room for the section and an open view towards the objective.
                        if (!Clear(ground + dir * 1.4f, g) || !Clear(ground - Vector3.Cross(Vector3.up, dir) * 1.4f, g)
                            || !Clear(ground + Vector3.Cross(Vector3.up, dir) * 1.4f, g)) { rejRoom++; continue; }
                        Vector3 look = objective - ground; look.y = 0f;
                        if (Physics.Raycast(ground + Vector3.up * 1.4f, look.normalized, 6f, ~0, QueryTriggerInteraction.Ignore)) { rejView++; continue; }
                    }
                    float score = Mathf.Abs(a) / 18f + d * 0.35f;
                    if (score < bestScore) { bestScore = score; best = ground; }
                    break;   // nearest clear point on this bearing
                }
            }
            return bestScore < float.MaxValue;
        }

        static Bounds GliderBounds(GliderApproach g)
        {
            bool any = false;
            Bounds b = new Bounds(g.transform.position, Vector3.one);
            foreach (Collider col in g.GetComponentsInChildren<Collider>())
            {
                if (!col.enabled || col.isTrigger) continue;
                if (!any) { b = col.bounds; any = true; } else b.Encapsulate(col.bounds);
            }
            return b;
        }

        static bool Ground(Vector3 p, GliderApproach g, out Vector3 ground)
        {
            ground = p;
            RaycastHit[] hits = Physics.RaycastAll(p + Vector3.up * 6f, Vector3.down, 14f, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            bool found = false;
            foreach (RaycastHit h in hits)
            {
                if (h.collider is CharacterController || Obstacle.IsGlider(h.collider)) return false;   // under or on a glider
                if (h.distance < nearest) { nearest = h.distance; ground = h.point; found = true; }
            }
            if (!found || Vector3.Angle(Vector3.up, NormalAt(ground)) > 25f) return false;
            float th;
            if (TerrainHeight(ground, out th) && ground.y - th > 0.35f) return false;   // on top of wreckage
            return true;
        }

        static bool TerrainHeight(Vector3 p, out float h)
        {
            h = 0f;
            foreach (Terrain t in Terrain.activeTerrains)
            {
                if (t == null || t.terrainData == null) continue;
                Vector3 o = t.transform.position, size = t.terrainData.size;
                if (p.x < o.x || p.z < o.z || p.x > o.x + size.x || p.z > o.z + size.z) continue;
                h = t.SampleHeight(p) + o.y;
                return true;
            }
            return false;
        }

        static Vector3 NormalAt(Vector3 p)
        {
            RaycastHit h;
            return Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out h, 1.5f, ~0, QueryTriggerInteraction.Ignore) ? h.normal : Vector3.up;
        }

        static bool Clear(Vector3 ground, GliderApproach g)
        {
            Collider[] hits = Physics.OverlapCapsule(ground + Vector3.up * 0.55f, ground + Vector3.up * 1.7f, 0.5f, ~0, QueryTriggerInteraction.Ignore);
            foreach (Collider c in hits)
                if (!(c is CharacterController) && !(c is TerrainCollider)) return false;
            // Keep a step away from the hull.
            foreach (Collider c in Physics.OverlapSphere(ground + Vector3.up * 1f, 1.2f, ~0, QueryTriggerInteraction.Ignore))
                if (Obstacle.IsGlider(c)) return false;
            return true;
        }

        static Quaternion Face(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from; d.y = 0f;
            return d.sqrMagnitude > 0.01f ? Quaternion.LookRotation(d.normalized, Vector3.up) : Quaternion.identity;
        }

        static void Teleport(Transform t, Vector3 pos, Quaternion rot)
        {
            CharacterController cc = t.GetComponent<CharacterController>();
            bool was = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;
            t.SetPositionAndRotation(pos, rot);
            if (cc != null) cc.enabled = was;
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        // ------------------------------------------------------------------ the exit

        IEnumerator Exit(MissionDirector md, SoldierController p)
        {
            exiting = true;
            Exiting = true;
            GliderApproach g = md.gliders[0];
            Vector3 spot = spots[0];
            Vector3 objective = ObjectiveHud.PhaseZeroTarget(md, spot);
            Quaternion face = Face(spot, objective);

            // Door: the point on the hull nearest the spot, just outside it, at sill height.
            Vector3 start = spot - face * Vector3.forward * 1.6f;
            Vector3 hullDir = GliderBounds(g).center - spot; hullDir.y = 0f;
            RaycastHit hit;
            if (hullDir.sqrMagnitude > 0.01f
                && Physics.Raycast(spot + Vector3.up * 1.2f, hullDir.normalized, out hit, 20f, ~0, QueryTriggerInteraction.Ignore)
                && Obstacle.IsGlider(hit.collider))
                start = new Vector3(hit.point.x, spot.y, hit.point.z) - hullDir.normalized * 0.45f;
            start.y = spot.y + 0.75f;

            CharacterController cc = p.GetComponent<CharacterController>();
            p.enabled = false;
            if (cc != null) cc.enabled = false;
            p.transform.SetPositionAndRotation(start, face);
            blackUntil = Time.time + 0.35f;
            fadeUntil = blackUntil + 0.9f;
            Play(doorClip, 0.9f);
            yield return new WaitForSeconds(0.35f);

            float t0 = Time.time;
            bool landed = false;
            while (Time.time - t0 < ExitSeconds && p != null)
            {
                float t = (Time.time - t0) / ExitSeconds;
                float move = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t / 0.8f));
                Vector3 pos = Vector3.Lerp(start, spot, move);
                pos.y = Mathf.Lerp(start.y, spot.y, move) + 0.35f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, t / 0.8f));
                if (t > 0.78f)
                {
                    if (!landed) { landed = true; Play(landClip, 0.8f); }
                    pos.y -= 0.22f * Mathf.Sin(Mathf.PI * Mathf.InverseLerp(0.78f, 1f, t));   // knees take the landing
                }
                p.transform.SetPositionAndRotation(pos, face);
                yield return null;
            }
            if (p != null)
            {
                p.transform.SetPositionAndRotation(spot, face);
                if (cc != null) cc.enabled = true;
                p.enabled = true;
            }
            exiting = false;
            Exiting = false;
            ObjectiveHud.Announce();
            Debug.Log("Stan's Game: glider exit played, player at " + spot.ToString("0.0"));
        }

        // Respawning during phase 1 puts the player inside the bridge radius, which completes the phase
        // on the next MissionDirector.Update. This runs earlier in the frame and moves the new player
        // back to the east bank.
        void GuardBridgeRespawn(MissionDirector md, SoldierController p)
        {
            if (p == lastPlayer) return;
            SoldierController before = lastPlayer;
            lastPlayer = p;
            if (before == null || p == null || md.CurrentPhase != 1 || md.bridgeObjective == null) return;
            if (Vector3.Distance(p.transform.position, md.bridgeObjective.position) > md.bridgeRadius + 2f) return;
            Vector3 back = spots.Length > 0 && done[0] ? spots[0]
                         : md.gliderSectionSpawns != null && md.gliderSectionSpawns.Length > 0 && md.gliderSectionSpawns[0] != null ? md.gliderSectionSpawns[0].position
                         : p.transform.position;
            Teleport(p.transform, back, p.transform.rotation);
            Debug.Log("Stan's Game: respawn during 'Over the bridge' moved back to the east bank (the bridge must be crossed)");
        }

        // ------------------------------------------------------------------ screen and sound

        void OnGUI()
        {
            GUI.depth = -20;
            if (!exiting || Event.current.type != EventType.Repaint) return;
            if (white == null)
            {
                white = new Texture2D(1, 1);
                white.SetPixel(0, 0, Color.white);
                white.Apply();
            }
            float a = Time.time < blackUntil ? 1f : Mathf.Clamp01((fadeUntil - Time.time) / (fadeUntil - blackUntil));
            Color keep = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, a);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), white);
            GUI.color = keep;
        }

        void Play(AudioClip c, float v)
        {
            if (c != null) sfx.PlayOneShot(c, v);
        }

        static AudioClip Thump(string name, float seconds, float freq, float noise)
        {
            int rate = 44100, n = Mathf.CeilToInt(rate * seconds);
            float[] d = new float[n];
            System.Random r = new System.Random(7);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float env = Mathf.Exp(-t * 14f);
                lp = Mathf.Lerp(lp, (float)(r.NextDouble() * 2.0 - 1.0), 0.18f);
                d[i] = (Mathf.Sin(2f * Mathf.PI * freq * t) * 0.7f + lp * noise) * env * 0.8f;
            }
            AudioClip c = AudioClip.Create(name, n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }
    }
}
