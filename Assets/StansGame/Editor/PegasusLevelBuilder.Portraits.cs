using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StansGame.EditorTools
{
    /// <summary>
    /// The sector selection shows the three protagonists (to-do, 23 Nov): each Prefabs/Protagonists/Protagonist_&lt;side&gt;
    /// is stood in his rifle idle in a scene of his own, lit from the front left, and photographed knee-up into
    /// Resources/Menu/Protagonist_&lt;side&gt;.png (320 x 440) for LevelSelect. Made with the protagonists in the Proving
    /// Ground build, or on its own with Stan's Game > Debug > Render Protagonist Portraits.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        static readonly string[] ProtagonistSides = { "British", "American", "German" };

        [MenuItem("Stan's Game/Debug/Render Protagonist Portraits", priority = 161)]
        public static void RenderProtagonistPortraitsMenu()
        {
            var ps = new GameObject[3];
            for (int i = 0; i < 3; i++) ps[i] = AssetDatabase.LoadAssetAtPath<GameObject>($"{ProtagonistDir}/Protagonist_{ProtagonistSides[i]}.prefab");
            RenderProtagonistPortraits(ps);
        }

        static void RenderProtagonistPortraits(GameObject[] protagonists)
        {
            string dir = Root + "/Resources/Menu";
            if (!AssetDatabase.IsValidFolder(Root + "/Resources")) AssetDatabase.CreateFolder(Root, "Resources");
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Root + "/Resources", "Menu");
            const int W = 320, H = 440;
            var scene = EditorSceneManager.NewPreviewScene();
            var rt = new RenderTexture(W * 2, H * 2, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            int made = 0;
            bool sampling = false;
            try
            {
                var camGo = new GameObject("Portrait camera");
                SceneManager_Move(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene; cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.07f, 0.065f, 0.055f, 1f);   // LevelSelect's card
                cam.fieldOfView = 24f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 30f; cam.targetTexture = rt;
                var cd = cam.GetUniversalAdditionalCameraData(); cd.renderShadows = false; cd.renderPostProcessing = false;
                foreach (var (euler, power, col) in new[] { (new Vector3(25f, 150f, 0f), 1.5f, new Color(1f, 0.93f, 0.82f)),
                                                            (new Vector3(10f, 230f, 0f), 0.55f, new Color(0.7f, 0.78f, 0.95f)),
                                                            (new Vector3(-10f, 20f, 0f), 0.8f, new Color(1f, 0.85f, 0.6f)) })   // key, fill, rim
                {
                    var lg = new GameObject("Light"); SceneManager_Move(lg, scene);
                    lg.transform.rotation = Quaternion.Euler(euler);
                    var l = lg.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = power; l.color = col; l.shadows = LightShadows.None;
                }
                for (int i = 0; i < protagonists.Length && i < 3; i++)
                {
                    if (!protagonists[i]) continue;
                    var man = (GameObject)PrefabUtility.InstantiatePrefab(protagonists[i], scene);
                    man.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 160f, 0f));   // turned a little to his left
                    // his rifle idle, half a second in (else he stands in the bind pose)
                    var anim = man.GetComponentInChildren<Animator>();
                    AnimationClip idle = null;
                    if (anim && anim.runtimeAnimatorController)
                        foreach (var c in anim.runtimeAnimatorController.animationClips)
                        {
                            string cn = c ? c.name.ToLowerInvariant() : "";
                            if (!cn.Contains("idle") || cn.Contains("crouch") || cn.Contains("kneel") || cn.Contains("prone") || cn.Contains("sit") || cn.Contains("injur") || cn.Contains("wound")) continue;
                            if (idle == null || (cn.Contains("rifle") && !idle.name.ToLowerInvariant().Contains("rifle"))) idle = c;
                        }
                    if (anim && !idle && anim.runtimeAnimatorController)
                        foreach (var c in anim.runtimeAnimatorController.animationClips)
                            if (c && c.name.ToLowerInvariant().Contains("idle")) { idle = c; break; }
                    if (anim && idle)
                    {
                        Debug.Log($"Stan's Game: portrait of {ProtagonistSides[i]} in '{idle.name}'");
                        if (!AnimationMode.InAnimationMode()) { AnimationMode.StartAnimationMode(); sampling = true; }
                        AnimationMode.BeginSampling(); AnimationMode.SampleAnimationClip(anim.gameObject, idle, 0.5f); AnimationMode.EndSampling();
                    }
                    // frame him from the knees to just over the helmet
                    // the weapons are put in the hands by IK at run time - here they would float: leave them out
                    foreach (var w in man.GetComponentsInChildren<Weapon>(true)) w.gameObject.SetActive(false);
                    var rs = man.GetComponentsInChildren<SkinnedMeshRenderer>();
                    if (rs.Length == 0) { Object.DestroyImmediate(man); continue; }
                    Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    float top = b.max.y + 0.06f, bottom = b.min.y + b.size.y * 0.32f, mid = (top + bottom) / 2f, span = top - bottom;
                    float dist = span / 2f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                    cam.transform.SetPositionAndRotation(new Vector3(b.center.x, mid + 0.05f, b.center.z - dist), Quaternion.Euler(-1.5f, 0f, 0f));
                    var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                    if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                    else cam.Render();
                    // read back at half size (the 2x render smooths the edges)
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    var big = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                    big.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); big.Apply();
                    RenderTexture.active = prev;
                    var small = new Texture2D(W, H, TextureFormat.RGBA32, false);
                    var px = new Color[W * H];
                    for (int y = 0; y < H; y++)
                        for (int x = 0; x < W; x++)
                            px[y * W + x] = (big.GetPixel(2 * x, 2 * y) + big.GetPixel(2 * x + 1, 2 * y) + big.GetPixel(2 * x, 2 * y + 1) + big.GetPixel(2 * x + 1, 2 * y + 1)) * 0.25f;
                    small.SetPixels(px); small.Apply();
                    string path = $"{dir}/Protagonist_{ProtagonistSides[i]}.png";
                    File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), path), small.EncodeToPNG());
                    Object.DestroyImmediate(big); Object.DestroyImmediate(small);
                    Object.DestroyImmediate(man);
                    AssetDatabase.ImportAsset(path);
                    if (AssetImporter.GetAtPath(path) is TextureImporter ti)
                    {
                        ti.textureType = TextureImporterType.Default; ti.mipmapEnabled = false; ti.alphaIsTransparency = false;
                        ti.textureCompression = TextureImporterCompression.Uncompressed; ti.SaveAndReimport();
                    }
                    made++;
                }
            }
            catch (System.Exception e) { Debug.LogWarning("Stan's Game: protagonist portraits failed - " + e.Message); }
            finally
            {
                if (sampling && AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
                EditorSceneManager.ClosePreviewScene(scene);
                rt.Release(); Object.DestroyImmediate(rt);
            }
            Debug.Log($"Stan's Game: {made} protagonist portraits for the sector selection (Resources/Menu)");
        }

        static void SceneManager_Move(GameObject go, UnityEngine.SceneManagement.Scene scene) =>
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
    }
}
