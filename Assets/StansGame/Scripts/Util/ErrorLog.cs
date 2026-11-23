using System.IO;
using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Errors and exceptions (and our own "Stan's Game" warnings) are also written to Logs/errors.txt - next to
    /// StansGame.exe in the built game, in the project's Logs folder in the Editor - so a bug seen while playing can
    /// be read back and fixed without a screenshot of the Console.
    /// Each run starts a new section; the file is trimmed when it gets big.
    /// </summary>
    public static class ErrorLog
    {
        static string path;
        static int written;
        static string last; static int repeats;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            try
            {
                string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs");
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, "errors.txt");
                if (File.Exists(path) && new FileInfo(path).Length > 2_000_000) File.Delete(path);
                File.AppendAllText(path, $"\n===== {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}  {Application.productName} {Application.version} =====\n");
                Application.logMessageReceivedThreaded -= OnLog;   // play mode without a domain reload: not twice
                Application.logMessageReceivedThreaded += OnLog;
            }
            catch { path = null; }
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            if (path == null || written > 2000) return;
            bool ours = type == LogType.Warning && msg.StartsWith("Stan's Game");
            if (type == LogType.Log || (type == LogType.Warning && !ours)) return;
            lock (typeof(ErrorLog))
            {
                if (msg == last) { if (++repeats == 20) Write($"  (the same message keeps repeating)\n"); return; }
                last = msg; repeats = 0;
                Write($"[{System.DateTime.Now:HH:mm:ss}] {type}: {msg}\n{(type == LogType.Warning ? "" : stack)}\n");
            }
        }

        static void Write(string s)
        {
            try { File.AppendAllText(path, s); written++; } catch { }
        }
    }
}
