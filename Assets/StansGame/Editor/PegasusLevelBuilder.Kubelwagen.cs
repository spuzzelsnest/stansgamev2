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
                var glass = VehicleGlassMat();   // see who is in it
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
            // the bonnet (front wheels, Wheel_F*) must lead, down +Z: the way W drives and the front pair steers
            float fz = 0f, rz = 0f; int nf = 0, nr = 0;
            foreach (var w in wheels) { float z = go.transform.InverseTransformPoint(w.position).z; if (w.name.StartsWith("Wheel_F")) { fz += z; nf++; } else { rz += z; nr++; } }
            if (nf > 0 && nr > 0 && fz / nf < rz / nr) m.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * m.transform.localRotation;
            // the interior: the converted model lost its floor pan and seats (too thin for the polygon cut), so a
            // plank floor, two front seats and the rear bench, where TruckController seats the driver and passengers
            if (m && nf > 0)
            {
                float front = 0f; foreach (var w in wheels) if (w.name.StartsWith("Wheel_F")) front += go.transform.InverseTransformPoint(w.position).z / nf;
                float sf = front - 0.95f, sr = front - 1.85f;   // the same seat rows as TruckController.MakeSeats (Car)
                var inside = new GameObject("Interior").transform; inside.SetParent(go.transform, false);
                var seatMat = Mat("Kw_Seat", new Color(0.20f, 0.17f, 0.12f), 0.2f);
                var floorMat = Mat("Kw_Floor", new Color(0.22f, 0.19f, 0.13f), 0.1f);
                Prim(PrimitiveType.Cube, "Floor", inside, new Vector3(0f, 0.36f, (sf + sr) * 0.5f + 0.15f), Vector3.zero, new Vector3(1.24f, 0.04f, 2.05f), floorMat);
                foreach (float x in new[] { -0.33f, 0.33f })
                {
                    Prim(PrimitiveType.Cube, "Front seat", inside, new Vector3(x, 0.55f, sf), Vector3.zero, new Vector3(0.46f, 0.12f, 0.46f), seatMat);
                    Prim(PrimitiveType.Cube, "Front seat back", inside, new Vector3(x, 0.84f, sf - 0.26f), new Vector3(-10f, 0f, 0f), new Vector3(0.46f, 0.5f, 0.08f), seatMat);
                    Prim(PrimitiveType.Cube, "Front seat frame", inside, new Vector3(x, 0.44f, sf), Vector3.zero, new Vector3(0.36f, 0.12f, 0.36f), seatMat);
                }
                Prim(PrimitiveType.Cube, "Rear bench", inside, new Vector3(0f, 0.61f, sr), Vector3.zero, new Vector3(1.16f, 0.12f, 0.46f), seatMat);
                Prim(PrimitiveType.Cube, "Rear bench back", inside, new Vector3(0f, 0.9f, sr - 0.26f), new Vector3(-10f, 0f, 0f), new Vector3(1.16f, 0.5f, 0.08f), seatMat);
                Prim(PrimitiveType.Cube, "Rear bench base", inside, new Vector3(0f, 0.47f, sr), Vector3.zero, new Vector3(1.16f, 0.2f, 0.4f), seatMat);
            }
            var tc = go.AddComponent<TruckController>();
            tc.displayName = "Kübelwagen"; tc.engineSound = "car_engine";
            tc.wheels = wheels.ToArray(); tc.wheelRadius = 0.34f;
            tc.maxForwardSpeed = 22f; tc.maxReverseSpeed = 4f; tc.acceleration = 3.0f; tc.maxSteer = 36f; tc.stepHeight = 0.4f;   // Typ 82: 25 hp, 80 km/h
            tc.cameraDistance = 7f; tc.cameraHeight = 2f;
            tc.layout = TruckController.SeatLayout.Car;   // four seats: driver, front passenger, two behind
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
