using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace StansGame.EditorTools
{
    /// <summary>
    /// The narrator of "The History" film. Writes the narration script (History/PB/Voice/narration-script.txt, one numbered line a shot)
    /// and has Windows read it with its own British voice (System.Speech: Hazel if installed, else another English female voice) into
    /// 01.wav, 02.wav ... To use a better voice (ElevenLabs, or your own recording): make 01.mp3, 02.mp3 ... from the script, drop them in the
    /// same folder and run "Use History Narration Files" (or rebuild the Pegasus level). An .mp3 wins over a .wav of the same number.
    /// </summary>
    public static class HistoryNarration
    {
        const string Dir = "Assets/StansGame/History/PB/Voice";

        // the Windows voice reads French names the English way: help it a little (the script file keeps the real spelling)
        static readonly (string from, string to)[] Say =
        {
            ("Bénouville", "Bay-noo-veel"), ("Gondrée", "Gon-dray"), ("Gondrées", "Gon-drays"), ("Thérèse", "Tay-rez"), ("Georgette", "Jorjette"),
            ("Varaville", "Vara-veel"), ("Dives", "Deev"), ("Le Port", "Luh Por"), ("Café", "Caffay"), ("PIAT", "pee-at"),
            ("Ox & Bucks", "Ox and Bucks"), (" - ", ", "), ("\"", ""), ("Lt.", "Lieutenant"),
        };

        [MenuItem("Stan's Game/Tools/Make History Narration (Windows voice)", priority = 80)]
        static void Make()
        {
            var film = UnityEngine.Object.FindAnyObjectByType<HistoryFilm>(FindObjectsInactive.Include);
            if (!film) { EditorUtility.DisplayDialog("History narration", "Open the Pegasus Bridge level first (it holds the History film).", "OK"); return; }
            var lines = film.Narration();
            Directory.CreateDirectory(Dir);
            File.WriteAllLines(Path.Combine(Dir, "narration-script.txt"),
                new[] { "The History - Pegasus Bridge. Narration, one line a shot. Make 01.mp3, 02.mp3 ... and drop them in this folder.", "" }
                .Concat(lines.Select(l => $"{l.id}  {l.text}")), new UTF8Encoding(true));

            string input = Path.GetFullPath(Path.Combine("Temp", "history-narration.txt"));
            File.WriteAllLines(input, lines.Select(l => l.id + "\t" + Spoken(l.text)), new UTF8Encoding(false));
            string outDir = Path.GetFullPath(Dir);
            string ps = string.Join("\n",
                "Add-Type -AssemblyName System.Speech",
                "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer",
                "$all = $s.GetInstalledVoices() | Where-Object { $_.Enabled } | ForEach-Object { $_.VoiceInfo }",
                "$v = $all | Where-Object { $_.Culture.Name -eq 'en-GB' -and $_.Gender -eq 'Female' } | Select-Object -First 1",
                "if (-not $v) { $v = $all | Where-Object { $_.Culture.Name -eq 'en-GB' } | Select-Object -First 1 }",
                "if (-not $v) { $v = $all | Where-Object { $_.Culture.Name -like 'en-*' -and $_.Gender -eq 'Female' } | Select-Object -First 1 }",
                "if ($v) { $s.SelectVoice($v.Name) }",
                "$s.Rate = -1",
                $"foreach ($line in Get-Content -Encoding UTF8 '{input}') {{",
                "  $p = $line.Split([char]9, 2); if ($p.Length -lt 2) { continue }",
                $"  $s.SetOutputToWaveFile((Join-Path '{outDir}' ($p[0] + '.wav')))",
                "  $s.Speak($p[1]) }",
                "Write-Output ('VOICE ' + $s.Voice.Name + ' | installed: ' + (($all | ForEach-Object { $_.Name }) -join ', '))",
                "$s.SetOutputToNull(); $s.Dispose()");
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(ps));
            try
            {
                EditorUtility.DisplayProgressBar("History narration", $"Windows is reading {lines.Count} lines...", 0.5f);
                var p = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + encoded)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
                string output = p.StandardOutput.ReadToEnd(), error = p.StandardError.ReadToEnd();
                p.WaitForExit();
                Debug.Log($"Stan's Game: History narration written ({lines.Count} lines). {output.Trim()}");
                if (!string.IsNullOrWhiteSpace(error)) Debug.LogWarning("History narration: " + error.Trim());
            }
            finally { EditorUtility.ClearProgressBar(); }
            AssetDatabase.Refresh();
            Assign(film);
        }

        [MenuItem("Stan's Game/Tools/Use History Narration Files", priority = 81)]
        static void Use()
        {
            var film = UnityEngine.Object.FindAnyObjectByType<HistoryFilm>(FindObjectsInactive.Include);
            if (!film) { EditorUtility.DisplayDialog("History narration", "Open the Pegasus Bridge level first (it holds the History film).", "OK"); return; }
            AssetDatabase.Refresh();
            Assign(film);
        }

        static string Spoken(string text)
        {
            foreach (var (from, to) in Say) text = text.Replace(from, to);
            return text;
        }

        /// <summary>The clips in History/PB/Voice, one a shot number; an .mp3 (a better voice) wins over the Windows .wav.</summary>
        public static AudioClip[] Clips()
        {
            var best = new Dictionary<string, (AudioClip clip, bool mp3)>();
            if (!AssetDatabase.IsValidFolder(Dir)) return new AudioClip[0];
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Dir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (!clip) continue;
                bool mp3 = !path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
                if (!best.TryGetValue(clip.name, out var have) || (mp3 && !have.mp3)) best[clip.name] = (clip, mp3);
            }
            return best.Values.Select(v => v.clip).OrderBy(c => c.name).ToArray();
        }

        static void Assign(HistoryFilm film)
        {
            film.voice = Clips();
            EditorUtility.SetDirty(film);
            EditorSceneManager.MarkSceneDirty(film.gameObject.scene);
            EditorSceneManager.SaveScene(film.gameObject.scene);
            Debug.Log($"Stan's Game: the History film has {film.voice.Length} narration clips");
        }
    }
}
