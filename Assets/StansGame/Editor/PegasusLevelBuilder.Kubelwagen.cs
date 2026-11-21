using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// The VW Kübelwagen (Joan's download, converted by Models/Source~/kubelwagen.py: 1.15M faces cut to 45k, no textures,
    /// so coloured by part: Dunkelgelb body, black tyres, dark metal, canvas, glass). The Germans' small staff car:
    /// Major Schmidt's and Sergeant Hickmann's cars in the History film, and a parked one in Bénouville.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        static GameObject Kubelwagen(Transform parent, string name, Vector3 pos, float yaw, bool active = true, bool networked = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(new Vector3(pos.x, GroundAt(pos), pos.z), Quaternion.Euler(0f, yaw, 0f));
            var m = PlaceModel("Kubelwagen", go.transform, 3.74f);
            if (!m) Debug.LogWarning("Stan's Game: Models/Vehicles/Kubelwagen.fbx not found");
            else
            {
                var body = Mat("Kw_Body", new Color(0.55f, 0.47f, 0.30f), 0.2f, 0.1f);
                var tyre = Mat("Kw_Tyre", new Color(0.05f, 0.05f, 0.05f), 0.05f);
                var metal = Mat("Kw_Metal", new Color(0.12f, 0.12f, 0.12f), 0.35f, 0.6f);
                var canvas = Mat("Kw_Canvas", new Color(0.36f, 0.33f, 0.24f), 0.05f);
                var seat = Mat("Kw_Seat", new Color(0.20f, 0.17f, 0.12f), 0.2f);
                var wood = Mat("Kw_Wood", new Color(0.30f, 0.22f, 0.14f), 0.1f);
                var glass = GlassMat();
                foreach (var r in m.GetComponentsInChildren<Renderer>(true))
                {
                    var ms = r.sharedMaterials;
                    for (int i = 0; i < ms.Length; i++)
                    {
                        string n = ms[i] ? ms[i].name : "";
                        ms[i] = n.Contains("Tyre") ? tyre : n.Contains("Metal") ? metal : n.Contains("Canvas") ? canvas : n.Contains("Seat") ? seat
                              : n.Contains("Wood") ? wood : n.Contains("Glass") ? glass : body;
                    }
                    r.sharedMaterials = ms;
                }
            }
            var col = go.AddComponent<BoxCollider>(); col.center = new Vector3(0f, 0.8f, 0f); col.size = new Vector3(1.6f, 1.5f, 3.74f);
            // drivable like the lorries: walk up, E; the front wheels steer, all four roll (Wheel_FL/FR/RL/RR, pivots on the hubs)
            var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true;
            var wheels = new System.Collections.Generic.List<Transform>();
            if (m) foreach (var t in m.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("Wheel_")) wheels.Add(t);
            var tc = go.AddComponent<TruckController>();
            tc.displayName = "Kübelwagen"; tc.engineSound = "car_engine";
            tc.wheels = wheels.ToArray(); tc.wheelRadius = 0.34f;
            tc.maxForwardSpeed = 22f; tc.acceleration = 4.2f; tc.maxSteer = 36f; tc.stepHeight = 0.4f;
            tc.cameraDistance = 7f; tc.cameraHeight = 2f;
            if (networked)
            {
                NetScenery(go, false);
#if STANS_NET
                if (!go.GetComponent<NetTruck>()) go.AddComponent<NetTruck>();
#endif
            }
            go.SetActive(active);
            return go;
        }
    }
}
