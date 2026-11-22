using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace StansGame
{
    /// <summary>
    /// Ways round the buildings for the AI. When a level starts, a navigation mesh is baked in the background from
    /// the level's own colliders (the ground, the houses, walls, hedges), over the area where the soldiers are.
    /// Until it is ready - and wherever it doesn't reach - the men steer as before (SoldierAI.Avoid). With it, a man
    /// whose goal is on the far side of a long terrace walks round the end of it instead of searching along the
    /// wall for a way through (Joan's printscreens 03 and 08).
    /// </summary>
    public static class NavPaths
    {
        static NavMeshDataInstance instance;
        static NavMeshData data;
        static AsyncOperation building;
        static string sceneName;
        public static bool Ready { get; private set; }

        /// <summary>Start baking for the open level, if not done yet. Cheap to call every frame.</summary>
        static int checkedFrame = -1;
        public static void Ensure()
        {
            if (checkedFrame == Time.frameCount) return;
            checkedFrame = Time.frameCount;
            string now = SceneManager.GetActiveScene().name;
            if (now == sceneName) return;
            Clear();
            sceneName = now;
            Bake();
        }

        static void Clear()
        {
            Ready = false;
            if (instance.valid) NavMesh.RemoveNavMeshData(instance);
            instance = default; data = null; building = null;
        }

        static void Bake()
        {
            // the area: round the soldiers and where they're headed, plus a margin; not the whole map
            var ais = Object.FindObjectsByType<SoldierAI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (ais.Length == 0) return;
            var b = new Bounds(ais[0].transform.position, Vector3.one);
            foreach (var a in ais) { b.Encapsulate(a.transform.position); if (a.advanceTarget) b.Encapsulate(a.advanceTarget.position); }
            b.Expand(new Vector3(160f, 80f, 160f));
            b.size = new Vector3(Mathf.Min(b.size.x, 1100f), Mathf.Min(b.size.y, 160f), Mathf.Min(b.size.z, 1100f));

            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(b, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
            // only what stays put: not the soldiers, the vehicles, the gliders' loose parts or anything else that moves
            sources.RemoveAll(s =>
            {
                var c = s.component;
                if (!c) return false;
                if (c is Collider col && col.isTrigger) return true;
                if (c.GetComponentInParent<Health>()) return true;
                var rb = c.GetComponentInParent<Rigidbody>();
                return rb && !rb.isKinematic;
            });

            // meshes the game can't read in a build (most models): use their box instead - enough to walk round
            for (int i = 0; i < sources.Count; i++)
            {
                var s = sources[i];
                if (s.shape == NavMeshBuildSourceShape.Mesh && s.sourceObject is Mesh m && !m.isReadable)
                {
                    var bb = m.bounds;
                    s.shape = NavMeshBuildSourceShape.Box;
                    s.size = bb.size;
                    s.transform = s.transform * Matrix4x4.Translate(bb.center);
                    s.sourceObject = null;
                    sources[i] = s;
                }
            }

            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = 0.35f; settings.agentHeight = 1.8f; settings.agentSlope = 45f; settings.agentClimb = 0.6f;
            settings.overrideVoxelSize = true; settings.voxelSize = 0.25f;
            settings.overrideTileSize = true; settings.tileSize = 128;
            data = new NavMeshData(settings.agentTypeID) { name = "Stan's Game navigation (" + sceneName + ")" };
            instance = NavMesh.AddNavMeshData(data);
            building = NavMeshBuilder.UpdateNavMeshDataAsync(data, settings, sources, b);
            building.completed += _ => { Ready = true; Debug.Log($"Stan's Game: navigation ready for {sceneName} ({sources.Count} pieces)"); };
        }

        static readonly NavMeshPath scratch = new NavMeshPath();

        /// <summary>The corners of the way from a to b, or false (not baked yet, off the mesh, no way found).</summary>
        public static bool Route(Vector3 from, Vector3 to, List<Vector3> corners)
        {
            corners.Clear();
            if (!Ready) return false;
            if (!NavMesh.SamplePosition(from, out var hf, 3f, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(to, out var ht, 6f, NavMesh.AllAreas)) return false;
            if (!NavMesh.CalculatePath(hf.position, ht.position, NavMesh.AllAreas, scratch) || scratch.status == NavMeshPathStatus.PathInvalid) return false;
            corners.AddRange(scratch.corners);
            return corners.Count > 1;
        }
    }
}
