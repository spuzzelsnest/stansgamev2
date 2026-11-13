using System.Collections.Generic;
using UnityEngine;

namespace StansGame.Additions
{
    /// <summary>
    /// The bootcamp's training ground, built at run time because there is no Unity source or editor for the
    /// demo build yet. It sits on a raised, flat field in a quiet corner of the level 1 map (about 680 m
    /// west of the bridge, out of sight of the mission) with hedges all round, and is dressed with what
    /// level 1 already loads (see <see cref="TrainingKit"/>): the terrain's ground textures, the level's
    /// shrubs, poplars, knife-rest wire, brushwood and the StuG, plus grass, bell tents,
    /// flags, a sky and daylight. Everything is placed in local coordinates: +z runs down the course, +x
    /// points down the ranges, and y = 0 is the top of the field. Built into the active scene, so leaving
    /// the game (which reloads the level) removes it again. With the Unity source this becomes a scene.
    /// </summary>
    public sealed class TrainingGround
    {
        // Where the field goes in the level 1 map: the flattest clear place found by a site search over
        // the terrain (height range under 1 m, no scene objects, 680 m from the bridge).
        static readonly Vector3 SiteOrigin = new Vector3(-500f, 0f, -400f);
        const float SiteYaw = 270f;

        // Field extent (local) and the holes in it.
        const float FieldX0 = -22f, FieldX1 = 74f, FieldZ0 = -14f, FieldZ1 = 186f;
        public const float DitchZ0 = 28f, DitchZ1 = 29.6f, DitchHalfX = 5f, DitchDepth = 1.0f;
        public const float PitZ0 = 68f, PitZ1 = 76f, PitHalfX = 8f, PitDepth = 2.9f, WaterY = -0.4f;
        public const float WallZ0 = 38f, WallZ1 = 39f, WallHeight = 1.1f;
        public const float WireZ0 = 52f, WireZ1 = 58f, WireHalfX = 4f, WireHeight = 0.75f;
        public const float BagsZ = 63.3f;

        // The jogging track round the west side, used by the squad that runs laps.
        public const float TrackX = -14f;

        // The bridge run: the clock starts at this line; the defenders stand here (local x, z), facing the start.
        public const float RunStartZ = 152.5f;
        static readonly Vector2[] DefenderSpots = { new Vector2(-3.6f, 174.6f), new Vector2(1.2f, 176.2f), new Vector2(4.4f, 174.4f), new Vector2(-6.8f, 166f), new Vector2(6.6f, 161.5f) };

        public Transform Root;
        public readonly List<Health> NearTargets = new List<Health>();
        public readonly List<Health> FarTargets = new List<Health>();
        public readonly List<Health> Defenders = new List<Health>();
        public Health GrenadeDummy;
        public Health Hulk;
        public Transform HulkTurret;
        public Renderer WaterSurface;
        public Pickup Rifle, Piat;

        Material turf, pasture, towpath, gravel, mud, trampled, planks, bags, hedge, canvas, crate, pole, wire, steel, white, tape, earth;
        System.Random rng = new System.Random(1944);

        /// <summary>World position of a local point on the field.</summary>
        public Vector3 W(float x, float y, float z)
        {
            return Root.TransformPoint(new Vector3(x, y, z));
        }

