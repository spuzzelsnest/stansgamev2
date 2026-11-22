using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Stan's Game > Tools > Set Aside Unused Downloads: moves the downloads the game does not use (checked 22 Nov
    /// 2026 against every importer, builder and Blender script) out of the way, into _downloads/_Unused:
    ///   Off-period   - not 1944 (modern signs, film characters, other eras)
    ///   Not used yet - 1944 material the game doesn't use (yet): kept for later levels
    /// Nothing is deleted. Anything already moved, or missing, is skipped. What moved is listed in
    /// _downloads/_Unused/moved.txt; to undo, move a folder back up one level.
    /// Kept where they are: everything the game uses, the importers' sources, and the planned villager clothes.
    /// </summary>
    public static class DownloadsTidy
    {
        static readonly string[] OffPeriod =
        {
            "tony-montana-1-t-pose", "lady-in-cheongsam", "malaysia-road-sign", "stop-sign-psx", "female model",
            "lowpoly-people-waldo", "frock-coat 1900",
        };
        static readonly string[] NotUsedYet =
        {
            "algae", "barbed wire barrier", "barbed-wire-pack-31", "bottle-of-wine", "british-infantry", "czech_hedgehog.glb",
            "electricity-wire", "great-coat-police-uniform", "police-messenger-uniform", "polish-woman-resistance-fighter-from-1944-warsaw",
            "jeep-willys-mb", "medieval_wooden-ladder.glb", "sdkfz-2-kettenkrad", "sdkfz-222", "sdkfz-25011-with-spzb-41",
            "sdkfz-303-goliath", "sdkfz171-pzkpfw-v-ausfg-panther", "sherman-m4a3-green", "shermanm4a2-76w", "stone-wall-with-ivy",
            "telephone-pole-with-wires", "tree-3d-model-linden-tree", "Tree3 - pine_tree.glb", "tree4",
            "wwii-handheld-radio-scr-536-radio-transceiver", "man-with-camera", "mp40-test",
        };

        [MenuItem("Stan's Game/Tools/Set Aside Unused Downloads", priority = 90)]
        static void SetAside()
        {
            string dl = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "_downloads"));
            if (!Directory.Exists(dl)) { Debug.LogWarning("Stan's Game: no _downloads folder"); return; }
            if (!EditorUtility.DisplayDialog("Set aside unused downloads",
                $"Move {OffPeriod.Length + NotUsedYet.Length} unused downloads into _downloads/_Unused (Off-period / Not used yet)?\n\nNothing is deleted; a list is written to _downloads/_Unused/moved.txt.", "Move them", "Cancel")) return;
            var log = new StringBuilder($"Set aside {System.DateTime.Now:yyyy-MM-dd HH:mm} (move a folder back up to _downloads to undo)\n");
            int moved = 0, skipped = 0;
            void Move(string name, string bucket)
            {
                string from = Path.Combine(dl, name), toDir = Path.Combine(dl, "_Unused", bucket), to = Path.Combine(toDir, name);
                bool isDir = Directory.Exists(from), isFile = File.Exists(from);
                if (!isDir && !isFile) { skipped++; return; }
                if (Directory.Exists(to) || File.Exists(to)) { skipped++; log.AppendLine($"  skipped (already there): {name}"); return; }
                try
                {
                    Directory.CreateDirectory(toDir);
                    if (isDir) Directory.Move(from, to); else File.Move(from, to);
                    moved++; log.AppendLine($"  {bucket}/{name}");
                }
                catch (System.Exception e) { skipped++; log.AppendLine($"  could not move {name}: {e.Message}"); }
            }
            foreach (var n in OffPeriod) Move(n, "Off-period");
            foreach (var n in NotUsedYet) Move(n, "Not used yet");
            Directory.CreateDirectory(Path.Combine(dl, "_Unused"));
            File.AppendAllText(Path.Combine(dl, "_Unused", "moved.txt"), log.ToString() + "\n");
            Debug.Log($"Stan's Game: {moved} unused downloads moved to _downloads/_Unused, {skipped} skipped (see _downloads/_Unused/moved.txt)");
        }
    }
}
