using System.IO;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>Copies the useful recorded sounds from Stan's Game v1 (sibling folder "stansgame") into Resources/Audio.</summary>
    public static class SoundImporter
    {
        const string Dest = "Assets/StansGame/Resources/Audio";
        static readonly string[] Sfx = { "env_hit", "human_fire", "human_footstep_01", "human_footstep_02", "human_footstep_03", "human_footstep_04", "human_hit", "human_reload" };
        static readonly string[] Voice = { "hit01", "hit02", "hit03", "hit04", "hit05", "hit06", "hit07", "hit08", "hit09", "hit10", "hit11", "hit12" };
        static readonly string[] Boom = { "FarExplosionA", "FarExplosionB", "NearExplosionA", "NearExplosionB" };

        [MenuItem("Stan's Game/Setup/Import v1 Sounds", priority = 100)]
        public static void Import()
        {
            string v1 = Path.GetFullPath(Path.Combine(Application.dataPath, "../../stansgame/Assets"));
            if (!Directory.Exists(v1)) { Debug.LogWarning("Stan's Game v1 not found at " + v1); return; }
            Directory.CreateDirectory(Dest);
            int n = 0;
            n += CopyAll(Path.Combine(v1, "Sounds/Sfx"), Sfx);
            n += CopyAll(Path.Combine(v1, "Sounds/Voice"), Voice);
            n += CopyAll(Path.Combine(v1, "Frameworks/Detonator/Sounds"), Boom);
            AssetDatabase.Refresh();
            Debug.Log($"Imported {n} sounds from Stan's Game v1 into {Dest}");
        }

        static int CopyAll(string dir, string[] names)
        {
            if (!Directory.Exists(dir)) return 0;
            int n = 0;
            foreach (var file in Directory.GetFiles(dir))
            {
                string stem = Path.GetFileNameWithoutExtension(file), ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".meta" || System.Array.IndexOf(names, stem) < 0) continue;
                string to = Path.Combine(Dest, Path.GetFileName(file));
                if (!File.Exists(to)) { File.Copy(file, to); n++; }
            }
            return n;
        }
    }
}
