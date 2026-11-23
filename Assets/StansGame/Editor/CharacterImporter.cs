using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Menu: Stan's Game > Import Characters &amp; Weapons.
    /// Pulls models from the project's _downloads folder and from the old 2012 project, configures them,
    /// and builds ready-to-use prefabs:
    ///   German_Rifleman / German_MG     (rigged Wehrmacht soldier, humanoid, ragdoll, retargeted Hero animations)
    ///   Weapon_Sten / Weapon_Bren        (real models, barrel along +Z, muzzle marker)
    /// Model credits: see Assets/StansGame/CREDITS.txt.
    /// </summary>
    public static class CharacterImporter
    {
        const string Root = "Assets/StansGame";
        const string ModelDir = Root + "/Models";
        const string AnimDir = Root + "/Animations";
        const string PrefabDir = Root + "/Prefabs";
        const string MatDir = Root + "/Materials";

        static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        static string Downloads => Path.Combine(ProjectRoot, "_downloads");

        /// <summary>A download by its own folder name, wherever Joan has sorted it: straight in _downloads, or in one of
        /// her sorting folders (_Characters, _Sound, ...). Falls back to the old place if it is nowhere.</summary>
        internal static string Dl(params string[] parts)
        {
            string rel = Path.Combine(parts), direct = Path.Combine(Downloads, rel);
            if (File.Exists(direct) || Directory.Exists(direct)) return direct;
            if (Directory.Exists(Downloads))
                foreach (var sub in Directory.GetDirectories(Downloads, "_*"))
                {
                    string p = Path.Combine(sub, rel);
                    if (File.Exists(p) || Directory.Exists(p)) return p;
                }
            return direct;
        }
        static string OldHeroDir => Path.GetFullPath(Path.Combine(ProjectRoot, "..", "stansgame", "Assets", "Models", "Character Models", "Hero Artwork"));

        static readonly string[] HeroClips =
        {
            "Hero@IdleAim", "Hero@Idle", "Hero@WalkForward", "Hero@WalkBackward", "Hero@RunForward", "Hero@RunBackward",
            "Hero@RunStrafeLeft", "Hero@RunStrafeRight", "Hero@WalkStrafeLeft", "Hero@WalkStrafeRight",
            "Hero@FireGun", "Hero@GunReload", "Hero@AimStraight"
        };

        // German soldier material -> texture (from the model's original .dds names)
        static readonly Dictionary<string, string> GermanTextures = new Dictionary<string, string>
        {
            { "file1Material", "german_head_d" }, { "file2Material", "german_torso_color" }, { "file3Material", "german_boots_d" },
            { "file4Material", "german_hands_d" }, { "file5Material", "german_helmet_d" }, { "file6Material", "german_insignia02_d" },
            { "file7Material", "german_strapes_d" }, { "file8Material", "german_canister_s" }, { "file9Material", "german_knife02_d" },
            { "file10Material", "german_pouches_detail_d" }, { "file12Material", "german_canten_d" }, { "file13Material", "german_knife_d" },
        };

        [MenuItem("Stan's Game/Import Characters and Weapons", priority = 20)]
        public static void ImportAll()
        {
            try
            {
                EditorUtility.DisplayProgressBar("Stan's Game", "Copying files", 0.1f);
                EnsureFolder(ModelDir); EnsureFolder(AnimDir); EnsureFolder(PrefabDir); EnsureFolder(MatDir);
                CopyFiles();
                AssetDatabase.Refresh();

                EditorUtility.DisplayProgressBar("Stan's Game", "Configuring Hero animations", 0.3f);
                var heroAvatar = SetupHeroAnimations();
                var controller = StansGame.EditorTools.AnimationSets.SoldierController();   // the Mixamo set, rebuilt in place (was: a new Soldier.controller from the old Hero clips, which broke every link to it)

                EditorUtility.DisplayProgressBar("Stan's Game", "Configuring weapons", 0.5f);
                var sten = BuildWeaponPrefab("Sten", $"{ModelDir}/Weapons/Sten.fbx", 0.762f, StenMaterials());
                var bren = BuildWeaponPrefab("Bren", $"{ModelDir}/Weapons/Bren_Mk2.fbx", 1.156f, BrenMaterials());
                // the SMLE Mk III (Joan's lee-enfield-smle-mkiii, 23 Nov, Models/Source~/lee_smle.py: same frame as the old model)
                var lee = BuildWeaponPrefab("LeeEnfield", $"{ModelDir}/Weapons/LeeEnfield.fbx", 1.132f, new Dictionary<string, Material>
                    { { "SMLE", LitMat("LeeEnfield", Tex($"{ModelDir}/Weapons/LeeEnfield_BaseColor.jpg"), Tex($"{ModelDir}/Weapons/LeeEnfield_Normal.jpg", true), null, 0.35f) } });
                var kar = BuildWeaponPrefab("Kar98k", $"{ModelDir}/Weapons/Kar98k.fbx", 1.11f, new Dictionary<string, Material>
                    { { "material_0", LitMat("Kar98k", Tex($"{ModelDir}/Weapons/Kar98k_BaseColor.jpg"), Tex($"{ModelDir}/Weapons/Kar98k_Normal.jpg", true), null, 0.35f) } });
                var mp40 = BuildWeaponPrefab("MP40", $"{ModelDir}/Weapons/MP40.fbx", 0.833f, new Dictionary<string, Material>
                    { { "None", LitMat("MP40", Tex($"{ModelDir}/Weapons/MP40_BaseColor.jpg"), Tex($"{ModelDir}/Weapons/MP40_Normal.jpg", true), Tex($"{ModelDir}/Weapons/MP40_MetallicSmoothness.png")) } });
                var mgA = LitMat("MG42_A", Tex($"{ModelDir}/Weapons/MG42Tex/A_albedo.jpg"), Tex($"{ModelDir}/Weapons/MG42Tex/A_normal.png", true), null, 0.45f);
                var mgB = LitMat("MG42_B", Tex($"{ModelDir}/Weapons/MG42Tex/B_albedo.jpg"), Tex($"{ModelDir}/Weapons/MG42Tex/B_normal.png", true), null, 0.45f);
                var mg42 = BuildWeaponPrefab("MG42", $"{ModelDir}/Weapons/MG42.fbx", 1.22f, new Dictionary<string, Material> { { "A.001", mgA }, { "B", mgB }, { "B.002", mgB } });
                // side arms (models from _downloads, prepared by Claude: Luger P08 222 mm, Colt M1911 216 mm long)
                BuildWeaponPrefab("Luger", $"{ModelDir}/Weapons/Luger.fbx", 0.222f, new Dictionary<string, Material>
                    { { "Luger", LitMat("Luger", Tex($"{ModelDir}/Weapons/Luger_BaseColor.jpg"), Tex($"{ModelDir}/Weapons/Luger_Normal.png", true), null, 0.45f) } }, 0.2f, 0.45f);
                BuildWeaponPrefab("M1911", $"{ModelDir}/Weapons/M1911.fbx", 0.216f, new Dictionary<string, Material>
                    { { "M1911", LitMat("M1911", Tex($"{ModelDir}/Weapons/M1911_BaseColor.jpg"), Tex($"{ModelDir}/Weapons/M1911_Normal.png", true), Tex($"{ModelDir}/Weapons/M1911_MetallicSmoothness.png"), 0.5f) } }, 0.22f, 0.45f);

                EditorUtility.DisplayProgressBar("Stan's Game", "Configuring German soldier", 0.7f);
                var german = SetupGermanModel();
                if (german)
                {
                    BuildSoldierPrefab("German_Rifleman", german, controller, Faction.German, kar, "Kar98k", 55f, 30f, false, 5);
                    BuildSoldierPrefab("German_MG", german, controller, Faction.German, mg42, "MG42", 20f, 1200f, true, 50);
                    BuildSoldierPrefab("German_SMG", german, controller, Faction.German, mp40, "MP40", 30f, 500f, true, 32);
                }
                WriteCredits();
                AssetDatabase.SaveAssets();
                Debug.Log($"Stan's Game: import done. Hero avatar: {(heroAvatar ? "ok" : "MISSING")}, animator: {(controller ? "ok" : "missing")}, Sten: {(sten ? "ok" : "missing")}, Bren: {(bren ? "ok" : "missing")}, German: {(german ? "ok" : "missing")}");
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        // ------------------------------------------------------------ files

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static string Abs(string assetPath) => Path.Combine(ProjectRoot, assetPath);

        static void CopyFiles()
        {
            // 1. model pack zips prepared by Claude (Bren + German soldier FBX), split into small parts
            var zips = Directory.Exists(Downloads) ? Directory.GetFiles(Downloads, "StansGame_ModelPack*.zip") : new string[0];
            if (zips.Length == 0) Debug.LogWarning("Stan's Game: no StansGame_ModelPack*.zip in " + Downloads);
            foreach (var zip in zips)
            {
                using (var za = new ZipArchive(File.OpenRead(zip), ZipArchiveMode.Read))
                    foreach (var e in za.Entries)
                    {
                        if (string.IsNullOrEmpty(e.Name)) continue;
                        string dest = Abs($"{ModelDir}/{e.FullName}");
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        using (var s = e.Open()) using (var f = File.Create(dest)) s.CopyTo(f);
                    }
            }

            // 2. German soldier textures (PNG) downloaded by the user
            CopyDir(Dl("German soldier", "textures"), Abs($"{ModelDir}/GermanSoldier/Textures"), "*.png");
            // 3. weapon textures that stay in the user's downloads (models come pre-aligned in the packs)
            CopyDir(Dl("British sten-mk-ii", "textures"), Abs($"{ModelDir}/Sten/Textures"), "Sten_Gun_low_*.png");
            foreach (var t in new[] { "A_albedo.jpg", "A_normal.png", "B_albedo.jpg", "B_normal.png" })
            {
                string s = Dl("German maschinengewehr-mg-42", "textures", t);
                if (File.Exists(s)) { Directory.CreateDirectory(Abs($"{ModelDir}/Weapons/MG42Tex")); File.Copy(s, Abs($"{ModelDir}/Weapons/MG42Tex/{t}"), true); }
            }
            // 4. Hero animations from the original 2012 Stan's Game
            if (Directory.Exists(OldHeroDir))
            {
                string dest = Abs($"{AnimDir}/Hero");
                Directory.CreateDirectory(dest);
                File.Copy(Path.Combine(OldHeroDir, "Hero.fbx"), Path.Combine(dest, "Hero.fbx"), true);
                foreach (var c in HeroClips)
                {
                    string src = Path.Combine(OldHeroDir, c + ".fbx");
                    if (File.Exists(src)) File.Copy(src, Path.Combine(dest, c + ".fbx"), true);
                }
            }
            else Debug.LogWarning("Stan's Game: old project not found at " + OldHeroDir);
        }

        static void CopyDir(string src, string dest, string pattern)
        {
            if (!Directory.Exists(src)) { Debug.LogWarning("Stan's Game: missing " + src); return; }
            Directory.CreateDirectory(dest);
            foreach (var f in Directory.GetFiles(src, pattern)) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
        }

        // ------------------------------------------------------------ animation

        static Avatar SetupHeroAnimations()
        {
            string heroPath = $"{AnimDir}/Hero/Hero.fbx";
            var mi = AssetImporter.GetAtPath(heroPath) as ModelImporter;
            if (!mi) { Debug.LogWarning("Stan's Game: Hero.fbx not imported (old FBX format?)"); return null; }
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(heroPath).OfType<Avatar>().FirstOrDefault();
            if (!avatar || !avatar.isHuman) { Debug.LogWarning("Stan's Game: Hero avatar is not a valid humanoid"); return avatar; }

            foreach (var c in HeroClips)
            {
                string p = $"{AnimDir}/Hero/{c}.fbx";
                var ci = AssetImporter.GetAtPath(p) as ModelImporter;
                if (!ci) continue;
                ci.animationType = ModelImporterAnimationType.Human;
                ci.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                ci.sourceAvatar = avatar;
                ci.importAnimation = true;
                var clips = ci.defaultClipAnimations;
                bool loop = !(c.Contains("FireGun") || c.Contains("Reload"));
                foreach (var clip in clips)
                {
                    clip.name = c.Replace("Hero@", "");
                    clip.loopTime = loop;
                    clip.lockRootRotation = true; clip.keepOriginalOrientation = true;
                    clip.lockRootHeightY = true; clip.keepOriginalPositionY = true;
                    clip.lockRootPositionXZ = true; clip.keepOriginalPositionXZ = true;
                }
                ci.clipAnimations = clips;
                ci.SaveAndReimport();
            }
            return avatar;
        }

        static AnimationClip Clip(string name)
        {
            string p = $"{AnimDir}/Hero/Hero@{name}.fbx";
            return AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
        }

        // ------------------------------------------------------------ materials

        static Material LitMat(string name, Texture2D albedo, Texture2D normal = null, Texture2D metalSmooth = null, float smooth = 0.3f)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            if (albedo) { m.SetTexture("_BaseMap", albedo); m.mainTexture = albedo; }
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", smooth);
            if (normal) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
            if (metalSmooth) { m.SetTexture("_MetallicGlossMap", metalSmooth); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Smoothness", 1f); }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D Tex(string path, bool normal = false, int maxSize = 2048)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!ti) return null;
            bool dirty = false;
            if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; dirty = true; }
            if (ti.maxTextureSize != maxSize) { ti.maxTextureSize = maxSize; dirty = true; }
            if (dirty) ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Dictionary<string, Material> StenMaterials()
        {
            string t = $"{ModelDir}/Sten/Textures/Sten_Gun_low_";
            var d = new Dictionary<string, Material>();
            foreach (var part in new[] { "gun", "chamber", "spring" })
                d[part + "_metal"] = LitMat("Sten_" + part, Tex($"{t}{part}_metal_BaseColor.png"), Tex($"{t}{part}_metal_Normal.png", true), null, 0.45f);
            return d;
        }

        static Dictionary<string, Material> BrenMaterials()
        {
            string t = $"{ModelDir}/Bren/Bren_";
            var d = new Dictionary<string, Material>();
            foreach (var (mat, part) in new[] { ("Bren_Main", "Main"), ("Bren_Magazine", "Magazine"), ("Bren_wood", "Wood"), ("Bren_rear_sight", "RearSight") })
                d[mat] = LitMat("Bren_" + part, Tex($"{t}{part}_BaseColor.jpg"), Tex($"{t}{part}_Normal.jpg", true), Tex($"{t}{part}_MetallicSmoothness.png"));
            return d;
        }

        static void Remap(string modelPath, Dictionary<string, Material> map)
        {
            var mi = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (!mi) return;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (var kv in map)
                if (kv.Value) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            mi.SaveAndReimport();
        }

        // ------------------------------------------------------------ weapons

        static GameObject BuildWeaponPrefab(string name, string modelPath, float realLength, Dictionary<string, Material> mats,
            float gripFromButt = 0.35f, float gripDrop = 0.2f)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (!model) { Debug.LogWarning("Stan's Game: missing " + modelPath); return null; }
            Remap(modelPath, mats);
            model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);

            var root = new GameObject("Weapon_" + name);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.SetParent(root.transform, false);
            foreach (var c in inst.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);

            var marker = inst.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "MuzzleMarker");
            if (marker)
            {
                // pre-aligned by Claude in Blender: just make sure the muzzle points along +Z
                var bb = WorldBounds(inst);
                Vector3 dir = marker.position - bb.center; dir.y = 0f;
                float ang = Vector3.SignedAngle(dir, Vector3.forward, Vector3.up);
                ang = Mathf.Round(ang / 90f) * 90f;   // square turns only: a side magazine (Sten) pulls the centre off the barrel's line
                inst.transform.RotateAround(bb.center, Vector3.up, ang);
                bb = WorldBounds(inst);
                Vector3 gripM = new Vector3(marker.position.x, bb.center.y - bb.extents.y * 0.2f, bb.min.z + bb.size.z * 0.35f);   // on the barrel's line
                inst.transform.position -= gripM;
                var mzM = new GameObject("Muzzle").transform;
                mzM.SetParent(root.transform, false);
                mzM.position = marker.position;
                var prefabM = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/Weapon_{name}.prefab");
                Object.DestroyImmediate(root);
                return prefabM;
            }
            // scale to real length and turn the long axis to +Z with the muzzle forward
            var b = WorldBounds(inst);
            float len = Mathf.Max(b.size.x, b.size.z);
            inst.transform.localScale *= realLength / Mathf.Max(0.001f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
            b = WorldBounds(inst);
            if (b.size.x > b.size.z) inst.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * inst.transform.rotation;
            b = WorldBounds(inst);
            if (MuzzleIsAtMinZ(inst, b)) inst.transform.rotation = Quaternion.Euler(0f, 180f, 0f) * inst.transform.rotation;
            b = WorldBounds(inst);
            // where the hand holds it: rifles and SMGs ~35% from the butt under the receiver; pistols by the grip at the back
            Vector3 grip = new Vector3(b.center.x, b.center.y - b.extents.y * gripDrop, b.min.z + b.size.z * gripFromButt);
            inst.transform.position -= grip;
            b = WorldBounds(inst);
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root.transform, false);
            muzzle.localPosition = new Vector3(b.center.x, b.center.y + b.extents.y * 0.25f, b.max.z);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/Weapon_{name}.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        static bool MuzzleIsAtMinZ(GameObject go, Bounds b)
        {
            // the muzzle end is the slimmer end: compare vertex spread in the first/last 8% of the length
            float slice = b.size.z * 0.08f;
            float minLo = float.MaxValue, maxLo = float.MinValue, minHi = float.MaxValue, maxHi = float.MinValue;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh) continue;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var w = mf.transform.TransformPoint(v);
                    if (w.z < b.min.z + slice) { minLo = Mathf.Min(minLo, w.y); maxLo = Mathf.Max(maxLo, w.y); }
                    if (w.z > b.max.z - slice) { minHi = Mathf.Min(minHi, w.y); maxHi = Mathf.Max(maxHi, w.y); }
                }
            }
            return (maxLo - minLo) < (maxHi - minHi);
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        // ------------------------------------------------------------ German soldier

        static GameObject SetupGermanModel()
        {
            string path = $"{ModelDir}/GermanSoldier/GermanSoldier.fbx";
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (!mi) { Debug.LogWarning("Stan's Game: German soldier model missing"); return null; }
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = false;
            mi.SaveAndReimport();
            var map = new Dictionary<string, Material>();
            foreach (var kv in GermanTextures)
                map[kv.Key] = LitMat("German_" + kv.Value, Tex($"{ModelDir}/GermanSoldier/Textures/{kv.Value}.png"), null, null, 0.2f);
            Remap(path, map);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            if (!avatar || !avatar.isHuman) Debug.LogWarning("Stan's Game: German soldier avatar not humanoid; check the Rig tab");
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        public static GameObject BuildSoldierPrefab(string name, GameObject model, RuntimeAnimatorController controller, Faction faction,
            GameObject weaponPrefab, string gunName, float dmg, float rpm, bool auto, int mag)
        {
            var root = new GameObject(name);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.name = "Model";
            inst.transform.SetParent(root.transform, false);
            // normalise height: 1.80 m to the top of the M40 helmet (a man of about 1.73 m)
            var b = WorldBounds(inst);
            if (b.size.y > 0.1f) inst.transform.localScale *= 1.80f / b.size.y;
            b = WorldBounds(inst);
            inst.transform.localPosition -= new Vector3(0f, b.min.y, 0f);

            var anim = inst.GetComponent<Animator>();
            if (!anim) anim = inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            var cc = root.AddComponent<CharacterController>(); cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.9f, 0f);
            var h = root.AddComponent<Health>(); h.faction = faction;

            BuildRagdoll(anim);
            inst.AddComponent<FootGrounder>().animator = anim;

            // weapon in the right hand
            Transform hand = anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
            var holderGo = new GameObject("WeaponHolder");
            holderGo.transform.SetParent(root.transform, false);
            holderGo.transform.localPosition = new Vector3(0.2f, 1.35f, 0.3f);
            GameObject gunGo;
            if (weaponPrefab) { gunGo = (GameObject)PrefabUtility.InstantiatePrefab(weaponPrefab); gunGo.transform.SetParent(holderGo.transform, false); }
            else
            {
                gunGo = new GameObject(gunName); gunGo.transform.SetParent(holderGo.transform, false);
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(body.GetComponent<Collider>());
                body.transform.SetParent(gunGo.transform, false); body.transform.localScale = new Vector3(0.05f, 0.07f, gunName == "MG42" ? 1.22f : 1.1f);
                body.transform.localPosition = new Vector3(0f, 0f, 0.25f);
                body.GetComponent<Renderer>().sharedMaterial = LitMat("Gun_Dark", null, null, null, 0.4f);
                var mz = new GameObject("Muzzle").transform; mz.SetParent(gunGo.transform, false); mz.localPosition = new Vector3(0f, 0f, 0.85f);
            }
            var w = gunGo.AddComponent<Weapon>();
            w.displayName = gunName; w.damage = dmg; w.roundsPerMinute = rpm; w.automatic = auto; w.magazineSize = mag;
            w.muzzle = gunGo.transform.Find("Muzzle");
            var src = gunGo.AddComponent<AudioSource>(); src.spatialBlend = 1f; src.maxDistance = 400f; w.audioSource = src;
            var flash = new GameObject("Muzzle light").AddComponent<Light>();
            flash.transform.SetParent(w.muzzle ? w.muzzle : gunGo.transform, false);
            flash.type = LightType.Point; flash.range = 6f; flash.intensity = 3f; flash.color = new Color(1f, 0.75f, 0.4f);
            w.muzzleLight = flash;
            var holder = holderGo.AddComponent<WeaponHolder>(); holder.hand = hand; holder.animator = anim;

            var ai = root.AddComponent<SoldierAI>(); ai.weapon = w; ai.animator = anim;
            ai.skill = faction == Faction.German ? 0.3f : 0.6f;
            var rd = root.AddComponent<Ragdoll>(); rd.animator = anim;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ------------------------------------------------------------ ragdoll

        internal static void BuildRagdoll(Animator a)
        {
            if (!a || !a.isHuman) return;
            Transform B(HumanBodyBones b) => a.GetBoneTransform(b);
            var hips = B(HumanBodyBones.Hips); var chest = B(HumanBodyBones.Chest);
            if (!chest) chest = B(HumanBodyBones.Spine);
            var head = B(HumanBodyBones.Head);
            if (!hips || !chest || !head) return;

            Rigidbody Body(Transform t, float mass)
            {
                var rb = t.gameObject.AddComponent<Rigidbody>(); rb.mass = mass; rb.isKinematic = true;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; return rb;
            }
            void Joint(Transform t, Transform parent, float lowTwist, float highTwist, float swing1, float swing2, Transform child = null)
            {
                var j = t.gameObject.AddComponent<CharacterJoint>();
                j.connectedBody = parent.GetComponent<Rigidbody>();
                if (child)
                {
                    // twist about the bone itself and swing about the body's left-right line: with Unity's default
                    // axes (the bone's local x) the arms locked out in a T after death (Joan's printscreen 12)
                    Vector3 along = t.InverseTransformDirection((child.position - t.position).normalized);
                    Vector3 side = t.InverseTransformDirection(a.transform.right);
                    side = Vector3.ProjectOnPlane(side, along);
                    if (side.sqrMagnitude < 1e-4f) side = Vector3.ProjectOnPlane(t.InverseTransformDirection(a.transform.forward), along);
                    j.axis = along; j.swingAxis = side.normalized;
                }
                j.lowTwistLimit = new SoftJointLimit { limit = lowTwist }; j.highTwistLimit = new SoftJointLimit { limit = highTwist };
                j.swing1Limit = new SoftJointLimit { limit = swing1 }; j.swing2Limit = new SoftJointLimit { limit = swing2 };
                j.enableProjection = true;
            }
            void Capsule(Transform from, Transform to, float radius)
            {
                var c = from.gameObject.AddComponent<CapsuleCollider>();
                Vector3 local = from.InverseTransformPoint(to.position);
                int dir = Mathf.Abs(local.x) > Mathf.Abs(local.y) ? (Mathf.Abs(local.x) > Mathf.Abs(local.z) ? 0 : 2) : (Mathf.Abs(local.y) > Mathf.Abs(local.z) ? 1 : 2);
                c.direction = dir; c.center = local * 0.5f;
                float lossy = Mathf.Max(0.0001f, from.lossyScale.x);
                c.radius = radius / lossy; c.height = local.magnitude + c.radius;
            }
            float Dist(Transform x, Transform y) => Vector3.Distance(x.position, y.position);

            Body(hips, 12f);
            var hb = hips.gameObject.AddComponent<BoxCollider>();
            hb.size = hips.InverseTransformVector(new Vector3(0.32f, 0.22f, 0.22f)); hb.size = new Vector3(Mathf.Abs(hb.size.x), Mathf.Abs(hb.size.y), Mathf.Abs(hb.size.z));
            Body(chest, 12f); Joint(chest, hips, -20f, 20f, 10f, 10f);
            var cb = chest.gameObject.AddComponent<BoxCollider>();
            float chestLen = Dist(chest, head) * 0.8f;
            cb.center = chest.InverseTransformPoint(Vector3.Lerp(chest.position, head.position, 0.35f));
            var cs = chest.InverseTransformVector(new Vector3(0.36f, chestLen, 0.24f)); cb.size = new Vector3(Mathf.Abs(cs.x), Mathf.Abs(cs.y), Mathf.Abs(cs.z));
            Body(head, 5f); Joint(head, chest, -40f, 25f, 25f, 0f);
            var hs = head.gameObject.AddComponent<SphereCollider>(); hs.radius = 0.12f / Mathf.Max(0.0001f, head.lossyScale.x);
            hs.center = head.InverseTransformPoint(head.position + Vector3.up * 0.08f);
            head.gameObject.name = head.gameObject.name.Contains("Head") ? head.gameObject.name : head.gameObject.name + "_Head";

            void Limb(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones end, Transform parent, float r, float massU, float massL, bool leg)
            {
                var u = B(upper); var l = B(lower); var e = B(end);
                if (!u || !l || !e) return;
                // shoulder and hip: a ball joint (arms hang and flop, legs swing forward more than sideways)
                Body(u, massU); Joint(u, parent, leg ? -20f : -45f, leg ? 20f : 45f, leg ? 75f : 95f, leg ? 35f : 70f, l); Capsule(u, l, r);
                // elbow and knee: they bend, they don't twist much or swing sideways
                Body(l, massL); Joint(l, u, -10f, 10f, leg ? 80f : 75f, 8f, e); Capsule(l, e, r * 0.85f);
            }
            Limb(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, chest, 0.06f, 2.5f, 2f, false);
            Limb(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, chest, 0.06f, 2.5f, 2f, false);
            Limb(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, hips, 0.09f, 7.5f, 4f, true);
            Limb(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, hips, 0.09f, 7.5f, 4f, true);
            // ragdoll bones should not block the living soldier's own hitscan / movement until death
            foreach (var c in hips.GetComponentsInChildren<Collider>()) c.enabled = false;
        }

        static void WriteCredits()
        {
            File.WriteAllText(Abs($"{Root}/CREDITS.txt"),
@"Stan's Game V2 - third-party assets

Bren Mk 2 - by Shorty_Digitan (Sketchfab) - CC BY 4.0
  https://sketchfab.com/3d-models/bren-mk-2-49cdc2f6f7054083a448fe54b9c07123
Soldier (German soldier) - by Vr-cvantorium (Sketchfab) - CC BY 4.0
  https://sketchfab.com/3d-models/soldier-6746867266f04bf5aed3b4f0a6df278c
Sten Mk II - downloaded by Joan to _downloads/British sten-mk-ii (add author and license here)
MG42 - downloaded by Joan (_downloads/German maschinengewehr-mg-42) (add author and license here)
MP40 - downloaded by Joan (_downloads/german - mp40.glb) (add author and license here)
Kar98k - downloaded by Joan (_downloads/German - kar-98_weapon.glb) (add author and license here)
Lee-Enfield SMLE Mk III - downloaded by Joan (_downloads/British lee-enfield_smle_mk3.glb) (add author and license here)
Hero animations - from the original Stan's Game (Unity 3rd Person Shooter demo assets)
Pegasus Bridge model - from the original Stan's Game (SketchUp export, 2012)
");
        }
    }
}
