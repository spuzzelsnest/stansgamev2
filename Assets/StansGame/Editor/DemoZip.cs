using System.IO;
using System.IO.Compression;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Menu: Stan's Game > Zip Demo for Sharing. Packs Builds/StansGame_Demo into Builds/StansGame_Demo_YYYY-MM-DD.zip
    /// (leaving out Unity's "DontShip" debug-symbol folder and the log files) and shows it in Explorer, ready to drag into
    /// Google Drive. It refuses to zip a folder whose build did not finish (no StansGame.exe), and says which files it
    /// could not read (a running copy of the game keeps some locked).
    /// </summary>
    public static class DemoZip
    {
        static bool busy;

        [MenuItem("Stan's Game/Zip Demo for Sharing", priority = 42)]
        static void Zip()
        {
            if (busy) { Debug.LogWarning("Stan's Game: already zipping"); return; }
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            // the newest complete build (a build that failed or was cut short has no StansGame.exe: never zip that)
            string src = DemoBuilder.LatestBuild();
            if (src == null)
            {
                EditorUtility.DisplayDialog("Stan's Game", "The demo folder has no StansGame.exe in it - the last build did not finish.\n\nRun Stan's Game > Build Windows Demo again (and check the Console for build errors), then zip.", "OK");
                return;
            }
            string dst = Path.Combine(root, "Builds", $"StansGame_Demo_{System.DateTime.Now:yyyy-MM-dd}.zip");
            busy = true;
            Debug.Log("Stan's Game: zipping the demo... (a minute or two)");
            new Thread(() =>
            {
                try
                {
                    if (File.Exists(dst)) File.Delete(dst);
                    int added = 0, skipped = 0;
                    using (var zip = ZipFile.Open(dst, ZipArchiveMode.Create))
                        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                        {
                            string rel = f.Substring(src.Length + 1).Replace('\\', '/');
                            // Unity's debug symbols ("..._BackUpThisFolder_ButDontShipItWithYourGame") and the game's own log files stay out
                            if (rel.Contains("DoNotShip") || rel.Contains("DontShip") || rel.StartsWith("Logs/")) continue;
                            try { zip.CreateEntryFromFile(f, "StansGame_Demo/" + rel, System.IO.Compression.CompressionLevel.Optimal); added++; }
                            catch (IOException e) { skipped++; Debug.LogWarning($"Stan's Game: left out {rel} ({e.Message}) - is the game still running?"); }
                        }
                    if (added == 0) { File.Delete(dst); Debug.LogError("Stan's Game: nothing to zip - build the demo again"); return; }
                    long mb = new FileInfo(dst).Length / (1024 * 1024);
                    if (skipped > 0) Debug.LogWarning($"Stan's Game: {skipped} file(s) could not be read and are missing from the zip - close the game and zip again.");
                    Debug.Log($"Stan's Game: demo zipped: {dst} ({mb} MB). Drag it into drive.google.com and share the link with Stan.");
                    EditorApplication.delayCall += () => EditorUtility.RevealInFinder(dst);
                }
                catch (System.Exception e) { Debug.LogError("Stan's Game: zip failed: " + e.Message); }
                finally { busy = false; }
            }) { IsBackground = true }.Start();
        }
    }
}
