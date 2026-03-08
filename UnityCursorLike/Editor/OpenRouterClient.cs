using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace CursorLike.Editor
{
    internal static class OpenRouterClient
    {
        private static readonly HttpClient HttpClient = new HttpClient();

        internal static async Task<string> RequestAsync(List<ChatMessage> messages)
        {
            var service = CursorLikeSettings.Service;
            var endpoint = CursorLikeSettings.Endpoint;
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                throw new InvalidOperationException("Missing endpoint. Set it in Preferences > CursorLike.");
            }

            using var request = BuildRequest(service, endpoint, messages);
            using var response = await HttpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(service + " error: " + response.StatusCode + "\n" + body);
            }

            return ParseResponse(service, body);
        }

        private static HttpRequestMessage BuildRequest(AiService service, string endpoint, List<ChatMessage> messages)
        {
            switch (service)
            {
                case AiService.OpenAICompatible:
                    return BuildOpenAiCompatibleRequest(endpoint, messages);
                case AiService.Anthropic:
                    return BuildAnthropicRequest(endpoint, messages);
                case AiService.Ollama:
                    return BuildOllamaRequest(endpoint, messages);
                default:
                    return BuildOpenRouterRequest(endpoint, messages);
            }
        }

        private static HttpRequestMessage BuildOpenRouterRequest(string endpoint, List<ChatMessage> messages)
        {
            EnsureApiKeyRequired();
            var payload = BuildOpenAiRequestPayload(messages);
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("Authorization", "Bearer " + CursorLikeSettings.ApiKey);
            request.Headers.Add("HTTP-Referer", CursorLikeSettings.Referer);
            request.Headers.Add("X-Title", CursorLikeSettings.AppTitle);
            request.Content = new StringContent(JsonUtility.ToJson(payload), Encoding.UTF8, "application/json");
            return request;
        }

        private static HttpRequestMessage BuildOpenAiCompatibleRequest(string endpoint, List<ChatMessage> messages)
        {
            EnsureApiKeyRequired();
            var payload = BuildOpenAiRequestPayload(messages);
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("Authorization", "Bearer " + CursorLikeSettings.ApiKey);
            request.Content = new StringContent(JsonUtility.ToJson(payload), Encoding.UTF8, "application/json");
            return request;
        }

        private static HttpRequestMessage BuildAnthropicRequest(string endpoint, List<ChatMessage> messages)
        {
            EnsureApiKeyRequired();
            var systemPrompt = string.Empty;
            var anthropicMessages = new List<AnthropicMessage>();
            for (var i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                if (msg.role.Equals("system", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(systemPrompt))
                    {
                        systemPrompt = msg.content;
                    }
                    else
                    {
                        systemPrompt += "\n\n" + msg.content;
                    }

                    continue;
                }

                anthropicMessages.Add(new AnthropicMessage
                {
                    role = msg.role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user",
                    content = msg.content
                });
            }

            var payload = new AnthropicRequest
            {
                model = CursorLikeSettings.Model,
                max_tokens = CursorLikeSettings.MaxTokens,
                system = systemPrompt,
                messages = anthropicMessages.ToArray()
            };

            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("x-api-key", CursorLikeSettings.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Content = new StringContent(JsonUtility.ToJson(payload), Encoding.UTF8, "application/json");
            return request;
        }

        private static HttpRequestMessage BuildOllamaRequest(string endpoint, List<ChatMessage> messages)
        {
            var requestMessages = new OllamaMessage[messages.Count];
            for (var i = 0; i < messages.Count; i++)
            {
                requestMessages[i] = new OllamaMessage { role = messages[i].role, content = messages[i].content };
            }

            var payload = new OllamaRequest
            {
                model = CursorLikeSettings.Model,
                messages = requestMessages,
                stream = false
            };

            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = new StringContent(JsonUtility.ToJson(payload), Encoding.UTF8, "application/json");
            return request;
        }

        private static OpenAiCompatibleRequest BuildOpenAiRequestPayload(List<ChatMessage> messages)
        {
            var requestMessages = new RequestMessage[messages.Count];
            for (var i = 0; i < messages.Count; i++)
            {
                requestMessages[i] = new RequestMessage { role = messages[i].role, content = messages[i].content };
            }

            return new OpenAiCompatibleRequest
            {
                model = CursorLikeSettings.Model,
                messages = requestMessages,
                temperature = CursorLikeSettings.Temperature,
                max_tokens = CursorLikeSettings.MaxTokens
            };
        }

        private static string ParseResponse(AiService service, string body)
        {
            switch (service)
            {
                case AiService.Anthropic:
                    var anthropic = JsonUtility.FromJson<AnthropicResponse>(body);
                    if (anthropic?.content == null || anthropic.content.Length == 0) throw new InvalidOperationException("Unexpected Anthropic response payload.");
                    return anthropic.content[0].text ?? string.Empty;
                case AiService.Ollama:
                    var ollama = JsonUtility.FromJson<OllamaResponse>(body);
                    if (ollama?.message == null) throw new InvalidOperationException("Unexpected Ollama response payload.");
                    return ollama.message.content ?? string.Empty;
                default:
                    var openAi = JsonUtility.FromJson<OpenAiCompatibleResponse>(body);
                    if (openAi?.choices == null || openAi.choices.Length == 0 || openAi.choices[0].message == null)
                    {
                        throw new InvalidOperationException("Unexpected OpenAI-compatible response payload.");
                    }

                    return openAi.choices[0].message.content ?? string.Empty;
            }
        }

        private static void EnsureApiKeyRequired()
        {
            if (string.IsNullOrWhiteSpace(CursorLikeSettings.ApiKey))
            {
                throw new InvalidOperationException("Missing API key. Set it in Preferences > CursorLike.");
            }
        }

        [Serializable]
        private class OpenAiCompatibleRequest
        {
            public string model;
            public RequestMessage[] messages;
            public float temperature;
            public int max_tokens;
        }

        [Serializable]
        private class RequestMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        private class OpenAiCompatibleResponse
        {
            public Choice[] choices;
        }

        [Serializable]
        private class Choice
        {
            public AssistantMessage message;
        }

        [Serializable]
        private class AssistantMessage
        {
            public string content;
        }

        [Serializable]
        private class AnthropicRequest
        {
            public string model;
            public int max_tokens;
            public string system;
            public AnthropicMessage[] messages;
        }

        [Serializable]
        private class AnthropicMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        private class AnthropicResponse
        {
            public AnthropicContent[] content;
        }

        [Serializable]
        private class AnthropicContent
        {
            public string text;
        }

        [Serializable]
        private class OllamaRequest
        {
            public string model;
            public OllamaMessage[] messages;
            public bool stream;
        }

        [Serializable]
        private class OllamaMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        private class OllamaResponse
        {
            public OllamaMessage message;
        }
    }

    [Serializable]
    internal class ChatMessage
    {
        public string role;
        public string content;
    }
}
