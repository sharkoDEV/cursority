using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace CursorLike.Editor
{
    internal sealed class CursorLikeWindow : EditorWindow
    {
        private readonly List<ChatMessage> _history = new List<ChatMessage>();
        private Vector2 _scroll;
        private string _prompt = string.Empty;
        private bool _includeProjectFiles = true;
        private bool _includeScripts = true;
        private bool _includeScene = true;
        private bool _autoApplyActions;
        private bool _agentMode = true;
        private bool _agentDryRun;
        private int _agentMaxIterations = 8;
        private bool _isBusy;
        private bool _agentCancelRequested;
        private string _status = "Idle";
        private List<EditorAction> _pendingActions;
        private string _pendingPreview = string.Empty;
        private readonly StringBuilder _agentLog = new StringBuilder();

        [MenuItem("Tools/CursorLike Chat")]
        private static void Open()
        {
            var window = GetWindow<CursorLikeWindow>();
            window.titleContent = new GUIContent("CursorLike");
            window.minSize = new Vector2(560, 460);
            window.Show();
        }

        private void OnGUI()
        {
            DrawTopToolbar();
            DrawMessages();
            DrawPromptBox();
            DrawPendingActions();
            DrawAgentLog();
            DrawFooter();
        }

        private void DrawTopToolbar()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Context", EditorStyles.boldLabel);
            _includeProjectFiles = EditorGUILayout.ToggleLeft("Project files", _includeProjectFiles);
            _includeScripts = EditorGUILayout.ToggleLeft("Script previews", _includeScripts);
            _includeScene = EditorGUILayout.ToggleLeft("Active scene", _includeScene);
            _autoApplyActions = EditorGUILayout.ToggleLeft("Auto apply actions (chat mode)", _autoApplyActions);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Agent", EditorStyles.boldLabel);
            _agentMode = EditorGUILayout.ToggleLeft("Agent mode (multi-step autonomous)", _agentMode);
            _agentDryRun = EditorGUILayout.ToggleLeft("Agent dry run (parse only, no apply)", _agentDryRun);
            _agentMaxIterations = Mathf.Clamp(EditorGUILayout.IntField("Agent max iterations", _agentMaxIterations), 1, 30);
            EditorGUILayout.EndVertical();
        }

        private void DrawMessages()
        {
            EditorGUILayout.LabelField("Conversation", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(position.height * 0.34f));
            if (_history.Count == 0)
            {
                EditorGUILayout.HelpBox("No messages yet.", MessageType.Info);
            }
            else
            {
                foreach (var message in _history)
                {
                    DrawMessage(message);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawMessage(ChatMessage message)
        {
            var style = new GUIStyle(EditorStyles.helpBox) { wordWrap = true };
            var prefix = message.role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "Assistant" : "You";
            EditorGUILayout.LabelField(prefix, EditorStyles.boldLabel);
            EditorGUILayout.TextArea(message.content, style, GUILayout.MinHeight(50));
            EditorGUILayout.Space(3);
        }

        private void DrawPromptBox()
        {
            EditorGUILayout.LabelField("Prompt", EditorStyles.boldLabel);
            _prompt = EditorGUILayout.TextArea(_prompt, GUILayout.MinHeight(80));

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_isBusy || string.IsNullOrWhiteSpace(_prompt)))
            {
                if (GUILayout.Button(_agentMode ? "Run Agent" : "Send Once"))
                {
                    var text = _prompt;
                    _prompt = string.Empty;
                    _ = _agentMode ? RunAgentAsync(text) : SendPromptAsync(text);
                }
            }

            using (new EditorGUI.DisabledScope(!_isBusy))
            {
                if (GUILayout.Button("Stop"))
                {
                    _agentCancelRequested = true;
                    _status = "Stop requested. Waiting for current request to finish...";
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawPendingActions()
        {
            if (_pendingActions == null || _pendingActions.Count == 0) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Pending Actions", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(_pendingPreview, MessageType.Warning);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply"))
            {
                try
                {
                    var report = EditorActionExecutor.Execute(_pendingActions);
                    _status = "Actions applied.";
                    if (!string.IsNullOrWhiteSpace(report.Summary))
                    {
                        _history.Add(new ChatMessage { role = "assistant", content = "Execution report:\n" + report.Summary });
                    }
                    _pendingActions = null;
                    _pendingPreview = string.Empty;
                }
                catch (Exception ex)
                {
                    _status = "Apply failed: " + ex.Message;
                }
            }

            if (GUILayout.Button("Dismiss"))
            {
                _pendingActions = null;
                _pendingPreview = string.Empty;
                _status = "Pending actions dismissed.";
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAgentLog()
        {
            if (_agentLog.Length == 0) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Agent Log", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(_agentLog.ToString(), GUILayout.MinHeight(100));
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(_status, MessageType.None);
            if (GUILayout.Button("Clear Conversation"))
            {
                _history.Clear();
                _pendingActions = null;
                _pendingPreview = string.Empty;
                _agentLog.Clear();
                _status = "Conversation cleared.";
            }
        }

        private async Task SendPromptAsync(string text)
        {
            _isBusy = true;
            _agentCancelRequested = false;
            _status = "Gathering context...";
            _history.Add(new ChatMessage { role = "user", content = text });

            try
            {
                var context = ProjectContextBuilder.BuildContext(_includeProjectFiles, _includeScripts, _includeScene);
                var requestMessages = BuildRequestMessages(BuildChatSystemPrompt(context));
                _status = "Sending request to OpenRouter...";
                Repaint();

                var response = await OpenRouterClient.RequestAsync(requestMessages);
                _history.Add(new ChatMessage { role = "assistant", content = response });
                _status = "Response received.";

                if (EditorActionExecutor.TryExtractActions(response, out var actions, out var parseError))
                {
                    _pendingActions = actions;
                    _pendingPreview = EditorActionExecutor.Preview(actions);
                    _status = "Actions parsed.";
                    if (_autoApplyActions)
                    {
                        var report = EditorActionExecutor.Execute(actions);
                        if (!string.IsNullOrWhiteSpace(report.Summary))
                        {
                            _history.Add(new ChatMessage { role = "assistant", content = "Execution report:\n" + report.Summary });
                        }
                        _pendingActions = null;
                        _pendingPreview = string.Empty;
                        _status = "Actions parsed and auto-applied.";
                    }
                }
                else if (!string.IsNullOrEmpty(parseError))
                {
                    _status = "Action parse error: " + parseError;
                }
            }
            catch (Exception ex)
            {
                _status = "Error: " + ex.Message;
                Debug.LogException(ex);
            }
            finally
            {
                _isBusy = false;
                Repaint();
            }
        }

        private async Task RunAgentAsync(string goal)
        {
            _isBusy = true;
            _agentCancelRequested = false;
            _agentLog.Clear();
            _history.Add(new ChatMessage { role = "user", content = goal });

            try
            {
                for (var iteration = 1; iteration <= _agentMaxIterations; iteration++)
                {
                    if (_agentCancelRequested)
                    {
                        _status = "Agent stopped by user.";
                        break;
                    }

                    _status = "Agent iteration " + iteration + "/" + _agentMaxIterations + ": building context...";
                    Repaint();
                    var context = ProjectContextBuilder.BuildContext(_includeProjectFiles, _includeScripts, _includeScene);
                    var requestMessages = BuildRequestMessages(BuildAgentSystemPrompt(context, iteration, _agentMaxIterations));

                    _status = "Agent iteration " + iteration + ": requesting model...";
                    Repaint();
                    var response = await OpenRouterClient.RequestAsync(requestMessages);
                    _history.Add(new ChatMessage { role = "assistant", content = response });
                    var stepStatus = ParseAgentStatus(response);

                    _agentLog.AppendLine("[Iteration " + iteration + "] model status: " + stepStatus);

                    if (EditorActionExecutor.TryExtractActions(response, out var actions, out var parseError))
                    {
                        _agentLog.AppendLine("[Iteration " + iteration + "] parsed actions: " + actions.Count);
                        if (_agentDryRun)
                        {
                            var preview = EditorActionExecutor.Preview(actions);
                            _agentLog.AppendLine("[Iteration " + iteration + "] dry-run preview:");
                            _agentLog.AppendLine(preview);
                            _history.Add(new ChatMessage
                            {
                                role = "user",
                                content = "Execution result (dry-run): parsed " + actions.Count + " actions. No changes were applied."
                            });
                        }
                        else
                        {
                            try
                            {
                                var report = EditorActionExecutor.Execute(actions);
                                _history.Add(new ChatMessage
                                {
                                    role = "user",
                                    content = "Execution result: success. Applied " + actions.Count + " actions.\n" + report.Summary
                                });
                                _agentLog.AppendLine("[Iteration " + iteration + "] apply success.");
                            }
                            catch (Exception ex)
                            {
                                var failMsg = "Execution result: failed. " + ex.Message;
                                _history.Add(new ChatMessage { role = "user", content = failMsg });
                                _agentLog.AppendLine("[Iteration " + iteration + "] apply failed: " + ex.Message);
                            }
                        }
                    }
                    else if (!string.IsNullOrEmpty(parseError))
                    {
                        _history.Add(new ChatMessage { role = "user", content = "Action parse error: " + parseError });
                        _agentLog.AppendLine("[Iteration " + iteration + "] parse error: " + parseError);
                    }
                    else
                    {
                        _agentLog.AppendLine("[Iteration " + iteration + "] no actions.");
                    }

                    if (stepStatus == AgentStepStatus.Done)
                    {
                        _status = "Agent finished objective.";
                        break;
                    }

                    if (stepStatus == AgentStepStatus.Blocked)
                    {
                        _status = "Agent blocked and needs manual input.";
                        break;
                    }

                    if (iteration == _agentMaxIterations)
                    {
                        _status = "Agent reached max iterations.";
                    }
                }
            }
            catch (Exception ex)
            {
                _status = "Agent error: " + ex.Message;
                Debug.LogException(ex);
            }
            finally
            {
                _isBusy = false;
                Repaint();
            }
        }

        private List<ChatMessage> BuildRequestMessages(string systemPrompt)
        {
            var requestMessages = new List<ChatMessage> { new ChatMessage { role = "system", content = systemPrompt } };
            requestMessages.AddRange(_history);
            return requestMessages;
        }

        private static AgentStepStatus ParseAgentStatus(string text)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = (lines[i] ?? string.Empty).Trim();
                if (!line.StartsWith("AGENT_STATUS:", StringComparison.OrdinalIgnoreCase)) continue;
                var value = line.Substring("AGENT_STATUS:".Length).Trim().ToUpperInvariant();
                if (value.StartsWith("DONE", StringComparison.Ordinal)) return AgentStepStatus.Done;
                if (value.StartsWith("BLOCKED", StringComparison.Ordinal)) return AgentStepStatus.Blocked;
                return AgentStepStatus.Continue;
            }

            return AgentStepStatus.Continue;
        }

        private static string BuildChatSystemPrompt(string context)
        {
            var sb = new StringBuilder(24 * 1024);
            sb.AppendLine("You are CursorLike, a Unity editor coding and scene assistant.");
            sb.AppendLine("When edit actions are needed, include a fenced block named cursor-actions.");
            sb.AppendLine("You may include multiple cursor-actions blocks in one answer.");
            sb.AppendLine("Quote arguments with spaces using double quotes.");
            AppendActionCommandSpec(sb);
            sb.AppendLine();
            sb.AppendLine("Context:");
            sb.AppendLine(context);
            return sb.ToString();
        }

        private static string BuildAgentSystemPrompt(string context, int iteration, int maxIterations)
        {
            var sb = new StringBuilder(32 * 1024);
            sb.AppendLine("You are CursorLike Autonomous Agent for Unity Editor.");
            sb.AppendLine("Operate in iterative tool-use style: inspect, propose actions, receive execution result, adapt.");
            sb.AppendLine("Every response must contain:");
            sb.AppendLine("1) Short reasoning summary.");
            sb.AppendLine("2) Optional cursor-actions block if edits are needed.");
            sb.AppendLine("3) A final status line: AGENT_STATUS: CONTINUE or AGENT_STATUS: DONE or AGENT_STATUS: BLOCKED");
            sb.AppendLine("Use BLOCKED only if you need impossible info or manual human decision.");
            sb.AppendLine("Current iteration: " + iteration + "/" + maxIterations);
            sb.AppendLine("Quote arguments with spaces using double quotes.");
            sb.AppendLine("Use project-relative paths only.");
            sb.AppendLine("You may include multiple cursor-actions blocks in one answer.");
            AppendActionCommandSpec(sb);
            sb.AppendLine();
            sb.AppendLine("Context:");
            sb.AppendLine(context);
            return sb.ToString();
        }

        private static void AppendActionCommandSpec(StringBuilder sb)
        {
            sb.AppendLine("Commands:");
            sb.AppendLine("WRITE_FILE <path> ... END_FILE");
            sb.AppendLine("APPEND_FILE <path> ... END_FILE");
            sb.AppendLine("INSERT_IN_FILE <path> <line> ... END_FILE");
            sb.AppendLine("REPLACE_IN_FILE <path> then SEARCH...END_SEARCH then REPLACE...END_REPLACE");
            sb.AppendLine("DELETE_FILE <path>");
            sb.AppendLine("MOVE_FILE <from> <to>");
            sb.AppendLine("COPY_FILE <from> <to>");
            sb.AppendLine("CREATE_DIR <path>");
            sb.AppendLine("DELETE_DIR <path>");
            sb.AppendLine("CREATE_GAMEOBJECT <name>");
            sb.AppendLine("CREATE_CHILD <parentPath> <name>");
            sb.AppendLine("DUPLICATE_GAMEOBJECT <path>");
            sb.AppendLine("RENAME_GAMEOBJECT <path> <newName>");
            sb.AppendLine("SET_TRANSFORM <path> <x> <y> <z>");
            sb.AppendLine("SET_LOCAL_TRANSFORM <path> <x> <y> <z>");
            sb.AppendLine("SET_ROTATION <path> <x> <y> <z>");
            sb.AppendLine("SET_LOCAL_ROTATION <path> <x> <y> <z>");
            sb.AppendLine("SET_SCALE <path> <x> <y> <z>");
            sb.AppendLine("SET_ACTIVE <path> <true|false>");
            sb.AppendLine("ADD_COMPONENT <path> <componentType>");
            sb.AppendLine("REMOVE_COMPONENT <path> <componentType>");
            sb.AppendLine("SET_COMPONENT_FIELD <path> <componentType> <fieldOrProperty> <value>");
            sb.AppendLine("DELETE_GAMEOBJECT <path>");
            sb.AppendLine("SELECT_GAMEOBJECT <path>");
            sb.AppendLine("OPEN_SCENE <path>");
            sb.AppendLine("SAVE_SCENE");
            sb.AppendLine("RUN_MENU_ITEM <menuPath>");
            sb.AppendLine("REFRESH_ASSETS");
            sb.AppendLine("READ_FILE <path> [maxLines]");
            sb.AppendLine("LIST_DIR <path>");
            sb.AppendLine("FIND_IN_FILES <pattern> <rootPath>");
            sb.AppendLine("GET_GAMEOBJECT_INFO <path>");
            sb.AppendLine("LIST_SCENE_OBJECTS");
            sb.AppendLine("GET_SELECTION_INFO");
        }

        private enum AgentStepStatus
        {
            Continue,
            Done,
            Blocked
        }
    }
}
