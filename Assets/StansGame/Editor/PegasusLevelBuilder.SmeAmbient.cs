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
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                var data = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                if (!data) data = cam.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                data.renderPostProcessing = true;   // the grade needs post-processing on the player's camera
            }
            SmePatrols(garrison);
            SmeTraffic(actors);
            SmeC47Serials(air);
            var houses = town.Find("Houses");
            if (houses) WindowLightsFor(houses, 1944, 0.15f, 0.1f, new Vector3(-30f, 0f, -20f), 8);   // blackout: few, faint
        }

        static void SmePatrols(Transform garrison)
        {
            var g = Group("Patrols", garrison);
            var plan = smePlan ? smePlan : SmeBattlePlan();
            var lead = SpawnPatrol(plan, "patrol-n13", g);       // up the N13 and along the street north of the square
            if (lead) GuardDogFor(lead.transform, g);
            SpawnPatrol(plan, "patrol-church", g);               // round the church, outside the churchyard wall
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
            fly.volume = 0.45f;   // heard, not deafening
            fly.speed = 55f; fly.firstPassAt = 45f; fly.gap = new Vector2(60f, 120f);
            vic.transform.position = new Vector3(0f, -2000f, 0f);
            NetScenery(vic, false);
        }
    }
}