        /// <summary>World rotation for a local heading (degrees, 0 = down the course).</summary>
        public Quaternion Facing(float yaw)
        {
            return Root.rotation * Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>World heading (degrees) for a local heading.</summary>
        public float Yaw(float localYaw)
        {
            return SiteYaw + localYaw;
        }

        /// <summary>Local position of a world point.</summary>
        public Vector3 Local(Vector3 world)
        {
            return Root.InverseTransformPoint(world);
        }

        public static TrainingGround Build()
        {
            TrainingGround g = new TrainingGround();
            g.Make();
            return g;
        }

        void Make()
        {
            Quaternion rot = Quaternion.Euler(0f, SiteYaw, 0f);
            // Field height: 3.2 m above the highest terrain under it, so the canal pit and the ditch can be
            // dug into the field without meeting the terrain (which cannot be changed at run time).
            float hi = float.MinValue, lo = float.MaxValue;
            Terrain t = Terrain.activeTerrain;
            for (float x = FieldX0; x <= FieldX1; x += 4f)
                for (float z = FieldZ0; z <= FieldZ1; z += 4f)
                {
                    Vector3 p = SiteOrigin + rot * new Vector3(x, 0f, z);
                    float h = t != null ? t.SampleHeight(p) + t.transform.position.y : 0f;
                    hi = Mathf.Max(hi, h);
                    lo = Mathf.Min(lo, h);
                }
            float top = hi + 3.2f;
            float depth = top - lo + 1.5f;

            GameObject root = new GameObject("Bootcamp training ground (Countess Wear)");
            Root = root.transform;
            Root.SetPositionAndRotation(new Vector3(SiteOrigin.x, top, SiteOrigin.z), rot);

            HideSceneObjects();
            Materials();

            // ---- The field, in strips round the ditch and the canal pit.
            Slab(FieldX0, FieldX1, FieldZ0, DitchZ0, 0f, depth, turf);
            Slab(FieldX0, -DitchHalfX, DitchZ0, DitchZ1, 0f, depth, turf);
            Slab(DitchHalfX, FieldX1, DitchZ0, DitchZ1, 0f, depth, turf);
            Slab(-DitchHalfX, DitchHalfX, DitchZ0, DitchZ1, -DitchDepth, depth, mud);
            Slab(FieldX0, FieldX1, DitchZ1, PitZ0, 0f, depth, turf);
            Slab(FieldX0, -PitHalfX, PitZ0, PitZ1, 0f, depth, turf);
            Slab(PitHalfX, FieldX1, PitZ0, PitZ1, 0f, depth, turf);
            Slab(-PitHalfX, PitHalfX, PitZ0, PitZ1, -PitDepth, depth, mud);
            Slab(FieldX0, FieldX1, PitZ1, FieldZ1, 0f, depth, turf);

            // Faces for the sides of the ditch (mud) and the canal pit (plank revetment), over the field's own sides.
            Facing("ditch side", -DitchHalfX, DitchHalfX, DitchZ0 + 0.03f, DitchZ0 + 0.03f, -DitchDepth, 0f, mud);
            Facing("ditch side", -DitchHalfX, DitchHalfX, DitchZ1 - 0.03f, DitchZ1 - 0.03f, -DitchDepth, 0f, mud);
            Facing("canal revetment", -PitHalfX, PitHalfX, PitZ0 + 0.04f, PitZ0 + 0.04f, -PitDepth, 0.02f, planks);
            Facing("canal revetment", -PitHalfX, PitHalfX, PitZ1 - 0.04f, PitZ1 - 0.04f, -PitDepth, 0.02f, planks);
            Facing("canal revetment", -PitHalfX + 0.04f, -PitHalfX + 0.04f, PitZ0, PitZ1, -PitDepth, 0.02f, planks);
            Facing("canal revetment", PitHalfX - 0.04f, PitHalfX - 0.04f, PitZ0, PitZ1, -PitDepth, 0.02f, planks);

            // Ground wear: a gravel parade square, a worn path down the course, trodden earth at the stations,
            // and the jogging track round the west side.
            Flat("parade ground", -10f, 6f, -3.5f, 11f, gravel, 0.012f);
            Flat("path", -1.5f, 1.5f, 11f, DitchZ0, towpath, 0.02f);
            Flat("path", -1.5f, 1.5f, DitchZ1, PitZ0, towpath, 0.02f);
            Flat("path", -1.5f, 1.5f, PitZ1, 178f, towpath, 0.02f);
            Flat("path to the tents", 6f, 30f, -1.5f, 1.2f, towpath, 0.02f);
            Flat("range path", 1.5f, 8f, 92.5f, 95.5f, towpath, 0.02f);
            Flat("grenade path", 1.5f, 8f, 114.5f, 117.5f, towpath, 0.02f);
            Flat("piat path", 1.5f, 8f, 130.5f, 133.5f, towpath, 0.02f);
            Flat("jogging track", TrackX - 2.6f, TrackX + 2.6f, -7f, 178f, towpath, 0.016f);
            Flat("firing point", 3.5f, 10.5f, 78f, 110f, trampled, 0.014f);
            Flat("throwing bay", 7.5f, 12f, 111.5f, 120.5f, trampled, 0.014f);
            Flat("piat point", 6f, 11f, 128.5f, 135.5f, trampled, 0.014f);
            Flat("aid post", -7f, -1.5f, 141.5f, 149f, trampled, 0.014f);
            Flat("bridge approach", -3f, 3f, 148f, 157.5f, trampled, 0.014f);
            Flat("pit floor", 25.4f, 32.6f, 112.6f, 119.4f, mud, 0.014f);
            Flat("tent lines", 9f, 34f, -9f, -2.5f, trampled, 0.012f);

            // Hedges all round (2.6 m: too tall to climb): an invisible wall, a dark leafy core a little lower and
            // thinner, two staggered rows of the level's hedge shrubs over it, box bushes along the inside, and
            // poplars beyond, like the field boundaries in level 1.
            HedgeWall(-18f, 70f, -11f, -9.4f);
            HedgeWall(-18f, 70f, 181f, 182.6f);
            HedgeWall(-19.6f, -18f, -11f, 182.6f);
            HedgeWall(70f, 71.6f, -11f, 182.6f);
            HedgeLine(-18f, 70f, -10.2f, true, 1f);
            HedgeLine(-18f, 70f, 181.8f, true, -1f);
            HedgeLine(-11f, 182.6f, -18.8f, false, 1f);
            HedgeLine(-11f, 182.6f, 70.8f, false, -1f);
            for (float z = -6f; z < 182f; z += 21f) Prop("Tree1_Poplar", 72.9f + Jitter(0.4f), z + Jitter(3f), Rand(0f, 360f), Rand(0.85f, 1.05f), false);
            for (float z = 4f; z < 182f; z += 24f) Prop("Tree1_Poplar", -21f + Jitter(0.4f), z + Jitter(3f), Rand(0f, 360f), Rand(0.85f, 1.05f), false);
            for (float x = -6f; x < 68f; x += 22f) Prop("Tree1_Poplar", x + Jitter(3f), 184.4f + Jitter(0.3f), Rand(0f, 360f), Rand(0.85f, 1.05f), false);
            for (float x = 0f; x < 68f; x += 24f) Prop("Tree1_Poplar", x + Jitter(3f), -12.8f + Jitter(0.3f), Rand(0f, 360f), Rand(0.85f, 1.05f), false);
            // Brushwood piles in the spare corners of the field.
            Prop("Brush_A", 56f, 30f, 20f, 0.4f, false);
            Prop("Brush_C", 60f, 172f, 110f, 0.38f, false);
            Prop("Brush_A", 36f, 62f, 200f, 0.32f, false);

            // ---- 1 Fall in: the parade square with a flagstaff, bell tents to the east of it.
            Post(0f, 8f, 6f, 0.06f, pole);
            Block("flagstaff finial", new Vector3(0f, 6.04f, 8f), new Vector3(0.12f, 0.08f, 0.12f), white, false);
            Material union = TrainingKit.WithTexture("bootcamp union flag", TrainingKit.UnionFlag(256), 0.05f);
            Flag(new Vector3(0f, 5.35f, 8f), new Vector2(1.5f, 0.75f), union);
            Mesh tent = TrainingKit.BellTent(2.4f, 0.55f, 3.1f, 18);
            for (int i = 0; i < 4; i++) BellTent(tent, 13f + i * 6.2f, -6f);
            Crates(4.5f, 9.5f, 2);

            // ---- 2 Assault course: tape lane, ditch, wall, finish post.
            Flat("tape", -5.1f, -4.95f, 12f, DitchZ0, tape, 0.03f);
            Flat("tape", 4.95f, 5.1f, 12f, DitchZ0, tape, 0.03f);
            Flat("tape", -5.1f, -4.95f, DitchZ1, 48f, tape, 0.03f);
            Flat("tape", 4.95f, 5.1f, DitchZ1, 48f, tape, 0.03f);
            Flat("start line", -5f, 5f, 13.9f, 14.1f, tape, 0.03f);
            for (float z = 12f; z <= 48.01f; z += 6f)
            {
                if (z > DitchZ0 - 0.5f && z < DitchZ1 + 0.5f) continue;
                Post(-5.05f, z, 0.9f, 0.035f, pole);
                Post(5.05f, z, 0.9f, 0.035f, pole);
            }
            Box("assault wall", -5f, 5f, WallHeight * 0.5f, WallZ0, WallZ1, WallHeight, planks);
            Block("wall brace", new Vector3(-4.6f, 0.45f, WallZ1 + 0.45f), new Vector3(0.12f, 0.9f, 0.9f), planks, false).transform.localRotation = Quaternion.Euler(45f, 0f, 0f);
            Block("wall brace", new Vector3(4.6f, 0.45f, WallZ1 + 0.45f), new Vector3(0.12f, 0.9f, 0.9f), planks, false).transform.localRotation = Quaternion.Euler(45f, 0f, 0f);
            Post(-5.4f, 46f, 1.6f, 0.08f, white);
            Post(5.4f, 46f, 1.6f, 0.08f, white);
            Block("finish board", new Vector3(0f, 1.5f, 46f), new Vector3(10.8f, 0.25f, 0.06f), white, false);

            // ---- 3 Under the wire: an invisible ceiling from 0.75 m up (only prone fits under), with
            // pickets and strands to show it, knife rests on the flanks, then a sandbag wall to kneel behind.
            GameObject ceiling = new GameObject("wire ceiling");
            ceiling.transform.SetParent(Root, false);
            ceiling.transform.localPosition = new Vector3(0f, WireHeight + 0.95f, (WireZ0 + WireZ1) * 0.5f);
            BoxCollider bc = ceiling.AddComponent<BoxCollider>();
            bc.size = new Vector3(WireHalfX * 2f, 1.9f, WireZ1 - WireZ0);
            for (float z = WireZ0; z <= WireZ1 + 0.01f; z += 1f)
            {
                Post(-WireHalfX, z, WireHeight + 0.05f, 0.035f, pole);
                Post(WireHalfX, z, WireHeight + 0.05f, 0.035f, pole);
                Strand(new Vector3(-WireHalfX, WireHeight, z), new Vector3(WireHalfX, WireHeight, z), wire);
            }
            for (float x = -WireHalfX; x <= WireHalfX + 0.01f; x += 1f)
                Strand(new Vector3(x, WireHeight - 0.02f, WireZ0), new Vector3(x, WireHeight - 0.02f, WireZ1), wire);
            for (float z = WireZ0; z < WireZ1; z += 2f)
                for (float x = -WireHalfX; x < WireHalfX; x += 2f)
                {
                    Strand(new Vector3(x, WireHeight + 0.02f, z), new Vector3(x + 2f, WireHeight + 0.02f, z + 2f), wire);
                    Strand(new Vector3(x + 2f, WireHeight + 0.04f, z), new Vector3(x, WireHeight + 0.04f, z + 2f), wire);
                }
            foreach (float x in new float[] { -6.4f, 6.4f })
                for (float z = WireZ0 + 0.4f; z < WireZ1; z += 5.4f)
                {
                    GameObject k = Prop("KnifeRest", x, z + 2.6f, 0f, 1f, true);
                    // (Local z runs along world -x here, local x along world +z: the field is turned 270 degrees.)
                    if (k != null) TrainingKit.LongAlong(k, true);
                }
            Box("sandbags", -2f, 2f, 0.5f, BagsZ, BagsZ + 0.7f, 1.0f, bags);
            Block("sandbag top", new Vector3(0f, 1.06f, BagsZ + 0.35f), new Vector3(3.6f, 0.12f, 0.55f), bags, false);

            // ---- 4 The canal: a water strip in the pit, with a duckboard on the near bank.
            GameObject water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            Object.Destroy(water.GetComponent<Collider>());
            water.name = "bootcamp canal water";
            water.transform.SetParent(Root, false);
            water.transform.localPosition = new Vector3(0f, WaterY, (PitZ0 + PitZ1) * 0.5f);
            water.transform.localScale = new Vector3(PitHalfX * 0.2f, 1f, (PitZ1 - PitZ0) * 0.1f);
            WaterSurface = water.GetComponent<Renderer>();
            Material canal = FindWater();
            if (canal != null) WaterSurface.sharedMaterial = canal;
            else WaterSurface.sharedMaterial = TrainingKit.Plain("bootcamp water", new Color(0.18f, 0.27f, 0.3f), 0.8f);
            Water.Register(WaterSurface);
            Box("duckboard", -1.5f, 1.5f, 0.04f, PitZ0 - 2f, PitZ0, 0.08f, planks);
            for (float z = PitZ0 - 1.9f; z < PitZ0; z += 0.45f)
                Block("duckboard slat", new Vector3(0f, 0.1f, z), new Vector3(3.1f, 0.04f, 0.14f), planks, false);
            Post(-PitHalfX - 0.6f, PitZ0 - 0.6f, 1.4f, 0.05f, pole);
            Post(PitHalfX + 0.6f, PitZ0 - 0.6f, 1.4f, 0.05f, pole);

            // ---- 5 Range: firing point with the rifle on a table, targets at 25 m and 50 m, a long earth backstop,
            // a red range flag, and extra lanes either side for the recruits shooting alongside.
            Box("range table", 7.8f, 8.6f, 0.4f, 93.2f, 94.8f, 0.8f, planks);
            Bank("backstop", 65.5f, 116f, 80f, 7.5f, 2.2f, 4.8f, turf);
            Post(11f, 84f, 5f, 0.05f, pole);
            Block("range flag", new Vector3(11f, 4.55f, 84.5f), new Vector3(0.03f, 0.6f, 0.95f), Red(), false);
            Crates(6.6f, 90.5f, 3);
            Crates(6.6f, 98.5f, 2);
            Material face = TrainingKit.WithTexture("bootcamp target face", TrainingKit.TargetFace(96, 3), 0.05f);
            float[] tz = { 86f, 90f, 94f, 98f, 102f };
            foreach (float z in tz)
            {
                NearTargets.Add(Target(32f, z, face, 1f, 0f));
                FarTargets.Add(Target(57f, z, face, 1f, 0f));
            }
            foreach (float z in new float[] { 80f, 82.5f, 105.5f, 108f })
            {
                Decoy(32f, z, face);
                Decoy(57f, z, face);
            }
            Rifle = MakePickup("Lee-Enfield", W(8.2f, 0.85f, 94f), Facing(90f));

            // ---- 6 Grenades: a sandbag bay to throw from and a pit ringed with sandbags, a dummy in it.
            Box("throwing bay", 9.4f, 10.1f, 0.5f, 113f, 119f, 1.0f, bags);
            Box("pit ring", 26f, 32f, 0.25f, 112.6f, 113.2f, 0.5f, bags);
            Box("pit ring", 26f, 32f, 0.25f, 118.8f, 119.4f, 0.5f, bags);
            Box("pit ring", 25.4f, 26f, 0.25f, 112.6f, 119.4f, 0.5f, bags);
            Box("pit ring", 32f, 32.6f, 0.25f, 112.6f, 119.4f, 0.5f, bags);
            GrenadeDummy = Dummy("grenade dummy", new Vector3(29f, 0f, 116f), face);
            Crates(8.4f, 112f, 2);

            // ---- 7 PIAT: the launcher on a table and a knocked-out StuG at about 42 m (the level's own model).
            Box("piat table", 7.8f, 8.6f, 0.4f, 131.2f, 132.8f, 0.8f, planks);
            Piat = MakePickup("PIAT", W(8.2f, 0.85f, 132f), Facing(90f));
            BuildHulk(new Vector3(49f, 0f, 132f));
            Crates(7.2f, 129.5f, 2);

            // ---- 8 Field dressing: an aid post with a stretcher, a bell tent and a red cross flag.
            Block("stretcher", new Vector3(-3.3f, 0.25f, 145f), new Vector3(0.6f, 0.06f, 2.0f), canvas, true);
            Post(-3.62f, 145f, 0.25f, 0.025f, pole);
            Post(-2.98f, 145f, 0.25f, 0.025f, pole);
            BellTent(tent, -8.2f, 146f);
            Post(-4.4f, 147.6f, 3.2f, 0.05f, pole);
            Material redCross = TrainingKit.WithTexture("bootcamp red cross", TrainingKit.RedCross(96), 0.05f);
            Flag(new Vector3(-4.4f, 2.85f, 147.6f), new Vector2(0.9f, 0.6f), redCross);

            // ---- 9 Bridge drill: a low timber bridge with rails, like the mock-ups the company used.
            Box("mock bridge deck", -2f, 2f, 0.1f, 158f, 170f, 0.2f, planks);
            Box("mock bridge ramp", -2f, 2f, 0.05f, 157f, 158f, 0.1f, planks);
            Box("mock bridge ramp", -2f, 2f, 0.05f, 170f, 171f, 0.1f, planks);
            for (float z = 158f; z <= 170.01f; z += 2f)
            {
                Post(-2.1f, z, 1.1f, 0.06f, pole);
                Post(2.1f, z, 1.1f, 0.06f, pole);
            }
            Box("rail", -2.16f, -2.04f, 1.0f, 158f, 170f, 0.1f, planks);
            Box("rail", 2.04f, 2.16f, 1.0f, 158f, 170f, 0.1f, planks);
            foreach (float x in new float[] { -4.6f, 4.6f })
            {
                GameObject k = Prop("KnifeRest", x, 154f, 0f, 1f, true);
                if (k != null) TrainingKit.LongAlong(k, false);
            }
            Box("bridge sandbags", -5.5f, -3f, 0.5f, 172.5f, 173.2f, 1.0f, bags);
            Box("bridge sandbags", 3f, 5.5f, 0.5f, 172.5f, 173.2f, 1.0f, bags);
            // The timed run: a start line, a table with a Sten for anyone still carrying the PIAT, and pop-up
            // defenders on the far bank and the flanks (down until the run starts; see Bootcamp).
            Flat("run start line", -3.2f, 3.2f, RunStartZ - 0.1f, RunStartZ + 0.1f, tape, 0.03f);
            Post(-3.3f, RunStartZ, 1.2f, 0.05f, white);
            Post(3.3f, RunStartZ, 1.2f, 0.05f, white);
            Box("sten table", 3.4f, 4.2f, 0.4f, 149.4f, 150.8f, 0.8f, planks);
            foreach (Vector2 d in DefenderSpots) Defenders.Add(Target(d.x, d.y, 180f, face, 1f, 0f));

            Grass();
            TrainingKit.Sky(Root, new Color(0.3f, 0.47f, 0.76f), Bootcamp.HorizonColour, new Color(0.5f, 0.53f, 0.5f));
            TrainingKit.PostProcessing(Root);
            Root.gameObject.AddComponent<TrainingAmbience>().ground = this;
        }

        // ------------------------------------------------------------------ materials

        void Materials()
        {
            turf = Or(TrainingKit.Metric("Ground_Turf_Albedo", "Ground_Turf_Normal", new Vector2(4.5f, 4.5f), new Color(0.92f, 1f, 0.88f), 0.05f), new Color(0.3f, 0.42f, 0.2f));
            pasture = Or(TrainingKit.Metric("Ground_Pasture_Albedo", "Ground_Pasture_Normal", new Vector2(3.5f, 3.5f), new Color(0.94f, 1f, 0.9f), 0.05f), new Color(0.33f, 0.43f, 0.21f));
            towpath = Or(TrainingKit.Metric("Ground_Towpath_Albedo", "Ground_Towpath_Normal", new Vector2(3f, 3f), Color.white, 0.06f), new Color(0.52f, 0.45f, 0.33f));
            gravel = Or(TrainingKit.Metric("Ground_Gravel_Albedo", "Ground_Gravel_Normal", new Vector2(2.6f, 2.6f), Color.white, 0.08f), new Color(0.55f, 0.52f, 0.47f));
            mud = Or(TrainingKit.Metric("Ground_Mud_Albedo", "Ground_Mud_Normal", new Vector2(3f, 3f), Color.white, 0.18f), new Color(0.22f, 0.18f, 0.13f));
            trampled = Or(TrainingKit.Metric("Ground_Trampled_Albedo", "Ground_Trampled_Normal", new Vector2(3f, 3f), Color.white, 0.06f), new Color(0.4f, 0.36f, 0.26f));
            earth = Or(TrainingKit.Metric("Ground_Mud_Albedo", "Ground_Mud_Normal", new Vector2(3f, 3f), new Color(0.95f, 0.9f, 0.84f), 0.08f), new Color(0.36f, 0.29f, 0.2f));
            hedge = Or(TrainingKit.Metric("Buxus_BaseColor", "Buxus_Normal", new Vector2(1.3f, 1.3f), new Color(0.62f, 0.72f, 0.55f), 0.05f), new Color(0.13f, 0.24f, 0.1f));
            planks = TrainingKit.MetricFrom("Trench_Planks", new Vector2(2f, 2f), Color.white, new Color(0.45f, 0.33f, 0.2f));
            bags = TrainingKit.MetricFrom("Sandbags", new Vector2(1.6f, 0.8f), new Color(0.8f, 0.7f, 0.52f), new Color(0.62f, 0.55f, 0.4f));
            crate = TrainingKit.MetricFrom("Trench_Planks", new Vector2(0.9f, 0.9f), new Color(0.85f, 0.9f, 0.62f), new Color(0.36f, 0.38f, 0.26f));
            canvas = TrainingKit.Plain("bootcamp canvas", new Color(0.8f, 0.76f, 0.63f), 0.04f);
            Material p = TrainingKit.Mat("Pole_Wood");
            pole = p != null ? p : TrainingKit.Plain("bootcamp pole", new Color(0.4f, 0.3f, 0.2f), 0.1f);
            wire = TrainingKit.Plain("bootcamp wire", new Color(0.2f, 0.19f, 0.17f), 0.35f);
            steel = TrainingKit.Plain("bootcamp hulk", new Color(0.27f, 0.29f, 0.24f), 0.2f);
            white = TrainingKit.Plain("bootcamp white", new Color(0.88f, 0.88f, 0.85f), 0.1f);
            tape = TrainingKit.Plain("bootcamp tape", new Color(0.92f, 0.92f, 0.88f), 0.05f);
        }

        static Material Or(Material m, Color fallback)
        {
            return m != null ? m : TrainingKit.Plain("bootcamp fallback", fallback, 0.1f);
        }

        static Material red;
        static Material Red()
        {
            if (red == null) red = TrainingKit.Plain("bootcamp red", new Color(0.72f, 0.08f, 0.06f), 0.05f);
            return red;
        }

        static Material FindWater()
        {
            return TrainingKit.Mat("Water_Canal");
        }

        // ------------------------------------------------------------------ pieces

        void Slab(float x0, float x1, float z0, float z1, float topY, float depth, Material m)
        {
            float h = depth + topY;
            Block("field", new Vector3((x0 + x1) * 0.5f, topY - h * 0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, h, z1 - z0), m, true);
        }

        GameObject Box(string name, float x0, float x1, float centreY, float z0, float z1, float height, Material m)
        {
            return Block(name, new Vector3((x0 + x1) * 0.5f, centreY, (z0 + z1) * 0.5f), new Vector3(x1 - x0, height, z1 - z0), m, true);
        }

        void Flat(string name, float x0, float x1, float z0, float z1, Material m, float h)
        {
            Block(name, new Vector3((x0 + x1) * 0.5f, h * 0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, h, z1 - z0), m, false);
        }

        /// <summary>A thin vertical facing from x0..x1 (or z0..z1) between two heights, visual only.</summary>
        void Facing(string name, float x0, float x1, float z0, float z1, float y0, float y1, Material m)
        {
            Vector3 size = new Vector3(Mathf.Max(0.02f, x1 - x0), y1 - y0, Mathf.Max(0.02f, z1 - z0));
            Block(name, new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), size, m, false);
        }

