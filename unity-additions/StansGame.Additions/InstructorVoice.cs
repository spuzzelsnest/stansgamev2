using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEngine;

namespace StansGame.Additions
{
    /// <summary>
    /// The bootcamp instructor's voice: every line he says is played from a recorded clip and shown as a
    /// subtitle (<see cref="InstructorSubtitles"/>). The clips are WAV files in
    /// <c>StreamingAssets/Sounds/voices/instructor/</c> (period processing) or <c>voices/instructor_clean/</c>
    /// (dry), named by line id (<c>s0.intro</c>, <c>s3.praise</c>, <c>end</c>), with <c>manifest.tsv</c>
    /// giving each id's speaker and subtitle text. They are generated offline by <c>tools/voice/</c>.
    ///
    /// The bootcamp raises <c>Bootcamp.InstructorLine(id, text)</c> for every line; this class finds that
    /// event by reflection, so it compiles and runs without the bootcamp (then it only speaks when
    /// <see cref="Say"/> is called). When the sound bank (<c>SoundBank</c>, <c>SoundSettings</c>) is in the
    /// build, clips come from it and the volume follows its Voices group; without it, a small reader of
    /// its own loads this one folder and the volume is <c>stans.voiceVolume</c>.
    ///
    /// Lines are queued, so a praise followed at once by the next station's instruction is said in order.
    /// A hint is dropped while anything else is said or waiting; "skip" cuts off what is being said.
    /// </summary>
    public sealed class InstructorVoice : MonoBehaviour
    {
        public const string Folder = "voices/instructor";
        public const string CleanFolder = "voices/instructor_clean";
        public const string DefaultSpeaker = "CSM Grant";
        const string VolumeKey = "stans.voiceVolume";
        const string EffectKey = "stans.voiceEffect";
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        /// <summary>One line from the manifest.</summary>
        public sealed class Line
        {
            public string Id, Speaker, Text;
            public float Seconds;
        }

        /// <summary>A line as it is said: what the subtitle shows and for how long.</summary>
        public sealed class Spoken
        {
            public string Id, Speaker, Text;
            public AudioClip Clip;
            public float Started, Until;
        }

        static InstructorVoice instance;
        static Dictionary<string, Line> lines = new Dictionary<string, Line>();
        static Dictionary<string, AudioClip> ownClips = new Dictionary<string, AudioClip>();
        static int loadGeneration;
        static readonly Queue<Spoken> queue = new Queue<Spoken>();
        static readonly HashSet<string> warned = new HashSet<string>();

        /// <summary>Raised when a line starts (id, text), after its clip has started playing (or at once for a line with no clip).</summary>
        public static event Action<string, string> LineStarted;

        /// <summary>The line being said, or null.</summary>
        public static Spoken Current { get; private set; }
        /// <summary>True while a line is said or waiting.</summary>
        public static bool Busy { get { return Current != null || queue.Count > 0; } }
        /// <summary>True once the manifest and the clips are loaded (or failed to load).</summary>
        public static bool Ready { get; private set; }
        /// <summary>Clips available (from the sound bank or loaded here).</summary>
        public static int ClipCount { get; private set; }
        /// <summary>The ids in the manifest.</summary>
        public static ICollection<string> Ids { get { return lines.Keys; } }
        /// <summary>The folder the clips came from ("voices/instructor" or "voices/instructor_clean").</summary>
        public static string ActiveFolder { get; private set; }
        /// <summary>True when the clips come from the sound bank, false when loaded here.</summary>
        public static bool FromSoundBank { get; private set; }
        /// <summary>True when the bootcamp's InstructorLine event was found and hooked.</summary>
        public static bool BoundToBootcamp { get; private set; }

        /// <summary>The voice volume, 0 to 1 (the sound bank's Voices group when present).</summary>
        public static float Volume
        {
            get
            {
                float v;
                if (TryVoicesVolume(out v)) return v;
                return Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
            }
        }

