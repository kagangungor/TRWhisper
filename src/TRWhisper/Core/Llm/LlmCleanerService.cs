using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TRWhisper.Core.Config;
using TRWhisper.Core.Diagnostics;

namespace TRWhisper.Core.Llm
{
    public interface ILlmCleaner
    {
        Task<string> CleanTranscriptAsync(string rawTranscript, CancellationToken cancellationToken = default);
    }

    public class LlmCleanerService : ILlmCleaner
    {
        private readonly ConfigManager _configManager;
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(8) // Kullanıcının bekleme süresini sınırla
        };

        public LlmCleanerService(ConfigManager configManager)
        {
            _configManager = configManager;
        }

        public async Task<string> CleanTranscriptAsync(string rawTranscript, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(rawTranscript))
                return rawTranscript;

            var config = _configManager.Current.LlmCleaning;

            // API anahtarı girilmemişse doğrudan ham transkripti döndür
            if (string.IsNullOrWhiteSpace(config.ApiKey))
            {
                return rawTranscript;
            }

            try
            {
                if (config.Provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
                {
                    return await CallGeminiAsync(rawTranscript, config, cancellationToken);
                }
                else if (config.Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
                {
                    return await CallOpenAiAsync(rawTranscript, config, cancellationToken);
                }
                else
                {
                    // Varsayılan olarak Gemini çağrısı
                    return await CallGeminiAsync(rawTranscript, config, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                FileLog.Write($"[LlmCleanerService] Hata oluştu, ham transkript kullanılıyor: {ex.Message}");
                return rawTranscript;
            }
        }

        private async Task<string> CallGeminiAsync(string rawTranscript, LlmCleaningConfig config, CancellationToken cancellationToken)
        {
            var model = string.IsNullOrWhiteSpace(config.Model) ? "gemini-2.5-flash" : config.Model;
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={config.ApiKey}";

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = $"{config.SystemPrompt}\n\nTranskript:\n{rawTranscript}" }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.2,
                    maxOutputTokens = 1024
                }
            };

            var jsonContent = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(cancellationToken);
                FileLog.Write($"[LlmCleanerService] Gemini API hata döndü ({response.StatusCode}): {errorText}");
                return rawTranscript;
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseJson);

            if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0 &&
                candidates[0].TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var textProp))
            {
                var result = textProp.GetString()?.Trim();
                return string.IsNullOrWhiteSpace(result) ? rawTranscript : result;
            }

            return rawTranscript;
        }

        private async Task<string> CallOpenAiAsync(string rawTranscript, LlmCleaningConfig config, CancellationToken cancellationToken)
        {
            var endpoint = string.IsNullOrWhiteSpace(config.Endpoint) ? "https://api.openai.com/v1/chat/completions" : config.Endpoint;
            var model = string.IsNullOrWhiteSpace(config.Model) ? "gpt-4o-mini" : config.Model;

            var payload = new
            {
                model = model,
                messages = new[]
                {
                    new { role = "system", content = config.SystemPrompt },
                    new { role = "user", content = rawTranscript }
                },
                temperature = 0.2
            };

            var jsonContent = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return rawTranscript;
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseJson);

            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var contentProp))
            {
                var result = contentProp.GetString()?.Trim();
                return string.IsNullOrWhiteSpace(result) ? rawTranscript : result;
            }

            return rawTranscript;
        }
    }
}
