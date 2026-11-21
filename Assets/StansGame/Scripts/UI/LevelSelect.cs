using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace StansGame
{
    /// <summary>
    /// The mission chooser, after Play alone / Host: the missions grouped by sector on the left - the British sector
    /// (6th Airborne Division, east of the Orne) and the American sector (82nd and 101st Airborne, the Cotentin) - with a
    /// gold bar on the selected one and COMPLETED / best time from MissionProgress; the briefing and objectives on the
    /// right, key hints at the bottom. Up/Down select, Enter confirm, Esc back; the mouse works too.
    /// New chapters go in Missions (and in DemoBuilder).
    /// </summary>
    public static class LevelSelect
    {
        public class Objective { public string title, text; public Objective(string t, string d) { title = t; text = d; } }

        public class Mission
        {
            public string sector;                     // the heading it is listed under
            public string title, place, when, scene, briefing;
            public bool multiplayer, locked;
            /// <summary>The history film of this level ("The History", HistoryFilm.cs), if it has one: its button's label.</summary>
            public string history;
            public Objective[] objectives;
        }

        public const string British = "BRITISH SECTOR  ·  6th Airborne Division";
        public const string American = "AMERICAN SECTOR  ·  82nd and 101st Airborne";

        public static readonly Mission[] Missions =
        {
            // ---------------------------------------------------------------- the British sector
            new Mission
            {
                sector = British,
                title = "Pegasus Bridge", place = "Bénouville and Ranville, Normandy", when = "6 June 1944, 00:16", scene = "PegasusBridge", multiplayer = true,
                history = "THE HISTORY: PEGASUS BRIDGE, 6 JUNE 1944",
                briefing = "Operation Deadstick. D Company, 2nd Ox & Bucks Light Infantry, lands by glider right next to the canal bridge, " +
                           "while two more platoons go for the Orne bridge at Ranville. Take both intact, signal the success and hold them " +
                           "until the commandos come up from Sword Beach.",
                objectives = new[]
                {
                    new Objective("Coup de main", "Get out of the glider. Knock out the pillbox and clear the trenches on the east bank."),
                    new Objective("Over the bridge", "Cross the bridge and take the west bank by the Café Gondrée. Lt Fox's and Lt Sweeney's platoons go for the Orne bridge."),
                    new Objective("Ham and Jam", "Both bridges taken: hold H to transmit \"Ham and Jam\" on the wireless until someone answers."),
                    new Objective("Hold the bridge", "Germans probing from Bénouville, Le Port and along the canal. Hold until 7th Parachute Battalion arrives."),
                    new Objective("Armour on the road!", "German armour coming from Bénouville. Stop the lead vehicle with the PIAT (3)."),
                    new Objective("Hold until relieved", "Snipers in the village and the château grounds. Hold on until Lord Lovat's commandos arrive, piper first."),
                },
            },
            new Mission
            {
                sector = British,
                title = "Château de Bénouville", place = "Bénouville, Normandy", when = "6 June 1944, 01:40", scene = "ChateauBenouville", multiplayer = true,
                briefing = "A Company, 7th Parachute Battalion (Major Nigel Taylor). Scattered in the drop, half the battalion has found its way " +
                           "to the bridges. Cross Pegasus Bridge, take the south of Bénouville and the grounds of the château, a maternity home " +
                           "run by Madame Léa Vion of the Resistance, and hold them against 21st Panzer Division until relieved.",
                objectives = new[]
                {
                    new Objective("Over the bridge", "Cross Pegasus Bridge past Major Howard's men and go south down the Caen road into Bénouville."),
                    new Objective("The south of the village", "Clear the Germans out of the houses at the south end of the village."),
                    new Objective("The château grounds", "Clear the park of the château. It flies the Red Cross: there are mothers and babies inside."),
                    new Objective("Hold Bénouville", "Counter-attacks all day from the west and the south. Taylor is wounded and fights on from a stretcher."),
                    new Objective("Armour!", "A Panzer IV and a self-propelled gun on the Caen road. Get close and stop them with Gammon bombs."),
                    new Objective("Hold until relieved", "Surrounded. Lt MacDonald's platoon gets through at 19:00; hold until the Royal Warwicks arrive."),
                },
            },
            new Mission
            {
                sector = British,
                title = "Ranville", place = "Ranville, Normandy", when = "6 June 1944", scene = "", locked = true,
                briefing = "East of the Orne bridge: the village where the 6th Airborne Division gathers after the drop.",
                objectives = new Objective[0],
            },

            // ---------------------------------------------------------------- the American sector
            new Mission
            {
                sector = American,
                title = "Sainte-Mère-Église", place = "Cotentin, Normandy", when = "6 June 1944, 01:50", scene = "SainteMereEglise", multiplayer = true,
                briefing = "Mission Boston. A stick of F Company, 2/505th Parachute Infantry, is dropped by mistake over the town while a house " +
                           "burns on the square and the Germans are awake. Survive the jump, join 3rd Battalion and take the first town in France.",
                objectives = new[]
                {
                    new Objective("In the door", "Stand in the door of the C-47. When the light turns green: SPACE to jump."),
                    new Objective("Coming down", "Steer the canopy: A/D turn, W run with the wind, S slip. Keep out of the square and away from the fire."),
                    new Objective("Out of the square", "Get clear of the square and make for the orchard west of the town, where 3rd Battalion is gathering."),
                    new Objective("Into the town", "Go in with Lt. Col. Krause's 3rd Battalion and clear the Germans out of the church square."),
                    new Objective("The first town in France", "Hoist the Stars and Stripes on the town hall."),
                    new Objective("Hold the town", "Counter-attacks up the Carentan road and down from Neuville, with armour. Hold the square until 06:00."),
                },
            },
            new Mission
            {
                sector = American,
                title = "La Fière", place = "Merderet river, Normandy", when = "6 June 1944", scene = "", locked = true,
                briefing = "The bridge and causeway over the flooded Merderet, west of Sainte-Mère-Église, held by the 82nd Airborne against German armour.",
                objectives = new Objective[0],
            },
            new Mission
            {
                sector = American,
                title = "Brécourt Manor", place = "Le Grand-Chemin, Normandy", when = "6 June 1944, morning", scene = "", locked = true,
                briefing = "Easy Company, 506th Parachute Infantry (101st Airborne), goes for a battery of German guns firing on Utah Beach.",
                objectives = new Objective[0],
            },
        };

        static int selected;
        static Texture2D white;
        static readonly Color Gold = new Color(0.80f, 0.59f, 0.29f), DimGold = new Color(0.55f, 0.42f, 0.24f),
                              Green = new Color(0.40f, 0.75f, 0.37f), Grey = new Color(0.62f, 0.62f, 0.62f), DarkGrey = new Color(0.45f, 0.45f, 0.45f);

        /// <summary>Can this mission be started in the current mode (alone, or hosting)?</summary>
        public static bool Playable(Mission m, bool alone)
        {
            if (m.locked || string.IsNullOrEmpty(m.scene)) return false;
            bool inBuild = m.scene == SceneManager.GetActiveScene().name || Application.CanStreamedLevelBeLoaded(m.scene);
            return inBuild && (alone || m.multiplayer);
        }

        /// <summary>Can its history film be watched from here? (The films live in their own level's scene.)</summary>
        public static bool HasHistory(Mission m) => !string.IsNullOrEmpty(m.history) && HistoryFilm.Instance && m.scene == SceneManager.GetActiveScene().name;

        static void Fill(Rect r, Color c)
        {
            if (!white) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
            var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = old;
        }

        static GUIStyle Style(float size, Color c, bool bold = false, TextAnchor anchor = TextAnchor.UpperLeft, bool wrap = false)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(8, Mathf.RoundToInt(size)), fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                alignment = anchor, wordWrap = wrap, richText = true, normal = { textColor = c }, clipping = TextClipping.Overflow,
            };
        }

        // ------------------------------------------------------------------ the sector screens (concept A, 21 Nov 2026)
        // After Play alone / Host: 1) choose your sector on the map of the Normandy coast; 2) the sector: PLAY (its
        // missions, the briefing of the one selected) on the left, LEARN (the documentary, and a slideshow of the
        // wartime photographs with a caption under each) on the right. Pictures: Resources/Menu, Resources/History/<side>.

        static int sector = -1, sectorSel, photo;
        static bool photoFull;
        static readonly string[] SectorKey = { "British", "American" };
        static readonly Color Blue = new Color(0.50f, 0.65f, 0.85f), Ink = new Color(0.95f, 0.93f, 0.90f), Sub = new Color(0.71f, 0.67f, 0.61f),
                              Bg = new Color(0.043f, 0.043f, 0.039f), Card = new Color(0.07f, 0.065f, 0.055f), Line = new Color(0.23f, 0.21f, 0.17f);
        class Photo { public string file, caption, credit; public Texture2D tex; }
        static readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Photo>> photos = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Photo>>();
        static Texture2D mapTex, glowBritish, glowAmerican, franceTex;
        static float s, ox;   // the 1280 x 720 layout, scaled to the screen height and centred

        static Rect R(float x, float y, float w, float h) => new Rect(ox + x * s, y * s, w * s, h * s);
        static string Heading(int i) => i == 0 ? British : American;

        static System.Collections.Generic.List<Photo> Photos(int side)
        {
            if (photos.TryGetValue(side, out var list)) return list;
            list = new System.Collections.Generic.List<Photo>();
            var txt = Resources.Load<TextAsset>($"History/{SectorKey[side]}/captions");
            if (txt)
                foreach (var raw in txt.text.Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    var p = line.Split('|');
                    var ph = new Photo { file = p[0].Trim(), caption = p.Length > 1 ? p[1].Trim() : "", credit = p.Length > 2 ? p[2].Trim() : "" };
                    ph.tex = Resources.Load<Texture2D>($"History/{SectorKey[side]}/{ph.file}");
                    if (ph.tex) list.Add(ph);
                }
            photos[side] = list;
            return list;
        }

        static System.Collections.Generic.List<int> SectorMissions(int side)
        {
            var l = new System.Collections.Generic.List<int>();
            for (int i = 0; i < Missions.Length; i++) if (Missions[i].sector == Heading(side)) l.Add(i);
            return l;
        }

        static void EnterSector(int side)
        {
            sector = side; photo = 0; photoFull = false;
            var l = SectorMissions(side);
            if (l.Count > 0 && !l.Contains(selected)) selected = l[0];
        }

        static Mission SectorFilm(int side)
        {
            foreach (int i in SectorMissions(side)) if (!string.IsNullOrEmpty(Missions[i].history)) return Missions[i];
            return null;
        }

        /// <summary>Keyboard (call from Update while the screen is up).</summary>
        public static void Keys(bool alone, Action<string> start, Action back, Action<Mission> history = null)
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            bool up = kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame, down = kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame;
            bool left = kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame, right = kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame;
            bool enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame, esc = kb.escapeKey.wasPressedThisFrame;
            if (sector < 0)
            {
                if (up || down || left || right) sectorSel = 1 - sectorSel;
                if (enter) EnterSector(sectorSel);
                if (esc) back();
                return;
            }
            var ph = Photos(sector);
            if (left && ph.Count > 0) photo = (photo + ph.Count - 1) % ph.Count;
            if (right && ph.Count > 0) photo = (photo + 1) % ph.Count;
            if (photoFull)
            {
                if (esc || enter || kb.spaceKey.wasPressedThisFrame || kb.fKey.wasPressedThisFrame) photoFull = false;
                return;
            }
            var ms = SectorMissions(sector);
            int at = Mathf.Max(0, ms.IndexOf(selected));
            if (ms.Count > 0 && up) selected = ms[(at + ms.Count - 1) % ms.Count];
            if (ms.Count > 0 && down) selected = ms[(at + 1) % ms.Count];
            if (enter && Playable(Missions[selected], alone)) start(Missions[selected].scene);
            var film = SectorFilm(sector);
            if (kb.hKey.wasPressedThisFrame && history != null && film != null && HasHistory(film)) history(film);
            if ((kb.fKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) && ph.Count > 0) photoFull = true;
            if (esc) sector = -1;
        }

        /// <summary>Draws the whole screen (call from OnGUI).</summary>
        public static void Draw(string mode, bool alone, bool busy, Action<string> start, Action back, Action<Mission> history = null)
        {
            s = Screen.height / 720f; ox = Mathf.Max(0f, (Screen.width - 1280f * s) * 0.5f);
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(Bg.r, Bg.g, Bg.b, 0.96f));
            if (sector < 0) DrawSectors(mode, back);
            else if (photoFull) DrawPhotoFull();
            else DrawSector(mode, alone, busy, start, history);
        }

        static void DrawSectors(string mode, Action back)
        {
            if (!mapTex) { mapTex = Resources.Load<Texture2D>("Menu/NormandyMap"); glowBritish = Resources.Load<Texture2D>("Menu/SectorBritish"); glowAmerican = Resources.Load<Texture2D>("Menu/SectorAmerican"); franceTex = Resources.Load<Texture2D>("Menu/FranceLocator"); }
            // left: the title and the two sectors
            GUI.Label(R(36, 44, 320, 18), "STAN'S GAME", Style(12 * s, Gold, true));
            GUI.Label(R(36, 66, 340, 120), "CHOOSE YOUR\nSECTOR", Style(46 * s, Ink, true));
            GUI.Label(R(36, 186, 340, 20), "Normandy  ·  the night of 5–6 June 1944", Style(14 * s, Sub));
            Fill(R(36, 216, 64, 2), Gold);
            SectorCard(0, R(36, 240, 308, 104), Gold, "BRITISH SECTOR", "6th Airborne Division", "Pegasus Bridge  ·  Bénouville  ·  Ranville");
            SectorCard(1, R(36, 360, 308, 104), Blue, "AMERICAN SECTOR", "82nd and 101st Airborne", "Sainte-Mère-Église  ·  La Fière");
            GUI.Label(R(36, 672, 360, 20), "← →  choose     Enter  confirm     Esc  back", Style(12 * s, DarkGrey));
            // right: the map, the sectors glowing on it, the places
            GUI.Label(R(400, 46, 700, 20), "CLICK A SECTOR ON THE MAP, OR CHOOSE ON THE LEFT", Style(12 * s, DarkGrey, true));
            var map = R(400, 80, 860, 560);
            if (mapTex) GUI.DrawTexture(map, mapTex);
            Vector2 mouse = Event.current.mousePosition;
            bool overBr = InEllipse(mouse, 621.4f, 339.3f, 110f, 80f), overAm = InEllipse(mouse, 340.4f, 270.7f, 150f, 95f);
            var old = GUI.color;
            if (glowAmerican) { GUI.color = new Color(Blue.r, Blue.g, Blue.b, sectorSel == 1 || overAm ? 1f : 0.45f); GUI.DrawTexture(map, glowAmerican); }
            if (glowBritish) { GUI.color = new Color(Gold.r, Gold.g, Gold.b, sectorSel == 0 || overBr ? 1f : 0.45f); GUI.DrawTexture(map, glowBritish); }
            GUI.color = old;
            Place(637.8f, 348.8f, "Pegasus Bridge", Gold, Ink, 13, true);
            Place(662.9f, 332.3f, "Merville", Gold, Sub, 12, false, 8, -16);
            Place(261.6f, 255.2f, "Sainte-Mère-Église", Blue, Ink, 13, true, -150, -22);
            Place(605.6f, 381.8f, "Caen", Sub, Sub, 12, false, 8, 2);
            Place(483.8f, 326.8f, "Bayeux", Sub, Sub, 12, false, 8, 2);
            Place(154.1f, 134.2f, "Cherbourg", Sub, Sub, 12, false);
            foreach (var (x, y, n) in new[] { (311.8f, 238.7f, "UTAH"), (419.2f, 266.2f, "OMAHA"), (519.6f, 282.8f, "GOLD"), (573.3f, 288.3f, "JUNO"), (619.9f, 310.3f, "SWORD") })
                GUI.Label(R(400 + x - 40, 80 + y - 24, 80, 16), n, Style(11 * s, new Color(0.56f, 0.64f, 0.70f), true, TextAnchor.MiddleCenter));
            GUI.Label(R(440, 120, 300, 20), "BAIE DE SEINE", Style(13 * s, new Color(0.31f, 0.40f, 0.47f), true));
            // where Normandy is in France
            Fill(R(1094, 96, 166, 176), Bg); Fill(R(1094, 96, 166, 1), Line);
            if (franceTex) GUI.DrawTexture(R(1102, 104, 150, 160), franceTex, ScaleMode.ScaleToFit);
            // clicks on the map
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                if (overBr) { sectorSel = 0; EnterSector(0); Event.current.Use(); }
                else if (overAm) { sectorSel = 1; EnterSector(1); Event.current.Use(); }
            }
        }

        static bool InEllipse(Vector2 m, float cx, float cy, float rx, float ry)
        {
            float x = (m.x - ox) / s - 400f - cx, y = m.y / s - 80f - cy;
            return x * x / (rx * rx) + y * y / (ry * ry) <= 1f;
        }

        static void Place(float x, float y, string name, Color dot, Color text, float size, bool big, float dx = 10, float dy = -8)
        {
            float r = big ? 6f : 3f;
            Fill(R(400 + x - r, 80 + y - r, r * 2, r * 2), dot);
            GUI.Label(R(400 + x + dx, 80 + y + dy, 200, 18), name, Style(size * s, text, big));
        }

        static void SectorCard(int side, Rect r, Color accent, string tag, string head, string places)
        {
            bool sel = sectorSel == side;
            Fill(r, sel ? new Color(0.10f, 0.094f, 0.075f) : Card);
            Fill(new Rect(r.x, r.y, 3 * s, r.height), sel ? accent : Line);
            if (sel) { Fill(new Rect(r.x, r.y, r.width, 2 * s), accent); Fill(new Rect(r.x, r.yMax - 2 * s, r.width, 2 * s), accent); Fill(new Rect(r.xMax - 2 * s, r.y, 2 * s, r.height), accent); }
            GUI.Label(new Rect(r.x + 18 * s, r.y + 14 * s, r.width, 16 * s), tag, Style(11 * s, accent, true));
            GUI.Label(new Rect(r.x + 18 * s, r.y + 32 * s, r.width, 28 * s), head, Style(21 * s, Ink, true));
            GUI.Label(new Rect(r.x + 18 * s, r.y + 62 * s, r.width - 24 * s, 18 * s), places, Style(12.5f * s, Sub));
            int playable = 0; foreach (int i in SectorMissions(side)) if (!Missions[i].locked) playable++;
            GUI.Label(new Rect(r.x + 18 * s, r.y + 80 * s, r.width, 16 * s), $"{playable} mission{(playable == 1 ? "" : "s")} to play", Style(11 * s, DarkGrey));
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { if (sel) EnterSector(side); sectorSel = side; }
        }

        static void DrawSector(string mode, bool alone, bool busy, Action<string> start, Action<Mission> history)
        {
            Color accent = sector == 0 ? Gold : Blue;
            // header
            if (GUI.Button(R(40, 38, 96, 22), "← Sectors", Style(13 * s, Gold))) { sector = -1; return; }
            GUI.Label(R(140, 28, 400, 44), sector == 0 ? "BRITISH SECTOR" : "AMERICAN SECTOR", Style(34 * s, Ink, true));
            GUI.Label(R(140 + (sector == 0 ? 300 : 340), 42, 500, 20), sector == 0 ? "6th Airborne Division  ·  east of the River Orne" : "82nd and 101st Airborne  ·  behind Utah Beach", Style(14 * s, Sub));

            // ---- left: PLAY
            GUI.Label(R(40, 92, 560, 16), "PLAY  ·  THE MISSIONS", Style(12 * s, accent, true));
            float y = 116;
            var ms = SectorMissions(sector);
            for (int k = 0; k < ms.Count; k++)
            {
                var m = Missions[ms[k]];
                bool sel = ms[k] == selected, ok = Playable(m, alone);
                var row = R(40, y, 570, 70);
                if (sel) { Fill(row, new Color(0.09f, 0.08f, 0.06f)); Fill(new Rect(row.x, row.y, 3 * s, row.height), accent); }
                if (GUI.Button(row, GUIContent.none, GUIStyle.none)) { if (sel && ok && !busy) start(m.scene); selected = ms[k]; }
                GUI.Label(R(58, y + 14, 50, 40), $"{k + 1:00}", Style(28 * s, m.locked ? DimGold * 0.8f : sel ? accent : DimGold, true));
                GUI.Label(R(110, y + 8, 490, 24), m.title.ToUpperInvariant(), Style(19 * s, m.locked ? DarkGrey : sel ? Color.white : Ink, true));
                string sub = string.IsNullOrEmpty(m.when) ? m.place : $"{m.place}   ·   {m.when}";
                GUI.Label(R(110, y + 31, 490, 16), sub, Style(11.5f * s, Grey));
                string status; Color stC;
                if (m.locked) { status = "LOCKED   ·   IN DEVELOPMENT"; stC = DarkGrey; }
                else if (!ok) { status = alone ? "NOT IN THIS COPY YET" : "SINGLE PLAYER ONLY FOR NOW"; stC = DarkGrey; }
                else if (MissionProgress.Completed(m.scene)) { float best = MissionProgress.Best(m.scene); status = best > 0f ? $"COMPLETED   ·   BEST {MissionProgress.Format(best)}" : "COMPLETED"; stC = Green; }
                else { status = m.multiplayer ? "NOT PLAYED   ·   CO-OP" : "NOT PLAYED   ·   SINGLE PLAYER"; stC = Grey; }
                GUI.Label(R(110, y + 48, 490, 16), status, Style(11 * s, stC));
                y += 76;
            }
            // the briefing of the mission selected, and the button
            var cur = Missions[selected];
            var bst = Style(12.5f * s, new Color(0.85f, 0.83f, 0.79f), false, TextAnchor.UpperLeft, true);
            float bh = Mathf.Min(bst.CalcHeight(new GUIContent(cur.briefing), 560 * s), (628 - y - 14) * s);
            GUI.BeginClip(new Rect(ox + 40 * s, (y + 12) * s, 560 * s, bh));
            GUI.Label(new Rect(0, 0, 560 * s, 2000), cur.briefing, bst);
            GUI.EndClip();
            bool can = Playable(cur, alone) && !busy;
            var play = R(40, 640, 300, 48);
            Fill(play, can ? accent : Line);
            GUI.Label(play, can ? $"PLAY {cur.title.ToUpperInvariant()}" : cur.locked ? "IN DEVELOPMENT" : "NOT AVAILABLE", Style(15 * s, can ? new Color(0.08f, 0.07f, 0.04f) : DarkGrey, true, TextAnchor.MiddleCenter));
            if (can && GUI.Button(play, GUIContent.none, GUIStyle.none)) start(cur.scene);

            // ---- right: LEARN
            GUI.Label(R(650, 92, 590, 16), "LEARN  ·  THE HISTORY", Style(12 * s, accent, true));
            var ph = Photos(sector);
            // 1) the documentary
            var film = SectorFilm(sector);
            bool canFilm = film != null && history != null && HasHistory(film) && !busy;
            var doc = R(650, 116, 590, 124);
            Fill(doc, Card); Fill(new Rect(doc.x, doc.yMax - 1, doc.width, 1), Line);
            var thumb = R(650, 116, 220, 124);
            Fill(thumb, Line);
            if (ph.Count > 0) GUI.DrawTexture(thumb, ph[0].tex, ScaleMode.ScaleAndCrop);
            Fill(R(738, 156, 44, 44), new Color(0f, 0f, 0f, canFilm ? 0.65f : 0.4f));
            GUI.Label(R(738, 156, 44, 44), "►", Style(22 * s, canFilm ? accent : DarkGrey, true, TextAnchor.MiddleCenter));
            GUI.Label(R(888, 130, 340, 16), "1  ·  THE DOCUMENTARY", Style(11 * s, accent, true));
            GUI.Label(R(888, 150, 340, 54), film != null ? FilmTitle(film) : "Coming soon", Style(19 * s, film != null ? Ink : DarkGrey, true, TextAnchor.UpperLeft, true));
            GUI.Label(R(888, 206, 340, 18), film != null ? "Narrated, with animated maps  ·  H to play" : "The story of this sector is being made", Style(11.5f * s, Grey));
            if (canFilm && GUI.Button(doc, GUIContent.none, GUIStyle.none)) history(film);
            // 2) the photographs
            var card = R(650, 256, 590, 432);
            Fill(card, Card);
            GUI.Label(R(664, 268, 300, 16), "2  ·  THE PHOTOGRAPHS", Style(11 * s, accent, true));
            if (ph.Count == 0) { GUI.Label(R(664, 300, 560, 20), "No photographs yet.", Style(13 * s, DarkGrey)); return; }
            photo = Mathf.Clamp(photo, 0, ph.Count - 1);
            var p = ph[photo];
            GUI.Label(R(1000, 268, 226, 16), $"{photo + 1} / {ph.Count}", Style(12 * s, Grey, false, TextAnchor.UpperRight));
            var pic = R(650, 292, 590, 272);
            Fill(pic, Color.black);
            GUI.DrawTexture(pic, p.tex, ScaleMode.ScaleToFit);
            if (GUI.Button(pic, GUIContent.none, GUIStyle.none)) photoFull = true;
            GUI.Label(R(664, 574, 562, 60), p.caption, Style(14 * s, new Color(0.89f, 0.87f, 0.82f), false, TextAnchor.UpperLeft, true));
            GUI.Label(R(664, 652, 330, 16), $"{p.credit}   ·   F  full screen", Style(11 * s, DarkGrey));
            var prev = R(1130, 644, 44, 32); var next = R(1182, 644, 44, 32);
            Fill(prev, Line); Fill(next, accent);
            GUI.Label(prev, "‹", Style(20 * s, Ink, true, TextAnchor.MiddleCenter)); GUI.Label(next, "›", Style(20 * s, new Color(0.08f, 0.07f, 0.04f), true, TextAnchor.MiddleCenter));
            if (GUI.Button(prev, GUIContent.none, GUIStyle.none)) photo = (photo + ph.Count - 1) % ph.Count;
            if (GUI.Button(next, GUIContent.none, GUIStyle.none)) photo = (photo + 1) % ph.Count;
            GUI.Label(R(40, 700, 900, 16), "↑ ↓  mission     Enter  play     ← →  photographs     H  the documentary     Esc  sectors", Style(11 * s, DarkGrey));
            GUI.Label(R(880, 700, 360, 16), mode, Style(11 * s, DarkGrey, false, TextAnchor.UpperRight));
        }

        static string FilmTitle(Mission m)
        {
            // "THE HISTORY: PEGASUS BRIDGE, 6 JUNE 1944" -> "Pegasus Bridge, 6 June 1944"
            return m.title + (string.IsNullOrEmpty(m.when) ? "" : ", " + m.when.Split(',')[0]);
        }

        static void DrawPhotoFull()
        {
            var ph = Photos(sector);
            if (ph.Count == 0) { photoFull = false; return; }
            var p = ph[Mathf.Clamp(photo, 0, ph.Count - 1)];
            Fill(new Rect(0, 0, Screen.width, Screen.height), Color.black);
            float capH = 96 * s;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height - capH), p.tex, ScaleMode.ScaleToFit);
            GUI.Label(new Rect(ox + 60 * s, Screen.height - capH + 16 * s, 1160 * s, 48 * s), p.caption, Style(17 * s, Ink, false, TextAnchor.UpperLeft, true));
            GUI.Label(new Rect(ox + 60 * s, Screen.height - 28 * s, 1160 * s, 18 * s), $"{p.credit}     {photo + 1} / {ph.Count}     ← →  next     Esc  back", Style(11.5f * s, DarkGrey));
            if (Event.current.type == EventType.MouseDown) { photoFull = false; Event.current.Use(); }
        }
    }

    /// <summary>Which missions have been finished on this PC, and the best time (PlayerPrefs).</summary>
    public static class MissionProgress
    {
        static float startedAt = -1f;

        /// <summary>A mission was chosen: the clock for its best time starts (it keeps running across checkpoint reloads).</summary>
        public static void MarkStart() => startedAt = Time.realtimeSinceStartup;

        /// <summary>Seconds since the mission was chosen (or since the level loaded, if it was started another way).</summary>
        public static float Elapsed => startedAt >= 0f ? Time.realtimeSinceStartup - startedAt : Time.timeSinceLevelLoad;

        public static bool Completed(string scene) => PlayerPrefs.GetInt($"stans.done.{scene}", 0) == 1;
        public static float Best(string scene) => PlayerPrefs.GetFloat($"stans.best.{scene}", 0f);

        /// <summary>The mission has been won: remember it and keep the fastest time (seconds of play).</summary>
        public static void Complete(string scene, float seconds)
        {
            PlayerPrefs.SetInt($"stans.done.{scene}", 1);
            float best = Best(scene);
            if (seconds > 0f && (best <= 0f || seconds < best)) PlayerPrefs.SetFloat($"stans.best.{scene}", seconds);
            PlayerPrefs.Save();
        }

        public static string Format(float seconds)
        {
            int t = Mathf.RoundToInt(seconds);
            return t >= 3600 ? $"{t / 3600}:{t / 60 % 60:00}:{t % 60:00}" : $"{t / 60}:{t % 60:00}";
        }
    }
}
