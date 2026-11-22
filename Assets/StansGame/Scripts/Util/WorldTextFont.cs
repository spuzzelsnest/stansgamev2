using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// On a TextMesh that uses the StansGame/WorldText material: keeps the material pointing at the font's current
    /// texture. A built-in font redraws its letter sheet as new characters are needed, and a material of our own
    /// isn't told, so the letters would turn to blocks.
    /// </summary>
    [RequireComponent(typeof(TextMesh))]
    public class WorldTextFont : MonoBehaviour
    {
        static Material shared;
        TextMesh tm;

        void Awake()
        {
            tm = GetComponent<TextMesh>();
            Font.textureRebuilt += Rebuilt;
            Apply();
        }

        void OnDestroy() => Font.textureRebuilt -= Rebuilt;

        void Rebuilt(Font f) { if (tm && f == tm.font) Apply(); }

        void Apply()
        {
            if (!tm || !tm.font) return;
            var r = GetComponent<MeshRenderer>();
            if (!r || !r.sharedMaterial) return;
            if (r.sharedMaterial.shader.name != "StansGame/WorldText") return;
            r.sharedMaterial.mainTexture = tm.font.material.mainTexture;
        }
    }
}
