using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Stan's Game > Debug > Validate Battle: checks every soldier in the open level (and its battle sheet) for the
    /// mistakes that show up as odd behaviour in play: a man placed inside a wall or a house, floating or under the
    /// ground, without a weapon, a patrol beat that runs through a wall, an attack with nowhere to go, men standing
    /// in each other, a landing point on a roof. Each problem is a warning in the Console; click it to select the man.
    /// Runs by itself after Sainte-Mère-Église is built.
    /// </summary>
    public static class BattleValidator
    {
        static readonly Collider[] hits = new Collider[32];

        [MenuItem("Stan's Game/Debug/Validate Battle (open level)", priority = 205)]
        static void Menu() => Validate(true);

        public static int Validate(bool verbose)
        {
            Physics.SyncTransforms();
            var terrain = Terrain.activeTerrain;
            int problems = 0, men = 0;
            var report = new System.Text.StringBuilder();
            void Problem(string what, Object ctx) { problems++; report.AppendLine("- " + what); Debug.LogWarning("Stan's Game - battle check: " + what, ctx); }

            var all = Object.FindObjectsByType<SoldierAI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var placed = new List<(SoldierAI ai, Vector3 p)>();
            foreach (var ai in all)
            {
                if (!ai || EditorUtility.IsPersistent(ai)) continue;
                men++;
                var t = ai.transform; Vector3 p = t.position;
                string who = $"'{t.name}' ({Path(t)})";
                var chute = ai.GetComponent<ParachuteDrop>();
                bool jumps = chute != null;   // he starts in the aircraft: check where he comes down instead
                Vector3 stand = jumps ? chute.aiLandingPoint : p;
                if (jumps && terrain) stand.y = Ground(terrain, stand);

                if (!ai.weapon) Problem($"{who} has no weapon", ai);
                if (ai.mode == SoldierAI.Mode.Advance && !ai.advanceTarget) Problem($"{who} is ordered to advance but has no objective", ai);

                if (terrain)
                {
                    var tp = terrain.transform.position; var size = terrain.terrainData.size;
                    if (stand.x < tp.x || stand.z < tp.z || stand.x > tp.x + size.x || stand.z > tp.z + size.z)
                        Problem($"{who} is off the map at {Fmt(stand)}", ai);
                    else if (!jumps)
                    {
                        float gy = Ground(terrain, p);
                        bool onSomething = Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out var hit, 1.3f, ~0, QueryTriggerInteraction.Ignore) && !Mine(hit.collider, t);
                        bool dugIn = Path(t).ToLowerInvariant().Contains("trench") || Path(t).ToLowerInvariant().Contains("pit");   // below ground on purpose
                        if (p.y < gy - (dugIn ? 1.8f : 0.4f)) Problem($"{who} is {gy - p.y:0.0} m under the ground", ai);
                        else if (p.y > gy + 1.2f && !onSomething) Problem($"{who} floats {p.y - gy:0.0} m above the ground with nothing under him", ai);
                    }
                }

                string inside = Blocked(stand, t);
                if (inside != null) Problem($"{who} {(jumps ? "lands" : "stands")} inside '{inside}' at {Fmt(stand)}", ai);

                var route = ai.patrolRoute;
                if (route != null && route.Length > 1)
                {
                    for (int k = 0; k < route.Length; k++)
                    {
                        Vector3 a = route[k], b = route[(k + 1) % route.Length];
                        if (terrain) { a.y = Mathf.Max(a.y, Ground(terrain, a)); b.y = Mathf.Max(b.y, Ground(terrain, b)); }
                        string atPoint = Blocked(a, t);
                        if (atPoint != null) Problem($"{who}: patrol point {k + 1} is inside '{atPoint}' at {Fmt(a)}", ai);
                        string wall = Through(a + Vector3.up * 1.0f, b + Vector3.up * 1.0f, t);
                        if (wall != null) Problem($"{who}: patrol leg {k + 1}->{(k + 1) % route.Length + 1} runs through '{wall}'", ai);
                    }
                }
                if (!jumps) placed.Add((ai, p));
            }

            // men standing in each other (placed by hand on the same spot)
            for (int i = 0; i < placed.Count; i++)
                for (int j = i + 1; j < placed.Count; j++)
                {
                    Vector3 d = placed[i].p - placed[j].p; d.y = 0f;
                    if (d.sqrMagnitude < 0.6f * 0.6f && Mathf.Abs(placed[i].p.y - placed[j].p.y) < 1.5f
                        && placed[i].ai.transform.parent == placed[j].ai.transform.parent)
                        Problem($"'{placed[i].ai.name}' and '{placed[j].ai.name}' stand in each other at {Fmt(placed[i].p)}", placed[i].ai);
                }

            // the battle sheets themselves
            foreach (var guid in AssetDatabase.FindAssets("t:BattlePlan"))
            {
                var plan = AssetDatabase.LoadAssetAtPath<BattlePlan>(AssetDatabase.GUIDToAssetPath(guid));
                if (!plan) continue;
                var ids = new HashSet<string>();
                foreach (var (side, g) in plan.Groups())
                {
                    string gw = $"sheet {plan.name}, {side.name} / '{g.name}'";
                    if (string.IsNullOrEmpty(g.id) || !ids.Add(g.id)) Problem($"{gw}: id '{g.id}' is empty or used twice", plan);
                    if (g.units.Count == 0) Problem($"{gw} has nobody in it", plan);
                    if (g.arrival == BattlePlan.Arrival.ByPhase && string.IsNullOrEmpty(g.phase)) Problem($"{gw} arrives by phase but has no phase", plan);
                }
            }

            string summary = $"Stan's Game - battle check: {men} soldiers checked, {problems} problem{(problems == 1 ? "" : "s")}";
            // the same list in a file per level (Logs/battle-check-<level>.txt), easier to read than the Console
            try
            {
                string level = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                System.IO.Directory.CreateDirectory("Logs");
                System.IO.File.WriteAllText($"Logs/battle-check-{level}.txt",
                    $"Battle check, {level}, {System.DateTime.Now:yyyy-MM-dd HH:mm}\n{men} soldiers checked, {problems} problems\n\n{report}");
            }
            catch (System.Exception e) { Debug.LogWarning("Stan's Game: could not write the battle check file: " + e.Message); }
            if (problems == 0) Debug.Log(summary + ". All clear.");
            else Debug.LogWarning(summary + " (see the warnings above; click one to select the man)");
            return problems;
        }

        /// <summary>Everyone standing inside a wall (or landing in a house) is moved to the nearest free spot within
        /// 8 m, and the new spot is written on the level's battle sheet so the next build keeps it. Then saves the scene.</summary>
        [MenuItem("Stan's Game/Debug/Move Men Out of Walls (open level)", priority = 206)]
        static void MoveOutOfWalls()
        {
            Physics.SyncTransforms();
            var terrain = Terrain.activeTerrain;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var plan = AssetDatabase.LoadAssetAtPath<BattlePlan>($"Assets/StansGame/Battles/{scene.name}.asset");
            int moved = 0, stuck = 0;
            foreach (var ai in Object.FindObjectsByType<SoldierAI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var t = ai.transform;
                var chute = ai.GetComponent<ParachuteDrop>();
                Vector3 at = chute ? chute.aiLandingPoint : t.position;
                if (chute && terrain) at.y = Ground(terrain, at);
                if (Blocked(at, t) == null) continue;
                Vector3? free = null;
                for (float r = 1f; r <= 8f && free == null; r += 1f)
                    for (int k = 0; k < 16 && free == null; k++)
                    {
                        float a = k * Mathf.PI / 8f + r;
                        Vector3 p = at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                        if (terrain) p.y = Ground(terrain, p);
                        if (Blocked(p, t) == null) free = p;
                    }
                if (free == null) { stuck++; Debug.LogWarning($"Stan's Game: no free spot within 8 m for '{t.name}' - move him by hand", ai); continue; }
                Undo.RecordObject(t, "Move men out of walls");
                if (chute) { Undo.RecordObject(chute, "Move men out of walls"); chute.aiLandingPoint = new Vector3(free.Value.x, chute.aiLandingPoint.y, free.Value.z); }
                else t.position = free.Value;
                moved++;
                // on the sheet too, so the next build keeps it
                var bu = ai.GetComponent<BattleUnit>();
                if (plan && bu)
                {
                    var g = plan.Find(bu.group);
                    var u = g != null ? g.units.Find(x => x.name == t.name && (x.position - at).sqrMagnitude < 4f) : null;
                    if (u == null && g != null) u = g.units.Find(x => x.name == t.name);
                    if (u != null) { Undo.RecordObject(plan, "Move men out of walls"); u.position = chute ? new Vector3(free.Value.x, u.position.y, free.Value.z) : free.Value; EditorUtility.SetDirty(plan); }
                }
                Debug.Log($"Stan's Game: moved '{t.name}' {Vector3.Distance(at, free.Value):0.0} m out of the wall", ai);
            }
            if (moved > 0)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }
            Debug.Log($"Stan's Game: {moved} men moved out of walls{(plan ? " (and on the battle sheet)" : "")}, {stuck} still need a hand");
            Validate(true);
        }

        static float Ground(Terrain t, Vector3 p) => t.SampleHeight(p) + t.transform.position.y;

        static bool Mine(Collider c, Transform self) => c.transform.IsChildOf(self);

        /// <summary>What solid thing a man standing here would be inside, or null.</summary>
        static string Blocked(Vector3 feet, Transform self)
        {
            int n = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * 0.5f, feet + Vector3.up * 1.5f, 0.25f, hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = hits[i];
                if (Mine(c, self) || c is TerrainCollider || c is CharacterController) continue;
                if (c.GetComponentInParent<SoldierAI>() || c.GetComponentInParent<Health>()) continue;   // other men, not walls
                if (c.attachedRigidbody && !c.attachedRigidbody.isKinematic) continue;                  // loose props he can push
                return Path(c.transform);
            }
            return null;
        }

        /// <summary>The first wall between two points at chest height, or null.</summary>
        static string Through(Vector3 a, Vector3 b, Transform self)
        {
            var all = Physics.RaycastAll(a, (b - a).normalized, Vector3.Distance(a, b), ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in all)
            {
                var c = h.collider;
                if (Mine(c, self) || c is TerrainCollider || c is CharacterController) continue;
                if (c.GetComponentInParent<SoldierAI>() || c.GetComponentInParent<Health>()) continue;
                if (c.bounds.size.y < 0.9f) continue;   // a kerb, a sandbag he steps over
                return Path(c.transform);
            }
            return null;
        }

        static string Path(Transform t)
        {
            string s = t.name;
            for (var p = t.parent; p && p.parent; p = p.parent) s = p.name + "/" + s;
            return s.Length > 90 ? "..." + s.Substring(s.Length - 87) : s;
        }

        static string Fmt(Vector3 p) => $"({p.x:0}, {p.y:0.0}, {p.z:0})";
    }
}
