using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace StansGame.EditorTools
{
    /// <summary>
    /// The intro's cast-off (Joan, Nov 2026, from the "glider in tow" photograph): a Halifax towing Horsa 91 high
    /// above the map, switched off until the film needs it. The glider rides in the high-tow position on a Y-bridle
    /// from both wing roots, the bridle joined to the long tow rope that runs to the Halifax's tail.
    /// CinematicDirector.TowAndCastOff flies them, drops the rope and sends the Halifax away.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        /// <summary>Where the shot starts: high over the north edge of the map, flying south (game -Z) toward the bridges.</summary>
        static readonly Vector3 TowShotStart = new Vector3(120f, 650f, 1500f);

        static void BuildTowShot(CinematicDirector cine, Transform parent)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Models/Aircraft/Halifax.fbx");
            if (!model) { Debug.LogWarning("Stan's Game: Halifax.fbx missing - the intro has no tow"); return; }

            var group = new GameObject("Film: Halifax towing Horsa 91 over the coast");
            group.transform.SetParent(parent);
            group.transform.SetPositionAndRotation(TowShotStart, Quaternion.Euler(0f, 180f, 0f));

            // the tug, ahead and a little below (the glider flew "high tow", above the slipstream)
            var tug = new GameObject("Halifax tug").transform;
            tug.SetParent(group.transform, false);
            tug.localPosition = new Vector3(0f, -9f, 90f);
            var body = (GameObject)PrefabUtility.InstantiatePrefab(model, tug);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation;   // nose to +Z
            tug.gameObject.AddComponent<PropellerSpin>();
            var hitch = new GameObject("Tow hitch (tail)").transform;
            hitch.SetParent(tug, false);
            hitch.localPosition = new Vector3(0f, -0.8f, -10.9f);

            // the glider, chalk 91 (Wallwork and Ainsworth, Howard and Brotheridge's platoon)
            var horsa = BuildHorsa("Horsa 91 on tow", group.transform, 91);
            horsa.transform.localPosition = Vector3.zero;
            horsa.transform.localRotation = Quaternion.identity;
            foreach (var ps in horsa.GetComponentsInChildren<ParticleSystem>(true)) Object.DestroyImmediate(ps.gameObject);   // no skid dust at 6,000 ft
            foreach (var c in group.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            Transform Point(string n, Vector3 p) { var t = new GameObject(n).transform; t.SetParent(horsa.transform, false); t.localPosition = p; return t; }
            var port = Point("Bridle (port wing root)", new Vector3(-1.3f, HorsaCy + 1.45f, 3.9f));
            var stbd = Point("Bridle (starboard wing root)", new Vector3(1.3f, HorsaCy + 1.45f, 3.9f));

            // the rope: pale hemp against the night sky
            string ropePath = $"{MatDir}/Tow_Rope.mat";
            var ropeMat = AssetDatabase.LoadAssetAtPath<Material>(ropePath);
            if (!ropeMat)
            {
                ropeMat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
                AssetDatabase.CreateAsset(ropeMat, ropePath);
            }
            ropeMat.SetColor("_BaseColor", new Color(0.42f, 0.40f, 0.36f)); ropeMat.color = new Color(0.42f, 0.40f, 0.36f);
            EditorUtility.SetDirty(ropeMat);
            LineRenderer Line(string n, int points, float width)
            {
                var lr = new GameObject(n).AddComponent<LineRenderer>();
                lr.transform.SetParent(group.transform, false);
                lr.useWorldSpace = true; lr.positionCount = points; lr.widthMultiplier = width;
                lr.sharedMaterial = ropeMat; lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false;
                lr.numCapVertices = 2; lr.alignment = LineAlignment.View;
                return lr;
            }
            var rope = Line("Tow rope", 18, 0.16f);
            var bridle = Line("Tow bridle", 3, 0.12f);

            // a little extra moonlight on the two aircraft while they're on screen (they're dark-painted)
            var moon = new GameObject("Moonlight on the aircraft").AddComponent<Light>();
            moon.transform.SetParent(group.transform, false);
            moon.transform.rotation = Quaternion.Euler(28f, 150f, 0f);
            moon.type = LightType.Directional; moon.intensity = 0.55f; moon.color = new Color(0.78f, 0.84f, 1f);
            moon.shadows = LightShadows.None;

            cine.towGroup = group;
            cine.towHalifax = tug; cine.towHitch = hitch;
            cine.towHorsa = horsa.transform; cine.bridlePort = port; cine.bridleStarboard = stbd;
            cine.towRope = rope; cine.towBridle = bridle;
            group.SetActive(false);
        }
    }
}
