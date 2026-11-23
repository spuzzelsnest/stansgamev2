using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StansGame
{
    /// <summary>
    /// Reflections in the water (to-do 6, 23 Nov): a second camera, mirrored under the surface of the water nearest the
    /// player's camera, renders the world into a texture at half resolution each frame; the StansGame/Water shader
    /// shows it, wobbled by the ripples. Made by the first WaterRipple that wakes; the water itself (layer 4, "Water")
    /// is left out of its own reflection. No shadows and no post-processing in the mirror, and it stops at 400 m.
    /// </summary>
    public class PlanarReflection : MonoBehaviour
    {
        public float resolutionScale = 0.5f;
        public float farClip = 400f;
        public float maxDistance = 250f;     // further from any water than this: no reflection rendered

        static PlanarReflection instance;
        static readonly int TexId = Shader.PropertyToID("_PlanarReflectionTex");
        static readonly int HeightId = Shader.PropertyToID("_PlanarReflectionHeight");
        static readonly int OnId = Shader.PropertyToID("_PlanarReflectionOn");
        const int WaterLayer = 4;

        Camera mirror;
        RenderTexture rt;

        public static void Ensure()
        {
            if (instance || !Application.isPlaying) return;
            var go = new GameObject("Planar reflection (water)");
            instance = go.AddComponent<PlanarReflection>();
        }

        void OnDisable()
        {
            Shader.SetGlobalFloat(OnId, 0f);
            if (rt) { rt.Release(); Destroy(rt); rt = null; }
            if (instance == this) instance = null;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (!cam || !Water.NearestSurface(cam.transform.position, maxDistance, out float h) || cam.transform.position.y < h + 0.05f)
            {
                Shader.SetGlobalFloat(OnId, 0f);
                return;
            }
            int w = Mathf.Max(64, Mathf.RoundToInt(cam.pixelWidth * resolutionScale)), hgt = Mathf.Max(64, Mathf.RoundToInt(cam.pixelHeight * resolutionScale));
            if (!rt || rt.width != w || rt.height != hgt)
            {
                if (rt) { rt.Release(); Destroy(rt); }
                rt = new RenderTexture(w, hgt, 24, RenderTextureFormat.DefaultHDR) { name = "Water reflection", useMipMap = false };
                rt.Create();
            }
            if (!mirror)
            {
                var go = new GameObject("Mirror camera");
                go.transform.SetParent(transform, false);
                mirror = go.AddComponent<Camera>();
                mirror.enabled = false;
                var data = mirror.GetUniversalAdditionalCameraData();
                data.renderShadows = false; data.renderPostProcessing = false;
                data.requiresColorTexture = false; data.requiresDepthTexture = false;
            }
            mirror.CopyFrom(cam);
            mirror.enabled = false;
            mirror.targetTexture = rt;
            mirror.cullingMask = cam.cullingMask & ~(1 << WaterLayer);
            mirror.farClipPlane = Mathf.Min(cam.farClipPlane, farClip);
            var p = cam.transform.position;
            mirror.transform.SetPositionAndRotation(new Vector3(p.x, 2f * h - p.y, p.z), cam.transform.rotation);

            // mirror the view in the plane y = h, and clip everything below the water (an oblique near plane)
            var plane = new Vector4(0f, 1f, 0f, -h);
            mirror.worldToCameraMatrix = cam.worldToCameraMatrix * Reflection(plane);
            Matrix4x4 m = mirror.worldToCameraMatrix;
            Vector3 cpos = m.MultiplyPoint(new Vector3(0f, h + 0.03f, 0f));
            Vector3 cnormal = m.MultiplyVector(Vector3.up).normalized;
            mirror.projectionMatrix = cam.CalculateObliqueMatrix(new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal)));

            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(mirror, request))
            {
                GL.invertCulling = true;    // the mirror turns the triangles' winding round
                try { RenderPipeline.SubmitRenderRequest(mirror, request); }
                finally { GL.invertCulling = false; }
                Shader.SetGlobalTexture(TexId, rt);
                Shader.SetGlobalFloat(HeightId, h);
                Shader.SetGlobalFloat(OnId, 1f);
            }
            else Shader.SetGlobalFloat(OnId, 0f);
        }

        static Matrix4x4 Reflection(Vector4 p)
        {
            var r = Matrix4x4.identity;
            r.m00 = 1f - 2f * p.x * p.x; r.m01 = -2f * p.x * p.y;      r.m02 = -2f * p.x * p.z;      r.m03 = -2f * p.w * p.x;
            r.m10 = -2f * p.y * p.x;      r.m11 = 1f - 2f * p.y * p.y; r.m12 = -2f * p.y * p.z;      r.m13 = -2f * p.w * p.y;
            r.m20 = -2f * p.z * p.x;      r.m21 = -2f * p.z * p.y;      r.m22 = 1f - 2f * p.z * p.z; r.m23 = -2f * p.w * p.z;
            r.m30 = 0f; r.m31 = 0f; r.m32 = 0f; r.m33 = 1f;
            return r;
        }
    }
}