        /// <summary>Period processing (parade-ground reverb, period EQ) or the dry recording. The other set is loaded in the background and swapped in when ready; a line still playing from the old set is stopped then.</summary>
        public static bool PeriodEffect
        {
            get { return PlayerPrefs.GetInt(EffectKey, 1) != 0; }
            set
            {
                PlayerPrefs.SetInt(EffectKey, value ? 1 : 0);
                PlayerPrefs.Save();
                if (instance != null) instance.StartCoroutine(instance.Load());
            }
        }

        AudioSource source;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (instance != null) return;
            GameObject go = new GameObject("StansGame.InstructorVoice");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<InstructorVoice>();
            go.AddComponent<InstructorSubtitles>();
        }

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.priority = 0;
            source.ignoreListenerPause = true;
            source.bypassReverbZones = true;
            BindBootcamp();
            StartCoroutine(Load());
        }

        // ------------------------------------------------------------------ saying a line

        /// <summary>
        /// Says a line by id. <paramref name="text"/> is the caller's subtitle; the manifest's text (what the
        /// clip says) wins when the id has a clip. Unknown ids are shown as a subtitle only. Returns the
        /// queued line, or null when it was dropped (a hint while busy).
        /// </summary>
        public static Spoken Say(string id, string text)
        {
            if (instance == null) Boot();
            if (string.IsNullOrEmpty(id)) return null;
            bool hint = id.EndsWith(".hint", StringComparison.Ordinal);
            if (hint && Busy) return null;
            if (id == "skip") Stop();

            Line l;
            lines.TryGetValue(id, out l);
            Spoken s = new Spoken();
            s.Id = id;
            s.Speaker = l != null && !string.IsNullOrEmpty(l.Speaker) ? l.Speaker : DefaultSpeaker;
            s.Text = l != null && !string.IsNullOrEmpty(l.Text) ? l.Text : (text ?? "");
            if (l != null && !string.IsNullOrEmpty(text) && Squash(text) != Squash(l.Text) && warned.Add("text " + id))
                Debug.Log("Stan's Game: instructor line " + id + " differs from the recording; the subtitle shows the recorded text");

            // Drop hints still waiting when a real line comes in, and keep the queue short.
            if (!hint && queue.Count > 0)
            {
                Spoken[] waiting = queue.ToArray();
                queue.Clear();
                foreach (Spoken w in waiting)
                    if (!w.Id.EndsWith(".hint", StringComparison.Ordinal)) queue.Enqueue(w);
            }
            while (queue.Count >= 3) queue.Dequeue();
            queue.Enqueue(s);
            return s;
        }

        /// <summary>Stops the line being said and clears the queue.</summary>
        public static void Stop()
        {
            queue.Clear();
            Current = null;
            if (instance != null && instance.source != null) instance.source.Stop();
        }

        void Update()
        {
            float now = Time.unscaledTime;
            if (Current != null)
            {
                bool clipDone = Current.Clip == null || !source.isPlaying;
                if (clipDone && now >= Current.Until) Current = null;
                else if (Current.Clip != null) source.volume = Volume;
            }
            // Wait for the clips before the first line, at most 5 s.
            if (Current == null && queue.Count > 0 && (Ready || Time.realtimeSinceStartup > 5f)) Begin(queue.Dequeue(), now);
        }

        void Begin(Spoken s, float now)
        {
            s.Clip = ClipFor(s.Id);
            s.Started = now;
            float seconds = s.Clip != null ? s.Clip.length : Mathf.Clamp(0.33f * Words(s.Text), 2f, 7f);
            s.Until = now + seconds + 0.5f;
            Current = s;
            if (s.Clip != null)
            {
                source.Stop();
                source.clip = s.Clip;
                source.volume = Volume;
                source.Play();
                Debug.Log("Stan's Game: instructor says " + s.Id + " (" + s.Clip.length.ToString("0.00") + " s clip, " + (FromSoundBank ? "sound bank" : ActiveFolder) + ")");
            }
            else if (warned.Add("clip " + s.Id))
                Debug.Log("Stan's Game: instructor line " + s.Id + " has no recording; subtitle only");
            Action<string, string> h = LineStarted;
            if (h != null) h(s.Id, s.Text);
        }

        // ------------------------------------------------------------------ the bootcamp's event

        static void OnBootcampLine(string id, string text)
        {
            Say(id, text);
        }

        /// <summary>Hooks <c>Bootcamp.InstructorLine</c> (a static <c>Action&lt;string, string&gt;</c> event) when the bootcamp is in the build.</summary>
        static void BindBootcamp()
        {
            if (BoundToBootcamp) return;
            try
            {
                Type t = typeof(InstructorVoice).Assembly.GetType("StansGame.Additions.Bootcamp");
                EventInfo e = t != null ? t.GetEvent("InstructorLine", Any) : null;
                if (e == null || e.EventHandlerType != typeof(Action<string, string>)) return;
                MethodInfo add = e.GetAddMethod(true);
                if (add == null || !add.IsStatic) return;
                Action<string, string> handler = OnBootcampLine;
                add.Invoke(null, new object[] { handler });
                BoundToBootcamp = true;
                Debug.Log("Stan's Game: instructor voice listens to the bootcamp");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Stan's Game: instructor voice could not hook the bootcamp: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ loading

        IEnumerator Load()
        {
            // A newer Load (the effect switched again) makes this one stop at its next step.
            int gen = ++loadGeneration;
            string root = Path.Combine(Application.streamingAssetsPath, "Sounds");
            string folder = PeriodEffect ? Folder : CleanFolder;
            if (!File.Exists(Path.Combine(root, Path.Combine(folder, "manifest.tsv"))) && folder == CleanFolder) folder = Folder;
            string dir = Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar));
            Dictionary<string, Line> newLines = ReadManifest(dir);

            // The sound bank, when it is in the build and has every one of these clips, owns loading. It is
            // started by SoundDirector after the scene loads, so wait for it to start (Folder set), then to finish.
            float waitUntil = Time.realtimeSinceStartup + 20f;
            while (SoundBankPresent() && !SoundBankStarted() && Time.realtimeSinceStartup < waitUntil) yield return null;
            while (SoundBankLoading() && Time.realtimeSinceStartup < waitUntil) yield return null;
            if (gen != loadGeneration) yield break;
            int bankClips = 0;
            foreach (string id in newLines.Keys) if (SoundBankHas(folder + "/" + id)) bankClips++;
            if (newLines.Count > 0 && bankClips == newLines.Count)
            {
                Swap(newLines, new Dictionary<string, AudioClip>(), folder, true, bankClips);
                Debug.Log("Stan's Game: instructor voice uses the sound bank (" + bankClips + " clips in " + folder + ")");
                yield break;
            }
            if (bankClips > 0)
                Debug.Log("Stan's Game: the sound bank has " + bankClips + " of " + newLines.Count + " instructor clips; reading them here instead");

            // Otherwise read the WAV files here, on a worker thread.
            List<string> ids = new List<string>(newLines.Keys);
            List<string> files = new List<string>();
            foreach (string id in ids) files.Add(Path.Combine(dir, id + ".wav"));
            Pcm[] decoded = new Pcm[files.Count];
            bool done = false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                for (int i = 0; i < files.Count; i++)
                {
                    try { if (File.Exists(files[i])) decoded[i] = ReadWav(files[i]); }
                    catch (Exception) { decoded[i] = null; }
                }
                done = true;
            });
            while (!done) yield return null;
            if (gen != loadGeneration) yield break;

            Dictionary<string, AudioClip> newClips = new Dictionary<string, AudioClip>();
            int missing = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                Pcm p = decoded[i];
                int frames = p != null && p.data != null ? p.data.Length / p.channels : 0;
                if (frames < 1) { missing++; continue; }
                try
                {
                    AudioClip c = AudioClip.Create(ids[i], frames, p.channels, p.rate, false);
                    c.SetData(p.data, 0);
                    newClips[ids[i]] = c;
                }
                catch (Exception ex)
                {
                    missing++;
                    Debug.LogWarning("Stan's Game: instructor clip " + ids[i] + " could not be made: " + ex.Message);
                }
            }
            Swap(newLines, newClips, folder, false, newClips.Count);
            Debug.Log("Stan's Game: instructor voice loaded " + newClips.Count + " of " + ids.Count + " clips from " + folder + (missing > 0 ? " (" + missing + " missing or unreadable)" : ""));
        }

        /// <summary>Puts a finished load in place in one step; a line still playing an old clip is stopped first.</summary>
        void Swap(Dictionary<string, Line> newLines, Dictionary<string, AudioClip> newClips, string folder, bool bank, int count)
        {
            Dictionary<string, AudioClip> old = ownClips;
            bool playingOld = Current != null && Current.Clip != null && old.ContainsValue(Current.Clip);
            if (playingOld) { source.Stop(); Current = null; }
            lines = newLines;
            ownClips = newClips;
            ActiveFolder = folder;
            FromSoundBank = bank;
            ClipCount = count;
            Ready = true;
            foreach (AudioClip c in old.Values) if (c != null) Destroy(c);
        }

        static Dictionary<string, Line> ReadManifest(string dir)
        {
            Dictionary<string, Line> result = new Dictionary<string, Line>();
            string path = Path.Combine(dir, "manifest.tsv");
            if (!File.Exists(path)) path = Path.Combine(dir, "lines.tsv");
            if (!File.Exists(path))
            {
                Debug.Log("Stan's Game: instructor voice found no manifest in " + dir);
                return result;
            }
            string[] rows;
            try { rows = File.ReadAllLines(path); }
            catch (Exception ex) { Debug.LogWarning("Stan's Game: " + path + " could not be read: " + ex.Message); return result; }
            int[] col = null;
            foreach (string raw in rows)
            {
                if (raw.Length == 0 || raw[0] == '#') continue;
                string[] c = raw.Split('\t');
                if (col == null)
                {
                    col = new int[] { Array.IndexOf(c, "id"), Array.IndexOf(c, "speaker"), Array.IndexOf(c, "text"), Array.IndexOf(c, "seconds") };
                    if (col[0] < 0 || col[2] < 0) { Debug.LogWarning("Stan's Game: " + path + " has no id or text column"); return result; }
                    continue;
                }
                Line l = new Line();
                l.Id = Cell(c, col[0]);
                if (string.IsNullOrEmpty(l.Id)) continue;
                l.Speaker = Cell(c, col[1]);
                l.Text = Cell(c, col[2]);
                float.TryParse(Cell(c, col[3]), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out l.Seconds);
                result[l.Id] = l;
            }
            return result;
        }

        static string Cell(string[] c, int i) { return i >= 0 && i < c.Length ? c[i].Trim() : null; }

        AudioClip ClipFor(string id)
        {
            if (FromSoundBank)
            {
                AudioClip c = SoundBankPick(ActiveFolder + "/" + id);
                if (c != null) return c;
            }
            AudioClip own;
            return ownClips.TryGetValue(id, out own) ? own : null;
        }

        sealed class Pcm
        {
            public float[] data;
            public int channels, rate;
        }

        /// <summary>Reads a RIFF WAVE file: 16-bit PCM or 32-bit float.</summary>
        static Pcm ReadWav(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            if (b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F' || b[8] != 'W' || b[9] != 'A' || b[10] != 'V' || b[11] != 'E') return null;
            int pos = 12, format = 0, channels = 0, rate = 0, bits = 0, dataAt = -1, dataLen = 0;
            while (pos + 8 <= b.Length)
            {
                int size = BitConverter.ToInt32(b, pos + 4);
                if (b[pos] == 'f' && b[pos + 1] == 'm' && b[pos + 2] == 't')
                {
                    format = BitConverter.ToUInt16(b, pos + 8);
                    channels = BitConverter.ToUInt16(b, pos + 10);
                    rate = BitConverter.ToInt32(b, pos + 12);
                    bits = BitConverter.ToUInt16(b, pos + 22);
                }
                else if (b[pos] == 'd' && b[pos + 1] == 'a' && b[pos + 2] == 't' && b[pos + 3] == 'a')
                {
                    dataAt = pos + 8;
                    dataLen = Math.Min(size, b.Length - dataAt);
                    break;
                }
                pos += 8 + size + (size & 1);
            }
            if (dataAt < 0 || channels < 1 || rate < 1) return null;
            Pcm p = new Pcm();
            p.channels = channels;
            p.rate = rate;
            if (format == 1 && bits == 16)
            {
                p.data = new float[dataLen / 2];
                for (int i = 0; i < p.data.Length; i++) p.data[i] = BitConverter.ToInt16(b, dataAt + 2 * i) / 32768f;
            }
            else if (format == 3 && bits == 32)
            {
                p.data = new float[dataLen / 4];
                for (int i = 0; i < p.data.Length; i++) p.data[i] = BitConverter.ToSingle(b, dataAt + 4 * i);
            }
            else return null;
            return p;
        }

        // ------------------------------------------------------------------ the sound bank, when present

        static Type bankType, settingsType, groupType;
        static bool looked;

        static void LookUpSound()
        {
            if (looked) return;
            looked = true;
            Assembly a = typeof(InstructorVoice).Assembly;
            bankType = a.GetType("StansGame.Additions.SoundBank");
            settingsType = a.GetType("StansGame.Additions.SoundSettings");
            groupType = a.GetType("StansGame.Additions.SoundGroup");
        }

        static bool SoundBankPresent()
        {
            LookUpSound();
            return bankType != null;
        }

        static bool SoundBankStarted()
        {
            LookUpSound();
            if (bankType == null) return false;
            PropertyInfo p = bankType.GetProperty("Folder", Any);
            return p != null && p.GetValue(null, null) != null;
        }

        static bool SoundBankLoading()
        {
            LookUpSound();
            if (bankType == null) return false;
            PropertyInfo p = bankType.GetProperty("Loading", Any);
            return p != null && (bool)p.GetValue(null, null);
        }

        static bool SoundBankHas(string key)
        {
            LookUpSound();
            if (bankType == null) return false;
            MethodInfo m = bankType.GetMethod("Has", Any, null, new Type[] { typeof(string) }, null);
            return m != null && (bool)m.Invoke(null, new object[] { key });
        }

        static AudioClip SoundBankPick(string key)
        {
            LookUpSound();
            if (bankType == null) return null;
            MethodInfo m = bankType.GetMethod("Pick", Any, null, new Type[] { typeof(string[]) }, null);
            return m != null ? m.Invoke(null, new object[] { new string[] { key } }) as AudioClip : null;
        }

        static bool TryVoicesVolume(out float v)
        {
            v = 1f;
            LookUpSound();
            if (settingsType == null || groupType == null || !Enum.IsDefined(groupType, "Voices")) return false;
            MethodInfo m = settingsType.GetMethod("Volume", Any, null, new Type[] { groupType }, null);
            if (m == null) return false;
            v = Mathf.Clamp01((float)m.Invoke(null, new object[] { Enum.Parse(groupType, "Voices") }));
            return true;
        }

        // ------------------------------------------------------------------ helpers

        static int Words(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return s.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        static string Squash(string s)
        {
            if (s == null) return "";
            System.Text.StringBuilder b = new System.Text.StringBuilder(s.Length);
            foreach (char ch in s) if (char.IsLetterOrDigit(ch)) b.Append(char.ToLowerInvariant(ch));
            return b.ToString();
        }
    }
}
