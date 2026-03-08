using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CursorLike.Editor
{
    internal static class EditorActionExecutor
    {
        private const string StartTag = "```cursor-actions";
        private const string EndTag = "```";

        internal static bool TryExtractActions(string assistantContent, out List<EditorAction> actions, out string parseError)
        {
            actions = new List<EditorAction>();
            parseError = string.Empty;
            var offset = 0;
            var blockIndex = 0;
            var foundAny = false;

            while (offset < assistantContent.Length)
            {
                var startIndex = assistantContent.IndexOf(StartTag, offset, StringComparison.OrdinalIgnoreCase);
                if (startIndex < 0)
                {
                    break;
                }

                foundAny = true;
                blockIndex++;
                var payloadStart = assistantContent.IndexOf('\n', startIndex);
                if (payloadStart < 0) { parseError = "Malformed cursor-actions block."; return false; }
                var endIndex = assistantContent.IndexOf(EndTag, payloadStart + 1, StringComparison.Ordinal);
                if (endIndex < 0) { parseError = "cursor-actions block is not closed."; return false; }
                var payload = assistantContent.Substring(payloadStart + 1, endIndex - payloadStart - 1);

                var blockActions = new List<EditorAction>();
                if (!ParsePayload(payload, blockActions, out var blockError))
                {
                    parseError = "Block " + blockIndex + ": " + blockError;
                    return false;
                }

                actions.AddRange(blockActions);
                offset = endIndex + EndTag.Length;
            }

            return foundAny;
        }

        internal static string Preview(List<EditorAction> actions)
        {
            var sb = new StringBuilder("Actions detected:\n");
            for (var i = 0; i < actions.Count; i++)
            {
                var a = actions[i];
                var details = string.IsNullOrWhiteSpace(a.Arg1) ? a.Arg0 : a.Arg0 + " -> " + a.Arg1;
                sb.AppendLine((i + 1) + ". " + a.Type + " " + details);
            }
            return sb.ToString();
        }

        internal static ExecutionReport Execute(List<EditorAction> actions)
        {
            var report = new ExecutionReport();
            foreach (var action in actions) ApplySingle(action, report);
            AssetDatabase.Refresh();
            if (EditorSceneManager.GetActiveScene().isLoaded && report.HasMutations) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            return report;
        }

        internal static void Apply(List<EditorAction> actions)
        {
            Execute(actions);
        }

        private static void ApplySingle(EditorAction a, ExecutionReport report)
        {
            switch (a.Type)
            {
                case EditorActionType.WriteFile: WriteFile(a.Arg0, a.Content, true); report.MarkMutation("WRITE_FILE " + a.Arg0); return;
                case EditorActionType.AppendFile: WriteFile(a.Arg0, a.Content, false); report.MarkMutation("APPEND_FILE " + a.Arg0); return;
                case EditorActionType.InsertInFile: InsertInFile(a.Arg0, a.LineNumber, a.Content); report.MarkMutation("INSERT_IN_FILE " + a.Arg0); return;
                case EditorActionType.ReplaceInFile: ReplaceInFile(a.Arg0, a.SearchText, a.ReplaceText); report.MarkMutation("REPLACE_IN_FILE " + a.Arg0); return;
                case EditorActionType.DeleteFile: DeleteFile(a.Arg0); report.MarkMutation("DELETE_FILE " + a.Arg0); return;
                case EditorActionType.MoveFile: MovePath(a.Arg0, a.Arg1); report.MarkMutation("MOVE_FILE " + a.Arg0 + " -> " + a.Arg1); return;
                case EditorActionType.CopyFile: CopyFile(a.Arg0, a.Arg1); report.MarkMutation("COPY_FILE " + a.Arg0 + " -> " + a.Arg1); return;
                case EditorActionType.CreateDir: Directory.CreateDirectory(ValidateProjectPath(a.Arg0)); report.MarkMutation("CREATE_DIR " + a.Arg0); return;
                case EditorActionType.DeleteDir: if (Directory.Exists(ValidateProjectPath(a.Arg0))) Directory.Delete(ValidateProjectPath(a.Arg0), true); report.MarkMutation("DELETE_DIR " + a.Arg0); return;
                case EditorActionType.CreateGameObject: CreateGameObject(a.Arg0); report.MarkMutation("CREATE_GAMEOBJECT " + a.Arg0); return;
                case EditorActionType.CreateChild: CreateChild(a.Arg0, a.Arg1); report.MarkMutation("CREATE_CHILD " + a.Arg0 + " -> " + a.Arg1); return;
                case EditorActionType.DuplicateGameObject: DuplicateGameObject(a.Arg0); report.MarkMutation("DUPLICATE_GAMEOBJECT " + a.Arg0); return;
                case EditorActionType.RenameGameObject: RenameGameObject(a.Arg0, a.Arg1); report.MarkMutation("RENAME_GAMEOBJECT " + a.Arg0 + " -> " + a.Arg1); return;
                case EditorActionType.SetTransform: SetTransform(a.Arg0, a.VectorValue); report.MarkMutation("SET_TRANSFORM " + a.Arg0); return;
                case EditorActionType.SetLocalTransform: SetLocalTransform(a.Arg0, a.VectorValue); report.MarkMutation("SET_LOCAL_TRANSFORM " + a.Arg0); return;
                case EditorActionType.SetRotation: SetRotation(a.Arg0, a.VectorValue, false); report.MarkMutation("SET_ROTATION " + a.Arg0); return;
                case EditorActionType.SetLocalRotation: SetRotation(a.Arg0, a.VectorValue, true); report.MarkMutation("SET_LOCAL_ROTATION " + a.Arg0); return;
                case EditorActionType.SetScale: SetScale(a.Arg0, a.VectorValue); report.MarkMutation("SET_SCALE " + a.Arg0); return;
                case EditorActionType.SetActive: SetActive(a.Arg0, a.BoolValue); report.MarkMutation("SET_ACTIVE " + a.Arg0); return;
                case EditorActionType.AddComponent: AddComponent(a.Arg0, a.Arg1); report.MarkMutation("ADD_COMPONENT " + a.Arg0 + " " + a.Arg1); return;
                case EditorActionType.RemoveComponent: RemoveComponent(a.Arg0, a.Arg1); report.MarkMutation("REMOVE_COMPONENT " + a.Arg0 + " " + a.Arg1); return;
                case EditorActionType.SetComponentField: SetComponentField(a.Arg0, a.Arg1, a.Arg2, a.Arg3); report.MarkMutation("SET_COMPONENT_FIELD " + a.Arg0 + " " + a.Arg1 + "." + a.Arg2); return;
                case EditorActionType.DeleteGameObject: DeleteGameObject(a.Arg0); report.MarkMutation("DELETE_GAMEOBJECT " + a.Arg0); return;
                case EditorActionType.SelectGameObject: SelectGameObject(a.Arg0); report.MarkInfo("Selected: " + a.Arg0); return;
                case EditorActionType.OpenScene: OpenScene(a.Arg0); report.MarkMutation("OPEN_SCENE " + a.Arg0); return;
                case EditorActionType.SaveScene: SaveScene(); report.MarkMutation("SAVE_SCENE"); return;
                case EditorActionType.RunMenuItem: if (!EditorApplication.ExecuteMenuItem(a.Arg0)) throw new InvalidOperationException("Menu not found: " + a.Arg0); report.MarkMutation("RUN_MENU_ITEM " + a.Arg0); return;
                case EditorActionType.RefreshAssets: AssetDatabase.Refresh(); report.MarkInfo("Assets refreshed."); return;
                case EditorActionType.ReadFile: report.MarkInfo(ReadFileInfo(a.Arg0, a.LineNumber > 0 ? a.LineNumber : 120)); return;
                case EditorActionType.ListDir: report.MarkInfo(ListDirectoryInfo(a.Arg0)); return;
                case EditorActionType.FindInFiles: report.MarkInfo(FindInFilesInfo(a.Arg0, a.Arg1)); return;
                case EditorActionType.GetGameObjectInfo: report.MarkInfo(GetGameObjectInfo(a.Arg0)); return;
                case EditorActionType.ListSceneObjects: report.MarkInfo(ListSceneObjectsInfo()); return;
                case EditorActionType.GetSelectionInfo: report.MarkInfo(GetSelectionInfo()); return;
                default: throw new InvalidOperationException("Unsupported action type: " + a.Type);
            }
        }

        private static bool ParsePayload(string payload, List<EditorAction> actions, out string parseError)
        {
            parseError = string.Empty;
            var lines = payload.Replace("\r\n", "\n").Split('\n');
            var i = 0;
            while (i < lines.Length)
            {
                var line = (lines[i] ?? string.Empty).Trim(); i++;
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal)) continue;
                var args = Tokenize(line); if (args.Count == 0) continue;
                var cmd = args[0].ToUpperInvariant();
                try
                {
                    switch (cmd)
                    {
                        case "WRITE_FILE": Require(args, 2, cmd); actions.Add(EditorAction.WriteFile(args[1], ConsumeUntil(lines, ref i, "END_FILE"))); break;
                        case "APPEND_FILE": Require(args, 2, cmd); actions.Add(EditorAction.AppendFile(args[1], ConsumeUntil(lines, ref i, "END_FILE"))); break;
                        case "INSERT_IN_FILE": Require(args, 3, cmd); actions.Add(EditorAction.InsertInFile(args[1], ParseInt(args[2]), ConsumeUntil(lines, ref i, "END_FILE"))); break;
                        case "REPLACE_IN_FILE": Require(args, 2, cmd); actions.Add(EditorAction.ReplaceInFile(args[1], ConsumeTagged(lines, ref i, "SEARCH", "END_SEARCH"), ConsumeTagged(lines, ref i, "REPLACE", "END_REPLACE"))); break;
                        case "DELETE_FILE": Require(args, 2, cmd); actions.Add(EditorAction.DeleteFile(args[1])); break;
                        case "MOVE_FILE": Require(args, 3, cmd); actions.Add(EditorAction.MoveFile(args[1], args[2])); break;
                        case "COPY_FILE": Require(args, 3, cmd); actions.Add(EditorAction.CopyFile(args[1], args[2])); break;
                        case "CREATE_DIR": Require(args, 2, cmd); actions.Add(EditorAction.CreateDir(args[1])); break;
                        case "DELETE_DIR": Require(args, 2, cmd); actions.Add(EditorAction.DeleteDir(args[1])); break;
                        case "CREATE_GAMEOBJECT": Require(args, 2, cmd); actions.Add(EditorAction.CreateGameObject(args[1])); break;
                        case "CREATE_CHILD": Require(args, 3, cmd); actions.Add(EditorAction.CreateChild(args[1], args[2])); break;
                        case "DUPLICATE_GAMEOBJECT": Require(args, 2, cmd); actions.Add(EditorAction.DuplicateGameObject(args[1])); break;
                        case "RENAME_GAMEOBJECT": Require(args, 3, cmd); actions.Add(EditorAction.RenameGameObject(args[1], args[2])); break;
                        case "SET_TRANSFORM": Require(args, 5, cmd); actions.Add(EditorAction.SetTransform(args[1], ParseVector3(args[2], args[3], args[4]))); break;
                        case "SET_LOCAL_TRANSFORM": Require(args, 5, cmd); actions.Add(EditorAction.SetLocalTransform(args[1], ParseVector3(args[2], args[3], args[4]))); break;
                        case "SET_ROTATION": Require(args, 5, cmd); actions.Add(EditorAction.SetRotation(args[1], ParseVector3(args[2], args[3], args[4]))); break;
                        case "SET_LOCAL_ROTATION": Require(args, 5, cmd); actions.Add(EditorAction.SetLocalRotation(args[1], ParseVector3(args[2], args[3], args[4]))); break;
                        case "SET_SCALE": Require(args, 5, cmd); actions.Add(EditorAction.SetScale(args[1], ParseVector3(args[2], args[3], args[4]))); break;
                        case "SET_ACTIVE": Require(args, 3, cmd); actions.Add(EditorAction.SetActive(args[1], ParseBool(args[2]))); break;
                        case "ADD_COMPONENT": Require(args, 3, cmd); actions.Add(EditorAction.AddComponent(args[1], args[2])); break;
                        case "REMOVE_COMPONENT": Require(args, 3, cmd); actions.Add(EditorAction.RemoveComponent(args[1], args[2])); break;
                        case "SET_COMPONENT_FIELD": Require(args, 5, cmd); actions.Add(EditorAction.SetComponentField(args[1], args[2], args[3], args[4])); break;
                        case "DELETE_GAMEOBJECT": Require(args, 2, cmd); actions.Add(EditorAction.DeleteGameObject(args[1])); break;
                        case "SELECT_GAMEOBJECT": Require(args, 2, cmd); actions.Add(EditorAction.SelectGameObject(args[1])); break;
                        case "OPEN_SCENE": Require(args, 2, cmd); actions.Add(EditorAction.OpenScene(args[1])); break;
                        case "SAVE_SCENE": actions.Add(EditorAction.SaveScene()); break;
                        case "RUN_MENU_ITEM": Require(args, 2, cmd); actions.Add(EditorAction.RunMenuItem(args[1])); break;
                        case "REFRESH_ASSETS": actions.Add(EditorAction.RefreshAssets()); break;
                        case "READ_FILE": Require(args, 2, cmd); actions.Add(EditorAction.ReadFile(args[1], args.Count >= 3 ? ParseInt(args[2]) : 120)); break;
                        case "LIST_DIR": Require(args, 2, cmd); actions.Add(EditorAction.ListDir(args[1])); break;
                        case "FIND_IN_FILES": Require(args, 3, cmd); actions.Add(EditorAction.FindInFiles(args[1], args[2])); break;
                        case "GET_GAMEOBJECT_INFO": Require(args, 2, cmd); actions.Add(EditorAction.GetGameObjectInfo(args[1])); break;
                        case "LIST_SCENE_OBJECTS": actions.Add(EditorAction.ListSceneObjects()); break;
                        case "GET_SELECTION_INFO": actions.Add(EditorAction.GetSelectionInfo()); break;
                        default: parseError = "Unknown action line: " + line; return false;
                    }
                }
                catch (Exception ex) { parseError = cmd + " parse error: " + ex.Message; return false; }
            }
            return true;
        }

        private static void Require(List<string> args, int minCount, string cmd) { if (args.Count < minCount) throw new InvalidOperationException(cmd + " needs " + (minCount - 1) + " args."); }
        private static int ParseInt(string v) { if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) throw new InvalidOperationException("Invalid int."); return i; }
        private static bool ParseBool(string v) { if (!bool.TryParse(v, out var b)) throw new InvalidOperationException("Invalid bool."); return b; }
        private static Vector3 ParseVector3(string x, string y, string z) => new Vector3(float.Parse(x, CultureInfo.InvariantCulture), float.Parse(y, CultureInfo.InvariantCulture), float.Parse(z, CultureInfo.InvariantCulture));

        private static List<string> Tokenize(string line)
        {
            var tokens = new List<string>(); var sb = new StringBuilder(); var inQuote = false; var quote = '"';
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (inQuote) { if (c == quote) { inQuote = false; } else { sb.Append(c); } continue; }
                if (c == '"' || c == '\'') { inQuote = true; quote = c; continue; }
                if (char.IsWhiteSpace(c)) { if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); } continue; }
                sb.Append(c);
            }
            if (sb.Length > 0) tokens.Add(sb.ToString());
            return tokens;
        }

        private static string ConsumeUntil(string[] lines, ref int index, string marker)
        {
            var sb = new StringBuilder();
            while (index < lines.Length)
            {
                var line = lines[index++]; if ((line ?? string.Empty).Trim().Equals(marker, StringComparison.OrdinalIgnoreCase)) return sb.ToString();
                sb.AppendLine(line);
            }
            throw new InvalidOperationException("Missing marker: " + marker);
        }

        private static string ConsumeTagged(string[] lines, ref int index, string start, string end)
        {
            while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index])) index++;
            if (index >= lines.Length || !lines[index].Trim().Equals(start, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Expected " + start);
            index++;
            return ConsumeUntil(lines, ref index, end);
        }

        private static void WriteFile(string relativePath, string content, bool overwrite)
        {
            var fullPath = ValidateProjectPath(relativePath);
            var dir = Path.GetDirectoryName(fullPath); if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            if (overwrite) File.WriteAllText(fullPath, content ?? string.Empty); else File.AppendAllText(fullPath, content ?? string.Empty);
        }

        private static void InsertInFile(string relativePath, int lineNumber, string content)
        {
            var fullPath = ValidateProjectPath(relativePath); if (!File.Exists(fullPath)) throw new InvalidOperationException("File not found: " + relativePath);
            var lines = new List<string>(File.ReadAllLines(fullPath));
            var index = Mathf.Clamp(lineNumber - 1, 0, lines.Count);
            lines.InsertRange(index, (content ?? string.Empty).Replace("\r\n", "\n").Split('\n'));
            File.WriteAllLines(fullPath, lines.ToArray());
        }

        private static void ReplaceInFile(string relativePath, string searchText, string replaceText)
        {
            var fullPath = ValidateProjectPath(relativePath); if (!File.Exists(fullPath)) throw new InvalidOperationException("File not found: " + relativePath);
            var source = File.ReadAllText(fullPath); if (!source.Contains(searchText)) throw new InvalidOperationException("Search block not found.");
            File.WriteAllText(fullPath, source.Replace(searchText, replaceText));
        }

        private static void DeleteFile(string relativePath) { var fullPath = ValidateProjectPath(relativePath); if (File.Exists(fullPath)) File.Delete(fullPath); }

        private static void MovePath(string from, string to)
        {
            var source = ValidateProjectPath(from); var target = ValidateProjectPath(to);
            var targetDir = Path.GetDirectoryName(target); if (!string.IsNullOrWhiteSpace(targetDir)) Directory.CreateDirectory(targetDir);
            if (File.Exists(source))
            {
                if (File.Exists(target)) File.Delete(target);
                File.Move(source, target);
                return;
            }
            if (Directory.Exists(source)) { if (Directory.Exists(target)) Directory.Delete(target, true); Directory.Move(source, target); return; }
            throw new InvalidOperationException("Source not found: " + from);
        }

        private static void CopyFile(string from, string to)
        {
            var source = ValidateProjectPath(from); var target = ValidateProjectPath(to); if (!File.Exists(source)) throw new InvalidOperationException("Source file not found.");
            var targetDir = Path.GetDirectoryName(target); if (!string.IsNullOrWhiteSpace(targetDir)) Directory.CreateDirectory(targetDir);
            File.Copy(source, target, true);
        }

        private static void CreateGameObject(string name)
        {
            var go = new GameObject(string.IsNullOrWhiteSpace(name) ? "New CursorLike Object" : name);
            Undo.RegisterCreatedObjectUndo(go, "CursorLike Create GameObject");
            Selection.activeGameObject = go;
        }

        private static void CreateChild(string parentPath, string name)
        {
            var parent = FindByPath(parentPath); if (parent == null) throw new InvalidOperationException("Parent not found: " + parentPath);
            var go = new GameObject(string.IsNullOrWhiteSpace(name) ? "New Child" : name);
            Undo.RegisterCreatedObjectUndo(go, "CursorLike Create Child");
            go.transform.SetParent(parent, false); Selection.activeGameObject = go;
        }

        private static void DuplicateGameObject(string path)
        {
            var source = FindByPath(path); if (source == null) throw new InvalidOperationException("GameObject not found: " + path);
            var clone = UnityEngine.Object.Instantiate(source.gameObject, source.parent); clone.name = source.name + "_Copy";
            Undo.RegisterCreatedObjectUndo(clone, "CursorLike Duplicate GameObject"); Selection.activeGameObject = clone;
        }

        private static void RenameGameObject(string path, string newName)
        {
            var t = FindByPath(path); if (t == null) throw new InvalidOperationException("GameObject not found: " + path);
            Undo.RecordObject(t.gameObject, "CursorLike Rename GameObject"); t.name = string.IsNullOrWhiteSpace(newName) ? t.name : newName; EditorUtility.SetDirty(t.gameObject);
        }

        private static void SetTransform(string path, Vector3 position) { var t = FindByPath(path); if (t == null) throw new InvalidOperationException("GameObject not found: " + path); Undo.RecordObject(t, "Set Transform"); t.position = position; EditorUtility.SetDirty(t); }
        private static void SetLocalTransform(string path, Vector3 position) { var t = FindByPath(path); if (t == null) throw new InvalidOperationException("GameObject not found: " + path); Undo.RecordObject(t, "Set Local Transform"); t.localPosition = position; EditorUtility.SetDirty(t); }
        private static void SetRotation(string path, Vector3 euler, bool local) { var t = FindByPath(path); if (t == null) throw new InvalidOperationException("GameObject not found: " + path); Undo.RecordObject(t, "Set Rotation"); if (local) t.localRotation = Quaternion.Euler(euler); else t.rotation = Quaternion.Euler(euler); EditorUtility.SetDirty(t); }
        private static void SetScale(string path, Vector3 scale) { var t = FindByPath(path); if (t == null) throw new InvalidOperationException("GameObject not found: " + path); Undo.RecordObject(t, "Set Scale"); t.localScale = scale; EditorUtility.SetDirty(t); }
        private static void SetActive(string path, bool active) { var t = FindByPath(path); if (t == null) throw new InvalidOperationException("GameObject not found: " + path); Undo.RecordObject(t.gameObject, "Set Active"); t.gameObject.SetActive(active); EditorUtility.SetDirty(t.gameObject); }

        private static void AddComponent(string path, string componentTypeName)
        {
            var t = FindByPath(path); if (t == null) throw new InvalidOperationException("GameObject not found: " + path);
            var type = ResolveComponentType(componentTypeName); if (type == null) throw new InvalidOperationException("Component type not found: " + componentTypeName);
            Undo.AddComponent(t.gameObject, type); EditorUtility.SetDirty(t.gameObject);
        }

        private static void RemoveComponent(string path, string componentTypeName)
        {
            var t = FindByPath(path); if (t == null) throw new InvalidOperationException("GameObject not found: " + path);
            var type = ResolveComponentType(componentTypeName); if (type == null) throw new InvalidOperationException("Component type not found: " + componentTypeName);
            var c = t.GetComponent(type); if (c == null) return; if (c is Transform) throw new InvalidOperationException("Cannot remove Transform.");
            Undo.DestroyObjectImmediate(c); EditorUtility.SetDirty(t.gameObject);
        }

        private static void SetComponentField(string path, string componentTypeName, string memberName, string valueLiteral)
        {
            var t = FindByPath(path); if (t == null) throw new InvalidOperationException("GameObject not found: " + path);
            var type = ResolveComponentType(componentTypeName); if (type == null) throw new InvalidOperationException("Component type not found: " + componentTypeName);
            var c = t.GetComponent(type); if (c == null) throw new InvalidOperationException("Component not present: " + componentTypeName);
            Undo.RecordObject(c, "Set Component Field");
            var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null) { field.SetValue(c, ConvertLiteral(valueLiteral, field.FieldType)); EditorUtility.SetDirty(c); return; }
            var prop = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null && prop.CanWrite) { prop.SetValue(c, ConvertLiteral(valueLiteral, prop.PropertyType), null); EditorUtility.SetDirty(c); return; }
            throw new InvalidOperationException("Field/property not writable: " + memberName);
        }

        private static object ConvertLiteral(string valueLiteral, Type t)
        {
            if (t == typeof(string)) return valueLiteral;
            if (t == typeof(int)) return int.Parse(valueLiteral, CultureInfo.InvariantCulture);
            if (t == typeof(float)) return float.Parse(valueLiteral, CultureInfo.InvariantCulture);
            if (t == typeof(double)) return double.Parse(valueLiteral, CultureInfo.InvariantCulture);
            if (t == typeof(bool)) return bool.Parse(valueLiteral);
            if (t == typeof(Vector2)) { var p = valueLiteral.Split(','); return new Vector2(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture)); }
            if (t == typeof(Vector3)) { var p = valueLiteral.Split(','); return new Vector3(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture)); }
            if (t.IsEnum) return Enum.Parse(t, valueLiteral, true);
            throw new InvalidOperationException("Unsupported type: " + t.Name);
        }

        private static void DeleteGameObject(string path) { var t = FindByPath(path); if (t != null) Undo.DestroyObjectImmediate(t.gameObject); }
        private static void SelectGameObject(string path) { var t = FindByPath(path); if (t == null) throw new InvalidOperationException("GameObject not found: " + path); Selection.activeGameObject = t.gameObject; }
        private static void OpenScene(string scenePath) { var fullPath = ValidateProjectPath(scenePath); if (!File.Exists(fullPath)) throw new InvalidOperationException("Scene not found: " + scenePath); EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single); }
        private static void SaveScene() { var scene = SceneManager.GetActiveScene(); if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("No loaded scene."); EditorSceneManager.SaveScene(scene); }

        private static string ReadFileInfo(string relativePath, int maxLines)
        {
            var fullPath = ValidateProjectPath(relativePath);
            if (!File.Exists(fullPath)) throw new InvalidOperationException("File not found: " + relativePath);
            maxLines = Mathf.Clamp(maxLines, 1, 5000);
            var lines = File.ReadAllLines(fullPath);
            var count = Mathf.Min(maxLines, lines.Length);
            var sb = new StringBuilder();
            sb.AppendLine("READ_FILE " + relativePath + " (" + lines.Length + " lines, showing " + count + ")");
            for (var i = 0; i < count; i++)
            {
                sb.AppendLine((i + 1).ToString(CultureInfo.InvariantCulture) + ": " + lines[i]);
            }

            return sb.ToString();
        }

        private static string ListDirectoryInfo(string relativePath)
        {
            var fullPath = ValidateProjectPath(relativePath);
            if (!Directory.Exists(fullPath)) throw new InvalidOperationException("Directory not found: " + relativePath);
            var sb = new StringBuilder();
            sb.AppendLine("LIST_DIR " + relativePath);
            foreach (var dir in Directory.GetDirectories(fullPath))
            {
                sb.AppendLine("[D] " + NormalizeRelativePath(dir));
            }

            foreach (var file in Directory.GetFiles(fullPath))
            {
                sb.AppendLine("[F] " + NormalizeRelativePath(file));
            }

            return sb.ToString();
        }

        private static string FindInFilesInfo(string pattern, string relativeRoot)
        {
            var fullRoot = ValidateProjectPath(relativeRoot);
            if (!Directory.Exists(fullRoot)) throw new InvalidOperationException("Directory not found: " + relativeRoot);
            var sb = new StringBuilder();
            var hitCount = 0;
            sb.AppendLine("FIND_IN_FILES \"" + pattern + "\" in " + relativeRoot);
            foreach (var file in Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories))
            {
                if (IsLikelyBinary(file)) continue;
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (lines[i].IndexOf(pattern, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    hitCount++;
                    sb.AppendLine(NormalizeRelativePath(file) + ":" + (i + 1).ToString(CultureInfo.InvariantCulture) + ": " + lines[i]);
                    if (hitCount >= 200)
                    {
                        sb.AppendLine("...truncated at 200 matches.");
                        return sb.ToString();
                    }
                }
            }

            if (hitCount == 0) sb.AppendLine("No matches.");
            return sb.ToString();
        }

        private static string GetGameObjectInfo(string objectPath)
        {
            var t = FindByPath(objectPath);
            if (t == null) throw new InvalidOperationException("GameObject not found: " + objectPath);
            var go = t.gameObject;
            var sb = new StringBuilder();
            sb.AppendLine("GET_GAMEOBJECT_INFO " + objectPath);
            sb.AppendLine("name: " + go.name);
            sb.AppendLine("path: " + BuildTransformPath(t));
            sb.AppendLine("activeSelf: " + go.activeSelf);
            sb.AppendLine("activeInHierarchy: " + go.activeInHierarchy);
            sb.AppendLine("tag: " + go.tag + ", layer: " + go.layer.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("worldPosition: " + FormatVector3(t.position));
            sb.AppendLine("localPosition: " + FormatVector3(t.localPosition));
            sb.AppendLine("worldEuler: " + FormatVector3(t.eulerAngles));
            sb.AppendLine("localEuler: " + FormatVector3(t.localEulerAngles));
            sb.AppendLine("localScale: " + FormatVector3(t.localScale));
            var comps = go.GetComponents<Component>();
            sb.AppendLine("components:");
            for (var i = 0; i < comps.Length; i++)
            {
                var c = comps[i];
                if (c == null) continue;
                sb.AppendLine("- " + c.GetType().FullName);
            }

            return sb.ToString();
        }

        private static string ListSceneObjectsInfo()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("No loaded active scene.");
            var sb = new StringBuilder();
            sb.AppendLine("LIST_SCENE_OBJECTS " + scene.path);
            foreach (var root in scene.GetRootGameObjects())
            {
                AppendSceneObjectInfo(sb, root.transform);
            }

            return sb.ToString();
        }

        private static void AppendSceneObjectInfo(StringBuilder sb, Transform t)
        {
            sb.AppendLine(BuildTransformPath(t) + " | wp=" + FormatVector3(t.position) + " | lp=" + FormatVector3(t.localPosition) + " | ls=" + FormatVector3(t.localScale));
            for (var i = 0; i < t.childCount; i++)
            {
                AppendSceneObjectInfo(sb, t.GetChild(i));
            }
        }

        private static string GetSelectionInfo()
        {
            if (Selection.activeGameObject == null) return "GET_SELECTION_INFO: no active selection.";
            return GetGameObjectInfo(BuildTransformPath(Selection.activeGameObject.transform));
        }

        private static Type ResolveComponentType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;
            var direct = Type.GetType(typeName);
            if (direct != null && typeof(Component).IsAssignableFrom(direct)) return direct;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                if (types == null) continue;
                for (var i = 0; i < types.Length; i++)
                {
                    var t = types[i];
                    if (t == null || !typeof(Component).IsAssignableFrom(t)) continue;
                    if (t.Name.Equals(typeName, StringComparison.Ordinal) ||
                        (!string.IsNullOrEmpty(t.FullName) && t.FullName.Equals(typeName, StringComparison.Ordinal)))
                    {
                        return t;
                    }
                }
            }
            return null;
        }

        private static Transform FindByPath(string objectPath)
        {
            if (string.IsNullOrWhiteSpace(objectPath)) return null;
            var go = GameObject.Find(objectPath);
            return go != null ? go.transform : null;
        }

        private static string ValidateProjectPath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) throw new InvalidOperationException("Empty path.");
            var root = Path.GetFullPath(Directory.GetCurrentDirectory());
            var target = Path.GetFullPath(Path.Combine(root, relativePath.Replace('\\', '/').Trim()));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Outside project root: " + relativePath);
            return target;
        }

        private static string NormalizeRelativePath(string fullPath)
        {
            var root = Path.GetFullPath(Directory.GetCurrentDirectory()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(fullPath);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return full.Replace('\\', '/');
            }

            return full.Substring(root.Length).Replace('\\', '/');
        }

        private static bool IsLikelyBinary(string fullPath)
        {
            var ext = Path.GetExtension(fullPath).ToLowerInvariant();
            switch (ext)
            {
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".gif":
                case ".mp3":
                case ".wav":
                case ".ogg":
                case ".mp4":
                case ".mov":
                case ".fbx":
                case ".psd":
                case ".dll":
                case ".exe":
                case ".ttf":
                case ".otf":
                case ".zip":
                case ".7z":
                    return true;
                default:
                    return false;
            }
        }

        private static string BuildTransformPath(Transform t)
        {
            if (t == null) return string.Empty;
            var names = new List<string>();
            var current = t;
            while (current != null)
            {
                names.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", names);
        }

        private static string FormatVector3(Vector3 v)
        {
            return v.x.ToString("R", CultureInfo.InvariantCulture) + "," +
                   v.y.ToString("R", CultureInfo.InvariantCulture) + "," +
                   v.z.ToString("R", CultureInfo.InvariantCulture);
        }
    }

    internal enum EditorActionType
    {
        WriteFile, AppendFile, InsertInFile, ReplaceInFile, DeleteFile, MoveFile, CopyFile, CreateDir, DeleteDir,
        CreateGameObject, CreateChild, DuplicateGameObject, RenameGameObject, SetTransform, SetLocalTransform,
        SetRotation, SetLocalRotation, SetScale, SetActive, AddComponent, RemoveComponent, SetComponentField,
        DeleteGameObject, SelectGameObject, OpenScene, SaveScene, RunMenuItem, RefreshAssets
        , ReadFile, ListDir, FindInFiles, GetGameObjectInfo, ListSceneObjects, GetSelectionInfo
    }

    internal sealed class EditorAction
    {
        internal EditorActionType Type;
        internal string Arg0;
        internal string Arg1;
        internal string Arg2;
        internal string Arg3;
        internal string Content;
        internal string SearchText;
        internal string ReplaceText;
        internal int LineNumber;
        internal Vector3 VectorValue;
        internal bool BoolValue;

        internal static EditorAction WriteFile(string path, string content) => new EditorAction { Type = EditorActionType.WriteFile, Arg0 = path, Content = content };
        internal static EditorAction AppendFile(string path, string content) => new EditorAction { Type = EditorActionType.AppendFile, Arg0 = path, Content = content };
        internal static EditorAction InsertInFile(string path, int line, string content) => new EditorAction { Type = EditorActionType.InsertInFile, Arg0 = path, LineNumber = line, Content = content };
        internal static EditorAction ReplaceInFile(string path, string s, string r) => new EditorAction { Type = EditorActionType.ReplaceInFile, Arg0 = path, SearchText = s, ReplaceText = r };
        internal static EditorAction DeleteFile(string path) => new EditorAction { Type = EditorActionType.DeleteFile, Arg0 = path };
        internal static EditorAction MoveFile(string from, string to) => new EditorAction { Type = EditorActionType.MoveFile, Arg0 = from, Arg1 = to };
        internal static EditorAction CopyFile(string from, string to) => new EditorAction { Type = EditorActionType.CopyFile, Arg0 = from, Arg1 = to };
        internal static EditorAction CreateDir(string path) => new EditorAction { Type = EditorActionType.CreateDir, Arg0 = path };
        internal static EditorAction DeleteDir(string path) => new EditorAction { Type = EditorActionType.DeleteDir, Arg0 = path };
        internal static EditorAction CreateGameObject(string name) => new EditorAction { Type = EditorActionType.CreateGameObject, Arg0 = name };
        internal static EditorAction CreateChild(string parent, string name) => new EditorAction { Type = EditorActionType.CreateChild, Arg0 = parent, Arg1 = name };
        internal static EditorAction DuplicateGameObject(string path) => new EditorAction { Type = EditorActionType.DuplicateGameObject, Arg0 = path };
        internal static EditorAction RenameGameObject(string path, string name) => new EditorAction { Type = EditorActionType.RenameGameObject, Arg0 = path, Arg1 = name };
        internal static EditorAction SetTransform(string path, Vector3 p) => new EditorAction { Type = EditorActionType.SetTransform, Arg0 = path, VectorValue = p };
        internal static EditorAction SetLocalTransform(string path, Vector3 p) => new EditorAction { Type = EditorActionType.SetLocalTransform, Arg0 = path, VectorValue = p };
        internal static EditorAction SetRotation(string path, Vector3 p) => new EditorAction { Type = EditorActionType.SetRotation, Arg0 = path, VectorValue = p };
        internal static EditorAction SetLocalRotation(string path, Vector3 p) => new EditorAction { Type = EditorActionType.SetLocalRotation, Arg0 = path, VectorValue = p };
        internal static EditorAction SetScale(string path, Vector3 p) => new EditorAction { Type = EditorActionType.SetScale, Arg0 = path, VectorValue = p };
        internal static EditorAction SetActive(string path, bool a) => new EditorAction { Type = EditorActionType.SetActive, Arg0 = path, BoolValue = a };
        internal static EditorAction AddComponent(string path, string c) => new EditorAction { Type = EditorActionType.AddComponent, Arg0 = path, Arg1 = c };
        internal static EditorAction RemoveComponent(string path, string c) => new EditorAction { Type = EditorActionType.RemoveComponent, Arg0 = path, Arg1 = c };
        internal static EditorAction SetComponentField(string path, string c, string m, string v) => new EditorAction { Type = EditorActionType.SetComponentField, Arg0 = path, Arg1 = c, Arg2 = m, Arg3 = v };
        internal static EditorAction DeleteGameObject(string path) => new EditorAction { Type = EditorActionType.DeleteGameObject, Arg0 = path };
        internal static EditorAction SelectGameObject(string path) => new EditorAction { Type = EditorActionType.SelectGameObject, Arg0 = path };
        internal static EditorAction OpenScene(string path) => new EditorAction { Type = EditorActionType.OpenScene, Arg0 = path };
        internal static EditorAction SaveScene() => new EditorAction { Type = EditorActionType.SaveScene };
        internal static EditorAction RunMenuItem(string path) => new EditorAction { Type = EditorActionType.RunMenuItem, Arg0 = path };
        internal static EditorAction RefreshAssets() => new EditorAction { Type = EditorActionType.RefreshAssets };
        internal static EditorAction ReadFile(string path, int maxLines) => new EditorAction { Type = EditorActionType.ReadFile, Arg0 = path, LineNumber = maxLines };
        internal static EditorAction ListDir(string path) => new EditorAction { Type = EditorActionType.ListDir, Arg0 = path };
        internal static EditorAction FindInFiles(string pattern, string rootPath) => new EditorAction { Type = EditorActionType.FindInFiles, Arg0 = pattern, Arg1 = rootPath };
        internal static EditorAction GetGameObjectInfo(string path) => new EditorAction { Type = EditorActionType.GetGameObjectInfo, Arg0 = path };
        internal static EditorAction ListSceneObjects() => new EditorAction { Type = EditorActionType.ListSceneObjects };
        internal static EditorAction GetSelectionInfo() => new EditorAction { Type = EditorActionType.GetSelectionInfo };
    }

    internal sealed class ExecutionReport
    {
        private readonly List<string> _lines = new List<string>();
        internal bool HasMutations { get; private set; }
        internal string Summary => string.Join("\n", _lines);

        internal void MarkMutation(string line)
        {
            HasMutations = true;
            _lines.Add("[MUTATION] " + line);
        }

        internal void MarkInfo(string line)
        {
            _lines.Add("[INFO] " + line);
        }
    }
}
