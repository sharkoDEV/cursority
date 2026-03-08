using UnityEditor;
using UnityEngine;

namespace CursorLike.Editor
{
    internal static class CursorLikeSettings
    {
        private const string ServiceKey = "CursorLike.Service";
        private const string ApiKeyKey = "CursorLike.ApiKey";
        private const string ModelKey = "CursorLike.Model";
        private const string EndpointKey = "CursorLike.Endpoint";
        private const string RefererKey = "CursorLike.Referer";
        private const string TitleKey = "CursorLike.Title";
        private const string TemperatureKey = "CursorLike.Temperature";
        private const string MaxTokensKey = "CursorLike.MaxTokens";

        internal static AiService Service
        {
            get => (AiService)EditorPrefs.GetInt(ServiceKey, (int)AiService.OpenRouter);
            set => EditorPrefs.SetInt(ServiceKey, (int)value);
        }

        internal static string ApiKey
        {
            get => EditorPrefs.GetString(ApiKeyKey, string.Empty);
            set => EditorPrefs.SetString(ApiKeyKey, value ?? string.Empty);
        }

        internal static string Model
        {
            get => EditorPrefs.GetString(ModelKey, "openai/gpt-4.1-mini");
            set => EditorPrefs.SetString(ModelKey, string.IsNullOrWhiteSpace(value) ? "openai/gpt-4.1-mini" : value.Trim());
        }

        internal static string Endpoint
        {
            get => EditorPrefs.GetString(EndpointKey, DefaultEndpointFor(Service));
            set => EditorPrefs.SetString(EndpointKey, string.IsNullOrWhiteSpace(value) ? DefaultEndpointFor(Service) : value.Trim());
        }

        internal static string Referer
        {
            get => EditorPrefs.GetString(RefererKey, "http://localhost");
            set => EditorPrefs.SetString(RefererKey, string.IsNullOrWhiteSpace(value) ? "http://localhost" : value.Trim());
        }

        internal static string AppTitle
        {
            get => EditorPrefs.GetString(TitleKey, "Unity CursorLike");
            set => EditorPrefs.SetString(TitleKey, string.IsNullOrWhiteSpace(value) ? "Unity CursorLike" : value.Trim());
        }

        internal static float Temperature
        {
            get => EditorPrefs.GetFloat(TemperatureKey, 0.1f);
            set => EditorPrefs.SetFloat(TemperatureKey, Mathf.Clamp(value, 0f, 2f));
        }

        internal static int MaxTokens
        {
            get => EditorPrefs.GetInt(MaxTokensKey, 4096);
            set => EditorPrefs.SetInt(MaxTokensKey, Mathf.Clamp(value, 256, 32000));
        }

        internal static string DefaultEndpointFor(AiService service)
        {
            switch (service)
            {
                case AiService.OpenAICompatible:
                    return "https://api.openai.com/v1/chat/completions";
                case AiService.Anthropic:
                    return "https://api.anthropic.com/v1/messages";
                case AiService.Ollama:
                    return "http://localhost:11434/api/chat";
                default:
                    return "https://openrouter.ai/api/v1/chat/completions";
            }
        }
    }

    internal enum AiService
    {
        OpenRouter = 0,
        OpenAICompatible = 1,
        Anthropic = 2,
        Ollama = 3
    }

    internal static class CursorLikeSettingsProvider
    {
        [SettingsProvider]
        private static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Preferences/CursorLike", SettingsScope.User)
            {
                label = "CursorLike",
                guiHandler = _ =>
                {
                    EditorGUILayout.LabelField("AI Service", EditorStyles.boldLabel);
                    var previousService = CursorLikeSettings.Service;
                    CursorLikeSettings.Service = (AiService)EditorGUILayout.EnumPopup("Provider", CursorLikeSettings.Service);
                    if (previousService != CursorLikeSettings.Service)
                    {
                        CursorLikeSettings.Endpoint = CursorLikeSettings.DefaultEndpointFor(CursorLikeSettings.Service);
                    }

                    CursorLikeSettings.ApiKey = EditorGUILayout.PasswordField("API Key", CursorLikeSettings.ApiKey);
                    CursorLikeSettings.Model = EditorGUILayout.TextField("Model", CursorLikeSettings.Model);
                    CursorLikeSettings.Endpoint = EditorGUILayout.TextField("Endpoint", CursorLikeSettings.Endpoint);
                    CursorLikeSettings.Referer = EditorGUILayout.TextField("HTTP-Referer", CursorLikeSettings.Referer);
                    CursorLikeSettings.AppTitle = EditorGUILayout.TextField("X-Title", CursorLikeSettings.AppTitle);
                    CursorLikeSettings.Temperature = EditorGUILayout.Slider("Temperature", CursorLikeSettings.Temperature, 0f, 2f);
                    CursorLikeSettings.MaxTokens = EditorGUILayout.IntField("Max Tokens", CursorLikeSettings.MaxTokens);

                    EditorGUILayout.Space();
                    EditorGUILayout.HelpBox(
                        "Supports OpenRouter, OpenAI-compatible APIs, Anthropic, and local Ollama. This plugin can modify project files and scene objects.",
                        MessageType.Warning);
                }
            };
        }
    }
}
