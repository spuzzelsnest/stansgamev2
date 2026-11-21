using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Sainte-Mère-Église in the Pegasus Bridge style (Joan, Nov 2026): the same colour grade, and a town that is up and
    /// about at 01:30 - the fire, the bell, the bucket chain. A patrol with a guard dog walks the N13 and the street
    /// north of the square, a second patrol goes round the church; German lorries come through the town on the N13 and
    /// the road to Sainte-Marie-du-Mont; more C-47 serials of the 82nd drone over long after the player's stick has gone;
    /// windows are already lit (the townspeople are awake) and more come on once the shooting starts.
    /// Everything that moves is run by the host in co-op (see NetScenery and SmeMissionNet).
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        static void SmeAmbientLife(Transform root, Transform town, Transform garrison, Transform actors, Transform air)
        {
            ColourGrade(root, "SainteMereGrade");   // the same grade as Pegasus Bridge (all levels match)
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var data = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                if (!data) data = cam.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                data.renderPostProcessing = true;   // the grade needs post-processing on the player's camera
            }
            SmePatrols(garrison);
            SmeTraffic(actors);
            SmeC47Serials(air);
            var houses = town.Find("Houses");
            if (houses) WindowLightsFor(houses, 1944, 0.32f, 0.35f, new Vector3(-30f, 0f, -20f), 24);
        }

        static void SmePatrols(Transform garrison)
        {
            var g = Group("Patrols", garrison);
            // up the N13 from the town hall to the street north of the square, along it to the east end, and back
            var n13 = new[] { OnGround(SmeN13X + 3f, -80f), OnGround(SmeN13X + 3f, 18f), OnGround(-60f, 26f), OnGround(20f, 26f), OnGround(-60f, 26f), OnGround(SmeN13X + 3f, 18f) };
            // round the church, outside the churchyard wall
            var church = new[] { OnGround(-40f, -20f), OnGround(28f, -20f), OnGround(28f, 18f), OnGround(-40f, 18f) };
            var lead = SmePatrol(g, "Sentry patrol 1 (N13)", "Sentry patrol 2 (N13)", n13);
            if (lead) GuardDogFor(lead.transform, g);
            SmePatrol(g, "Sentry patrol 3 (church)", "Sentry patrol 4 (church)", church);
        }

        /// <summary>Two men of the garrison walking a round (awake: the town is up); the first one is returned.</summary>
        static Health SmePatrol(Transform g, string lead, string second, Vector3[] route)
        {
            Vector3 dir = route[1] - route[0]; dir.y = 0f; dir.Normalize();
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            Health first = null;
            for (int i = 0; i < 2; i++)
            {
                Vector3 p = route[0] - dir * (i * 3f) + Vector3.Cross(Vector3.up, dir) * (i * 0.8f);
                p.y = GroundAt(p);
                SmeGerman(g, i == 0 ? lead : second, p, yaw, SoldierAI.Mode.HoldPosition, true);
                var h = g.Find(i == 0 ? lead : second)?.GetComponent<Health>();
                if (i == 0) first = h;
                var ai = h ? h.GetComponent<SoldierAI>() : null;
                if (!ai) continue;
                var r = new Vector3[route.Length];
                for (int k = 0; k < route.Length; k++) r[k] = route[k] - dir * (i * 3f);
                ai.patrolRoute = r; ai.patrolPause = 2.5f; ai.walkSpeed = 1.25f;
                ai.startAlert = false; ai.sleepsUntilAlarm = false; ai.combatRoam = 10f;
            }
            return first;
        }

        static void SmeTraffic(Transform actors)
        {
            var g = Group("Traffic", actors);
            // from Neuville down the N13, east along the street north of the square and out on the Sainte-Marie-du-Mont road
            var south = new List<Vector3> { new Vector3(SmeN13X, 0f, 470f), new Vector3(SmeN13X, 0f, 26f), new Vector3(33f, 0f, 24f), new Vector3(110f, 0f, 40f), new Vector3(330f, 0f, 95f), new Vector3(500f, 0f, 137f) };
            // from Sainte-Marie-du-Mont the other way, then west out of town on the road to La Fière
            var west = new List<Vector3> { new Vector3(500f, 0f, 137f), new Vector3(330f, 0f, 95f), new Vector3(110f, 0f, 40f), new Vector3(33f, 0f, 24f), new Vector3(SmeN13X, 0f, 26f), new Vector3(SmeN13X - 4f, 0f, 46f), new Vector3(-480f, 0f, 46f) };
            // the town was full of shooting that night and the lorries drove on regardless: they do not stop for the
            // alarm here, and each comes through twice before the night is over
            AmbientLorry(g, "OpelBlitzCovered", "Opel Blitz (Neuville - Sainte-Marie-du-Mont)", Lane(south, 1.7f), 6f, 150f, false, 2);
            AmbientLorry(g, "OpelBlitzOpen", "Opel Blitz (Sainte-Marie-du-Mont - La Fière)", Lane(west, 1.7f), 40f, 170f, false, 2);
        }

        /// <summary>
        /// The 82nd Airborne's serials kept coming over for most of an hour: a vic of three C-47s (the player's
        /// model, invasion stripes and all) passes over the town every minute or two along the run-in, west to east.
        /// </summary>
        static void SmeC47Serials(Transform air)
        {
            var vic = new GameObject("C-47 vic (later serials)");
            vic.transform.SetParent(air);
            foreach (var (off, name) in new[] { (Vector3.zero, "Leader"), (new Vector3(-40f, -8f, -45f), "No. 2"), (new Vector3(40f, 6f, -50f), "No. 3") })
            {
                var plane = SmeC47($"C-47 ({name})", vic.transform);
                foreach (var f in plane.GetComponentsInChildren<C47Flight>(true)) Object.DestroyImmediate(f);
                foreach (var l in plane.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);   // no jump light
                plane.transform.localPosition = off;
                plane.transform.localRotation = Quaternion.identity;
            }
            var fly = vic.AddComponent<AmbientAircraft>();
            fly.passes = new[]
            {
                new AmbientAircraft.Pass { from = new Vector3(-900f, 165f, 120f), to = new Vector3(900f, 165f, 60f) },
                new AmbientAircraft.Pass { from = new Vector3(-900f, 185f, -140f), to = new Vector3(900f, 185f, -200f) },
                new AmbientAircraft.Pass { from = new Vector3(-900f, 150f, -10f), to = new Vector3(900f, 150f, -60f) },
            };
            fly.speed = 55f; fly.firstPassAt = 45f; fly.gap = new Vector2(60f, 120f);
            vic.transform.position = new Vector3(0f, -2000f, 0f);
            NetScenery(vic, false);
        }
    }
}
