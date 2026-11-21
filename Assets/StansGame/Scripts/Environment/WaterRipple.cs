using UnityEngine;

namespace StansGame
{
    /// <summary>Slowly drifts the water's normal map so the canal and river surface ripples and catches the moonlight.</summary>
    [RequireComponent(typeof(Renderer))]
    public class WaterRipple : MonoBehaviour
    {
        public Vector2 flow = new Vector2(0.004f, 0.012f);   // the canal flows gently north to the sea
        MaterialPropertyBlock block;
        Renderer rend;
        Vector4 baseST;

        void Awake()
        {
            rend = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
            var m = rend.sharedMaterial;
            baseST = m && m.HasProperty("_BaseMap_ST") ? m.GetVector("_BaseMap_ST") : new Vector4(1, 1, 0, 0);
        }

        void OnEnable() => Water.Register(GetComponent<Renderer>());
        void OnDisable() => Water.Unregister(GetComponent<Renderer>());

        void Update()
        {
            if (block == null) block = new MaterialPropertyBlock();   // survives a script reload during play
            if (!rend) rend = GetComponent<Renderer>();
            rend.GetPropertyBlock(block);
            Vector2 off = flow * Time.time;
            block.SetVector("_BaseMap_ST", new Vector4(baseST.x, baseST.y, baseST.z + off.x, baseST.w + off.y));
            rend.SetPropertyBlock(block);
        }
    }
}
