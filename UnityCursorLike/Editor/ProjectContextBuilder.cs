using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CursorLike.Editor
{
    internal static class ProjectContextBuilder
    {
        internal static string BuildContext(bool includeProjectFiles, bool includeScripts, bool includeScene)
        {
            var sb = new StringBuilder(16 * 1024);
            sb.AppendLine("Project root: " + Directory.GetCurrentDirectory());

            if (includeProjectFiles)
            {
                AppendProjectTree(sb);
            }

            if (includeScripts)
            {
                AppendScriptsPreview(sb);
            }

            if (includeScene)
            {
                AppendSceneSnapshot(sb);
            }

            return sb.ToString();
        }

        private static void AppendProjectTree(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("## Project Files (Assets + ProjectSettings)");
            foreach (var path in Directory.EnumerateFiles("Assets", "*", SearchOption.AllDirectories))
            {
                sb.AppendLine(path.Replace("\\", "/"));
            }

            if (Directory.Exists("ProjectSettings"))
            {
                foreach (var path in Directory.EnumerateFiles("ProjectSettings", "*", SearchOption.AllDirectories))
                {
                    sb.AppendLine(path.Replace("\\", "/"));
                }
            }
        }

        private static void AppendScriptsPreview(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("## Script Preview (first lines)");

            var guids = AssetDatabase.FindAssets("t:Script", new[] { "Assets" });
            var maxScripts = Mathf.Min(20, guids.Length);
            for (var i = 0; i < maxScripts; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var lines = SafeReadFirstLines(path, 120);
                sb.AppendLine("### " + path);
                sb.AppendLine(lines);
            }
        }

        private static void AppendSceneSnapshot(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("## Scene Snapshot");
            var scene = SceneManager.GetActiveScene();
            sb.AppendLine("Active scene: " + scene.path);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                AppendGameObject(sb, root.transform, 0);
            }
        }

        private static void AppendGameObject(StringBuilder sb, Transform transform, int depth)
        {
            var prefix = new string(' ', depth * 2);
            var go = transform.gameObject;
            sb.AppendLine(prefix + "- " + BuildTransformPath(transform));
            sb.AppendLine(prefix + "  activeSelf=" + go.activeSelf + " activeInHierarchy=" + go.activeInHierarchy + " tag=" + go.tag + " layer=" + go.layer.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine(prefix + "  worldPos=" + FormatVector3(transform.position) + " localPos=" + FormatVector3(transform.localPosition));
            sb.AppendLine(prefix + "  worldEuler=" + FormatVector3(transform.eulerAngles) + " localEuler=" + FormatVector3(transform.localEulerAngles));
            sb.AppendLine(prefix + "  localScale=" + FormatVector3(transform.localScale));
            var comps = go.GetComponents<Component>();
            var compNames = new List<string>();
            for (var i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null) continue;
                compNames.Add(comps[i].GetType().Name);
            }

            sb.AppendLine(prefix + "  components=" + string.Join(",", compNames));
            for (var i = 0; i < transform.childCount; i++)
            {
                AppendGameObject(sb, transform.GetChild(i), depth + 1);
            }
        }

        private static string BuildTransformPath(Transform t)
        {
            var names = new List<string>();
            var cur = t;
            while (cur != null)
            {
                names.Insert(0, cur.name);
                cur = cur.parent;
            }

            return string.Join("/", names);
        }

        private static string FormatVector3(Vector3 v)
        {
            return v.x.ToString("R", CultureInfo.InvariantCulture) + "," +
                   v.y.ToString("R", CultureInfo.InvariantCulture) + "," +
                   v.z.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string SafeReadFirstLines(string assetPath, int maxLines)
        {
            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
            if (!File.Exists(fullPath))
            {
                return "<file not found>";
            }

            var lines = new List<string>(maxLines);
            using var stream = File.OpenRead(fullPath);
            using var reader = new StreamReader(stream);
            while (!reader.EndOfStream && lines.Count < maxLines)
            {
                lines.Add(reader.ReadLine() ?? string.Empty);
            }

            return string.Join("\n", lines);
        }
    }
}
