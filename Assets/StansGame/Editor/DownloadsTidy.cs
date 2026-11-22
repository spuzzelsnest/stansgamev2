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

        // ------------------------------------------------------------------ sorting by kind (22 Nov, evening)

        /// <summary>
        /// Where each download goes: guns (small arms, grenades, machine guns, mortars, field and flak guns and their
        /// ammunition), vehicles (tanks, lorries, cars, aircraft), characters (people and their clothes) and objects
        /// (props, kit, buildings, walls, trees). The importers find a download by its folder name in any of these
        /// (CharacterImporter.Dl), so nothing else needs to change. Textures, sounds, animations, history, photos and
        /// the previews keep their own folders.
        /// </summary>
        static readonly (string bucket, string[] names)[] Kinds =
        {
            ("_Weapons", new[] {
                "2cm-vierlingflak-38-ww2", "75-cm-pak-40-munition", "bazooka_us_ww2.glb", "bren_mk_2.glb", "British lee-enfield_smle_mk3.glb",
                "British sten-mk-ii", "flak-88", "German - kar-98_weapon.glb", "german - mp40.glb", "German maschinengewehr-mg-42",
                "luger_p08.glb", "m1-garand", "m1911", "m1a1-carabine", "m8-hc-smoke-grenade", "mills_bomb.glb", "mk-2-grenade",
                "mortars", "nebelhandgranate-39", "no-82-gammonbomb", "pak-38", "pak40-75mm-canon-ww2", "stielhandgranate",
                "submachine-gun-thompson", "vickers-machine-gun", "mp40-test" }),
            ("_Vehicles", new[] {
                "c47.glb", "cromwell-iv", "flakpanzer-iv-wirbelwind", "halifax-biii", "hotchkiss-h35", "jagdpanzer-38t-hetzer",
                "m16-mgmc-halftrack", "m4-sherman-tank-challenger", "m5a1-stuart", "manex3d_generated_sherman_tank.glb", "marder-iiim",
                "opel-blitz", "panzer-iv", "panzer-iv-medium-tank-toshueyi", "stug-iii", "tanks-call-of-duty-world-at-war-hd",
                "trucks-call-of-duty-world-at-war-hd", "vw-kubelwagen", "willy-jeep", "willys-mb-ww2-scene", "jeep-willys-mb",
                "sdkfz-2-kettenkrad", "sdkfz-222", "sdkfz-25011-with-spzb-41", "sdkfz-303-goliath", "sdkfz171-pzkpfw-v-ausfg-panther",
                "sherman-m4a3-green", "shermanm4a2-76w" }),
            ("_Characters", new[] {
                "female", "male-apparel-pack", "black-white-pent-shirts", "pantalon-varon-anos-40-50", "nurse",
                "tripo_convert_a76f1747-c578-4f35-b262-c677e6b0dd96", "normandy-nazis-call-of-duty-2-hd", "French-resistent-character",
                "british-infantry", "great-coat-police-uniform", "police-messenger-uniform",
                "polish-woman-resistance-fighter-from-1944-warsaw", "man-with-camera" }),
            ("_Objects", new[] {
                "Backpack military-radio", "beret", "helmet mk-iii", "German-trench-shovel-asset", "RadioSet_Para.fbx",
                "wwii-handheld-radio-scr-536-radio-transceiver", "bottle-of-champagne", "bottle-of-wine", "boundary-quarter-post",
                "brouette", "bucket", "chair", "rustic-chair", "old_table.glb", "simple_table_low_poly.glb", "wood_table.glb",
                "checkpoint-barrier", "crates", "warehouse-crates", "wooden-crates", "Fences", "glass-window.fbx", "ionic-column",
                "ladder-muddy", "medieval_wooden-ladder.glb", "pole", "telephone-poles", "telephone-pole-with-wires", "electricity-wire",
                "sand-bags", "sandbag", "sandbag-barrier", "sandbags", "sandbags-corner", "sandbags-defense-line",
                "barbed wire barrier", "barbed-wire-pack-31", "czech_hedgehog.glb", "shed", "ww2-field-bunker",
                "chateau", "eglise-normandie-france", "small-house-in-benouville-for-pegasus-bridge-map",
                "stone-wall", "stone-wall-with-ivy", "concrete-wall",
                "tree1", "tree2", "trees small", "tree-3d-model-linden-tree", "Tree3 - pine_tree.glb", "tree4" }),
            ("_Textures", new[] { "algae", "lroc_color_poles_1k.jpg" }),
            ("Photos", new[] { "Horsa glider.png" }),
        };

        [MenuItem("Stan's Game/Tools/Sort Downloads by Kind (weapons, vehicles, characters, objects)", priority = 91)]
        static void SortByKind()
        {
            string dl = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "_downloads"));
            if (!Directory.Exists(dl)) { Debug.LogWarning("Stan's Game: no _downloads folder"); return; }
            if (!EditorUtility.DisplayDialog("Sort downloads by kind",
                "Move the downloads into _Weapons, _Vehicles, _Characters and _Objects (also the 1944 ones set aside in _Unused/Not used yet)?\n\n" +
                "Nothing is deleted. Anything missing or already sorted is skipped. The list goes to _downloads/sorted.txt.", "Sort them", "Cancel")) return;
            var log = new StringBuilder($"Sorted {System.DateTime.Now:yyyy-MM-dd HH:mm} (to undo, move a folder back from where it went)\n");
            int moved = 0, skipped = 0;
            foreach (var (bucket, names) in Kinds)
                foreach (var name in names)
                {
                    string toDir = Path.Combine(dl, bucket), to = Path.Combine(toDir, name);
                    string from = null;
                    foreach (var cand in new[] { Path.Combine(dl, name), Path.Combine(dl, "_Unused", "Not used yet", name) })
                        if (Directory.Exists(cand) || File.Exists(cand)) { from = cand; break; }
                    if (from == null) { skipped++; continue; }
                    if (Directory.Exists(to) || File.Exists(to)) { skipped++; log.AppendLine($"  skipped (already in {bucket}): {name}"); continue; }
                    try
                    {
                        Directory.CreateDirectory(toDir);
                        if (Directory.Exists(from)) Directory.Move(from, to); else File.Move(from, to);
                        moved++; log.AppendLine($"  {Path.GetRelativePath(dl, from)}  ->  {bucket}/{name}");
                    }
                    catch (System.Exception e) { skipped++; log.AppendLine($"  could not move {name}: {e.Message}"); }
                }
            File.AppendAllText(Path.Combine(dl, "sorted.txt"), log.ToString() + "\n");
            Debug.Log($"Stan's Game: {moved} downloads sorted into _Weapons/_Vehicles/_Characters/_Objects, {skipped} skipped (see _downloads/sorted.txt)");
        }
    }
}
