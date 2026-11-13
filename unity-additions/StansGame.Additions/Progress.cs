using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace StansGame.Additions
{
    [Serializable]
    public sealed class LevelRecord
    {
        public string id;
        public bool completed;
        public float bestTimeSeconds;      // real time from first control to the end of the last phase
        public string firstCompletedUtc;   // ISO 8601
    }

    [Serializable]
    public sealed class SaveFile
    {
        public int version = 1;
        public List<LevelRecord> levels = new List<LevelRecord>();
    }

    /// <summary>
    /// Saved progress in <c>save.json</c> next to Player.log
    /// (<c>%USERPROFILE%\AppData\LocalLow\Joan _ Stan\Stan's Game - Pegasus Bridge 1944\</c>).
    /// Written to a temporary file first and then swapped in, so a crash cannot leave half a file.
    /// A damaged file is kept as <c>save.json.bad</c> and the game starts with a fresh save.
    /// Settings stay in PlayerPrefs (<see cref="GameSettings"/>).
    /// </summary>
    public static class Progress
    {
        static SaveFile data;

        static string FilePath { get { return Path.Combine(Application.persistentDataPath, "save.json"); } }

        static SaveFile Data
        {
            get
            {
                if (data == null) Load();
                return data;
            }
        }

        public static void Load()
        {
            data = null;
            string path = FilePath;
            try
            {
                if (File.Exists(path)) data = JsonUtility.FromJson<SaveFile>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogWarning("Stan's Game: save file could not be read, starting fresh (" + e.Message + ")");
                try { File.Copy(path, path + ".bad", true); } catch (Exception) { }
                data = null;
            }
            if (data == null) data = new SaveFile();
            if (data.levels == null) data.levels = new List<LevelRecord>();
        }

        public static LevelRecord Get(string id)
        {
            foreach (LevelRecord r in Data.levels)
                if (r.id == id) return r;
            LevelRecord n = new LevelRecord();
            n.id = id;
            return n;   // not stored until something is recorded
        }

        /// <summary>Records a completed level; keeps the best time. Returns true when it is a new best.</summary>
        public static bool MarkCompleted(string id, float seconds)
        {
            LevelRecord r = null;
            foreach (LevelRecord x in Data.levels)
                if (x.id == id) r = x;
            if (r == null)
            {
                r = new LevelRecord();
                r.id = id;
                Data.levels.Add(r);
            }
            bool best = !r.completed || seconds < r.bestTimeSeconds;
            if (!r.completed) r.firstCompletedUtc = DateTime.UtcNow.ToString("o");
            r.completed = true;
            if (best) r.bestTimeSeconds = seconds;
            Save();
            return best;
        }

        public static void Save()
        {
            string path = FilePath, tmp = path + ".tmp";
            try
            {
                File.WriteAllText(tmp, JsonUtility.ToJson(Data, true));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Stan's Game: could not write the save file (" + e.Message + ")");
            }
        }

        public static string FormatTime(float seconds)
        {
            int s = Mathf.RoundToInt(seconds);
            return s >= 3600 ? string.Format("{0}:{1:00}:{2:00}", s / 3600, s / 60 % 60, s % 60)
                             : string.Format("{0}:{1:00}", s / 60, s % 60);
        }
    }
}
