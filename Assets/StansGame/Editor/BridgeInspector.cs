using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>Writes every part of the bridge model with its bounds and the ground under it to _downloads/previews/bridge_parts.txt.</summary>
    public static class BridgeInspector
    {
        [MenuItem("Stan's Game/Debug/List Bridge Parts", priority = 50)]
        public static void ListParts()
        {
            var root = GameObject.Find("Bridge");
            var sb = new StringBuilder();
            Physics.SyncTransforms();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var b = r.bounds;
                if (Mathf.Abs(b.center.x) > 45f || Mathf.Abs(b.center.z) > 30f) continue;
                string path = r.name; var t = r.transform.parent;
                for (int i = 0; i < 3 && t; i++, t = t.parent) path = t.name + "/" + path;
                float ground = float.NaN;
                Vector3 foot = new Vector3(b.center.x, b.min.y + 0.05f, b.center.z);
                foreach (var h in Physics.RaycastAll(foot + Vector3.up * 0.0f, Vector3.down, 30f))
                    if (!h.collider.transform.IsChildOf(r.transform) && (float.IsNaN(ground) || h.point.y > ground)) ground = h.point.y;
                sb.AppendLine($"{path}\tcentre {b.center.x:F1},{b.center.y:F1},{b.center.z:F1}\tsize {b.size.x:F1},{b.size.y:F1},{b.size.z:F1}\tminY {b.min.y:F2}\tground {ground:F2}");
            }
            string file = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "_downloads", "previews", "bridge_parts.txt"));
            File.WriteAllText(file, sb.ToString());
            Debug.Log("Stan's Game: bridge parts written to " + file);
        }
    }
}
