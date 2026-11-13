using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace StansGame.Additions
{
    /// <summary>
    /// The people on the training ground, made from the game's own British soldier models (see
    /// <see cref="TrainingKit.Soldier"/>): a sergeant-major who walks ahead to each station and faces the
    /// player, and recruits on the sidelines: a squad fallen in on the parade square, others waiting their
    /// turn at the assault course, the wire and the bridge, a Bren gunner at the end of the wire, riflemen
    /// shooting in the lanes either side of the player's, a grenade party, a PIAT team, a medic with a
    /// casualty, onlookers by the canal and a squad running laps of the field. None of them fight, take
    /// damage or block the drills; they are there to make the place feel like a working depot.
    /// </summary>
    public sealed class TrainingCrowd
    {
        // The sergeant-major's place at each station (local x, z) and the way he walks there from the one before.
        static readonly Vector2[] Spots =
        {
            new Vector2(1.8f, 9.5f), new Vector2(-6.5f, 15f), new Vector2(-5.5f, 50.5f), new Vector2(-9.6f, 79f),
            new Vector2(4.5f, 90.5f), new Vector2(6.5f, 112.5f), new Vector2(6f, 129.5f), new Vector2(-5.8f, 143f), new Vector2(-4f, 156f),
        };
        static readonly Vector2[][] Via =
        {
            new Vector2[0], new Vector2[0], new Vector2[0],
            new Vector2[] { new Vector2(-10.2f, 62f), new Vector2(-10.2f, 78f) },   // round the canal pit
            new Vector2[0], new Vector2[0], new Vector2[0], new Vector2[0], new Vector2[0],
        };

        readonly TrainingGround g;
        public Drill Sergeant;
        public Drill BrenGunner;
        public Drill FirstJogger;
        public Drill Casualty;
        readonly List<Drill> everyone = new List<Drill>();
        readonly List<Drill> riflemen = new List<Drill>();
        readonly List<float> nextShot = new List<float>();
        int sergeantStation = -1;

        // Counters the scripted test reads to confirm the wire crawl was under fire.
        public static int RoundsFired;
        public static int Thunderflashes;

        TrainingCrowd(TrainingGround ground) { g = ground; }

        public int Count { get { return everyone.Count; } }

        public static TrainingCrowd Build(TrainingGround ground)
        {
            TrainingCrowd c = new TrainingCrowd(ground);
            c.Make();
            return c;
        }

        Drill Put(string prefab, float x, float z, float localYaw, DrillPose pose)
        {
            return Put(prefab, x, 0f, z, localYaw, pose);
        }

        Drill Put(string prefab, float x, float y, float z, float localYaw, DrillPose pose)
        {
            Drill d = TrainingKit.Soldier(prefab, g.Root, g.W(x, y, z), g.Yaw(localYaw), pose);
            if (d == null && prefab != "BritishSoldier") d = TrainingKit.Soldier("BritishSoldier", g.Root, g.W(x, y, z), g.Yaw(localYaw), pose);
            if (d != null) everyone.Add(d);
            return d;
        }

        static string Recruit(int i)
        {
            return i % 3 == 1 ? "BritishRifleman" : "BritishSoldier";
        }

        void Make()
        {
            Sergeant = Put("BritishParaOfficer", Spots[0].x, Spots[0].y, 200f, DrillPose.Stand);
            if (Sergeant != null) Sergeant.name = "Sergeant-major (bootcamp)";
            sergeantStation = 0;

            // 1 Fall in: a squad in two ranks on the player's left, facing down the course like the player.
            int n = 0;
            for (int rank = 0; rank < 2; rank++)
                for (int file = 0; file < 4; file++)
                    Put(Recruit(n++), -3.6f - file * 1.55f, 2.1f - rank * 1.5f, 0f, DrillPose.Stand);

            // 2 Assault course: the next lot waiting by the start line.
            Put("BritishSoldier", -7.2f, 12f, 80f, DrillPose.Stand);
            Put("BritishRifleman", -8.1f, 13.3f, 70f, DrillPose.Crouch);
            Put("BritishSoldier", -7.4f, 14.6f, 95f, DrillPose.Stand);
            Put("BritishSoldier", -8.4f, 16f, 85f, DrillPose.Crouch);

            // 3 Wire: two waiting their turn, and the Bren gunner at the far end firing over the wire.
            Put("BritishSoldier", -9.4f, 51f, 10f, DrillPose.Prone);
            Put("BritishRifleman", -10.2f, 53.4f, 0f, DrillPose.Prone);
            BrenGunner = Put("BritishBrenGunner", 5.6f, 60.6f, 185f, DrillPose.ProneAim);
            Put("BritishParaOfficer", 7.2f, 61.5f, 200f, DrillPose.Stand);   // the instructor watching the gun

            // 4 Canal: onlookers on the far bank.
            Put("BritishSoldier", 10.2f, 66.2f, -95f, DrillPose.Stand);
            Put("BritishRifleman", 11.4f, 67f, -80f, DrillPose.Stand);
            Put("BritishSoldier", 10.6f, 78.4f, -110f, DrillPose.Crouch);

            // 5 Range: riflemen shooting in the lanes either side of the player's, a range officer behind them.
            foreach (float z in new float[] { 80f, 82.5f, 105.5f, 108f })
            {
                Drill r = Put(riflemen.Count % 2 == 0 ? "BritishRifleman" : "BritishSoldier", 5.6f, z, 90f, DrillPose.ProneAim);
                if (r != null) { riflemen.Add(r); nextShot.Add(Time.time + Random.Range(1f, 5f)); }
            }
            Put("BritishParaOfficer", 2.4f, 81.5f, 100f, DrillPose.Stand);

            // 6 Grenades: the next throwers crouched behind the bay.
            Put("BritishSoldier", 4.2f, 111f, 90f, DrillPose.Crouch);
            Put("BritishRifleman", 3.4f, 112.2f, 80f, DrillPose.Crouch);
            Put("BritishSoldier", 4.1f, 120.6f, 100f, DrillPose.Stand);

            // 7 PIAT: a two-man team waiting with their bombs.
            Put("BritishSoldier", 4.6f, 127.4f, 90f, DrillPose.Crouch);
            Put("BritishRifleman", 3.4f, 128.4f, 70f, DrillPose.Stand);

            // 8 Aid post: a casualty on the stretcher and a medic kneeling over him.
            Casualty = Put("BritishSoldier", -3.3f, 0.32f, 145.4f, 0f, DrillPose.Prone);
            if (Casualty != null) Casualty.Unarmed();
            Put("BritishSoldier", -4.3f, 145.4f, 90f, DrillPose.Crouch);

            // 9 Bridge: a section waiting at the approach.
            Put("BritishSoldier", -4.6f, 150f, 10f, DrillPose.Crouch);
            Put("BritishRifleman", -5.7f, 151.3f, 0f, DrillPose.Stand);
            Put("BritishSoldier", -4.4f, 152.6f, 20f, DrillPose.Crouch);

            // A squad running laps of the field on the track along the west hedge.
            Vector3[] lap = { g.W(-12.6f, 0f, 176f), g.W(-15.4f, 0f, 177.5f), g.W(-15.4f, 0f, -6.5f), g.W(-12.6f, 0f, -5f) };
            for (int i = 0; i < 6; i++)
            {
                Drill j = Put(Recruit(i), -12.6f, -4f + i * 1.7f, 0f, DrillPose.Stand);
                if (j != null) j.Go(lap, 3.4f, true);
                if (i == 0) FirstJogger = j;
            }
            Debug.Log("Stan's Game: bootcamp crowd of " + everyone.Count + " soldiers on parade");
        }

        /// <summary>Sends the sergeant-major to a station (walking round obstacles), or puts him straight there.</summary>
        public void Station(int s, bool warp)
        {
            if (Sergeant == null || s < 0 || s >= Spots.Length || s == sergeantStation) return;
            Vector2 spot = Spots[s];
            if (warp)
            {
                Sergeant.Warp(g.W(spot.x, 0f, spot.y), g.Yaw(180f));
            }
            else
            {
                List<Vector3> route = new List<Vector3>();
                foreach (Vector2 v in Via[s]) route.Add(g.W(v.x, 0f, v.y));
                route.Add(g.W(spot.x, 0f, spot.y));
                Sergeant.Go(route, 3.6f, false);
            }
            sergeantStation = s;
        }

        /// <summary>Every frame: the sergeant faces the player, the riflemen shoot now and then, and the Bren fires over the wire when <paramref name="liveFire"/>.</summary>
        public void Tick(Transform player, bool liveFire)
        {
            if (Sergeant != null) Sergeant.faceTarget = player;
            for (int i = 0; i < riflemen.Count; i++)
            {
                if (riflemen[i] == null || Time.time < nextShot[i]) continue;
                nextShot[i] = Time.time + Random.Range(2.5f, 6.5f);
                riflemen[i].ShotSound(riflemen[i].WeaponName ?? "Lee-Enfield");
            }
            LiveFire(liveFire);
        }

        // ------------------------------------------------------------------ live firing over the wire

        // Battle inoculation, as the airborne battalions trained it: a Bren fires on a fixed line over the
        // crawl, with tracer, and thunderflashes go off beside the wire. No round can hit anyone: the bullets
        // are drawn and heard only (the game's near-miss crack is raised for them so the player feels them).
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static readonly FieldInfo shotPassed = typeof(Weapon).GetField("ShotPassed", Any);
        static readonly MethodInfo shellFlash = typeof(Shell).GetMethod("Flash", Any);
        GameObject fireLine;
        int burstLeft;
        float nextRound, nextBurst, nextBang;
        bool wasFiring;
        static Material tracerMat;

        void LiveFire(bool on)
        {
            if (!on || BrenGunner == null)
            {
                wasFiring = false;
                return;
            }
            if (!wasFiring)
            {
                wasFiring = true;
                nextBurst = Time.time + 0.6f;
                nextBang = Time.time + Random.Range(2f, 4f);
                burstLeft = 0;
            }
            if (burstLeft == 0 && Time.time >= nextBurst) { burstLeft = Random.Range(4, 8); nextRound = Time.time; }
            if (burstLeft > 0 && Time.time >= nextRound)
            {
                burstLeft--;
                nextRound = Time.time + 0.115f;
                if (burstLeft == 0) nextBurst = Time.time + Random.Range(1.6f, 3.2f);
                Round(burstLeft % 2 == 0);
            }
            if (Time.time >= nextBang)
            {
                nextBang = Time.time + Random.Range(4f, 7f);
                Thunderflash();
            }
        }

        void Round(bool tracer)
        {
            Transform b = BrenGunner.transform;
            Vector3 muzzle = b.position + Vector3.up * 0.34f + b.forward * 1.0f;
            Vector3 aim = g.W(Random.Range(-2.5f, 2.5f), Random.Range(1.9f, 2.3f), TrainingGround.WireZ0 - 4f);
            Vector3 dir = (aim - muzzle).normalized;
            Vector3 end = muzzle + dir * 180f;
            RoundsFired++;
            Sfx.Shot(BrenGunner.WeaponName ?? "Bren", muzzle);
            if (tracer && !OtherAdditions.Tracer(muzzle, end, Faction.British)) TracerStreak.Fire(muzzle, dir, 180f, TracerMaterial());
            // The near-miss crack and suppression the game plays for enemy rounds passing close.
            if (fireLine == null)
            {
                fireLine = new GameObject("live firing (no damage)");
                fireLine.transform.SetParent(g.Root, false);
                fireLine.SetActive(false);
                Health h = fireLine.AddComponent<Health>();
                h.faction = Faction.German;
                h.enabled = false;
            }
            if (shotPassedBroken) return;
            try
            {
                Delegate d = shotPassed != null ? shotPassed.GetValue(null) as Delegate : null;
                if (d != null) d.DynamicInvoke(muzzle, end, fireLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Stan's Game: bootcamp live fire crack failed (" + e.Message + ")");
                shotPassedBroken = true;
            }
        }

        static bool shotPassedBroken;

        static Material TracerMaterial()
        {
            if (tracerMat == null)
            {
                Shader s = Shader.Find("Sprites/Default");
                if (s != null) tracerMat = new Material(s);
            }
            return tracerMat;
        }

        void Thunderflash()
        {
            float side = Random.value < 0.5f ? -1f : 1f;
            Vector3 p = g.W(side * Random.Range(8.6f, 10.5f), 0.05f, Random.Range(50f, 60f));
            Thunderflashes++;
            Sfx.Explosion(p, false);
            if (OtherAdditions.Explode(p, "Grenade")) return;   // the effects' own blast, when that addition is in
            try { if (shellFlash != null) shellFlash.Invoke(null, new object[] { p }); } catch (Exception) { }
            GameObject fx = MillsEffect();
            if (fx != null)
            {
                GameObject e = Object.Instantiate(fx, p, Quaternion.identity);
                foreach (MonoBehaviour mb in e.GetComponentsInChildren<MonoBehaviour>(true)) Object.Destroy(mb);   // the look only
                Object.Destroy(e, 6f);
            }
        }

        static GameObject millsFx;
        static GameObject MillsEffect()
        {
            if (millsFx != null) return millsFx;
            foreach (Shell s in Resources.FindObjectsOfTypeAll<Shell>())
                if (s.name == "Grenade_Mills" && s.explosionPrefab != null) { millsFx = s.explosionPrefab; break; }
            return millsFx;
        }
    }
}
