using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame.Additions
{
    /// <summary>
    /// Calling the section for help. T shouts for a stretcher-bearer, B for ammunition: the nearest
    /// British soldier breaks off, runs to the player and dresses his wounds or hands over magazines,
    /// then goes back to what he was doing. One call at a time, each kind with its own wait before
    /// it can be called again (shorter for the section commander). While he runs over he does not stop
    /// to fight, so a call in the open can cost him. A marker shows who is coming and how far off he is.
    /// Also draws two small lines top right: the role with its G action, and the two calls with their wait.
    /// The AI is moved with its own Advance mode, like <see cref="AiUnstick"/> routes do.
    /// </summary>
    public sealed class SquadCalls : MonoBehaviour
    {
        enum Need { Medic, Ammo }

        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly FieldInfo targetField = typeof(SoldierAI).GetField("target", Any);
        static readonly FieldInfo coverField = typeof(SoldierAI).GetField("coverSpot", Any);
        static readonly FieldInfo runningField = typeof(SoldierAI).GetField("running", Any);
        static readonly FieldInfo moveGoalField = typeof(SoldierAI).GetField("moveGoal", Any);
        static readonly FieldInfo nextMoveField = typeof(SoldierAI).GetField("nextMoveDecision", Any);
        static readonly FieldInfo nextScanField = typeof(SoldierAI).GetField("nextScan", Any);
        static readonly FieldInfo nextBurstField = typeof(SoldierAI).GetField("nextBurst", Any);

        const float SearchRadius = 90f, Reach = 3f, GiveUpAfter = 45f, MedicHeal = 80f;

        public static int Calls, Arrivals;

        SoldierController soldier;
        Loadout loadout;
        Health health;
        RoleKit kit;

        // The call under way
        bool active;
        Need need;
        SoldierAI responder;
        Health responderHealth;
        Transform goal;
        SoldierAI.Mode savedMode;
        Transform savedTarget;
        float startedAt;
        float closest, closerAt;
        readonly System.Collections.Generic.List<SoldierAI> tried = new System.Collections.Generic.List<SoldierAI>();
        readonly float[] readyAt = new float[2];

        const float StuckAfter = 5f;
        public static int Switches;

        static Texture2D white;
        static GUIStyle line, small, marker;

        /// <summary>For tests: who is answering the current call, or null.</summary>
        public SoldierAI Responder { get { return active ? responder : null; } }

        void Awake()
        {
            soldier = GetComponent<SoldierController>();
            loadout = GetComponent<Loadout>();
            if (loadout == null) loadout = GetComponentInChildren<Loadout>();
            health = GetComponent<Health>();
            kit = GetComponent<RoleKit>();
        }

        void OnDisable() { EndCall(); }
        void OnDestroy() { EndCall(); if (goal != null) Destroy(goal.gameObject); }

        float Wait(Need n)
        {
            bool commander = kit != null && kit.Applied && kit.Role == Role.SectionCommander;
            return commander ? 40f : 75f;
        }

        bool InControl()
        {
            return soldier != null && soldier.enabled && AdditionsBootstrap.LocalPlayer() == soldier && !CinematicDirector.Playing
                && !GliderExit.Exiting && !Net.InMenu && !MainMenu.Showing && !SettingsPanel.Open && (health == null || !health.IsDead)
                && !Bootcamp.Active;   // nobody to call on the training ground
        }

        /// <summary>Keeps the runner from stopping to look for targets or to fire a burst.</summary>
        void HoldFire()
        {
            if (!active || responder == null) return;
            if (targetField != null) targetField.SetValue(responder, null);
            if (nextScanField != null) nextScanField.SetValue(responder, Time.time + 0.5f);
            if (nextBurstField != null) nextBurstField.SetValue(responder, Time.time + 0.5f);
        }

        // The AI may have found a target in its own Update this frame; drop it again before the next one.
        void LateUpdate() { HoldFire(); }

        void Update()
        {
            if (kit == null) kit = GetComponent<RoleKit>();
            if (active) Follow();
            if (!InControl()) return;
            Keyboard k = Keyboard.current;
            if (k == null) return;
            if (k.tKey.wasPressedThisFrame) Call(Need.Medic);
            if (k.bKey.wasPressedThisFrame) Call(Need.Ammo);
        }

        void Call(Need n)
        {
            if (active) { Say("Someone is already on his way"); return; }
            if (!Net.Simulates) { Say("Calling for help works when you play alone or host"); return; }
            if (Time.time < readyAt[(int)n]) { Say("Wait " + Mathf.CeilToInt(readyAt[(int)n] - Time.time) + " s before calling again"); return; }
            if (n == Need.Medic && health != null && health.Current >= health.maxHealth - 1f) { Say("You are not wounded"); return; }
            if (n == Need.Ammo && !NeedsAmmo()) { Say("Your pouches are full"); return; }

            tried.Clear();
            SoldierAI best = Pick();
            Calls++;
            if (best == null) { Say(n == Need.Medic ? "\"Stretcher-bearer!\"  No one is near enough to hear" : "\"Ammo!\"  No one is near enough to hear"); readyAt[(int)n] = Time.time + 5f; return; }

            if (goal == null)
            {
                goal = new GameObject("Squad call point").transform;
                goal.gameObject.hideFlags = HideFlags.HideInHierarchy;
            }
            goal.position = transform.position;
            active = true;
            need = n;
            startedAt = Time.time;
            Send(best);
            Say(n == Need.Medic ? "\"Stretcher-bearer!\"  A soldier is coming to dress your wounds" : "\"Ammo!\"  A soldier is bringing up magazines");
        }

        /// <summary>
        /// The British soldier best placed to come: the nearest one who can see the player (an open
        /// run), else the nearest one; soldiers who already got stuck on this call are skipped.
        /// </summary>
        SoldierAI Pick()
        {
            SoldierAI best = null;
            float bestScore = float.MaxValue;
            Vector3 me = transform.position + Vector3.up * 1.2f;
            foreach (SoldierAI ai in FindObjectsByType<SoldierAI>())
            {
                if (ai == null || !ai.enabled || tried.Contains(ai)) continue;
                Health h = ai.GetComponent<Health>();
                if (h == null || h.IsDead || h.faction != Faction.British) continue;
                float d = Vector3.Distance(ai.transform.position, transform.position);
                if (d > SearchRadius) continue;
                Vector3 eye = ai.transform.position + Vector3.up * 1.2f;
                bool open = !Physics.Linecast(eye, me, ~0, QueryTriggerInteraction.Ignore) || OnlySoldiersBetween(eye, me, ai);
                float score = d + (open ? 0f : 30f);
                if (score < bestScore) { bestScore = score; best = ai; }
            }
            return best;
        }

        /// <summary>True when the only things between the two points are the two soldiers themselves.</summary>
        bool OnlySoldiersBetween(Vector3 from, Vector3 to, SoldierAI ai)
        {
            RaycastHit[] hits = Physics.RaycastAll(from, to - from, Vector3.Distance(from, to), ~0, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits)
            {
                Transform t = hit.collider.transform;
                if (!t.IsChildOf(ai.transform) && !t.IsChildOf(transform)) return false;
            }
            return true;
        }

        void Send(SoldierAI ai)
        {
            responder = ai;
            responderHealth = ai.GetComponent<Health>();
            savedMode = ai.mode;
            savedTarget = ai.advanceTarget;
            ai.mode = SoldierAI.Mode.Advance;
            ai.advanceTarget = goal;
            tried.Add(ai);
            closest = Flat(ai.transform.position - transform.position);
            closerAt = Time.time;
        }

        /// <summary>Hands the soldier back his own orders.</summary>
        void Release()
        {
            if (responder != null)
            {
                responder.mode = savedMode;
                responder.advanceTarget = savedTarget;
                if (runningField != null) runningField.SetValue(responder, false);
                if (moveGoalField != null) moveGoalField.SetValue(responder, null);
            }
            responder = null;
            responderHealth = null;
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        bool NeedsAmmo()
        {
            if (loadout == null) return false;
            Weapon p = loadout.primary, s = loadout.sidearm;
            return (p != null && p.SpareRounds < p.MaxSpare) || (s != null && s.SpareRounds < s.MaxSpare);
        }

        void Follow()
        {
            if (responder == null || responderHealth == null || responderHealth.IsDead || !responder.enabled)
            {
                EndCall();
                readyAt[(int)need] = Time.time + 10f;
                Say("The soldier coming to you was hit");
                return;
            }
            if (health != null && health.IsDead) { EndCall(); return; }
            if (Time.time - startedAt > GiveUpAfter)
            {
                EndCall();
                readyAt[(int)need] = Time.time + 10f;
                Say("No one could get through to you");
                return;
            }

            goal.position = transform.position;
            // He runs over without stopping to fight or to take cover.
            HoldFire();
            if (coverField != null) coverField.SetValue(responder, null);
            if (runningField != null) runningField.SetValue(responder, true);
            // Advance mode picks a free spot somewhere near its target; he has to come to the player's side.
            if (moveGoalField != null) moveGoalField.SetValue(responder, (Vector3?)transform.position);
            if (nextMoveField != null) nextMoveField.SetValue(responder, Time.time + 0.5f);
            if (responder.mode != SoldierAI.Mode.Advance) responder.mode = SoldierAI.Mode.Advance;
            if (responder.advanceTarget == null) responder.advanceTarget = goal;

            float dist = Flat(responder.transform.position - transform.position);
            if (dist <= Reach) { Deliver(); return; }

            // Getting closer? The player may be walking too, so only a real gain counts.
            if (dist < closest - 1f) { closest = dist; closerAt = Time.time; }
            else if (Time.time - closerAt > StuckAfter)
            {
                // He is stuck behind something: send the next man instead.
                Release();
                SoldierAI next = tried.Count < 3 ? Pick() : null;
                if (next == null)
                {
                    active = false;
                    readyAt[(int)need] = Time.time + 10f;
                    Say("No one could get through to you");
                    return;
                }
                Switches++;
                Send(next);
            }
        }

        void Deliver()
        {
            Arrivals++;
            Need n = need;
            EndCall();
            readyAt[(int)n] = Time.time + Wait(n);
            if (n == Need.Medic)
            {
                if (health != null) health.Heal(MedicHeal);
                Say("Your wounds are dressed  (+" + Mathf.RoundToInt(MedicHeal) + " health)");
            }
            else
            {
                int p = Give(loadout != null ? loadout.primary : null, 2);
                int s = Give(loadout != null ? loadout.sidearm : null, 1);
                Say("Ammunition handed over: +" + p + " " + (loadout.primary != null ? loadout.primary.displayName : "") + " rounds, +" + s + " pistol rounds");
            }
        }

        static int Give(Weapon w, int mags)
        {
            if (w == null || w.hold == HoldStyle.Launcher) return 0;
            return w.AddSpareRounds(w.magazineSize * mags);
        }

        void EndCall()
        {
            if (!active) return;
            active = false;
            Release();
        }

        void Say(string text)
        {
            if (kit != null) kit.Say(text);
        }

        // ------------------------------------------------------------------ HUD

        static void EnsureStyles(float s)
        {
            if (white == null)
            {
                white = new Texture2D(1, 1);
                white.SetPixel(0, 0, Color.white);
                white.Apply();
            }
            if (line == null)
            {
                line = new GUIStyle(GUI.skin.label);
                line.fontStyle = FontStyle.Bold;
                line.normal.textColor = Color.white;
                line.alignment = TextAnchor.MiddleLeft;
                line.wordWrap = false;
                small = new GUIStyle(line);
                small.fontStyle = FontStyle.Normal;
                marker = new GUIStyle(line);
                marker.alignment = TextAnchor.MiddleCenter;
            }
            line.fontSize = Mathf.RoundToInt(13f * s);
            small.fontSize = Mathf.RoundToInt(12f * s);
            marker.fontSize = Mathf.RoundToInt(14f * s);
        }

        static void Shadowed(Rect r, string text, GUIStyle style, Color c)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.7f * c.a);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, style);
            GUI.color = c;
            GUI.Label(r, text, style);
        }

        string CallState(Need n)
        {
            if (active && need == n) return "coming";
            float left = readyAt[(int)n] - Time.time;
            return left > 0f ? Mathf.CeilToInt(left) + " s" : "ready";
        }

        void OnGUI()
        {
            GUI.depth = -10;
            if (Event.current.type != EventType.Repaint) return;
            if (soldier == null || AdditionsBootstrap.LocalPlayer() != soldier || kit == null || !kit.Applied) return;
            if (CinematicDirector.Playing || Net.InMenu || MainMenu.Showing) return;

            float s = Mathf.Clamp(Screen.height / 1080f, 0.85f, 1.25f);
            EnsureStyles(s);
            Color old = GUI.color;

            // Two small right-aligned lines top right, under the game's "Playing alone / Esc: menu" line,
            // so the bottom of the screen keeps only health and the weapon bar.
            string act = kit.Role == Role.StretcherBearer ? "   ·   " + KeyLabel.Of(Key.G) + " treat (" + (loadout != null ? loadout.dressings : 0) + ")"
                       : kit.Role == Role.BrenNumberTwo ? "   ·   " + KeyLabel.Of(Key.G) + " bandolier (" + kit.Bandoliers + ")" : "";
            string calls = KeyLabel.Of(Key.T) + " medic " + CallState(Need.Medic) + "   ·   " + KeyLabel.Of(Key.B) + " ammo " + CallState(Need.Ammo);
            float w = 360f * s, right = Screen.width - 20f;
            float y = Mathf.Max(44f, Screen.height * 0.062f);
            Rect r1 = new Rect(right - w, y, w, 18f * s), r2 = new Rect(right - w, y + 18f * s, w, 17f * s);
            line.alignment = TextAnchor.MiddleRight;
            small.alignment = TextAnchor.MiddleRight;
            Shadowed(r1, Roles.NameOf(kit.Role) + act, line, new Color(0.88f, 0.74f, 0.45f, 0.95f));
            Shadowed(r2, calls, small, new Color(1f, 1f, 1f, 0.75f));
            line.alignment = TextAnchor.MiddleLeft;
            small.alignment = TextAnchor.MiddleLeft;

            // Marker over the soldier who is coming.
            Camera cam = soldier.cam;
            if (active && responder != null && cam != null)
            {
                Vector3 head = responder.transform.position + Vector3.up * 2.1f;
                Vector3 sp = cam.WorldToScreenPoint(head);
                float dist = Vector3.Distance(responder.transform.position, transform.position);
                string text = (need == Need.Medic ? "DRESSINGS  " : "AMMO  ") + Mathf.RoundToInt(dist) + " m";
                Rect r;
                if (sp.z > 0f && sp.x > 0f && sp.x < Screen.width && sp.y > 0f && sp.y < Screen.height)
                    r = new Rect(sp.x - 90f * s, Screen.height - sp.y - 24f * s, 180f * s, 22f * s);
                else
                {
                    // Off screen: say where he is coming from, under the crosshair.
                    Vector3 local = transform.InverseTransformPoint(responder.transform.position);
                    string from = local.z < -Mathf.Abs(local.x) ? "behind you" : local.x < 0f ? "on your left" : "on your right";
                    text += "  " + from;
                    r = new Rect(Screen.width * 0.5f - 160f * s, Screen.height * 0.62f, 320f * s, 22f * s);
                }
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, marker);
                GUI.color = need == Need.Medic ? new Color(0.95f, 0.45f, 0.4f) : new Color(0.95f, 0.8f, 0.4f);
                GUI.Label(r, text, marker);
            }
            GUI.color = old;
        }
    }
}
