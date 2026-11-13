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
            Ui.ApplyGameFont();
            float s = Ui.HudScale;

            // Two small right-aligned lines top right, under the session line ("Playing alone", PauseMenu),
            // so the bottom of the screen keeps only health and the kit bar.
            float right = Screen.width - Ui.HudMargin;
            float y = PauseMenu.StatusBottom + 2f * s;
            float h = 20f * s;
            string act = kit.Role == Role.StretcherBearer ? "treat (" + (loadout != null ? loadout.dressings : 0) + ")"
                       : kit.Role == Role.BrenNumberTwo ? "bandolier (" + kit.Bandoliers + ")" : null;
            float rx = right;
            if (act != null) rx -= Ui.Hints(rx, y, h, new string[] { KeyLabel.Of(Key.G), act }, 0.9f, TextAnchor.MiddleRight) + 14f * s;
            Ui.Text(new Rect(rx - 400f * s, y, 400f * s, h), Roles.NameOf(kit.Role).ToUpperInvariant(), UiFont.Body, 15f * s, Ui.A(Ui.Brass, 0.95f), TextAnchor.MiddleRight, true, false, true);
            y += h + 5f * s;
            Ui.Hints(right, y, h, new string[]
            {
                KeyLabel.Of(Key.T), "medic " + CallState(Need.Medic), KeyLabel.Of(Key.B), "ammo " + CallState(Need.Ammo),
            }, 0.8f, TextAnchor.MiddleRight);

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
                    r = new Rect(sp.x - 110f * s, Screen.height - sp.y - 24f * s, 220f * s, 22f * s);
                else
                {
                    // Off screen: say where he is coming from, under the crosshair.
                    Vector3 local = transform.InverseTransformPoint(responder.transform.position);
                    string from = local.z < -Mathf.Abs(local.x) ? "BEHIND YOU" : local.x < 0f ? "ON YOUR LEFT" : "ON YOUR RIGHT";
                    text += "   " + from;
                    r = new Rect(Screen.width * 0.5f - 200f * s, Screen.height * 0.62f, 400f * s, 22f * s);
                }
                Ui.Text(r, text, UiFont.Body, 15f * s, need == Need.Medic ? new Color(0.95f, 0.50f, 0.42f) : Ui.Amber, TextAnchor.MiddleCenter, true, false, true);
            }
        }
    }
}
