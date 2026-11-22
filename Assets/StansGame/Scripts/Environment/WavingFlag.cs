using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// A flag that flies from its pole: a cloth grid (hoist at the pole, x = 0, flying out along +x) moved every frame by
    /// waves that run from the pole to the fly end and grow towards it, with a little flutter on top and the cloth
    /// sagging when the wind drops. The wind itself rises and falls slowly. Built at run time (and in the editor, so the
    /// level looks right), never saved into the scene.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class WavingFlag : MonoBehaviour
    {
        public float width = 1.9f, height = 1.0f;
        public int columns = 30, rows = 14;
        [Tooltip("Waves a second along the flag.")] public float speed = 3.4f;
        [Tooltip("How far the fly end swings out of the flat (metres) in a full wind.")] public float amplitude = 0.17f;
        [Tooltip("0 calm .. 1 a good breeze; it gusts round this.")] [Range(0f, 1f)] public float wind = 0.75f;

        Mesh mesh;
        Vector3[] rest, verts;
        Renderer rend;
        float phase, t;

        void OnEnable()
        {
            rend = GetComponent<Renderer>();
            phase = (transform.position.x * 0.37f + transform.position.z * 0.21f) % 10f;   // two flags never wave in step
            Build();
            Move(0f);
        }

        void OnDisable() { if (mesh) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); } }

        void Build()
        {
            int cx = columns + 1, cy = rows + 1;
            rest = new Vector3[cx * cy]; verts = new Vector3[cx * cy];
            var uv = new Vector2[cx * cy];
            var tris = new int[columns * rows * 6];
            for (int j = 0; j < cy; j++)
                for (int i = 0; i < cx; i++)
                {
                    int k = j * cx + i;
                    float u = i / (float)columns, v = j / (float)rows;
                    rest[k] = new Vector3(u * width, (v - 0.5f) * height, 0f);
                    uv[k] = new Vector2(u, v);
                }
            int n = 0;
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < columns; i++)
                {
                    int a = j * cx + i, b = a + 1, c = a + cx, d = c + 1;
                    tris[n++] = a; tris[n++] = c; tris[n++] = b;
                    tris[n++] = b; tris[n++] = c; tris[n++] = d;
                }
            mesh = new Mesh { name = "Flag cloth", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            mesh.vertices = rest; mesh.uv = uv; mesh.triangles = tris;
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        void Update()
        {
            if (!mesh) return;
            if (Application.isPlaying && rend && !rend.isVisible) return;   // nobody looking: leave it
            Move(Application.isPlaying ? Time.deltaTime : 0.02f);
        }

        void Move(float dt)
        {
            t += dt;
            // the wind gusts: a slow swell between about half and all of 'wind'
            float gust = wind * (0.62f + 0.38f * Mathf.PerlinNoise(t * 0.11f + phase, 3.1f));
            float time = t * speed * (0.75f + 0.5f * gust) + phase;
            for (int k = 0; k < rest.Length; k++)
            {
                Vector3 r = rest[k];
                float u = r.x / width, v = r.y / height;                       // 0 at the pole .. 1 at the fly; -0.5 .. 0.5
                float reach = u * (0.55f + 0.45f * u);                          // the hoist is tied to the pole; the fly end swings most
                float wave = Mathf.Sin(u * 6.2f - time) + 0.35f * Mathf.Sin(u * 12.5f + v * 3.5f - time * 1.65f)   // the main wave and a flutter
                           + 0.15f * Mathf.Sin(u * 21f - v * 6f - time * 2.7f);
                float z = amplitude * reach * wave * (0.45f + 0.55f * gust);
                // the cloth gathers a little as it waves, and the fly end drops when the wind falls
                float x = r.x * (1f - 0.045f * Mathf.Abs(wave) * u * gust);
                float y = r.y - 0.28f * height * u * u * (1f - gust) - 0.02f * u * Mathf.Cos(u * 6.2f - time);
                verts[k] = new Vector3(x, y, z);
            }
            mesh.vertices = verts;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }
}