        GameObject Block(string name, Vector3 localPos, Vector3 size, Material m, bool collide)
        {
            return TrainingKit.Block(name, Root, localPos, size, m, collide);
        }

        /// <summary>A hedge: an invisible wall the full 2.6 m, a darker leafy core inside it for the shrubs to cover.</summary>
        void HedgeWall(float x0, float x1, float z0, float z1)
        {
            GameObject wall = Box("hedge", x0, x1, 1.3f, z0, z1, 2.6f, hedge);
            wall.GetComponent<Renderer>().enabled = false;
            float inset = 0.3f;
            bool alongX = x1 - x0 > z1 - z0;
            float ax0 = alongX ? x0 : x0 + inset, ax1 = alongX ? x1 : x1 - inset;
            float az0 = alongX ? z0 + inset : z0, az1 = alongX ? z1 - inset : z1;
            Block("hedge core", new Vector3((ax0 + ax1) * 0.5f, 1.05f, (az0 + az1) * 0.5f), new Vector3(ax1 - ax0, 2.1f, az1 - az0), hedge, false);
        }

        /// <summary>An earth bank (a range butt): a trapezoid section run along z, solid.</summary>
        void Bank(string name, float x, float z, float length, float bottom, float top, float height, Material m)
        {
            Vector2[] section = { new Vector2(-bottom * 0.5f, -0.3f), new Vector2(bottom * 0.5f, -0.3f), new Vector2(top * 0.5f, height), new Vector2(-top * 0.5f, height) };
            Vector3 at = new Vector3(x, 0f, z);
            GameObject go = new GameObject(name);
            go.transform.SetParent(Root, false);
            go.transform.localPosition = at;
            Mesh mesh = TrainingKit.Prism(section, length, at);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        void Post(float x, float z, float height, float radius, Material m)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "post";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(Root, false);
            go.transform.localPosition = new Vector3(x, height * 0.5f, z);
            go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            go.GetComponent<Renderer>().sharedMaterial = m;
        }

