using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// The 1944 field pattern between the canal and the Orne (Joan's high vertical "Pic_BridgesAerial_high.jpg"):
    /// long, straight lines of tall trees and hedge running parallel to the canal, 70-80 m apart, broken where the
    /// Ranville road crosses them and at the field gates. LZ X by the bridge and the Orne gliders' fields stay open.
    /// Added 21 Nov 2026 (Joan: "more trees, as historical as possible"); the LiDAR is the bare ground, so the rows
    /// come from the photo.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        static void PegasusFieldRows(Transform trees, Material poplarBark, Material leaves, Material leaves2, Material bark, Material broad)
        {
            var g = Group("Field boundaries 1944 (tree lines and hedges)", trees);
            var keepOpen = new System.Collections.Generic.List<Vector3>();
            foreach (var r in GliderRests) keepOpen.Add(r.pos);
            keepOpen.Add(new Vector3(356f, 0f, 190f)); keepOpen.Add(new Vector3(272f, 0f, 730f));   // the Orne gliders 96 and 95
            bool Clear(Vector3 p)
            {
                foreach (var k in keepOpen) if ((new Vector2(p.x - k.x, p.z - k.z)).magnitude < 45f) return false;
                if (Mathf.Abs(p.z - RoadZ(p.x)) < 15f) return false;                  // the Ranville road
                if (p.x > OrneCentreX(p.z) - 40f) return false;                       // the Orne and its bank
                if (p.x < CanalHalfWidth + 150f && p.z < 0f && p.z > -340f) return false;   // LZ X and the orchard
                return true;
            }
            float half = TerrainSize * 0.5f - 20f;
            foreach (float x in new[] { 215f, 295f, 375f, 455f, 525f })
            {
                float gate = Random.Range(-400f, 400f);
                for (float z = -half; z < half; z += 11f)
                {
                    var p = new Vector3(x + Mathf.Sin(z * 0.004f + x) * 4f + Random.Range(-1.2f, 1.2f), 0f, z + Random.Range(-2f, 2f));
                    if (!Clear(p) || Mathf.Abs(z - gate) < 12f) continue;
                    if (Random.value < 0.8f) TallPoplar(g, p, poplarBark, Random.value < 0.5f ? leaves : leaves2);
                    else Broadleaf(g, p, bark, broad);
                }
                // the hedge under the trees, in lengths, with gaps
                for (float z = -half; z < half - 60f; z += 60f)
                {
                    var a = new Vector3(x, 0f, z + 4f); var b = new Vector3(x, 0f, z + 52f);
                    if (Clear(a) && Clear(b) && Clear((a + b) * 0.5f)) Hedgerow(g, a, b);
                }
            }
        }
    }
}
