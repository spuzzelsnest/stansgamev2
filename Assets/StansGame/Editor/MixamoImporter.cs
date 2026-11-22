using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Menu: Stan's Game > Import Mixamo Animations.
    /// Joan's Mixamo downloads (_downloads/_Animations/Soldier, Civilian, ...; "With Skin", ~23 MB each) become small
    /// humanoid clips: Animations/Mixamo/&lt;folder&gt;/&lt;clip&gt;.anim. Each FBX is copied into the project for a moment,
    /// imported as a Humanoid (its own avatar, the mixamorig skeleton), its take copied out as a clip, and the FBX
    /// deleted again, so the 2 GB of downloads never enter the project. Walks, runs, idles and other repeating actions
    /// loop; one-shots (deaths, throws, reloads, getting up) don't. The body's height and turning are baked in, the
    /// travel isn't: our scripts move the characters, so the forward drift of a clip is simply not used.
    /// Clips already made are skipped; delete one to make it again.
    /// </summary>
    public static class MixamoImporter
    {
        public const string OutDir = "Assets/StansGame/Animations/Mixamo";
        const string TempDir = "Assets/StansGame/Animations/_MixamoTemp";

        static readonly string[] OneShot = { "Death", "Dying", "Hit", "React", "Throw", "Reload", "Jump", "Salute", " To ", "Stop", "Turn", "Entering", "Exiting", "Pull Out", "Situp", "Honking", "Step" };
        static readonly string[] KeepsHeight = { "Death", "Dying", "Prone", "Laying", "Crawl", "Situp", "Sitting", "Jump", "Kneel" };

        [MenuItem("Stan's Game/Import Mixamo Animations", priority = 24)]
        public static void ImportAll()
        {
            string src = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_downloads", "_Animations");
            if (!Directory.Exists(src)) { EditorUtility.DisplayDialog("Mixamo", "No folder _downloads/_Animations.", "OK"); return; }
            var files = Directory.GetFiles(src, "*.fbx", SearchOption.AllDirectories);
            Directory.CreateDirectory(TempDir);
            int made = 0, skipped = 0, failed = 0;
            var log = new List<string>();
            try
            {
                for (int i = 0; i < files.Length; i++)
                {
                    string f = files[i];
                    string folder = new DirectoryInfo(Path.GetDirectoryName(f)).Name;
                    string name = Path.GetFileNameWithoutExtension(f);
                    int at = name.LastIndexOf('@'); if (at >= 0) name = name.Substring(at + 1);
                    name = name.Trim();
                    string outFolder = $"{OutDir}/{folder}";
                    string outPath = $"{outFolder}/{name}.anim";
                    if (File.Exists(outPath)) { skipped++; continue; }
                    if (EditorUtility.DisplayCancelableProgressBar("Mixamo animations", $"{folder}/{name}", (float)i / files.Length)) break;
                    Directory.CreateDirectory(outFolder);
                    string tmp = $"{TempDir}/clip.fbx";
                    File.Copy(f, tmp, true);
                    AssetDatabase.ImportAsset(tmp, ImportAssetOptions.ForceSynchronousImport);
                    var mi = (ModelImporter)AssetImporter.GetAtPath(tmp);
                    mi.materialImportMode = ModelImporterMaterialImportMode.None;
                    mi.animationType = ModelImporterAnimationType.Human;
                    mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    mi.importAnimation = true; mi.resampleCurves = true;
                    mi.animationCompression = ModelImporterAnimationCompression.Optimal;
                    var clips = mi.defaultClipAnimations;
                    if (clips.Length == 0) { failed++; log.Add($"no take: {folder}/{name}"); AssetDatabase.DeleteAsset(tmp); continue; }
                    bool loop = !Has(name, OneShot);
                    bool height = Has(name, KeepsHeight);
                    foreach (var c in clips)
                    {
                        c.name = name; c.loopTime = loop; c.loopPose = loop;
                        c.lockRootRotation = true; c.keepOriginalOrientation = true;
                        c.lockRootHeightY = !height; c.keepOriginalPositionY = height; c.heightFromFeet = !height;
                        c.lockRootPositionXZ = false; c.keepOriginalPositionXZ = false;
                    }
                    mi.clipAnimations = new[] { clips[0] };
                    mi.SaveAndReimport();
                    AnimationClip clip = null;
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(tmp))
                        if (o is AnimationClip ac && !ac.name.StartsWith("__preview")) { clip = ac; break; }
                    var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(tmp);
                    if (!clip || !avatar || !avatar.isHuman) { failed++; log.Add($"not humanoid: {folder}/{name}"); AssetDatabase.DeleteAsset(tmp); continue; }
                    float len = clip.length;
                    var copy = Object.Instantiate(clip); copy.name = name;
                    AssetDatabase.CreateAsset(copy, outPath);
                    AssetDatabase.DeleteAsset(tmp);
                    made++;
                    log.Add($"{folder}/{name}: {len:0.0} s{(loop ? ", loops" : "")}");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.DeleteAsset(TempDir);
                AssetDatabase.SaveAssets();
            }
            string logs = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs");
            Directory.CreateDirectory(logs);
            File.WriteAllLines(Path.Combine(logs, "mixamo-import.txt"), log);
            Debug.Log($"Stan's Game: Mixamo animations - {made} made, {skipped} already there, {failed} failed (Logs/mixamo-import.txt)");
        }

        static bool Has(string name, string[] words)
        {
            foreach (var w in words) if (name.IndexOf(w, System.StringComparison.OrdinalIgnoreCase) >= 0 || (w == " To " && name.Contains(" To "))) return true;
            return false;
        }
    }
}