        void Strand(Vector3 a, Vector3 b, Material m)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "wire strand";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(Root, false);
            go.transform.localPosition = (a + b) * 0.5f;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
            go.transform.localScale = new Vector3(0.014f, (b - a).magnitude * 0.5f, 0.014f);
            go.GetComponent<Renderer>().sharedMaterial = m;
        }

        /// <summary>A flag hanging from a staff, its hoist edge on the staff at <paramref name="hoist"/>.</summary>
        void Flag(Vector3 hoist, Vector2 size, Material m)
        {
            GameObject pivot = new GameObject("flag");
            pivot.transform.SetParent(Root, false);
            pivot.transform.localPosition = hoist;
            pivot.transform.localRotation = Quaternion.Euler(0f, 200f, 0f);   // flying off to the east, in the wind
            GameObject f = GameObject.CreatePrimitive(PrimitiveType.Cube);
            f.name = "flag cloth";
            Object.Destroy(f.GetComponent<Collider>());
            f.transform.SetParent(pivot.transform, false);
            f.transform.localPosition = new Vector3(size.x * 0.5f + 0.05f, 0f, 0f);
            f.transform.localScale = new Vector3(size.x, size.y, 0.01f);
            f.GetComponent<Renderer>().sharedMaterial = m;
            pivot.AddComponent<FlagFlutter>();
        }

