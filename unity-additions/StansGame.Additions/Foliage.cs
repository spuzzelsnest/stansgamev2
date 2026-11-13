using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StansGame.Additions
{
    /// <summary>
    /// "Detailed foliage" setting: grass and trees that hold up at night and in the distance.
    /// <para>Grass: the terrain's two grass billboards get generated tufts of thin blades with gaps between
    /// them (the originals were dense at the root and read as dark cards with bright tips), colours closer
    /// to the turf so the grass sits in the ground instead of on it, a calmer waving tint, and grass drawn
    /// out to 130 m instead of 110 m.</para>
    /// <para>Trees and hedges: every alpha-clipped leaf texture gets coverage-preserving, fringe-free
    /// mipmaps (<see cref="FoliageTextures"/>), so crowns stay full in the distance instead of breaking up
    /// into speckle.</para>
    /// The textures are made once, a few per frame, when the level first loads. Turning the setting off
    /// puts the original textures, colours and distances back.
    /// </summary>
    public sealed class Foliage : MonoBehaviour
    {
        const float DetailDistance = 130f;

        sealed class TerrainState
        {
            public DetailPrototype[] details;
            public float detailDistance;
            public Color wavingTint;
            public float wavingAmount;
        }

        // Originals, kept for good: terrain data and materials are assets that outlive a level reload.
        static readonly Dictionary<TerrainData, TerrainState> terrains = new Dictionary<TerrainData, TerrainState>();
        static readonly Dictionary<Material, Texture> leafTextures = new Dictionary<Material, Texture>();
        static readonly Dictionary<Texture, Texture2D> fixedTextures = new Dictionary<Texture, Texture2D>();
        static Texture2D shortGrass, longGrass;

        readonly List<Terrain> levelTerrains = new List<Terrain>();
        MissionDirector level;
        bool on, working;
        float nextCheck;

        public static bool Applied { get; private set; }
        public static int LeafTextures { get { return fixedTextures.Count; } }

        void Update()
        {
            if (Time.unscaledTime < nextCheck || working) return;
            nextCheck = Time.unscaledTime + 0.5f;

            MissionDirector md = MissionDirector.Instance;
            if (md == null) return;
            if (md != level)
            {
                level = md;
                on = Applied = false;
                levelTerrains.Clear();
                levelTerrains.AddRange(FindObjectsByType<Terrain>(FindObjectsSortMode.None));
                foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (r is ParticleSystemRenderer) continue;
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m == null || leafTextures.ContainsKey(m) || !m.HasProperty("_AlphaClip") || !m.HasProperty("_BaseMap")) continue;
                        if (m.GetFloat("_AlphaClip") < 0.5f || m.GetTexture("_BaseMap") == null) continue;
                        leafTextures[m] = m.GetTexture("_BaseMap");
                    }
                }
                foreach (Terrain t in levelTerrains)
                {
                    TerrainData d = t.terrainData;
                    if (d == null || terrains.ContainsKey(d)) continue;
                    terrains[d] = new TerrainState { details = d.detailPrototypes, detailDistance = t.detailObjectDistance, wavingTint = d.wavingGrassTint, wavingAmount = d.wavingGrassAmount };
                }
            }

            bool want = GameSettings.DetailedFoliage;
            if (want != on) StartCoroutine(Set(want));
        }

        IEnumerator Set(bool want)
        {
            working = true;
            if (want)
            {
                // Make what is missing, a texture per frame so the level does not stall.
                if (shortGrass == null) { shortGrass = FoliageTextures.Grass(11, 46, 0.45f, 0.16f, 0.10f, "Grass tuft (Stan's Game additions)"); yield return null; }
                if (longGrass == null) { longGrass = FoliageTextures.Grass(23, 24, 0.62f, 0.26f, 0.35f, "Long grass tuft (Stan's Game additions)"); yield return null; }
                foreach (KeyValuePair<Material, Texture> kv in new List<KeyValuePair<Material, Texture>>(leafTextures))
                {
                    if (kv.Key == null || kv.Value == null || fixedTextures.ContainsKey(kv.Value)) continue;
                    float cutoff = kv.Key.HasProperty("_Cutoff") ? kv.Key.GetFloat("_Cutoff") : 0.5f;
                    float t0 = Time.realtimeSinceStartup;
                    fixedTextures[kv.Value] = FoliageTextures.WithCoverageMips(kv.Value, cutoff);
                    Debug.Log("Stan's Game: coverage mipmaps for " + kv.Value.name + " (" + kv.Value.width + "x" + kv.Value.height + ") in " + ((Time.realtimeSinceStartup - t0) * 1000f).ToString("0") + " ms");
                    yield return null;
                }
                if (GameSettings.DetailedFoliage) Apply(true);
            }
            else Apply(false);
            working = false;
        }

        void Apply(bool want)
        {
            foreach (KeyValuePair<Material, Texture> kv in leafTextures)
            {
                Material m = kv.Key;
                Texture2D t;
                if (m == null || kv.Value == null) continue;
                Texture use = want && fixedTextures.TryGetValue(kv.Value, out t) ? t : kv.Value;
                m.SetTexture("_BaseMap", use);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", use);
            }

            foreach (Terrain t in levelTerrains)
            {
                TerrainState s;
                if (t == null || t.terrainData == null || !terrains.TryGetValue(t.terrainData, out s)) continue;
                TerrainData d = t.terrainData;
                if (want)
                {
                    DetailPrototype[] dp = d.detailPrototypes;   // copies
                    for (int i = 0; i < dp.Length; i++)
                    {
                        if (dp[i].renderMode != DetailRenderMode.GrassBillboard || dp[i].usePrototypeMesh) continue;
                        bool tall = i > 0 || (dp[i].prototypeTexture != null && dp[i].prototypeTexture.name.ToLowerInvariant().Contains("long"));
                        dp[i].prototypeTexture = tall ? longGrass : shortGrass;
                        if (tall)
                        {
                            dp[i].healthyColor = new Color(0.40f, 0.45f, 0.27f);
                            dp[i].dryColor = new Color(0.52f, 0.48f, 0.31f);
                            dp[i].minWidth = 0.40f; dp[i].maxWidth = 0.75f;
                            dp[i].minHeight = 0.40f; dp[i].maxHeight = 0.75f;
                        }
                        else
                        {
                            dp[i].healthyColor = new Color(0.38f, 0.46f, 0.26f);
                            dp[i].dryColor = new Color(0.47f, 0.46f, 0.29f);
                            dp[i].minWidth = 0.35f; dp[i].maxWidth = 0.60f;
                            dp[i].minHeight = 0.20f; dp[i].maxHeight = 0.40f;
                        }
                    }
                    d.detailPrototypes = dp;
                    d.wavingGrassTint = new Color(0.80f, 0.82f, 0.74f);
                    d.wavingGrassAmount = Mathf.Min(s.wavingAmount, 0.3f);
                    t.detailObjectDistance = Mathf.Max(s.detailDistance, DetailDistance);
                }
                else
                {
                    d.detailPrototypes = s.details;
                    d.wavingGrassTint = s.wavingTint;
                    d.wavingGrassAmount = s.wavingAmount;
                    t.detailObjectDistance = s.detailDistance;
                }
                d.RefreshPrototypes();
                t.Flush();
            }
            on = want;
            Applied = want;
            Debug.Log("Stan's Game: detailed foliage " + (want ? "on" : "off") + " (" + fixedTextures.Count + " leaf textures, " + levelTerrains.Count + " terrains)");
        }
    }
}
