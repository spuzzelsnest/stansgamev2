using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Proving Ground: each side's section on parade in its own lines, marching round the camp path at sling arms in
    /// a column of threes with the leader out in front - the opening of "Battleground" (1949). Each one is a real
    /// 1944 section with its own weapons:
    ///   British (6th Airborne lines): an officer, the section commander with his Sten, the Bren gunner and ten riflemen
    ///     with the Lee-Enfield (a rifle section was ten: a Sten, a Bren and its No. 2, riflemen).
    ///   American (505th PIR lines): a sergeant with the Thompson, two with the M1 carbine, ten with the M1 Garand
    ///     (a rifle squad was twelve; the BAR isn't in the game yet).
    ///   German (camp east): the Gruppenführer with his MP 40, the MG 42 gunner and nine riflemen with the Kar 98k
    ///     (a Gruppe was ten: leader, MG team, riflemen) - Heer and Panzergrenadier uniforms mixed.
    /// ParadeSquad (Scripts/AI) does the marching; WeaponHolder.slingArms hangs each gun on its sling (WeaponSling).
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        static void BuildParades(Transform actors, List<Bounds> keepOut)
        {
            var g = Group("On parade (each side's section)", actors);
            var british = new List<string> { "BritishParaOfficer", "BritishSoldier", "BritishBrenGunner" };
            for (int i = 0; i < 10; i++) british.Add("BritishRifleman");
            var american = new List<string> { "US_Paratrooper_Thompson", "US_Paratrooper_Thompson", "US_Paratrooper_Carbine", "US_Paratrooper_Carbine" };
            for (int i = 0; i < 9; i++) american.Add("US_Paratrooper_Rifleman");
            var german = new List<string> { "German_SMG_Heer_A", "German_MG_Heer_A", "German_Rifleman_Panzergren" };
            string[] gr = { "German_Rifleman_Heer_A", "German_Rifleman_Heer_B", "German_Rifleman_Heer_C", "German_Rifleman_Panzergren" };
            for (int i = 0; i < 10; i++) german.Add(gr[i % gr.Length]);
            Parade(g, "No. 1 Section, 6th Airborne (British lines)", british, -57f, -8f, 98f, false, keepOut);
            Parade(g, "1st Squad, 505th P.I.R. (American lines)", american, -105f, -8f, 96f, false, keepOut);
            Parade(g, "1. Gruppe (German camp)", german, 56f, -8f, 95f, true, keepOut);
        }

        /// <summary>A loop up one side of the camp path and down the other, wheeling round at the ends (radius r).</summary>
        static Vector3[] Stadium(float cx, float z0, float z1, float r)
        {
            var pts = new List<Vector3>();
            pts.Add(new Vector3(cx + r, 0f, z0)); pts.Add(new Vector3(cx + r, 0f, z1));
            for (int i = 1; i < 8; i++) { float a = Mathf.PI * i / 8f; pts.Add(new Vector3(cx + r * Mathf.Cos(a), 0f, z1 + r * Mathf.Sin(a))); }
            pts.Add(new Vector3(cx - r, 0f, z1)); pts.Add(new Vector3(cx - r, 0f, z0));
            for (int i = 1; i < 8; i++) { float a = Mathf.PI + Mathf.PI * i / 8f; pts.Add(new Vector3(cx + r * Mathf.Cos(a), 0f, z0 + r * Mathf.Sin(a))); }
            return pts.ToArray();
        }

        static void Parade(Transform parent, string name, List<string> who, float cx, float z0, float z1, bool german, List<Bounds> keepOut)
        {
            var g = Group(name, parent);
            const float r = 1.7f;
            var sq = g.gameObject.AddComponent<ParadeSquad>();
            sq.route = Stadium(cx, z0, z1, r);
            sq.haltEvery = 75f; sq.haltFor = 8f;
            var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Root + (german ? "/Animations/Soldier_German.overrideController" : "/Animations/Soldier_Allied.overrideController"));
            var men = new List<Transform>();
            int ranks = Mathf.CeilToInt((who.Count - 1f) / sq.files);
            float s0 = sq.leaderLead + ranks * sq.rankGap;
            for (int i = 0; i < who.Count; i++)
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{who[i]}.prefab");
                if (!pf) { Debug.LogWarning($"Stan's Game: parade - no prefab {who[i]}"); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, g);
                go.name = i == 0 ? $"{who[i]} (leading)" : $"{who[i]} (rank {(i - 1) / sq.files + 1})";
                float d, side;
                if (i == 0) { d = s0; side = -sq.fileGap; }
                else { int k = i - 1; d = s0 - sq.leaderLead - (k / sq.files) * sq.rankGap; side = (k % sq.files - (sq.files - 1) * 0.5f) * sq.fileGap; }
                go.transform.SetPositionAndRotation(new Vector3(cx + r + side, 0f, z0 + d), Quaternion.identity);   // up the east side of the loop, facing north
                foreach (var ai in go.GetComponentsInChildren<SoldierAI>(true)) ai.enabled = false;
                foreach (var h in go.GetComponentsInChildren<WeaponHolder>(true)) h.slingArms = true;
                var anim = go.GetComponentInChildren<Animator>(true);
                if (anim) { if (ctrl) anim.runtimeAnimatorController = ctrl; anim.applyRootMotion = false; }
                men.Add(go.transform);
            }
            sq.men = men.ToArray();
            // the strollers keep off the parade ground
            keepOut.Add(new Bounds(new Vector3(cx, 1f, (z0 + z1) * 0.5f), new Vector3(2f * r + 3.2f, 4f, z1 - z0 + 2f * r + 3f)));
        }
    }
}
