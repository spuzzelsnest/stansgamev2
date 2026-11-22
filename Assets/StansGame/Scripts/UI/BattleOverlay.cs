using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame
{
    /// <summary>
    /// F10 in play (the editor and the built game): the battle as the AI sees it. A panel lists each side's groups
    /// (from the battle sheet; men not on a sheet are grouped by army) with how many are alive, alert, asleep and
    /// stuck, and a tag floats over every soldier in view: his group, his orders and what he is doing.
    /// "Stuck" = has a reason to move (alarm raised, advancing, a patrol) but has not got 0.7 m further in 15 s:
    /// the men walking into walls or bunched behind each other show up in red.
    /// </summary>
    public class BattleOverlay : MonoBehaviour
    {
        static BattleOverlay instance;
        bool show;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (instance) return;
            var go = new GameObject("Battle overlay (F10)");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<BattleOverlay>();
        }

        class Watch { public Vector3 from; public float since; public bool stuck; }
        readonly Dictionary<SoldierAI, Watch> watch = new Dictionary<SoldierAI, Watch>();
        SoldierAI[] ais = new SoldierAI[0];
        float nextScan;

        class Row { public string side, name, id; public bool attacker; public int men, alive, alert, asleep, stuck; public string mode; }
        readonly List<Row> rows = new List<Row>();
        GUIStyle small, head, tag;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f10Key.wasPressedThisFrame)
            {
                show = !show;
                Debug.Log("Stan's Game: battle overlay " + (show ? "on" : "off") + Report());
            }
            if (!show) return;
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 1f;
                ais = FindObjectsByType<SoldierAI>(FindObjectsSortMode.None);
                Tally();
            }
        }

        bool HasReasonToMove(SoldierAI ai)
            => ai.IsAlert || ai.mode == SoldierAI.Mode.Advance || ai.mode == SoldierAI.Mode.FollowPlayer
               || (ai.patrolRoute != null && ai.patrolRoute.Length > 1 && !ai.IsAsleep);

        void Tally()
        {
            rows.Clear();
            var byKey = new Dictionary<string, Row>();
            foreach (var ai in ais)
            {
                if (!ai) continue;
                var h = ai.GetComponent<Health>();
                var bu = ai.GetComponent<BattleUnit>();
                string side = bu ? bu.side : h && h.faction == Faction.German ? "German (not on a sheet)" : "Allied (not on a sheet)";
                string group = bu ? bu.groupName : "-";
                string key = side + "|" + group;
                if (!byKey.TryGetValue(key, out var r))
                {
                    r = new Row { side = side, name = group, id = bu ? bu.group : null, attacker = bu ? bu.attacker : h && h.faction != Faction.German, mode = ai.mode.ToString() };
                    byKey[key] = r; rows.Add(r);
                }
                r.men++;
                bool dead = h && h.IsDead;
                if (dead) { watch.Remove(ai); continue; }
                r.alive++;
                if (ai.IsAlert) r.alert++;
                if (ai.IsAsleep) r.asleep++;

                if (!watch.TryGetValue(ai, out var w)) watch[ai] = w = new Watch { from = ai.transform.position, since = Time.time };
                Vector3 moved = ai.transform.position - w.from; moved.y = 0f;
                if (moved.sqrMagnitude > 0.49f || !HasReasonToMove(ai) || ai.Target) { w.from = ai.transform.position; w.since = Time.time; w.stuck = false; }
                else if (Time.time - w.since > 15f) w.stuck = true;
                if (w.stuck) r.stuck++;
            }
            rows.Sort((a, b) => a.attacker != b.attacker ? (a.attacker ? 1 : -1) : string.CompareOrdinal(a.name, b.name));
        }

        string Report()
        {
            if (!show) return "";
            Tally();
            var sb = new System.Text.StringBuilder("\n");
            foreach (var r in rows) sb.Append($"  {(r.attacker ? "ATT" : "DEF")}  {r.name}: {r.alive}/{r.men} alive, {r.alert} alert, {r.asleep} asleep, {r.stuck} stuck ({r.mode})\n");
            return sb.ToString();
        }

        void OnGUI()
        {
            if (!show) return;
            if (small == null)
            {
                small = new GUIStyle(GUI.skin.label) { fontSize = 12 }; small.normal.textColor = Color.white;
                head = new GUIStyle(small) { fontStyle = FontStyle.Bold, fontSize = 13 };
                tag = new GUIStyle(small) { fontSize = 11, alignment = TextAnchor.LowerCenter };
            }
            GUI.depth = -50;

            // the panel
            int orderLines = 0;
            foreach (var bc in BattleCommander.All) orderLines += 1 + Mathf.Min(4, bc.Orders.Count);
            float w = 560f, lh = 17f, h = 46f + rows.Count * lh * 2f + 30f + orderLines * lh;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(10, 60, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(20, 64, w, 20), "Battle (F10)   alive / men · alert · asleep · stuck", head);
            float y = 88f; string lastSide = null;
            foreach (var r in rows)
            {
                if (r.side != lastSide) { lastSide = r.side; GUI.Label(new Rect(20, y, w, lh), (r.attacker ? "ATTACKER  " : "DEFENDER  ") + r.side, head); y += lh; }
                small.normal.textColor = r.stuck > 0 ? new Color(1f, 0.45f, 0.4f) : r.alive == 0 ? new Color(0.6f, 0.6f, 0.6f) : Color.white;
                GUI.Label(new Rect(30, y, w - 20, lh), $"{r.name}   {r.alive}/{r.men} · {r.alert} · {r.asleep} · {r.stuck}   [{r.mode}]", small);
                y += lh;
                string order = null;
                if (r.id != null) foreach (var bc in BattleCommander.All) if (bc.GroupOrder.TryGetValue(r.id, out var o)) order = o;
                if (order != null) { small.normal.textColor = new Color(0.7f, 0.9f, 0.7f); GUI.Label(new Rect(44, y, w - 34, lh), "order: " + order, small); y += lh; }
            }
            foreach (var bc in BattleCommander.All)
            {
                y += 4f;
                GUI.Label(new Rect(20, y, w, lh), bc.Title, head); y += lh;
                small.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
                for (int i = Mathf.Max(0, bc.Orders.Count - 4); i < bc.Orders.Count; i++) { GUI.Label(new Rect(30, y, w - 20, lh), bc.Orders[i], small); y += lh; }
            }
            small.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
            GUI.Label(new Rect(20, y + 4, w, lh), $"Alarm: {(SoldierAI.Alarm ? "raised" : "not yet")}   ·   tags: within 120 m", small);
            small.normal.textColor = Color.white;

            // a tag over each man in view
            var cam = Camera.main;
            if (!cam) return;
            foreach (var ai in ais)
            {
                if (!ai) continue;
                var hp = ai.GetComponent<Health>();
                if (hp && hp.IsDead) continue;
                Vector3 p = ai.transform.position + Vector3.up * 2.1f;
                if ((p - cam.transform.position).sqrMagnitude > 120f * 120f) continue;
                Vector3 sp = cam.WorldToScreenPoint(p);
                if (sp.z <= 0f) continue;
                var bu = ai.GetComponent<BattleUnit>();
                watch.TryGetValue(ai, out var wt);
                string doing = ai.Target ? "fighting " + ai.Target.name : ai.IsAsleep ? "asleep" : ai.Searching ? "searching" : ai.IsAlert ? "alert" : ai.patrolRoute != null && ai.patrolRoute.Length > 1 ? "on patrol" : "at ease";
                string text = $"{(bu ? bu.group : ai.name)}\n{ai.mode} · {doing}{(wt != null && wt.stuck ? " · STUCK" : "")}";
                tag.normal.textColor = wt != null && wt.stuck ? new Color(1f, 0.35f, 0.3f) : (bu ? bu.attacker : hp && hp.faction != Faction.German) ? new Color(0.6f, 0.85f, 1f) : new Color(1f, 0.85f, 0.55f);
                GUI.Label(new Rect(sp.x - 110f, Screen.height - sp.y - 34f, 220f, 34f), text, tag);
            }
        }
    }
}
