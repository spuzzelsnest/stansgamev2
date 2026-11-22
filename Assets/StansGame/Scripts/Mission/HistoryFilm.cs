using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace StansGame
{
    /// <summary>
    /// "The History": a short documentary of 5/6 June 1944 at the bridges, played from the main menu inside the
    /// Pegasus Bridge level. It opens on the 1944 aerial photographs and Major Howard, then moves to "action footage"
    /// made with the game itself: the gliders in tow over the Channel (CinematicDirector's flyby), the sentries walking
    /// the bridge, the gliders landing and the fight played by the game's own AI (Net.ForceSimulate lets the world run
    /// under the menu), and two animated 2D maps of the troop movements. Ends with the men who were killed.
    ///
    /// Written for Stan. Facts from Neil Barber, "The Pegasus and Orne Bridges" (read for reference, retold in our own
    /// words), the 7th Parachute Battalion war diary and the CWGC. Where the sources disagree we say so.
    /// </summary>
    public class HistoryFilm : MonoBehaviour
    {
        public static HistoryFilm Instance { get; private set; }
        public static bool Playing { get; private set; }

        [Tooltip("The 1944 aerial photographs, by file name (History/PB).")]
        public Texture2D[] photos;

        [Tooltip("The narrator, one clip a shot, named by shot number: 01, 02 ... (History/PB/Voice). An .mp3 wins over a .wav of the same number.")]
        public AudioClip[] voice;
        AudioSource narrator;

        enum Kind { World, Photo, Flyby, Map }

        class Move
        {
            public string label; public Color colour; public Vector2[] path; public float t0, t1;
            public bool german, fadeOut;
        }

        class Shot
        {
            public Kind kind;
            public string stamp, text, photo, say;   // say: what the narrator reads, when not the caption
            public float secs = 9f;
            public Func<float, (Vector3 pos, Vector3 look)> cam;   // t 0..1 -> camera
            public Action start;
            public List<Move> moves;                              // for a map
        }

        readonly List<Shot> shots = new List<Shot>();
        int index = -1;
        float shotStart, letterbox, fade;
        bool next, stop;
        Texture2D photo;

        void Awake() { Instance = this; }

        /// <summary>The narration, one line a shot, for the voice (Editor/HistoryNarration.cs): (number, text).</summary>
        public List<(string id, string text)> Narration()
        {
            Build();
            var list = new List<(string, string)>();
            for (int i = 0; i < shots.Count; i++) list.Add(((i + 1).ToString("00"), shots[i].say ?? shots[i].text));
            return list;
        }

        AudioClip VoiceFor(int i)
        {
            if (voice == null) return null;
            string id = (i + 1).ToString("00");
            foreach (var c in voice) if (c && c.name == id) return c;
            return null;
        }

        public void Play()
        {
            if (Playing) return;
            Build();
            StartCoroutine(Run());
        }

        // ------------------------------------------------------------------ the places, found in the level

        static Vector3 Find(string part, Vector3 fallback)
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
                if (t.name.Contains(part)) return t.position;
            return fallback;
        }

        static GliderApproach Glider(int chalk)
        {
            foreach (var g in FindObjectsByType<GliderApproach>(FindObjectsInactive.Include))
                if (g.name.Contains($"chalk {chalk}")) return g;
            return null;
        }

        static (Vector3, Vector3) Dolly(Vector3 a, Vector3 b, Vector3 look, float t) => (Vector3.Lerp(a, b, Mathf.SmoothStep(0f, 1f, t)), look);
        static Vector2 V(float x, float z) => new Vector2(x, z);
        static readonly Color Blue = new Color(0.25f, 0.45f, 0.95f), Green = new Color(0.2f, 0.65f, 0.3f), Yellow = new Color(0.95f, 0.8f, 0.2f),
                              Maroon = new Color(0.65f, 0.2f, 0.55f), Red = new Color(0.85f, 0.15f, 0.12f);

        void Build()
        {
            shots.Clear();
            Vector3 bridge = Vector3.zero;
            Vector3 cafe = Find("Café Gondrée", new Vector3(-38f, 0f, -12.5f));
            Vector3 orne = Find("Horsa Bridge", new Vector3(592f, 0f, 0f));
            Vector3 lz = new Vector3(55f, 0f, -60f);
            var g91 = Glider(91); var g92 = Glider(92); var g93 = Glider(93);
            Vector3 Follow(GliderApproach g, Vector3 off) => g ? g.transform.position + off : lz + off;

            void World(string stamp, string text, float secs, Func<float, (Vector3, Vector3)> cam, Action start = null)
                => shots.Add(new Shot { kind = Kind.World, stamp = stamp, text = text, secs = secs, cam = cam, start = start });
            void Photo(string stamp, string text, float secs, string name)
                => shots.Add(new Shot { kind = Kind.Photo, stamp = stamp, text = text, secs = secs, photo = name });

            // ---- the scene: the 1944 aerial photographs, and the man for the job
            Photo("Spring 1944", "Before the invasion, RAF reconnaissance aircraft photograph every bridge in Normandy. Here: the bridge over the Caen Canal " +
                  "at Bénouville. The Germans are digging trenches and building a concrete pillbox to guard it.", 12f, "aerial photo bridge March 1944");
            Photo("June 1944", "Two bridges, a few hundred metres apart: over the canal, and over the River Orne. They carry the only road across. " +
                  "Whoever holds them can stop German tanks reaching the beaches - or let the British through.", 12f, "aerial photo bridge June 1944");
            Photo("June 1944", "Between the bridges: flat fields cut by long lines of trees. And next to the canal bridge, a small triangle of open field. " +
                  "The plan is to land gliders right there - in the dark.", 12f, "Pic_BridgesAerial_high");
            World("Major John Howard", "The job goes to Major John Howard. A Londoner, he joined the army as a private, left to become a policeman in Oxford, " +
                  "came back when war broke out and became an officer. Now 31, he commands D Company of the Ox and Bucks Light Infantry.", 13f,
                  t => Dolly(new Vector3(-80f, 40f, -140f), new Vector3(10f, 28f, -110f), bridge, t));
            World("Operation Deadstick", "For months he trains them, day and night, on bridges in England. On D-Day: about 180 men - D Company, two platoons " +
                  "of B Company and 30 Royal Engineers - in six Horsa gliders. Three for each bridge.", 12f,
                  t => Dolly(new Vector3(140f, 30f, -170f), new Vector3(80f, 20f, -90f), lz, t));

            // ---- the action
            shots.Add(new Shot
            {
                kind = Kind.Flyby, secs = 40f,
                say = "The fifth of June, 1944, four minutes to eleven at night. From RAF Tarrant Rushton in Dorset, six Halifax bombers tow the six Horsas " +
                      "into the dark. Ninety minutes across the sea. Behind them, the greatest invasion fleet the world has ever seen. Just after midnight, " +
                      "over the French coast, the gliders cast off - and the pilots fly by stopwatch and compass, down through the cloud, towards the canal.",
            });
            World("00:15", "On the bridge the German sentries walk their rounds. Most of the garrison is asleep. They hear nothing but the wind.", 13f,
                  t => Dolly(new Vector3(-34f, 2.2f, -40f), new Vector3(-30f, 3f, -25f), bridge + Vector3.up * 3f, t),
                  () => { Net.ForceSimulate = true; if (g91) g91.Begin(); });
            World("00:16", "Glider 91 - Staff Sergeants Jim Wallwork and John Ainsworth - crashes through the wire just yards from the bridge. " +
                  "The pilots are thrown out, stunned. Everyone inside is alive.", 18f,
                  t => { var p = Follow(g91, Vector3.zero); return (p + new Vector3(-60f, 12f, 35f) * (1.2f - t * 0.6f), p); },
                  () => { if (g92) g92.Begin(); if (g93) StartCoroutine(After(6f, g93.Begin)); });
            World("00:17", "Glider 92 lands behind it, then 93, which breaks its back on the edge of a pond. " +
                  "Private Fred Greenhalgh of glider 93 is killed in the landing. Nobody knows exactly how - he is usually said to have drowned in the pond.", 15f,
                  t => Dolly(new Vector3(110f, 22f, -150f), new Vector3(95f, 14f, -110f), lz, t));
            shots.Add(new Shot
            {
                kind = Kind.Map, stamp = "00:16 - 00:26", secs = 30f,
                text = "25 Platoon (Brotheridge) rushes the bridge. 24 Platoon (Wood) clears the trenches on the east bank, 14 Platoon (Smith) " +
                       "follows over to the west bank. The sappers go over the bridge looking for the charges. In ten minutes it is over.",
                moves = new List<Move>
                {
                    new Move { label = "Pillbox", german = true, fadeOut = true, path = new[] { V(30f, 9f) }, t0 = 0f, t1 = 14f },
                    new Move { label = "Trenches", german = true, fadeOut = true, path = new[] { V(32f, -14f) }, t0 = 0f, t1 = 13f },
                    new Move { label = "Sentry", german = true, fadeOut = true, path = new[] { V(-2f, 2f) }, t0 = 0f, t1 = 9f },
                    new Move { label = "MG (west bank)", german = true, fadeOut = true, path = new[] { V(-34f, 8f) }, t0 = 0f, t1 = 18f },
                    new Move { label = "25 Pl  Brotheridge", colour = Blue, path = new[] { V(40f, -37f), V(24f, -4f), V(0f, 0f), V(-26f, 1f), V(-36f, -9f) }, t0 = 2f, t1 = 12f },
                    new Move { label = "Sappers  Neilson", colour = Yellow, path = new[] { V(42f, -40f), V(22f, -6f), V(2f, -2f) }, t0 = 4f, t1 = 11f },
                    new Move { label = "Howard HQ", colour = Blue, path = new[] { V(40f, -37f), V(30f, -14f) }, t0 = 3f, t1 = 7f },
                    new Move { label = "24 Pl  Wood", colour = Green, path = new[] { V(54f, -60f), V(36f, -18f), V(34f, 12f), V(40f, 28f) }, t0 = 7f, t1 = 17f },
                    new Move { label = "14 Pl  Smith", colour = Maroon, path = new[] { V(70f, -72f), V(26f, -6f), V(0f, -1f), V(-28f, -2f), V(-34f, -32f) }, t0 = 10f, t1 = 22f },
                },
            });
            World("00:17", "Lieutenant Den Brotheridge leads 25 Platoon straight across the bridge. The Germans are taken completely by surprise. " +
                  "Grenades go into the pillbox; the trenches on both banks are cleared.", 12f,
                  t => Dolly(new Vector3(32f, 4f, 6f), new Vector3(-10f, 4f, 3f), cafe, t));
            World("00:20", "Near the Café Gondrée, Den Brotheridge is shot in the neck. He dies about an hour later at the aid post - " +
                  "one of the first Allied soldiers killed by enemy fire on D-Day.", 12f,
                  t => Dolly(cafe + new Vector3(22f, 6f, 14f), cafe + new Vector3(12f, 4f, 9f), cafe + Vector3.up * 2f, t));
            World("00:20", "Lieutenant Sandy Smith of glider 93 has badly hurt his knee in the landing, but he runs over the bridge anyway, keeping to the side. " +
                  "At the far end a German throws a stick grenade: it tears his wrist open to the bone. Smith shoots the man as he climbs over the café wall.", 13f,
                  t => Dolly(new Vector3(18f, 3f, 4f), new Vector3(-14f, 3f, -2f), cafe + Vector3.up * 1.5f, t));
            World("00:25", "By chance a German sergeant, Hickmann, drives up the road with a few young paratroopers. They see that the bridge has fallen, " +
                  "fire at it for a while, and slip away into the dark.", 11f,
                  t => Dolly(new Vector3(-200f, 6f, 30f), new Vector3(-150f, 4f, 22f), new Vector3(-175f, 1f, 9f), t),
                  () => { Show("History car: Sergeant Hickmann"); DriveBy("History car: Sergeant Hickmann"); });
            World("00:25", "Captain Jock Neilson's sappers climb all over the bridge. The demolition charges are not in place: " +
                  "the Germans have not fitted them. Where are the explosives?", 10f,
                  t => Dolly(new Vector3(12f, 3f, -14f), new Vector3(-6f, 2.5f, -12f), bridge, t));
            shots.Add(new Shot
            {
                kind = Kind.Map, stamp = "00:20 - 02:00", secs = 32f,
                text = "At the river: glider 96 (Fox) lands nearest. Sergeant Thornton's mortar hits the German machine gun with its first bomb, Fox charges, and the guards run. " +
                       "Glider 95 (Sweeney) lands further away and arrives to find it all over. " +
                       "Glider 94 was cast off over the wrong river. At 00:50 the paras drop east of the Orne; 7th Battalion hurries over both bridges " +
                       "into Bénouville. About 02:00 German armour comes down the road - Sergeant Thornton's PIAT stops the leading vehicle.",
                moves = new List<Move>
                {
                    new Move { label = "Guard (Orne bridge)", german = true, fadeOut = true, path = new[] { V(600f, 6f) }, t0 = 0f, t1 = 8f },
                    new Move { label = "96  17 Pl  Fox", colour = Green, path = new[] { V(356f, 190f), V(420f, 60f), V(560f, 4f) }, t0 = 0.5f, t1 = 8f },
                    new Move { label = "2-inch mortar  Thornton", colour = Green, path = new[] { V(470f, 40f), V(520f, 20f) }, t0 = 4f, t1 = 6.5f },
                    new Move { label = "95  23 Pl  Sweeney", colour = Maroon, path = new[] { V(272f, 730f), V(400f, 300f), V(550f, 10f) }, t0 = 2f, t1 = 12f },
                    new Move { label = "94  to the River Dives (Varaville)", colour = Yellow, path = new[] { V(690f, 640f), V(780f, 700f) }, t0 = 5f, t1 = 9f },
                    new Move { label = "7th Parachute Bn", colour = Blue, path = new[] { V(780f, -260f), V(640f, -6f), V(300f, 2f), V(0f, 0f), V(-130f, 3f), V(-260f, -200f) }, t0 = 12f, t1 = 24f },
                    new Move { label = "German armour", german = true, colour = Red, path = new[] { V(-340f, 12f), V(-260f, 12f), V(-150f, 4f) }, t0 = 21f, t1 = 27f },
                    new Move { label = "PIAT  Thornton", colour = Green, path = new[] { V(-60f, -6f), V(-120f, 0f) }, t0 = 26f, t1 = 28f },
                },
            });
            World("00:30", "Both bridges are taken. Corporal Ted Tappenden sends the success signal on the wireless: \"Ham and Jam\" - " +
                  "over and over, for a long time before anybody answers.", 11f,
                  t => Dolly(new Vector3(30f, 8f, -20f), new Vector3(24f, 5f, -12f), new Vector3(18f, 1f, -4f), t));
            World("about 01:00", "A German car and a motorcyclist race over the bridges from the east - straight into the British guns. In the car is the commander " +
                  "of the bridge garrison, Major Hans Schmidt, wounded in the leg. He tells the doctor, Captain John Vaughan, that they will all be thrown back into the sea. " +
                  "A shot of morphine calms him down.", 14f,
                  t => Dolly(new Vector3(30f, 4f, 14f), new Vector3(-24f, 4f, 10f), new Vector3(-46f, 1f, 3f), t),
                  () => { Hide("History car: Sergeant Hickmann"); Show("History car: Major Schmidt"); DriveBy("History car: Major Schmidt"); });
            World("Café Gondrée", "Georges and Thérèse Gondrée and their daughters, Georgette and Arlette, hid in their cellar. Their café is one of the first " +
                  "houses in France to be freed. Georges digs up the champagne he had buried in 1940 to keep it from the Germans.", 13f,
                  t => Dolly(cafe + new Vector3(-6f, 3f, 18f), cafe + new Vector3(-3f, 2f, 10f), cafe + Vector3.up * 2f, t));
            shots.Add(new Shot
            {
                kind = Kind.Map, stamp = "6 June, dawn to midday", secs = 30f,
                text = "In Bénouville, A Company of 7th Parachute Battalion holds the crossroads by the château. At first light German guns fire straight up the street " +
                       "and the paras fall back house by house. German infantry creep up through the château park; tanks come up the road. " +
                       "A half-track heading for the bridge is knocked out by the Mairie. A Company is cut off - but the Germans do not get through.",
                moves = new List<Move>
                {
                    new Move { label = "A Coy  Taylor", colour = Blue, path = new[] { V(-262f, -640f), V(-262f, -560f) }, t0 = 4f, t1 = 10f },
                    new Move { label = "B Coy  (Le Port)", colour = Blue, path = new[] { V(-60f, 40f), V(-120f, 90f) }, t0 = 0f, t1 = 3f },
                    new Move { label = "German guns", german = true, path = new[] { V(-262f, -1050f), V(-262f, -900f) }, t0 = 1f, t1 = 6f },
                    new Move { label = "German infantry (château park)", german = true, path = new[] { V(-170f, -930f), V(-180f, -760f), V(-220f, -640f) }, t0 = 6f, t1 = 15f },
                    new Move { label = "Tanks", german = true, path = new[] { V(-262f, -900f), V(-262f, -575f) }, t0 = 12f, t1 = 19f },
                    new Move { label = "Gammon bombs  McGee", colour = Green, path = new[] { V(-240f, -560f), V(-258f, -572f) }, t0 = 19f, t1 = 21f },
                    new Move { label = "Half-track", german = true, path = new[] { V(-300f, -560f), V(-300f, -250f), V(-262f, 12f) }, t0 = 16f, t1 = 25f },
                },
            });
            World("6 June, morning", "At the crossroads a tank comes within four yards of A Company's trench. Private Michael McGee walks out at it, firing his Bren " +
                  "from the hip, while two paras run in with Gammon bombs. Further back, Germans storm a house full of wounded men, and the battalion padre, " +
                  "George Parry, is killed defending them.", 14f,
                  t => Dolly(new Vector3(-230f, 30f, -560f), new Vector3(-200f, 22f, -640f), new Vector3(-162f, 8f, -800f), t));
            World("6 June, morning", "At the bridge, Wally Parr's men learn to fire a captured German anti-tank gun. One shot goes clean through the water tower, " +
                  "where snipers are hiding; another hits a tree by the château. Howard won't let them fire at the château itself: it is a maternity hospital.", 13f,
                  t => Dolly(new Vector3(-20f, 25f, 60f), new Vector3(-60f, 30f, -40f), new Vector3(-160f, 10f, -700f), t));
            World("6 June", "A German plane drops a bomb right onto the bridge. It hits the steel with a clang, falls into the canal - and does not go off. " +
                  "Later the sappers find the explosives at last, stored in a shed near the bridge. The bridge was never ready to be blown.", 13f,
                  t => Dolly(new Vector3(6f, 22f, -30f), new Vector3(2f, 12f, -12f), bridge, t));
            World("about 13:00", "From Sword Beach the commandos of Lord Lovat's 1st Special Service Brigade fight their way through, " +
                  "Piper Bill Millin playing. The airborne men and the men from the sea have met.", 12f,
                  t => Dolly(new Vector3(-150f, 12f, 40f), new Vector3(-60f, 8f, 15f), bridge, t));
            World("Horsa Bridge", "Lord Lovat shakes hands with Howard, and the commandos cross at the double. Lovat tells Bill Millin to keep playing all the way over " +
                  "the river bridge. Snipers fire from the trees and towers - some commandos are killed on the bridge - but Millin walks over, playing.", 13f,
                  t => Dolly(orne + new Vector3(-60f, 8f, 25f), orne + new Vector3(-25f, 5f, 12f), orne + Vector3.up * 2f, t));
            World("6 June, night", "That night the 2nd Battalion of the Royal Warwickshire Regiment arrives, and Howard's men hand over the bridges. " +
                  "The canal bridge gets a new name: Pegasus Bridge, after the winged horse on the airborne soldiers' badge.", 13f,
                  t => Dolly(new Vector3(60f, 40f, 80f), new Vector3(30f, 25f, 40f), bridge, t));
            World("7 June", "In the dark, Howard's men march to Ranville. There, out of the night, come the men of glider 94: they had landed by the River Dives, " +
                  "miles to the east, and found their own way back.", 12f,
                  t => Dolly(new Vector3(700f, 30f, -200f), new Vector3(780f, 25f, -300f), new Vector3(900f, 0f, -400f), t));
            World("After", "John Howard is awarded the Distinguished Service Order. Den Brotheridge lies in the churchyard at Ranville. " +
                  "The river bridge is named Horsa Bridge, after the gliders - and the Café Gondrée still stands beside Pegasus Bridge.", 13f,
                  t => Dolly(cafe + new Vector3(-30f, 10f, 40f), cafe + new Vector3(-12f, 5f, 18f), cafe + Vector3.up * 3f, t));
            World("We remember", "Lieutenant Den Brotheridge, 25 Platoon, 2nd Ox & Bucks.   Private Fred Greenhalgh, 14 Platoon, 2nd Ox & Bucks.   " +
                  "And the men of 7th Parachute Battalion, the commandos, the glider pilots and the aircrews who did not come home.", 15f,
                  t => Dolly(new Vector3(0f, 120f, -40f), new Vector3(0f, 160f, -60f), bridge, t));
            shots[shots.Count - 1].say = "We remember. " + shots[shots.Count - 1].text;
        }

        static GameObject Car(string name)
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include))
                if (t.name == name) return t.gameObject;
            return null;
        }

        static void Show(string name)
        {
            var c = Car(name); if (!c) return;
            c.SetActive(true);
            var p = c.transform.position;
            if (Terrain.activeTerrain) p.y = Terrain.activeTerrain.SampleHeight(p) + Terrain.activeTerrain.transform.position.y;
            c.transform.position = p;
        }

        /// <summary>A car racing in: the sound of it coming up the road and past, from the car itself.</summary>
        static void DriveBy(string name)
        {
            var c = Car(name); var clip = Sfx.CarPassing;
            if (!c || !clip) return;
            var a = c.GetComponent<AudioSource>() ?? c.AddComponent<AudioSource>();
            a.clip = clip; a.spatialBlend = 0.6f; a.minDistance = 15f; a.maxDistance = 400f; a.volume = 0.8f; a.loop = false;
            a.time = Mathf.Max(0f, clip.length - 8f); a.Play();   // the last 8 s: the loud pass, not the long approach
        }

        static void Hide(string name) { var c = Car(name); if (c) c.SetActive(false); }

        static IEnumerator After(float s, Action a) { yield return new WaitForSeconds(s); a(); }

        // ------------------------------------------------------------------ running it

        IEnumerator Run()
        {
            Playing = true;
            narrator = gameObject.AddComponent<AudioSource>();
            narrator.playOnAwake = false; narrator.spatialBlend = 0f; narrator.volume = 1f; narrator.priority = 0;
            narrator.ignoreListenerVolume = true;   // the narrator stays at full volume while the rest of the sound is turned down
            // a soft "film light" over the whole scene, as the night shots of the old war films (blue filter, real daylight)
            var lightGo = new GameObject("History film light");
            var fl = lightGo.AddComponent<Light>(); fl.type = LightType.Directional; fl.intensity = 0.9f;
            fl.color = new Color(0.78f, 0.84f, 1f); fl.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(42f, 205f, 0f);
            var cine = FindAnyObjectByType<CinematicDirector>();
            if (cine) cine.filmLookInPlay = true;
            fade = 1f;
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            for (index = 0; index < shots.Count && !stop; index++)
            {
                var s = shots[index];
                shotStart = Time.time; next = false;
                photo = null;
                if (!string.IsNullOrEmpty(s.photo) && photos != null)
                    foreach (var p in photos) if (p && p.name == s.photo) photo = p;
                s.start?.Invoke();
                narrator.Stop();
                var clip = VoiceFor(index);
                if (clip) { narrator.clip = clip; narrator.PlayDelayed(0.6f); s.secs = Mathf.Max(s.secs, clip.length + 2f); }
                if (s.kind == Kind.Flyby)
                {
                    // the gliders in tow over the Channel and Horsa 91 casting off (the game's own intro shots)
                    if (cine)
                    {
                        cine.PlayHistoryFlyby("5 JUNE 1944 — 22:56", "From RAF Tarrant Rushton in Dorset, six Halifax bombers of 298 and 644 Squadrons " +
                                              "tow the six Horsas into the night. In glider 91 the men are singing.",
                                              "OVER THE CHANNEL", "Ninety minutes across the sea, in the noise and the dark. Behind them the greatest invasion fleet " +
                                              "the world has ever seen is on its way.",
                                              "6 JUNE 1944 — JUST AFTER MIDNIGHT", "Over the French coast the gliders cast off. The pilots fly by stopwatch and compass, " +
                                              "down through the cloud towards the canal.");
                        yield return null;
                        while (CinematicDirector.Playing && !stop) yield return null;
                        while (narrator.isPlaying && !stop) yield return null;   // let the narrator finish over the last shot
                    }
                    continue;
                }
                while (Time.time - shotStart < s.secs && !next && !stop) yield return null;
            }
            narrator.Stop();
            fade = 0f;
            for (float t = 0f; t < 1.2f; t += Time.deltaTime) { fade = t / 1.2f; yield return null; }
            Playing = false;
            Net.ForceSimulate = false;
            AudioListener.volume = 1f;
            // the gliders have flown and the world has moved on: start the level fresh, back at the menu
            SceneManager.LoadScene(gameObject.scene.name);
        }

        void OnDestroy() { if (Playing) { Playing = false; Net.ForceSimulate = false; AudioListener.volume = 1f; } }

        void Update()
        {
            if (!Playing) return;
            var kb = Keyboard.current;
            if (kb != null)
            {
                bool inFlyby = index >= 0 && index < shots.Count && shots[index].kind == Kind.Flyby;
                if (!inFlyby && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame)) next = true;
                if (kb.escapeKey.wasPressedThisFrame) stop = true;
            }
            letterbox = Mathf.MoveTowards(letterbox, 1f, Time.deltaTime * 2f);
            // the battle, the engines and the music go quiet under the narrator, and come back up between his lines
            float duck = narrator && narrator.isPlaying ? 0.25f : 0.7f;
            AudioListener.volume = Mathf.MoveTowards(AudioListener.volume, duck, Time.deltaTime * 1.5f);
            fade = Mathf.MoveTowards(fade, 0f, Time.deltaTime * 0.8f);
        }

        float baseFog = -1f;

        void LateUpdate()
        {
            if (!Playing || index < 0 || index >= shots.Count) return;
            // "day for night", as the old war films did it: the real night of 5/6 June is too dark to tell the story in
            if (baseFog < 0f) baseFog = RenderSettings.fogDensity;
            var s = shots[index];
            if (s.kind == Kind.Flyby) return;   // CinematicDirector has the camera and the light
            RenderSettings.fogDensity = baseFog * 0.25f;
            var a = RenderSettings.ambientLight;
            RenderSettings.ambientLight = new Color(Mathf.Max(a.r, 0.32f), Mathf.Max(a.g, 0.34f), Mathf.Max(a.b, 0.42f));
            foreach (var sky in FindObjectsByType<NightSky>())
                if (sky.moon && sky.moon.enabled) sky.moon.intensity = Mathf.Max(sky.moon.intensity * 3f, 1.2f);
            if (s.cam == null) return;
            var cam = Camera.main;
            if (!cam) return;
            float t = Mathf.Clamp01((Time.time - shotStart) / s.secs);
            var (pos, look) = s.cam(t);
            float ground = Terrain.activeTerrain ? Terrain.activeTerrain.SampleHeight(pos) + Terrain.activeTerrain.transform.position.y : 0f;
            if (pos.y < ground + 1.5f) pos.y = ground + 1.5f;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation((look - pos).normalized, Vector3.up));
        }

        // ------------------------------------------------------------------ drawing

        void OnGUI()
        {
            if (!Playing || index < 0 || index >= shots.Count) return;
            var s = shots[index];
            if (s.kind == Kind.Flyby) return;   // CinematicDirector draws its own frame and captions
            GUI.depth = -20;
            float t = Time.time - shotStart;
            float alpha = Mathf.Clamp01(t / 0.8f) * Mathf.Clamp01((s.secs - t) / 0.8f);
            float bar = Screen.height * 0.14f;
            if (s.kind == Kind.Photo || s.kind == Kind.Map)
            {
                GUI.color = Color.black;
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            if (s.kind == Kind.Photo && photo)
            {
                // the photograph, held still (a slow push-in jitters when drawn on whole pixels)
                float h = (Screen.height - bar * 2f) * 0.94f, w = h * photo.width / Mathf.Max(1, photo.height);
                if (w > Screen.width * 0.94f) { w = Screen.width * 0.94f; h = w * photo.height / photo.width; }
                var r = new Rect(Mathf.Round((Screen.width - w) / 2f), Mathf.Round((Screen.height - h) / 2f), Mathf.Round(w), Mathf.Round(h));
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(t / 0.8f));
                GUI.DrawTexture(r, photo, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
                var credit = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.LowerRight };
                credit.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
                GUI.Label(new Rect(0, Screen.height - bar - 22, Screen.width - 16, 18), "RAF reconnaissance photograph, 1944", credit);
            }
            if (s.kind == Kind.Map) DrawMap(s, t);
            MissionHud.Film(letterbox, s.stamp, s.text, alpha);
            var hint = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.UpperRight };
            hint.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
            GUI.Label(new Rect(0, 8, Screen.width - 12, 20), $"The History - Pegasus Bridge   {index + 1} / {shots.Count}   ·   Space: next   Esc: back to the menu", hint);
            MissionHud.Fade(fade);
        }

        // ---- the 2D map: a plan of the bridges in the game's own coordinates (+z up the page, towards Ouistreham)

        Rect mapRect; Vector2 mapMin, mapMax;
        static readonly Vector2[] Orne = { V(531.8f, -700f), V(586.5f, -525f), V(556.4f, -350f), V(557.8f, -175f), V(600f, 0f), V(587.9f, 175f), V(585.2f, 350f), V(652.1f, 525f), V(616.6f, 700f), V(640f, 800f) };

        Vector2 P(Vector2 g) => new Vector2(mapRect.x + (g.x - mapMin.x) / (mapMax.x - mapMin.x) * mapRect.width,
                                            mapRect.yMax - (g.y - mapMin.y) / (mapMax.y - mapMin.y) * mapRect.height);

        static void Line(Vector2 a, Vector2 b, float width, Color c)
        {
            Vector2 d = b - a; float len = d.magnitude; if (len < 0.5f) return;
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.color = c;
            GUI.DrawTexture(new Rect(a.x, a.y - width / 2f, len, width), Texture2D.whiteTexture);
            GUI.matrix = m; GUI.color = Color.white;
        }

        void Poly(Vector2[] pts, float width, Color c) { for (int i = 0; i < pts.Length - 1; i++) Line(P(pts[i]), P(pts[i + 1]), width, c); }

        static void Dot(Vector2 p, float r, Color c) { GUI.color = c; GUI.DrawTexture(new Rect(p.x - r, p.y - r, r * 2f, r * 2f), Texture2D.whiteTexture); GUI.color = Color.white; }

        void Label(Vector2 p, string text, Color c, int size = 13)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = size, richText = true };
            st.normal.textColor = c;
            GUI.Label(new Rect(p.x + 8f, p.y - 9f, 320f, 22f), $"<b>{text}</b>", st);
        }

        void DrawMap(Shot s, float t)
        {
            float bar = Screen.height * 0.14f;
            // the area: the canal bridge close up for the assault, both bridges and Bénouville for the rest
            bool close = s.moves.Exists(m => m.label.StartsWith("25 Pl"));
            bool village = s.moves.Exists(m => m.label.StartsWith("A Coy"));
            if (close) { mapMin = V(-80f, -110f); mapMax = V(110f, 50f); }
            else if (village) { mapMin = V(-560f, -1080f); mapMax = V(140f, 140f); }
            else { mapMin = V(-380f, -320f); mapMax = V(820f, 780f); }
            float h = Screen.height - bar * 2f - 20f, w = h * (mapMax.x - mapMin.x) / (mapMax.y - mapMin.y);
            if (w > Screen.width - 40f) { w = Screen.width - 40f; h = w * (mapMax.y - mapMin.y) / (mapMax.x - mapMin.x); }
            mapRect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.BeginClip(mapRect);
            var keep = mapRect; mapRect = new Rect(0, 0, keep.width, keep.height);
            GUI.color = new Color(0.80f, 0.77f, 0.66f); GUI.DrawTexture(mapRect, Texture2D.whiteTexture); GUI.color = Color.white;
            float px = mapRect.width / (mapMax.x - mapMin.x);   // pixels a metre
            var water = new Color(0.35f, 0.48f, 0.62f); var road = new Color(0.45f, 0.40f, 0.33f);
            Poly(new[] { V(0f, -1000f), V(0f, 1000f) }, 42f * px, water);                       // the Caen Canal
            Poly(Orne, 60f * px, water);                                                          // the River Orne
            float rw = Mathf.Max(2f, 6f * px);
            Poly(new[] { V(-700f, -2f), V(-330f, 12f), V(-260f, 12f), V(-140f, 4f), V(-22f, 0f), V(22f, 0f), V(330f, -11f), V(900f, -40f) }, rw, road);   // Bénouville - Ranville
            Poly(new[] { V(-262f, -1100f), V(-260f, -320f), V(-260f, 312f) }, rw, road);                             // the Caen - Ouistreham road
            Poly(new[] { V(-22.5f, -560f), V(-22.5f, 560f) }, rw * 0.7f, road);                    // the west bank road
            Line(P(V(-22f, 0f)), P(V(22f, 0f)), Mathf.Max(4f, 8f * px), new Color(0.15f, 0.15f, 0.15f));          // the bridges
            Line(P(V(560f, -2f)), P(V(640f, -6f)), Mathf.Max(4f, 8f * px), new Color(0.15f, 0.15f, 0.15f));
            var ink = new Color(0.2f, 0.17f, 0.12f);
            Label(P(V(4f, close ? 40f : 520f)), "Caen Canal", ink, 12);
            if (!close) { Label(P(V(600f, 420f)), "River Orne", ink, 12); Label(P(V(-330f, 40f)), "Bénouville", ink, 12); Label(P(V(700f, -20f)), "to Ranville", ink, 12); }
            Label(P(V(-30f, -14f)), "Café", ink, 11);
            if (close) Label(P(V(60f, -100f)), "LZ X", ink, 12);
            if (village)
            {
                var park = P(V(-200f, -720f)); var park2 = P(V(-110f, -900f));
                GUI.color = new Color(0.45f, 0.6f, 0.35f, 0.6f); GUI.DrawTexture(Rect.MinMaxRect(park.x, park.y, park2.x, park2.y), Texture2D.whiteTexture); GUI.color = Color.white;
                Dot(P(V(-162f, -800f)), 6f, ink); Label(P(V(-162f, -800f)), "Château (maternity hospital)", ink, 12);
                Label(P(V(-262f, -640f)), "crossroads", ink, 11); Dot(P(V(-262f, 12f)), 4f, ink); Label(P(V(-250f, 30f)), "Mairie", ink, 11);
                Label(P(V(-90f, 90f)), "Le Port", ink, 12);
            }
            foreach (var m in s.moves)
            {
                if (t < m.t0 - (m.german ? 99f : 0.01f)) continue;
                float k = m.german && m.path.Length == 1 ? 1f : Mathf.Clamp01((t - m.t0) / Mathf.Max(0.01f, m.t1 - m.t0));
                float a = m.fadeOut ? Mathf.Clamp01((m.t1 - t) / 1.5f) : 1f;
                var c = m.german ? (m.colour.a > 0f ? m.colour : Red) : m.colour; c.a *= a;
                if (a <= 0f) continue;
                if (m.path.Length == 1) { var q = P(m.path[0]); Dot(q, 6f, c); Label(q, m.label, c); continue; }
                // the route drawn so far, and the unit at its head
                float total = 0f; for (int i = 0; i < m.path.Length - 1; i++) total += (m.path[i + 1] - m.path[i]).magnitude;
                float want = total * k, done = 0f; Vector2 head = m.path[0];
                for (int i = 0; i < m.path.Length - 1; i++)
                {
                    float seg = (m.path[i + 1] - m.path[i]).magnitude;
                    float f = Mathf.Clamp01((want - done) / seg);
                    var e = Vector2.Lerp(m.path[i], m.path[i + 1], f);
                    Line(P(m.path[i]), P(e), 3f, new Color(c.r, c.g, c.b, c.a * 0.8f));
                    head = e; done += seg;
                    if (f < 1f) break;
                }
                var hp = P(head);
                Dot(hp, 7f, c);
                Label(hp, m.label, c);
                if (m.label.StartsWith("PIAT") && k >= 1f) { Dot(P(V(-145f, 4f)), 10f + Mathf.PingPong(t * 20f, 6f), new Color(1f, 0.55f, 0.1f, a)); }
            }
            mapRect = keep;
            GUI.EndClip();
        }
    }
}
