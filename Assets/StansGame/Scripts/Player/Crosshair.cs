using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// The player's aiming mark, shaped for what he carries:
    ///   rifles (Lee-Enfield, Kar98k) — a period "post" sight: pointed post from below, side bars, centre dot;
    ///   SMGs (Sten, MP 40) — an open cross that widens as the spread grows;
    ///   Bren / MG 42 — corner brackets around the cone of fire;
    ///   pistols — a ring;
    ///   PIAT — a chevron with range marks (50 / 75 / 100 yd) below it;
    ///   grenade in hand — the throw arc and a ring where it will land.
    /// The gap follows the real spread (stance, aiming, moving, firing fast), it turns red over an enemy and
    /// blue-green over a friend, shows hit and kill markers, and a ring fills while reloading.
    /// </summary>
    public class Crosshair : MonoBehaviour
    {
        SoldierController soldier;
        Loadout kit;
        CharacterController cc;
        Health self;

        static Texture2D ring, disc;
        float hitUntil, killUntil;
        float gapPx = 12f;
        Color tint = Color.white;

        void Awake()
        {
            soldier = GetComponent<SoldierController>();
            kit = GetComponent<Loadout>();
            cc = GetComponent<CharacterController>();
            self = GetComponent<Health>();
            if (!ring) ring = MakeCircle(128, 0.84f);
            if (!disc) disc = MakeCircle(32, 0f);
        }

        void OnEnable() => Weapon.HitConfirmed += OnHit;
        void OnDisable() => Weapon.HitConfirmed -= OnHit;

        void OnHit(GameObject shooter, Health victim, bool killed)
        {
            if (shooter != gameObject || (self && victim && victim.faction == self.faction)) return;
            hitUntil = Time.time + 0.18f;
            if (killed) killUntil = Time.time + 0.45f;
        }

        static Texture2D MakeCircle(int size, float inner)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            float r = size * 0.5f - 1f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f)) / r;
                    float a = Mathf.Clamp01((1f - d) * r) * (inner > 0f ? Mathf.Clamp01((d - inner) * r) : 1f);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }

        void Update()
        {
            if (!soldier || !soldier.cam) return;
            var w = soldier.CurrentWeapon;
            // what's under the mark: enemy red, friend blue-green
            Color want = Color.white;
            Ray ray = soldier.AimRay();
            if (Physics.Raycast(ray, out RaycastHit hit, w ? w.range : 60f, ~0, QueryTriggerInteraction.Ignore))
            {
                var h = hit.collider.GetComponentInParent<Health>();
                if (h && !h.IsDead && h != self) want = self && h.faction == self.faction ? new Color(0.45f, 0.95f, 0.85f) : new Color(1f, 0.28f, 0.22f);
            }
            tint = Color.Lerp(tint, want, 1f - Mathf.Exp(-14f * Time.deltaTime));

            // gap from the real spread cone
            if (w)
            {
                bool moving = cc && new Vector3(cc.velocity.x, 0f, cc.velocity.z).sqrMagnitude > 0.5f;
                float scale = (soldier.Stance ? soldier.Stance.SpreadScale : 1f) * (moving ? 1.5f : 1f);
                float spread = w.CurrentSpread(soldier.IsAiming, scale);
                float px = Mathf.Tan(spread * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(soldier.cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * Screen.height * 0.5f;
                gapPx = Mathf.Lerp(gapPx, Mathf.Clamp(px, 3f, Screen.height * 0.2f), 1f - Mathf.Exp(-18f * Time.deltaTime));
            }
        }

        void OnGUI()
        {
            if (!soldier || !soldier.enabled || !soldier.cam || Net.InMenu || CinematicDirector.Playing) return;
            if (kit && kit.Bandaging) return;
            var w = soldier.CurrentWeapon;
            Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            bool sprinting = cc && cc.velocity.magnitude > soldier.runSpeed + 0.5f;
            float alpha = sprinting ? 0.15f : soldier.Stance && soldier.Stance.Changing ? 0.35f : 0.95f;
            Color col = new Color(tint.r, tint.g, tint.b, alpha);

            if (!w)
            {
                if (kit && kit.Selected == 2 && kit.grenades.Count > 0) DrawThrowArc(col);
                DrawMarkers(c);
                return;
            }

            float g = gapPx;
            float s = Mathf.Max(1f, Screen.height / 1080f);    // thickness scale for big screens
            switch (w.hold)
            {
                case HoldStyle.Rifle:
                    // post sight: a tapered post rising from below to the aim point, side bars, a fine dot
                    Bar(c + new Vector2(-1.5f * s, g * 0.6f + 2f), 3f * s, 26f * s, col);
                    Bar(c + new Vector2(-3f * s, g * 0.6f + 14f * s), 6f * s, 18f * s, col);
                    Bar(c + new Vector2(-g - 22f * s, -1f * s), 18f * s, 2f * s, col);
                    Bar(c + new Vector2(g + 4f * s, -1f * s), 18f * s, 2f * s, col);
                    Dot(c, 3f * s, col);
                    break;
                case HoldStyle.SMG:
                    {
                        float len = 9f * s;
                        Bar(c + new Vector2(-1f * s, -g - len), 2f * s, len, col);
                        Bar(c + new Vector2(-1f * s, g), 2f * s, len, col);
                        Bar(c + new Vector2(-g - len, -1f * s), len, 2f * s, col);
                        Bar(c + new Vector2(g, -1f * s), len, 2f * s, col);
                        Dot(c, 2.5f * s, col);
                        break;
                    }
                case HoldStyle.Heavy:
                    {
                        // corner brackets around the cone of fire
                        float r = g + 6f * s, l = 10f * s, t = 2f * s;
                        foreach (var sx in new[] { -1f, 1f })
                            foreach (var sy in new[] { -1f, 1f })
                            {
                                Vector2 corner = c + new Vector2(sx * r, sy * r);
                                Bar(new Vector2(sx < 0 ? corner.x : corner.x - l, corner.y - t * 0.5f), l, t, col);
                                Bar(new Vector2(corner.x - t * 0.5f, sy < 0 ? corner.y : corner.y - l), t, l, col);
                            }
                        Dot(c, 3f * s, col);
                        break;
                    }
                case HoldStyle.Pistol:
                    {
                        float r = g + 7f * s;
                        GUI.color = new Color(0f, 0f, 0f, alpha * 0.5f);
                        GUI.DrawTexture(new Rect(c.x - r - 1, c.y - r - 1, 2 * r + 2, 2 * r + 2), ring);
                        GUI.color = col;
                        GUI.DrawTexture(new Rect(c.x - r, c.y - r, 2 * r, 2 * r), ring);
                        Dot(c, 2.5f * s, col);
                        break;
                    }
                case HoldStyle.Launcher:
                    {
                        // chevron at the aim point and a range ladder below: the bomb drops, so hold over
                        Diag(c, -1f, 10f * s, col); Diag(c, 1f, 10f * s, col);
                        Bar(c + new Vector2(-1f * s, 4f * s), 2f * s, 70f * s, col);
                        var lbl = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(11 * s) };
                        lbl.normal.textColor = col;
                        int i = 0;
                        foreach (var yd in new[] { 50, 75, 100 })
                        {
                            float y = c.y + (18f + i * 22f) * s;
                            Bar(new Vector2(c.x - 8f * s, y), 16f * s, 2f * s, col);
                            GUI.Label(new Rect(c.x + 11f * s, y - 9f * s, 60, 20), yd + " yd", lbl);
                            i++;
                        }
                        break;
                    }
            }

            // reloading: a ring fills around the mark; empty: a reminder
            if (w.IsReloading)
            {
                float r = g + 16f * s;
                GUI.color = new Color(1f, 1f, 1f, 0.25f);
                GUI.DrawTexture(new Rect(c.x - r, c.y - r, 2 * r, 2 * r), ring);
                var lbl = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(12 * s), alignment = TextAnchor.MiddleCenter };
                lbl.normal.textColor = new Color(0.95f, 0.85f, 0.5f);
                GUI.Label(new Rect(c.x - 60, c.y + r + 4, 120, 20), "reloading", lbl);
            }
            else if (w.Ammo == 0)
            {
                var lbl = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(13 * s), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                lbl.normal.textColor = new Color(1f, 0.55f, 0.3f, 0.9f);
                GUI.Label(new Rect(c.x - 80, c.y + g + 26f * s, 160, 22), w.SpareRounds > 0 ? "R  reload" : "out of ammunition", lbl);
            }
            DrawMarkers(c);
            GUI.color = Color.white;
        }

        void DrawMarkers(Vector2 c)
        {
            float s = Mathf.Max(1f, Screen.height / 1080f);
            if (Time.time < killUntil)
            {
                float k = (killUntil - Time.time) / 0.45f;
                var col = new Color(1f, 0.2f, 0.15f, k);
                float r = (14f + (1f - k) * 6f) * s;
                foreach (var d in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) }) Tick(c + d * r, d, 11f * s, 3f * s, col);
            }
            else if (Time.time < hitUntil)
            {
                var col = new Color(1f, 1f, 1f, (hitUntil - Time.time) / 0.18f);
                foreach (var d in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) }) Tick(c + d * 10f * s, d, 8f * s, 2f * s, col);
            }
        }

        // the grenade: dotted throw arc and the ring where it lands (same throw as Loadout.Throw)
        void DrawThrowArc(Color col)
        {
            Ray aim = soldier.AimRay();
            Vector3 p = transform.position + Vector3.up * 1.7f + transform.right * 0.25f + aim.direction * 0.4f;
            Vector3 v = aim.direction * kit.throwSpeed + Vector3.up * 3.5f;
            var cam = soldier.cam;
            float dt = 0.05f;
            for (int i = 0; i < 80; i++)
            {
                Vector3 next = p + v * dt; v += Physics.gravity * dt;
                if (Physics.Linecast(p, next, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(transform))
                {
                    Vector3 sp = cam.WorldToScreenPoint(hit.point);
                    if (sp.z > 0f)
                    {
                        float r = Mathf.Clamp(500f / sp.z, 8f, 60f);
                        GUI.color = new Color(1f, 0.75f, 0.3f, col.a);
                        GUI.DrawTexture(new Rect(sp.x - r, Screen.height - sp.y - r * 0.5f, 2 * r, r), ring);
                    }
                    break;
                }
                if (i % 2 == 0)
                {
                    Vector3 sp = cam.WorldToScreenPoint(next);
                    if (sp.z > 0.5f) { GUI.color = new Color(1f, 1f, 1f, col.a * 0.7f); GUI.DrawTexture(new Rect(sp.x - 2, Screen.height - sp.y - 2, 4, 4), disc); }
                }
                p = next;
            }
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------ drawing helpers (dark outline for night and snow alike)

        static void Bar(Vector2 pos, float w, float h, Color col)
        {
            GUI.color = new Color(0f, 0f, 0f, col.a * 0.55f);
            GUI.DrawTexture(new Rect(pos.x - 1, pos.y - 1, w + 2, h + 2), Texture2D.whiteTexture);
            GUI.color = col;
            GUI.DrawTexture(new Rect(pos.x, pos.y, w, h), Texture2D.whiteTexture);
        }

        static void Dot(Vector2 c, float r, Color col)
        {
            GUI.color = new Color(0f, 0f, 0f, col.a * 0.6f);
            GUI.DrawTexture(new Rect(c.x - r - 1, c.y - r - 1, 2 * r + 2, 2 * r + 2), disc);
            GUI.color = col;
            GUI.DrawTexture(new Rect(c.x - r, c.y - r, 2 * r, 2 * r), disc);
        }

        static void Diag(Vector2 c, float side, float len, Color col)
        {
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(side * 45f, c);
            Bar(new Vector2(c.x - 1f, c.y), 2f, len, col);
            GUI.matrix = m;
        }

        static void Tick(Vector2 at, Vector2 dir, float len, float thick, Color col)
        {
            var m = GUI.matrix;
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            GUIUtility.RotateAroundPivot(ang, at);
            Bar(new Vector2(at.x - thick * 0.5f, at.y), thick, len, col);
            GUI.matrix = m;
        }
    }
}