        void BellTent(Mesh mesh, float x, float z)
        {
            GameObject t = new GameObject("bell tent");
            t.transform.SetParent(Root, false);
            t.transform.localPosition = new Vector3(x, 0f, z);
            t.transform.localRotation = Quaternion.Euler(0f, Rand(0f, 360f), 0f);
            t.AddComponent<MeshFilter>().sharedMesh = mesh;
            t.AddComponent<MeshRenderer>().sharedMaterial = canvas;
            BoxCollider c = t.AddComponent<BoxCollider>();
            c.center = new Vector3(0f, 0.9f, 0f);
            c.size = new Vector3(3.6f, 1.8f, 3.6f);
            Post(x, z, 3.25f, 0.04f, pole);
        }

        /// <summary>A small stack of ammunition boxes (no collider, so nobody snags on them).</summary>
        void Crates(float x, float z, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float y = i < 2 ? 0.17f : 0.51f;
                float dz = i == 1 ? 0.45f : i == 2 ? 0.22f : 0f;
                GameObject c = Block("ammunition box", new Vector3(x + Jitter(0.04f), y, z + dz), new Vector3(0.72f, 0.34f, 0.42f), crate, false);
                c.transform.localRotation = Quaternion.Euler(0f, Jitter(6f), 0f);
            }
        }

        /// <summary>A copy of a level prop at a local spot (heading in local degrees), sat on the field.</summary>
        GameObject Prop(string mesh, float x, float z, float localYaw, float scale, bool collide)
        {
            return TrainingKit.CopyMesh(mesh, Root, W(x, 0f, z), Yaw(localYaw), scale, Root.position.y, collide);
        }

        /// <summary>The level's hedge shrubs along one hedge line, with box bushes on the field side.</summary>
        void HedgeLine(float from, float to, float at, bool alongX, float inward)
        {
            int row = 0;
            for (float s = from + 0.6f; s < to; s += 0.95f, row++)
            {
                float a = s + Jitter(0.3f), b = at + (row % 2 == 0 ? 0.42f : -0.42f) + Jitter(0.15f);
                Prop("Shrub", alongX ? a : b, alongX ? b : a, Rand(0f, 360f), Rand(0.55f, 0.7f), false);
            }
            for (float s = from + 2f; s < to - 1f; s += 3.4f)
            {
                float a = s + Jitter(0.8f), b = at + inward * Rand(1.1f, 1.5f);
                if (IsOpenGround(alongX ? a : b, alongX ? b : a))
                    Prop("Bush_low", alongX ? a : b, alongX ? b : a, Rand(0f, 360f), Rand(1.0f, 1.5f), false);
            }
        }

        bool IsOpenGround(float x, float z)
        {
            return !(x > TrackX - 3f && x < TrackX + 3f);
        }

        float Rand(float a, float b) { return a + (b - a) * (float)rng.NextDouble(); }
        float Jitter(float r) { return Rand(-r, r); }

        // ------------------------------------------------------------------ grass

        struct Area
        {
            public float x0, x1, z0, z1;
            public Area(float a, float b, float c, float d) { x0 = a; x1 = b; z0 = c; z1 = d; }
            public bool Has(float x, float z) { return x > x0 && x < x1 && z > z0 && z < z1; }
        }

        // Where no grass grows: paths, stations, structures, the jogging track.
        static readonly Area[] Bare =
        {
            new Area(-2f, 2f, -3.5f, 178f),                      // the path down the course
            new Area(TrackX - 2.8f, TrackX + 2.8f, -8f, 179f),   // jogging track
            new Area(-10.5f, 6.5f, -4f, 11.5f),                  // parade ground
            new Area(9f, 34f, -9.2f, -2.3f),                     // tent lines
            new Area(6f, 30f, -1.8f, 1.5f),                      // path to the tents
            new Area(-5.4f, 5.4f, 11f, 49f),                     // assault lane
            new Area(-7.8f, 7.8f, 50f, 65f),                     // wire and knife rests
            new Area(-9f, 9f, 66f, 78f),                         // canal pit
            new Area(3f, 11f, 77.5f, 110.5f),                    // firing point
            new Area(29.5f, 34.5f, 78f, 110f),                   // near targets
            new Area(54.5f, 59.5f, 78f, 110f),                   // far targets
            new Area(61.5f, 69.5f, 74f, 158f),                   // backstop
            new Area(1.5f, 34f, 110.5f, 121f),                   // grenade bay and pit
            new Area(1.5f, 55f, 127.5f, 137f),                   // PIAT lane and hulk
            new Area(-12f, 0f, 140f, 150.5f),                    // aid post
            new Area(-6.5f, 6.5f, 147.5f, 175f),                 // bridge
        };

        // Where the grass is kept short: the ranges' lines of fire.
        static readonly Area[] Mown =
        {
            new Area(11f, 61.5f, 78f, 110f),
            new Area(34f, 61.5f, 127.5f, 137f),
        };

        static bool InAny(Area[] list, float x, float z)
        {
            foreach (Area a in list) if (a.Has(x, z)) return true;
            return false;
        }

        void Grass()
        {
            Material tall = TrainingKit.Cutout("Tree1_Leaves", "GrassLong", new Color(0.78f, 0.86f, 0.62f));
            Material shortGrass = TrainingKit.Cutout("Tree1_Leaves", "GrassBlades", new Color(0.82f, 0.88f, 0.66f));
            if (tall == null || shortGrass == null) return;
            const float bandZ = 40f;
            for (float z0 = -9f; z0 < 181f; z0 += bandZ)
            {
                List<Vector3> tufts = new List<Vector3>();
                List<Vector3> blades = new List<Vector3>();
                float z1 = Mathf.Min(181f, z0 + bandZ);
                int tries = (int)((z1 - z0) * 88f * 2.2f);
                for (int i = 0; i < tries; i++)
                {
                    float x = Rand(-17.6f, 69.6f), z = Rand(z0, z1);
                    if (InAny(Bare, x, z)) continue;
                    bool mown = InAny(Mown, x, z);
                    // Clumps: patches of long grass where Perlin noise is high, short blades everywhere else.
                    float patch = Mathf.PerlinNoise(x * 0.09f + 3.1f, z * 0.09f + 7.7f);
                    if (!mown && patch > 0.55f && rng.NextDouble() < 0.6) tufts.Add(new Vector3(x, 0f, z));
                    else if (rng.NextDouble() < (mown ? 0.6 : 0.85)) blades.Add(new Vector3(x, 0f, z));
                }
                GrassChunk("long grass", tufts, 0.35f, 0.75f, 0.4f, 0.8f, tall);
                GrassChunk("short grass", blades, 0.12f, 0.28f, 0.35f, 0.6f, shortGrass);
            }
            // Long grass along the foot of the hedges, where a mower would not reach.
            List<Vector3> edge = new List<Vector3>();
            for (int i = 0; i < 2600; i++)
            {
                int side = rng.Next(4);
                float s = (float)rng.NextDouble();
                float x = side == 0 ? -17.6f + Rand(0f, 1.6f) : side == 1 ? 69.6f - Rand(0f, 1.6f) : Mathf.Lerp(-17.6f, 69.6f, s);
                float z = side == 2 ? -8.9f + Rand(0f, 1.6f) : side == 3 ? 180.6f - Rand(0f, 1.6f) : Mathf.Lerp(-8.9f, 180.6f, s);
                if (InAny(Bare, x, z)) continue;
                edge.Add(new Vector3(x, 0f, z));
            }
            GrassChunk("hedge-foot grass", edge, 0.45f, 0.9f, 0.45f, 0.85f, tall);
        }

        void GrassChunk(string name, List<Vector3> spots, float minH, float maxH, float minW, float maxW, Material m)
        {
            if (spots.Count == 0) return;
            GameObject g = new GameObject(name);
            g.transform.SetParent(Root, false);
            g.transform.localPosition = Vector3.zero;
            g.AddComponent<MeshFilter>().sharedMesh = TrainingKit.GrassClumps(spots, minH, maxH, minW, maxW, rng);
            MeshRenderer r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ targets

        /// <summary>A figure-11 style target board on a post, facing the firing point. One hit puts it down.</summary>
        Health Target(float x, float z, Material faceMat, float maxHealth, float armour)
        {
            return Target(x, z, -90f, faceMat, maxHealth, armour);
        }

        /// <summary>A target facing a local heading (0 = down the course, 180 = back towards the start).</summary>
        Health Target(float x, float z, float faceYaw, Material faceMat, float maxHealth, float armour)
        {
            GameObject t = TargetBoard(x, z, faceYaw, faceMat, true);
            Health h = t.AddComponent<Health>();
            h.faction = Faction.German;   // counts as the enemy for the hit markers
            h.maxHealth = maxHealth;
            h.regenPerSecond = 0f;
            h.armour = armour;
            t.SetActive(true);
            return h;
        }

        /// <summary>A board in the recruits' lanes: looks the same, takes no part in the drill.</summary>
        void Decoy(float x, float z, Material faceMat)
        {
            GameObject t = TargetBoard(x, z, -90f, faceMat, false);
            t.name = "lane target " + x + "/" + z;
            t.SetActive(true);
        }

        GameObject TargetBoard(float x, float z, float faceYaw, Material faceMat, bool solid)
        {
            GameObject t = new GameObject("target " + x + "/" + z);
            t.SetActive(false);
            t.transform.SetParent(Root, false);
            t.transform.localPosition = new Vector3(x, 0f, z);
            t.transform.localRotation = Quaternion.Euler(0f, faceYaw, 0f);   // -90: face -x, the firing point
            GameObject hinge = new GameObject("hinge");
            hinge.transform.SetParent(t.transform, false);
            hinge.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            GameObject b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = "board";
            if (!solid) Object.Destroy(b.GetComponent<Collider>());
            b.transform.SetParent(hinge.transform, false);
            b.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            b.transform.localScale = new Vector3(0.6f, 1.0f, 0.04f);
            b.GetComponent<Renderer>().sharedMaterial = faceMat;
            // Two uprights and a frame behind the board, and a low earth mound in front to hide the mechanism.
            foreach (float px in new float[] { -0.28f, 0.28f })
            {
                GameObject p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                p.name = "target upright";
                Object.Destroy(p.GetComponent<Collider>());
                p.transform.SetParent(t.transform, false);
                p.transform.localPosition = new Vector3(px, 0.47f, -0.06f);
                p.transform.localScale = new Vector3(0.05f, 0.94f, 0.05f);
                p.GetComponent<Renderer>().sharedMaterial = pole;
            }
            // (The target's +z faces the firing point.)
            GameObject mound = TrainingKit.Block("target mound", t.transform, new Vector3(0f, 0.2f, 0.6f), new Vector3(1.4f, 0.42f, 0.55f), earth, false);
            mound.transform.localRotation = Quaternion.Euler(16f, 0f, 0f);
            return t;
        }

        Health Dummy(string name, Vector3 local, Material faceMat)
        {
            // Armour 70 stops every bullet (the Lee-Enfield does 70); a Mills bomb anywhere in the pit gets through.
            Health h = Target(local.x, local.z, faceMat, 15f, 70f);
            h.gameObject.name = name;
            return h;
        }

        void BuildHulk(Vector3 local)
        {
            GameObject t = new GameObject("dummy tank hulk");
            t.SetActive(false);
            t.transform.SetParent(Root, false);
            t.transform.localPosition = local;
            t.transform.localRotation = Quaternion.Euler(0f, 25f, 0f);
            // The hit boxes stay simple; the look is a copy of the StuG from the level's counter-attack.
            List<Renderer> boxes = new List<Renderer>();
            boxes.Add(Part(t.transform, "hull", new Vector3(0f, 1.0f, 0f), new Vector3(2.9f, 1.3f, 6.2f), steel));
            boxes.Add(Part(t.transform, "track", new Vector3(-1.35f, 0.5f, 0f), new Vector3(0.5f, 1.0f, 6.4f), wire));
            boxes.Add(Part(t.transform, "track", new Vector3(1.35f, 0.5f, 0f), new Vector3(0.5f, 1.0f, 6.4f), wire));
            boxes.Add(Part(t.transform, "superstructure", new Vector3(0f, 1.85f, -0.2f), new Vector3(2.3f, 0.5f, 3.4f), steel));
            GameObject turret = new GameObject("gun");
            turret.transform.SetParent(t.transform, false);
            turret.transform.localPosition = new Vector3(0f, 1.6f, 1.6f);
            HulkTurret = turret.transform;

            Transform stug = TrainingKit.SceneObject("Model", "StuG III (second)");
            t.SetActive(true);
            GameObject copy = stug != null ? TrainingKit.CopyObject(stug, Root, W(local.x, 0f, local.z), Yaw(0f), Root.position.y, false) : null;
            if (copy != null)
            {
                TrainingKit.LongAlong(copy, true);
                Bounds b = TrainingKit.Bounds(copy);
                copy.transform.position += W(local.x, 0f, local.z) - new Vector3(b.center.x, b.min.y, b.center.z);
                TrainingKit.TurnAboutCentre(copy, 25f);
                copy.transform.SetParent(t.transform, true);
                foreach (Renderer r in boxes) r.enabled = false;
            }
            else
            {
                Debug.LogWarning("Stan's Game: bootcamp found no StuG to copy; the PIAT target stays a plain hulk");
                Part(turret.transform, "gun barrel", new Vector3(0f, 0f, 1.2f), new Vector3(0.16f, 0.16f, 2.4f), steel);
            }

            Health h = t.AddComponent<Health>();
            // Armour 150 and 300 health: bullets and grenades do nothing, one PIAT bomb (500 direct) knocks it out.
            h.faction = Faction.German;
            h.maxHealth = 300f;
            h.regenPerSecond = 0f;
            h.armour = 150f;
            Hulk = h;
        }

        static Renderer Part(Transform parent, string name, Vector3 pos, Vector3 size, Material m)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            Renderer r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            return r;
        }

        /// <summary>A copy of one of the level's own weapons (taken from its pickups) lying on a table, as a pickup.</summary>
        public static Pickup MakePickup(string weaponName, Vector3 pos, Quaternion rot)
        {
            Weapon source = null;
            foreach (Pickup p in Pickup.All)
                if (p != null && p.weapon != null && p.weapon.displayName == weaponName) { source = p.weapon; break; }
            if (source == null)
                foreach (Weapon w in Resources.FindObjectsOfTypeAll<Weapon>())
                    if (w.displayName == weaponName) { source = w; break; }
            if (source == null)
            {
                Debug.LogWarning("Stan's Game: bootcamp found no " + weaponName + " to copy");
                return null;
            }
            GameObject copy = Object.Instantiate(source.gameObject);
            copy.name = weaponName;
            copy.SetActive(true);
            Weapon cw = copy.GetComponent<Weapon>();
            return Pickup.DropWeapon(cw, pos, rot);
        }

        /// <summary>Hides the level's own objects (trees, fences, buildings) that stand where the field goes.</summary>
        void HideSceneObjects()
        {
            Vector3 c = Root.TransformPoint(new Vector3((FieldX0 + FieldX1) * 0.5f, 0f, (FieldZ0 + FieldZ1) * 0.5f));
            Vector3 half = new Vector3((FieldX1 - FieldX0) * 0.5f + 4f, 60f, (FieldZ1 - FieldZ0) * 0.5f + 4f);
            foreach (Collider col in Physics.OverlapBox(c, half, Root.rotation, ~0, QueryTriggerInteraction.Collide))
            {
                if (col is TerrainCollider || col.GetComponentInParent<SoldierController>() != null) continue;
                col.enabled = false;
            }
            Bounds field = new Bounds(c, Vector3.zero);
            field.Encapsulate(Root.TransformPoint(new Vector3(FieldX0 - 4f, 0f, FieldZ0 - 4f)));
            field.Encapsulate(Root.TransformPoint(new Vector3(FieldX1 + 4f, 0f, FieldZ1 + 4f)));
            field.Expand(new Vector3(0f, 120f, 0f));
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (r is ParticleSystemRenderer || r.transform.IsChildOf(Root) || r.GetComponentInParent<SoldierController>() != null) continue;
                Bounds b = r.bounds;
                if (b.size.x > 300f || b.size.z > 300f) continue;   // water sheets and the like
                if (field.Contains(new Vector3(b.center.x, field.center.y, b.center.z))) r.enabled = false;
            }
        }
    }

    /// <summary>A gentle flutter for a flag: a small sway about the staff.</summary>
    public sealed class FlagFlutter : MonoBehaviour
    {
        Quaternion rest;
        float seed;

        void Start()
        {
            rest = transform.localRotation;
            seed = Random.value * 10f;
        }

        void Update()
        {
            float a = (Mathf.PerlinNoise(seed, Time.time * 0.6f) - 0.5f) * 16f;
            transform.localRotation = rest * Quaternion.Euler(0f, a, Mathf.Sin(Time.time * 2.1f + seed) * 1.5f);
        }
    }
}
