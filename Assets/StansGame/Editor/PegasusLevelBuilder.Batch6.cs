using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Download batch 6 (Models/Props/SME, made by Models/Source~/downloads_batch6.py; chequer plate texture by
    /// plate_bake.py): German checkpoints with a striped post on the roads into Bénouville and Sainte-Mère-Église,
    /// entrenching tools in the trenches and pits, and "Rommelspargel" - anti-glider poles - planted in a field outside
    /// Sainte-Mère-Église and stacked ready for planting at the edge of LZ X (the Germans had only begun on the fields
    /// by the bridges that spring).
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        /// <summary>A prop from Models/Props/SME at a position relative to its parent (no ground lookup).</summary>
        static GameObject SmeLocal(string file, Transform parent, Vector3 localPos, Quaternion localRot, string name = null, bool collider = false)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{SmePropDir}/{file}.fbx");
            if (!asset) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name ?? file;
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot * Quaternion.Euler(0f, 180f, 0f) * asset.transform.rotation;
            if (collider)
            {
                var b = UsKitImporter.WorldBounds(go);
                var bc = go.AddComponent<BoxCollider>();
                bc.center = go.transform.InverseTransformPoint(b.center);
                bc.size = go.transform.InverseTransformVector(b.size);
                bc.size = new Vector3(Mathf.Abs(bc.size.x), Mathf.Abs(bc.size.y), Mathf.Abs(bc.size.z));
            }
            return go;
        }

        /// <summary>An entrenching tool leaning against a wall (tilt towards localDir).</summary>
        static void Spade(Transform parent, Vector3 localPos, float yaw, float lean = 16f) =>
            SmeLocal("TrenchSpade", parent, localPos, Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(lean, 0f, 0f), "Entrenching tool");

        /// <summary>Studded steel chequer plate (from _downloads/Textures/texture-studded-metal-floor), tiled for a plate of this size.</summary>
        static Material ChequerPlate(Vector2 size)
        {
            var m = TexMat($"Chequer_Plate_{size.x:F1}x{size.y:F1}", "ChequerPlate_d.png", Color.white, 0.4f, false, "ChequerPlate_n.png");
            m.SetFloat("_Metallic", 0.55f);
            var tiling = new Vector2(size.x / 0.09f, size.y / 0.09f);   // one repeat of the pattern about 9 cm
            m.SetTextureScale("_BaseMap", tiling); m.SetTextureScale("_BumpMap", tiling); m.mainTextureScale = tiling;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>A checkpoint pushed open onto the verge (boom along the road) with a striped post beside it.</summary>
        static void Checkpoint(Transform parent, Vector3 pos, Vector3 roadDir, string name)
        {
            roadDir.y = 0f; roadDir.Normalize();
            float yaw = Mathf.Atan2(roadDir.x, roadDir.z) * Mathf.Rad2Deg - 90f;   // the model's boom lies along its X
            SmeProp("Checkpoint", parent, pos, yaw, 0f, name);
            SmeProp("BorderPost", parent, pos + roadDir * 3.4f + Vector3.Cross(Vector3.up, roadDir) * 0.4f, Random.Range(0f, 90f), 0f, name + " - post");
        }

        /// <summary>Anti-glider poles: a planted grid (some leaning) or a stack waiting to go in.</summary>
        static void AntiGliderPoles(Transform parent, Vector3 corner, int nx, int nz, float spacing, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    Vector3 p = corner + rot * new Vector3(i * spacing + Random.Range(-2f, 2f), -0.6f, k * spacing + Random.Range(-2f, 2f));   // 0.6 m in the ground
                    SmeProp("AntiGliderPole", parent, p, Random.Range(0f, 360f), Random.Range(-5f, 5f), "Anti-glider pole (Rommelspargel)");
                }
        }

        static void PoleStack(Transform parent, Vector3 c, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            int[] rows = { 4, 3, 2 };
            for (int r = 0; r < rows.Length; r++)
                for (int i = 0; i < rows[r]; i++)
                {
                    float off = (i - (rows[r] - 1) * 0.5f) * 0.23f;
                    SmeProp("AntiGliderPoleLying", parent, c + rot * new Vector3(off, r * 0.19f, Random.Range(-0.3f, 0.3f)), yaw + Random.Range(-2f, 2f), 0f, "Anti-glider pole (stacked)", r == 0);
                }
        }

        /// <summary>Pegasus Bridge: anti-glider poles stacked at LZ X.</summary>
        static void PegasusBatch6(Transform defences)
        {
            if (!AssetDatabase.LoadAssetAtPath<GameObject>($"{SmePropDir}/Checkpoint.fbx")) return;
            var g = Group("Props (downloads, batch 6)", defences);
            Vector3 a = new Vector3(-BridgeWestEnd, 0f, 0f), bend = new Vector3(BenouvilleBend.x, 0f, BenouvilleBend.z);
            Vector3 dir = (bend - a).normalized, north = Vector3.Cross(dir, Vector3.up) * -1f;
            if (north.z < 0f) north = -north;
            // (the checkpoint on the Bénouville road was taken out - it had no business there)
            PoleStack(g, new Vector3(CanalHalfWidth + 120f, 0f, -24f), 90f);
            PoleStack(g, new Vector3(CanalHalfWidth + 126f, 0f, -26.5f), 92f);
        }

        /// <summary>Sainte-Mère-Église: checkpoints at the N13 entrances, a field of anti-glider poles south-west of the town.</summary>
        static void SmeBatch6(Transform parent)
        {
            if (!AssetDatabase.LoadAssetAtPath<GameObject>($"{SmePropDir}/Checkpoint.fbx")) return;
            var g = Group("Props (downloads, batch 6)", parent);
            Checkpoint(g, new Vector3(SmeN13X - 6.2f, 0f, -150f), Vector3.forward, "Checkpoint (N13 south)");
            Checkpoint(g, new Vector3(SmeN13X - 6.2f, 0f, 152f), Vector3.forward, "Checkpoint (N13 north)");
            AntiGliderPoles(g, new Vector3(-265f, 0f, -150f), 4, 3, 28f, 8f);
        }
    }
}
