using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Menu: Stan's Game > Git > Commit & Push to GitHub.
    /// Commits the project (Assets, Packages, ProjectSettings, .gitignore — never _downloads, Library or Builds)
    /// on the "main" branch of origin and pushes it. The first time, it starts "main" from GitHub's main without
    /// touching any file on disk, and keeps the old local history on a "backup-master" branch.
    /// Everything git prints goes to the Console and to _downloads/git_log.txt.
    /// </summary>
    public static class GitPush
    {
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        static string LogPath => Path.Combine(Root, "_downloads", "git_log.txt");
        static bool running;

        [MenuItem("Stan's Game/Git/Commit && Push to GitHub", priority = 60)]
        static void CommitAndPush()
        {
            if (running) { Debug.LogWarning("Stan's Game: git is still busy"); return; }
            CommitMessageWindow.Ask(Push);
        }

        /// <summary>A small window asking what changed; the commit message is "Stan's Game V2 (date): what you typed".</summary>
        class CommitMessageWindow : EditorWindow
        {
            string text = ""; System.Action<string> done; bool focused;
            public static void Ask(System.Action<string> then)
            {
                var w = CreateInstance<CommitMessageWindow>();
                w.titleContent = new GUIContent("Commit & push"); w.done = then;
                w.minSize = w.maxSize = new Vector2(460f, 150f);
                w.ShowUtility();
            }
            void OnGUI()
            {
                EditorGUILayout.LabelField("What changed? (one line, e.g. \"Proving Ground hills and Mixamo animations\")", EditorStyles.wordWrappedLabel);
                GUI.SetNextControlName("msg");
                text = EditorGUILayout.TextField(text);
                if (!focused) { EditorGUI.FocusTextInControl("msg"); focused = true; }
                GUILayout.FlexibleSpace();
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Cancel", GUILayout.Width(90f))) Close();
                    GUI.enabled = text.Trim().Length > 0;
                    if (GUILayout.Button("Commit & push", GUILayout.Width(120f)) || (GUI.enabled && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return))
                    { var t = text.Trim(); Close(); done?.Invoke(t); }
                    GUI.enabled = true;
                }
            }
        }

        static void Push(string what)
        {
            AssetDatabase.SaveAssets();
            string msg = $"Stan's Game V2 ({System.DateTime.Now:yyyy-MM-dd}): {what}\n\n" +
                         "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>\n" +
                         "Claude-Session: https://claude.ai/code/session_011ANkRibquZj7zCzW4NoPAP\n";
            string msgFile = Path.Combine(Root, "Temp", "git_commit_msg.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(msgFile));
            File.WriteAllText(msgFile, msg);
            running = true;
            new Thread(() => Run(msgFile)) { IsBackground = true }.Start();
        }

        static void Run(string msgFile)
        {
            var log = new StringBuilder();
            void Say(string s) { log.AppendLine(s); Debug.Log("Stan's Game git: " + s); try { File.WriteAllText(LogPath, log.ToString()); } catch { } }
            try
            {
                if (Git("--version", log, Say) != 0) { Say("git is not installed or not on the PATH"); return; }
                Git("fetch origin", log, Say);
                string branch = Out("rev-parse --abbrev-ref HEAD").Trim();
                if (branch != "main")
                {
                    // keep the old local history, then move onto GitHub's main without touching the files on disk
                    Git("branch -f backup-master HEAD", log, Say);
                    Git("symbolic-ref HEAD refs/heads/main", log, Say);
                    bool remoteMain = Git("rev-parse --verify origin/main", log, Say) == 0;
                    if (remoteMain) Git("reset --mixed origin/main", log, Say);
                    else Git("rm -r -q --cached .", log, Say);
                }
                Git("add -A -- Assets Packages ProjectSettings .gitignore", log, Say);
                Git("status --short --branch", log, Say, 40);
                if (Git($"commit -q -F \"{msgFile}\"", log, Say) != 0) Say("(nothing new to commit)");
                Say("pushing... (a GitHub sign-in window may open the first time)");
                int code = Git("push -u origin main", log, Say);
                Say(code == 0 ? "DONE: pushed to https://github.com/spuzzelsnest/stansgamev2" : $"push failed (exit {code})");
            }
            catch (System.Exception e) { Say("error: " + e.Message); }
            finally { running = false; }
        }

        static string Out(string args)
        {
            var p = Start(args);
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return o;
        }

        static Process Start(string args)
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            return Process.Start(psi);
        }

        static int Git(string args, StringBuilder log, System.Action<string> say, int maxLines = 15)
        {
            say("> git " + args);
            var p = Start(args);
            var err = p.StandardError.ReadToEndAsync();
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            int n = 0;
            foreach (var line in (o + err.Result).Split('\n'))
                if (line.Trim().Length > 0 && n++ < maxLines) say("  " + line.TrimEnd());
            return p.ExitCode;
        }
    }
}
