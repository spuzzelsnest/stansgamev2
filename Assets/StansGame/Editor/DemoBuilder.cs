using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Menu: Stan's Game > Build Windows Demo.
    /// Builds the Pegasus Bridge level as a stand-alone Windows game in Builds/StansGame_Demo/
    /// (run StansGame.exe), and writes a short README with the controls.
    /// </summary>
    public static class DemoBuilder
    {
        const string Scene = "Assets/StansGame/Scenes/PegasusBridge.unity";
        // further chapters, chosen from the start menu after Play alone / Host (NetSession.Maps)
        static readonly string[] MoreScenes = { "Assets/StansGame/Scenes/SainteMereEglise.unity", "Assets/StansGame/Scenes/ChateauBenouville.unity", "Assets/StansGame/Scenes/ProvingGround.unity" };

        [MenuItem("Stan's Game/Build Windows Demo", priority = 40)]
        public static void BuildDemo()
        {
            if (!File.Exists(Scene)) { EditorUtility.DisplayDialog("Stan's Game", "Build the level first (Stan's Game > Build Pegasus Bridge Level).", "OK"); return; }
            string project = Path.GetDirectoryName(Application.dataPath);
            if (System.Diagnostics.Process.GetProcessesByName("StansGame").Length > 0)
            {
                EditorUtility.DisplayDialog("Stan's Game", "StansGame.exe is still running - close the game first, then build again.", "OK");
                return;
            }
            string outDir = FreshFolder(Path.Combine(project, "Builds", "StansGame_Demo"));
            if (outDir == null) return;

            PlayerSettings.companyName = "Joan & Stan";
            PlayerSettings.productName = "Stan's Game - Pegasus Bridge 1944";
            PlayerSettings.bundleVersion = "0.1-demo";
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.resizableWindow = true;

            RuntimeMaterialAssets.Ensure();
            var scenes = new System.Collections.Generic.List<string> { Scene };
            foreach (var extra in MoreScenes) if (File.Exists(extra)) scenes.Add(extra);
            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = Path.Combine(outDir, "StansGame.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            if (s.result != BuildResult.Succeeded)
            {
                Debug.LogError($"Stan's Game: demo build {s.result} with {s.totalErrors} error(s). See the messages above.");
                return;
            }

            File.WriteAllText(Path.Combine(outDir, "README.txt"),
@"STAN'S GAME - Pegasus Bridge, 6 June 1944 (demo 0.1)
Made by Joan and Stan.

Run StansGame.exe. Press Esc in game for the menu, Alt+F4 to quit.

MULTIPLAYER (co-op: everyone is Ox & Bucks, the host's PC runs the Germans)
  Play alone           - just you. Then choose the mission: Pegasus Bridge or Sainte-Mere-Eglise.
  Home network         - one PC clicks 'Host a game'; the others type the host's IP
                         (shown on the host's menu, e.g. 192.168.1.20) and click Join.
                         The first time, allow StansGame through the Windows firewall
                         (Private networks) on the host PC.
  Over the internet    - the host clicks 'Host online' and reads out the join code;
                         friends type the code and click 'Join online'.
                         (Needs the game linked to a Unity Cloud project - ask Joan.)
  Killed? You are back in the fight after 8 seconds.

00:16 - three Horsa gliders of the Ox & Bucks land beside the Benouville bridge.
Take the bridge, hold it, and stop the German counter-attack at 02:00
(knock out the lead tank with the PIAT, just like Sgt Thornton did).

CONTROLS
  WASD          move           Shift   sprint
  Mouse         look           C       crouch
  Right mouse   aim            Space   jump
  Left mouse    fire           R       reload
  1 / 2         main weapon / pistol
  3             grenades (left mouse throws)
  4             field dressing
  5             smoke grenade
  V             first / third person
  E             get in / out of a tank

Ground: IGN LiDAR HD (Etalab Open Licence 2.0). Model credits: see CREDITS.txt in the project.
");
            Debug.Log($"Stan's Game: demo built in {s.totalTime.TotalSeconds:F0} s, {s.totalSize / (1024f * 1024f):F0} MB -> {outDir}");
            // (open the Builds folder in Explorer to find StansGame.exe)
        }

        /// <summary>
        /// An empty folder to build into. The old build is deleted first; if Windows won't let go of it (OneDrive
        /// still syncing it, or an Explorer window on it) it is renamed out of the way, and if even that fails the
        /// demo is built into a new folder next to it (Zip Demo for Sharing always takes the newest complete build).
        /// </summary>
        static string FreshFolder(string dir)
        {
            if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); return dir; }
            try { Directory.Delete(dir, true); Directory.CreateDirectory(dir); return dir; }
            catch (System.Exception first)
            {
                Debug.LogWarning($"Stan's Game: couldn't clear the old build ({first.Message}) - trying around it");
                try
                {
                    foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                    Directory.Delete(dir, true); Directory.CreateDirectory(dir); return dir;
                }
                catch { }
                try
                {
                    string old = dir + "_old_" + System.DateTime.Now.ToString("HHmmss");
                    Directory.Move(dir, old);
                    Debug.Log($"Stan's Game: the old build was moved to {old} (delete it when Windows lets go of it)");
                    Directory.CreateDirectory(dir); return dir;
                }
                catch { }
                string fresh = dir + "_" + System.DateTime.Now.ToString("yyyy-MM-dd_HHmm");
                Debug.LogWarning($"Stan's Game: building into {fresh} instead");
                Directory.CreateDirectory(fresh); return fresh;
            }
        }

        /// <summary>The newest complete demo build (a folder Builds/StansGame_Demo* with StansGame.exe in it), or null.</summary>
        public static string LatestBuild()
        {
            string builds = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Builds");
            if (!Directory.Exists(builds)) return null;
            string best = null; System.DateTime when = System.DateTime.MinValue;
            foreach (var d in Directory.GetDirectories(builds, "StansGame_Demo*"))
            {
                string exe = Path.Combine(d, "StansGame.exe");
                if (!File.Exists(exe) || !File.Exists(Path.Combine(d, "UnityPlayer.dll"))) continue;
                var t = File.GetLastWriteTime(exe);
                if (t > when) { when = t; best = d; }
            }
            return best;
        }

        /// <summary>Starts the built demo in a window, e.g. as a second player to test multiplayer against the editor.</summary>
        [MenuItem("Stan's Game/Run Built Demo (windowed)", priority = 41)]
        public static void RunDemo()
        {
            string latest = LatestBuild();
            string exe = latest != null ? Path.Combine(latest, "StansGame.exe") : "";
            if (!File.Exists(exe)) { EditorUtility.DisplayDialog("Stan's Game", "Build the Windows demo first.", "OK"); return; }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "-screen-fullscreen 0 -screen-width 1280 -screen-height 720")
            { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
        }
    }
}
