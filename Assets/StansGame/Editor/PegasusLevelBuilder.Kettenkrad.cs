using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// The Sd.Kfz. 2 Kettenkrad (NSU HK 101), 23 Nov 2026: Joan's download, made game-ready by
    /// Models/Source~/kettenkrad.py (true size, flat FBX, the front wheel and each track wheel on its own hub) and
    /// kettenkrad_tex.py (the four UDIM tiles as Kettenkrad_1..4 with the baked shadows and Normandy camouflage).
    /// Drivable like the cars: E at it, W/S, A/D. The front wheel steers (steerByName: Wheel_F only), the road wheels
    /// and sprockets roll. 36 hp for 1.2 t: 70 km/h on the road. The driver sits astride, two men face backwards on
    /// the rear bench (TruckController.SeatLayout.Kettenkrad). The Luftwaffe's airborne troops brought it in 1940;
    /// in Normandy it towed light guns, ammunition trailers and field telephone cable for the infantry.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        const string KettenkradTex = PgVehicles + "/Textures/Kettenkrad";

        static GameObject Kettenkrad(Transform parent, string name, Vector3 pos, float yaw)
        {
            var go = new GameObject(name); var t = go.transform;
            t.SetParent(parent); t.SetPositionAndRotation(new Vector3(pos.x, GroundAt(pos), pos.z), Quaternion.Euler(0f, yaw, 0f));
            var model = PgModel($"{PgVehicles}/Kettenkrad.fbx", t, t.position, yaw, KettenkradTex, "Model");
            if (!model) { Debug.LogWarning("Stan's Game: Models/Vehicles/Kettenkrad.fbx not found"); Object.DestroyImmediate(go); return null; }
            // normal maps (Kettenkrad_n_N.jpg) on the four tile materials
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (!m) continue;
                    string nrm = $"{KettenkradTex}/{m.name.Replace("PG_Kettenkrad_", "")}_N.jpg";
                    if (AssetImporter.GetAtPath(nrm) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
                    var n = AssetDatabase.LoadAssetAtPath<Texture2D>(nrm);
                    if (n) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_Smoothness", 0.25f); EditorUtility.SetDirty(m); }
                }
            var wheels = model.GetComponentsInChildren<Transform>(true).Where(c => c.name.StartsWith("Wheel_")).ToList();
            // the front wheel leads, down +Z (the way W drives)
            var front = wheels.FirstOrDefault(w => w.name == "Wheel_F");
            float rz = wheels.Where(w => w != front).Select(w => t.InverseTransformPoint(w.position).z).DefaultIfEmpty(0f).Average();
            if (front && t.InverseTransformPoint(front.position).z < rz) model.transform.rotation = Quaternion.AngleAxis(180f, t.up) * model.transform.rotation;
            var b = WorldBounds(model);
            var col = go.AddComponent<BoxCollider>();
            col.center = t.InverseTransformPoint(b.center) + new Vector3(0f, 0.1f, 0f);
            var sz = t.InverseTransformVector(b.size); col.size = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y) - 0.2f, Mathf.Abs(sz.z));
            var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true;
            var tc = go.AddComponent<TruckController>();
            tc.displayName = "Kettenkrad"; tc.engineSound = "car_engine";
            tc.wheels = wheels.ToArray(); tc.wheelRadius = 0.24f; tc.steerByName = true;
            tc.maxForwardSpeed = 19.4f; tc.maxReverseSpeed = 3f; tc.acceleration = 3.0f; tc.maxSteer = 30f; tc.stepHeight = 0.45f;   // 70 km/h on the road
            tc.cameraDistance = 6f; tc.cameraHeight = 1.8f;
            tc.layout = TruckController.SeatLayout.Kettenkrad;
            NetScenery(go, false);
#if STANS_NET
            if (!go.GetComponent<NetTruck>()) go.AddComponent<NetTruck>();
#endif
            return go;
        }
    }
}
