using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Menu: Stan's Game > Build Animation Sets.
    /// Two animators for everyone, from Joan's Mixamo clips (Animations/Mixamo, made by Import Mixamo Animations):
    /// <list type="bullet">
    /// <item>Animations/Soldier.controller - every soldier, player and AI: walk, run, strafe, walk backwards, aim,
    /// crouch, crawl, swim, and the actions in AnimActions (rest, smoke, salute, look around, talk, defend behind cover,
    /// lie down, throw, reload, fire). Soldier_German / Soldier_Allied override files give each side its own idle and
    /// talk.</item>
    /// <item>Animations/Civilian.controller - townspeople: stand, walk, run, and the civilian actions (toast, chat,
    /// sitting, digging...).</item>
    /// </list>
    /// Both are rebuilt inside their existing files, so every prefab and level that uses them picks them up without a
    /// rebuild. The old Hero and generated clips stand in for anything not downloaded. Report: Logs/animation-sets.txt.
    /// </summary>
    public static class AnimationSets
    {
        const string Anim = "Assets/StansGame/Animations";
        static readonly StringBuilder report = new StringBuilder();

        [MenuItem("Stan's Game/Build Animation Sets", priority = 25)]
        public static void Build()
        {
            report.Clear();
            report.AppendLine($"Animation sets - {System.DateTime.Now}\n");
            var soldier = BuildSoldier();
            BuildOverride("Soldier_German", soldier, ("Talking", "Talking (2)"), ("Smoking", "Smoking"));
            BuildOverride("Soldier_Allied", soldier);
            BuildCivilian();
            AssetDatabase.SaveAssets();
            string logs = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs");
            File.WriteAllText(Path.Combine(logs, "animation-sets.txt"), report.ToString());
            Debug.Log("Stan's Game: animation sets built - Soldier, Soldier_German, Soldier_Allied, Civilian (Logs/animation-sets.txt)");
        }

        /// <summary>The soldier set alone (with both sides' overrides), for Import Characters &amp; Weapons: rebuilt in
        /// its own file, so the prefabs keep pointing at it.</summary>
        public static RuntimeAnimatorController SoldierController()
        {
            report.Clear();
            var ac = BuildSoldier();
            BuildOverride("Soldier_German", ac, ("Talking", "Talking (2)"), ("Smoking", "Smoking"));
            BuildOverride("Soldier_Allied", ac);
            AssetDatabase.SaveAssets();
            return ac;
        }

        // ------------------------------------------------------------------ clips

        static AnimationClip M(string folder, string name)
        {
            var c = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Anim}/Mixamo/{folder}/{name}.anim");
            if (c) return c;
            foreach (var f in new[] { "Soldier", "Civilian" })
            {
                c = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Anim}/Mixamo/{f}/{name}.anim");
                if (c) return c;
            }
            return null;
        }
        static AnimationClip Hero(string n)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath($"{Anim}/Hero/Hero@{n}.fbx"))
                if (o is AnimationClip c && !c.name.StartsWith("__preview")) return c;
            return null;
        }
        static AnimationClip Gen(string n) => AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Anim}/Generated/{n}.anim");
        static AnimationClip Pick(string role, params AnimationClip[] options)
        {
            foreach (var c in options) if (c) { report.AppendLine($"  {role,-22} {c.name}{(c.isHumanMotion ? $"  ({c.averageSpeed.magnitude:0.00} m/s)" : "")}"); return c; }
            report.AppendLine($"  {role,-22} MISSING");
            return null;
        }

        // ------------------------------------------------------------------ controller in place

        /// <summary>The controller at path, emptied (same file, same GUID: everything that points at it still does).</summary>
        static AnimatorController Fresh(string path)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (!ac) return AnimatorController.CreateAnimatorControllerAtPath(path);
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o && o != ac && !(o is AnimatorController)) Object.DestroyImmediate(o, true);
            ac.parameters = new AnimatorControllerParameter[0];
            ac.layers = new AnimatorControllerLayer[0];
            ac.AddLayer("Base Layer");
            return ac;
        }

        static void Params(AnimatorController ac)
        {
            foreach (var p in new[] { "Speed", "MoveX", "MoveZ" }) ac.AddParameter(p, AnimatorControllerParameterType.Float);
            foreach (var p in new[] { "Aiming", "Crouch", "Prone", "Swim", "Grounded", "Injured" }) ac.AddParameter(p, AnimatorControllerParameterType.Bool);
            ac.AddParameter("Action", AnimatorControllerParameterType.Int);
        }

        /// <summary>A 2D move tree: still in the middle, walk and run ahead, behind and to each side (left = the right clip mirrored).</summary>
        static AnimatorState MoveState(AnimatorController ac, string name, AnimationClip still, AnimationClip walk, AnimationClip run,
                                       AnimationClip back, AnimationClip backRun, AnimationClip side, AnimationClip sideRun, float walkAt, float runAt)
        {
            var st = ac.CreateBlendTreeInController(name, out BlendTree bt);
            bt.blendType = BlendTreeType.FreeformDirectional2D; bt.blendParameter = "MoveX"; bt.blendParameterY = "MoveZ";
            var kids = new List<ChildMotion>();
            void Add(AnimationClip c, float x, float z, bool mirror = false, float speed = 1f)
            { if (c) kids.Add(new ChildMotion { motion = c, position = new Vector2(x, z), timeScale = speed, mirror = mirror }); }
            Add(still, 0f, 0f);
            Add(walk, 0f, walkAt);
            Add(run, 0f, runAt);
            if (back) Add(back, 0f, -walkAt * 0.9f); else Add(walk, 0f, -walkAt * 0.9f, false, -1f);
            Add(backRun, 0f, -runAt * 0.85f);
            if (side) { Add(side, walkAt * 0.9f, 0f); Add(side, -walkAt * 0.9f, 0f, true); }
            if (sideRun) { Add(sideRun, runAt * 0.85f, 0f); Add(sideRun, -runAt * 0.85f, 0f, true); }
            bt.children = kids.ToArray();
            return st;
        }

        static void Any(AnimatorStateMachine sm, AnimatorState to, float fade, params (AnimatorConditionMode m, float v, string p)[] conds)
        {
            var t = sm.AddAnyStateTransition(to);
            t.canTransitionToSelf = false; t.hasExitTime = false; t.duration = fade;
            foreach (var c in conds) t.AddCondition(c.m, c.v, c.p);
        }

        /// <summary>The actions: from anywhere when Action = n; loops go back when Action is 0 again, one-shots when done.</summary>
        static void Actions(AnimatorController ac, AnimatorState home, params (int id, string name, AnimationClip clip)[] list)
        {
            var sm = ac.layers[0].stateMachine;
            foreach (var a in list)
            {
                if (!a.clip) continue;
                var st = sm.AddState(a.name); st.motion = a.clip;
                Any(sm, st, 0.3f, (AnimatorConditionMode.Equals, a.id, "Action"));
                var back = st.AddTransition(home);
                back.duration = 0.35f;
                if (StansGame.AnimActions.OneShot(a.id)) { back.hasExitTime = true; back.exitTime = 0.92f; }
                else { back.hasExitTime = false; back.AddCondition(AnimatorConditionMode.Equals, 0, "Action"); }
            }
        }

        // ------------------------------------------------------------------ soldier

        static AnimatorController BuildSoldier()
        {
            report.AppendLine("Soldier.controller");
            var ac = Fresh($"{Anim}/Soldier.controller");
            Params(ac);
            var sm = ac.layers[0].stateMachine;
            const string S = "Soldier";
            // standing with a gun: the old square-on "AimStraight" pose, which WeaponHolder and HandIK were fitted to. Mixamo's
            // rifle idles turn the chest away, so the gun pointed off to the side (printscreen 21)
            var idle = Pick("idle", Hero("AimStraight"), M(S, "Rifle Idle"), Hero("Idle"));
            var aimIdle = Pick("aim", Hero("AimStraight"), M(S, "Rifle Aiming Idle"));
            var walk = Pick("walk", M(S, "Rifle Walk"), Hero("WalkForward"));
            var run = Pick("run", M(S, "Run Forward"), Hero("RunForward"));
            var back = Pick("walk backwards", M(S, "Stepping Backward"), Hero("WalkBackward"));
            var backRun = Pick("run backwards", M(S, "Sprint Backward"), Hero("RunBackward"));
            var side = Pick("side step", M(S, "Walk Right"), Hero("WalkStrafeRight"));
            var sideRun = Pick("strafe run", M(S, "Strafe Right"), Hero("RunStrafeRight"));
            var loco = MoveState(ac, "Locomotion", idle, walk, run, back, backRun, side, sideRun, 2.2f, 4.2f);
            var aim = MoveState(ac, "Aim", aimIdle, walk, run, back, backRun, side, sideRun, 2.2f, 4.2f);
            sm.defaultState = loco;
            var crouchIdle = Pick("crouch", M(S, "Idle Crouching Aiming"), Gen("CrouchIdle"));
            var crouchWalk = Pick("crouch walk", M(S, "Rifle Crouch Walk"), M(S, "Crouch Walking"), Gen("CrouchWalk"));
            var crouchBack = Pick("crouch backwards", M(S, "Walk Crouching Backward Left"));
            var crouch = MoveState(ac, "Crouch", crouchIdle, crouchWalk, Pick("crouch run", M(S, "Crouch Running")), crouchBack, null, null, null, 1.4f, 2.6f);
            var proneIdle = Pick("prone", M(S, "Prone Idle"), Gen("ProneIdle"));
            var crawl = Pick("crawl", M(S, "Prone Forward"), Gen("ProneCrawl"));
            var crawlBack = Pick("crawl backwards", M(S, "Crawl Backwards In Prone"), M(S, "Moving Backward In Crawl Position"));
            var prone = MoveState(ac, "Prone", proneIdle, crawl, null, crawlBack, null, null, null, 0.7f, 1.2f);
            var still0 = (AnimatorConditionMode.Equals, 0f, "Action");
            Any(sm, prone, 0.45f, (AnimatorConditionMode.If, 0f, "Prone"), (AnimatorConditionMode.IfNot, 0f, "Swim"), still0);
            Any(sm, crouch, 0.25f, (AnimatorConditionMode.If, 0f, "Crouch"), (AnimatorConditionMode.IfNot, 0f, "Prone"), (AnimatorConditionMode.IfNot, 0f, "Swim"), still0);
            Any(sm, aim, 0.2f, (AnimatorConditionMode.If, 0f, "Aiming"), (AnimatorConditionMode.IfNot, 0f, "Injured"), (AnimatorConditionMode.IfNot, 0f, "Crouch"), (AnimatorConditionMode.IfNot, 0f, "Prone"), (AnimatorConditionMode.IfNot, 0f, "Swim"), still0);
            Any(sm, loco, 0.25f, (AnimatorConditionMode.IfNot, 0f, "Aiming"), (AnimatorConditionMode.IfNot, 0f, "Injured"), (AnimatorConditionMode.IfNot, 0f, "Crouch"), (AnimatorConditionMode.IfNot, 0f, "Prone"), (AnimatorConditionMode.IfNot, 0f, "Swim"), still0);
            // badly hurt (Health sets "Injured" under 40%): the limping walk and run, clutching the wound when still
            const string I = "Male Injured Pack";
            var hurtIdle = Pick("injured still", M(I, "injured idle"));
            if (hurtIdle)
            {
                var hurt = MoveState(ac, "Injured", hurtIdle, Pick("injured walk", M(I, "injured walk")), Pick("injured run", M(I, "injured run")),
                                     Pick("injured backwards", M(I, "injured walk backwards")), Pick("injured run backwards", M(I, "injured run backwards")), null, null, 1.2f, 2.6f);
                Any(sm, hurt, 0.4f, (AnimatorConditionMode.If, 0f, "Injured"), (AnimatorConditionMode.IfNot, 0f, "Crouch"), (AnimatorConditionMode.IfNot, 0f, "Prone"), (AnimatorConditionMode.IfNot, 0f, "Swim"), still0);
            }
            var tread = Pick("tread water", M(S, "Treading Water"), Gen("TreadWater")); var swimCrawl = Pick("swim", M(S, "Swimming"), Gen("SwimCrawl"));
            if (tread && swimCrawl)
            {
                var swim = ac.CreateBlendTreeInController("Swim", out BlendTree st);
                st.blendType = BlendTreeType.Simple1D; st.blendParameter = "Speed";
                st.AddChild(tread, 0f); st.AddChild(swimCrawl, 1.0f);
                Any(sm, swim, 0.4f, (AnimatorConditionMode.If, 0f, "Swim"));
            }
            report.AppendLine(" actions:");
            Actions(ac, loco,
                (StansGame.AnimActions.Rest, "Rest (sitting)", Pick("rest", M(S, "Sitting Dazed"), M("Civilian", "Sitting Talking"))),
                (StansGame.AnimActions.Smoke, "Smoke", Pick("smoke", M(S, "Smoking"))),
                (StansGame.AnimActions.Salute, "Salute", Pick("salute", M(S, "Salute"))),
                (StansGame.AnimActions.LookAround, "Look around", Pick("look around", M(S, "Looking Around"))),
                (StansGame.AnimActions.Talk, "Talk", Pick("talk", M("Civilian", "Talking"))),
                (StansGame.AnimActions.Defend, "Defend (at a corner)", Pick("defend", M(S, "Crouch Look Around Corner"))),
                (StansGame.AnimActions.LieDown, "Lie down", Pick("lie down", M(S, "Laying Idle"))),
                (StansGame.AnimActions.Throw, "Throw", Pick("throw", M(S, "Grenade Throw"))),
                (StansGame.AnimActions.Reload, "Reload", Pick("reload", M(S, "Reloading"), M(S, "Reload"))),
                (StansGame.AnimActions.Fire, "Fire", Pick("fire", M(S, "Firing Rifle"), M(S, "Shoot Rifle"))),
                (StansGame.AnimActions.KneelFire, "Kneel and fire", Pick("kneel and fire", M(S, "Crouch Rapid Fire"))),
                (StansGame.AnimActions.Kneel, "Kneel (on guard)", Pick("kneel", M(S, "Rifle Kneel Idle"))),
                (StansGame.AnimActions.Hurting, "Hurting", Pick("hurting", M(I, "injured hurting idle"))),
                (StansGame.AnimActions.Stumble, "Stumble", Pick("stumble", M(I, "injured stumble idle"))),
                (StansGame.AnimActions.WaveHello, "Wave", Pick("wave (soldier)", M("Civilian", "Waving"))),
                (StansGame.AnimActions.Driving, "Driving", Pick("driving", M("Civilian", "Driving"))),
                (StansGame.AnimActions.Seated, "Seated (riding)", Pick("seated", M("Civilian", "Sitting Talking"), M(S, "Sitting Dazed"))));
            var layers = ac.layers; layers[0].iKPass = true; ac.layers = layers;   // HandIK: both hands on the weapon
            EditorUtility.SetDirty(ac);
            report.AppendLine();
            return ac;
        }

        static void BuildOverride(string name, AnimatorController baseCtrl, params (string from, string to)[] swaps)
        {
            string path = $"{Anim}/{name}.overrideController";
            var oc = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
            if (!oc) { oc = new AnimatorOverrideController(baseCtrl); AssetDatabase.CreateAsset(oc, path); }
            oc.runtimeAnimatorController = baseCtrl;
            var list = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            oc.GetOverrides(list);
            for (int i = 0; i < list.Count; i++)
            {
                AnimationClip to = null;
                foreach (var s in swaps) if (list[i].Key && list[i].Key.name == s.from) to = M("Soldier", s.to);
                list[i] = new KeyValuePair<AnimationClip, AnimationClip>(list[i].Key, to);
            }
            oc.ApplyOverrides(list);
            EditorUtility.SetDirty(oc);
            report.AppendLine($"{name}.overrideController: {(swaps.Length == 0 ? "as the base" : string.Join(", ", System.Array.ConvertAll(swaps, s => $"{s.from} -> {s.to}")))}");
        }

        // ------------------------------------------------------------------ civilian

        static void BuildCivilian()
        {
            report.AppendLine("\nCivilian.controller");
            var ac = Fresh($"{Anim}/Civilian.controller");
            Params(ac);
            var sm = ac.layers[0].stateMachine;
            const string C = "Civilian", F = "Farming Pack";
            var still = Pick("stand", M(C, "Standing Idle"), M(C, "Idle"), Hero("Idle"));
            var walk = Pick("walk", M(C, "Walk"), Hero("WalkForward"));
            var run = Pick("run", M(C, "Running"), Hero("RunForward"));
            var st = ac.CreateBlendTreeInController("Locomotion", out BlendTree bt);
            bt.blendType = BlendTreeType.Simple1D; bt.blendParameter = "Speed"; bt.useAutomaticThresholds = false;
            if (still) bt.AddChild(still, 0f);
            if (walk) bt.AddChild(walk, Locomotion.Walk);
            if (run) bt.AddChild(run, Locomotion.Run);
            sm.defaultState = st;
            report.AppendLine(" actions:");
            Actions(ac, st,
                (StansGame.AnimActions.Toast, "Toast", Pick("toast", M(C, "Toast"), M(C, "Drinking"))),
                (StansGame.AnimActions.Chat, "Chat", Pick("chat", M(C, "Talking"))),
                (StansGame.AnimActions.Chat2, "Chat 2", Pick("chat 2", M(C, "Talking (1)"))),
                (StansGame.AnimActions.SitTalk, "Sit and talk", Pick("sit and talk", M(C, "Sitting Talking"))),
                (StansGame.AnimActions.SitDrink, "Sit and drink", Pick("sit and drink", M(C, "Sitting Drinking"))),
                (StansGame.AnimActions.Meeting, "Meeting", Pick("meeting", M(C, "Having A Meeting, Male"))),
                (StansGame.AnimActions.Dig, "Dig", Pick("dig", M(F, "dig and plant seeds"), M(C, "Dig And Plant Seeds"))),
                (StansGame.AnimActions.Watercooler, "Gossip", Pick("gossip", M(C, "Talking At Watercooler"))),
                (StansGame.AnimActions.Wave, "Wave", Pick("wave", M(C, "Waving"))),
                (StansGame.AnimActions.Wave2, "Wave 2", Pick("wave 2", M(C, "Waving01"))),
                (StansGame.AnimActions.Wave3, "Wave 3", Pick("wave 3", M(C, "Waving03"))),
                (StansGame.AnimActions.Watering, "Watering", Pick("watering", M(C, "Watering"), M(F, "watering"))),
                (StansGame.AnimActions.PickFruit, "Pick fruit", Pick("pick fruit", M(F, "pick fruit"))),
                (StansGame.AnimActions.PullWeeds, "Pull weeds", Pick("pull weeds", M(F, "pull plant"))),
                (StansGame.AnimActions.Planting, "Planting", Pick("planting", M(F, "plant a plant"))),
                (StansGame.AnimActions.Gardening, "Gardening (kneeling)", Pick("gardening", M(F, "kneeling idle"))));
            var layers = ac.layers; layers[0].iKPass = true; ac.layers = layers;
            EditorUtility.SetDirty(ac);
        }
    }
}
