using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InterviewAudit.Infrastructure.Llm
{
    public class LlmApiKeyManager : ILlmApiKeyManager
    {
        private readonly LlmSettings _settings;
        private readonly ILogger<LlmApiKeyManager> _logger;
        private readonly ConcurrentDictionary<string, DateTimeOffset> _exhaustedKeys;

        public LlmApiKeyManager(IOptions<LlmSettings> options, ILogger<LlmApiKeyManager> logger)
        {
            _settings = options.Value;
            _logger = logger;
            _exhaustedKeys = new ConcurrentDictionary<string, DateTimeOffset>();
        }

        public string GetNextAvailableKey(string providerName)
        {
            var keys = GetKeysForProvider(providerName);
            if (!keys.Any())
            {
                return string.Empty;
            }

            foreach (var key in keys)
            {
                if (_exhaustedKeys.TryGetValue(key, out var resetTime))
                {
                    if (DateTimeOffset.UtcNow >= resetTime)
                    {
                        // The penalty time has passed. The key is available again.
                        _exhaustedKeys.TryRemove(key, out _);
                        _logger.LogInformation("{Provider} API Key ending in '...{Masked}' has passed its reset period and is available again.", providerName, MaskKey(key));
                        return key;
                    }
                }
                else
                {
                    // Not in the exhausted dictionary, it is available
                    return key;
                }
            }

            // All keys exhausted
            return string.Empty;
        }

        public void MarkKeyExhausted(string providerName, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;

            int resetMinutes = GetRateLimitResetMinutes(providerName);
            var resetTime = DateTimeOffset.UtcNow.AddMinutes(resetMinutes);

            _exhaustedKeys.AddOrUpdate(key, resetTime, (k, old) => resetTime);

            _logger.LogWarning("{Provider} API Key ending in '...{Masked}' is EXHAUSTED. Marked as unavailable until {ResetTime} (Local: {LocalResetTime}).", 
                providerName, MaskKey(key), resetTime, resetTime.ToLocalTime());
        }

        public bool HasAvailableKeys(string providerName)
        {
            var keys = GetKeysForProvider(providerName);
            if (!keys.Any()) return false;

            return keys.Any(key => 
                !_exhaustedKeys.TryGetValue(key, out var resetTime) || DateTimeOffset.UtcNow >= resetTime
            );
        }

        private List<string> GetKeysForProvider(string providerName)
        {
            var keys = new List<string>();

            if (string.Equals(providerName, "OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                if (_settings.OpenAiSettings?.ApiKeys != null)
                    keys.AddRange(_settings.OpenAiSettings.ApiKeys);
                if (!string.IsNullOrWhiteSpace(_settings.OpenAiSettings?.ApiKey))
                    keys.Add(_settings.OpenAiSettings.ApiKey);
            }
            else if (string.Equals(providerName, "Claude", StringComparison.OrdinalIgnoreCase))
            {
                if (_settings.ClaudeSettings?.ApiKeys != null)
                    keys.AddRange(_settings.ClaudeSettings.ApiKeys);
                if (!string.IsNullOrWhiteSpace(_settings.ClaudeSettings?.ApiKey))
                    keys.Add(_settings.ClaudeSettings.ApiKey);
            }
            else if (string.Equals(providerName, "Gemini", StringComparison.OrdinalIgnoreCase))
            {
                if (_settings.GeminiSettings?.ApiKeys != null)
                    keys.AddRange(_settings.GeminiSettings.ApiKeys);
                if (!string.IsNullOrWhiteSpace(_settings.GeminiSettings?.ApiKey))
                    keys.Add(_settings.GeminiSettings.ApiKey);
            }
            else if (string.Equals(providerName, "Groq", StringComparison.OrdinalIgnoreCase))
            {
                if (_settings.GroqSettings?.ApiKeys != null)
                    keys.AddRange(_settings.GroqSettings.ApiKeys);
                if (!string.IsNullOrWhiteSpace(_settings.GroqSettings?.ApiKey))
                    keys.Add(_settings.GroqSettings.ApiKey);
                
                // Root fallback list compatibility
                if (_settings.GroqApiKeys != null)
                    keys.AddRange(_settings.GroqApiKeys);
            }

            // Global fallback
            if (!keys.Any() && !string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                keys.Add(_settings.ApiKey);
            }

            return keys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .Where(k => !k.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();
        }

        private int GetRateLimitResetMinutes(string providerName)
        {
            if (string.Equals(providerName, "OpenAI", StringComparison.OrdinalIgnoreCase))
                return _settings.OpenAiSettings?.RateLimitResetMinutes > 0 ? _settings.OpenAiSettings.RateLimitResetMinutes : 60;
            
            if (string.Equals(providerName, "Claude", StringComparison.OrdinalIgnoreCase))
                return _settings.ClaudeSettings?.RateLimitResetMinutes > 0 ? _settings.ClaudeSettings.RateLimitResetMinutes : 60;
            
            if (string.Equals(providerName, "Gemini", StringComparison.OrdinalIgnoreCase))
                return _settings.GeminiSettings?.RateLimitResetMinutes > 0 ? _settings.GeminiSettings.RateLimitResetMinutes : 60;
            
            if (string.Equals(providerName, "Groq", StringComparison.OrdinalIgnoreCase))
            {
                if (_settings.GroqSettings?.RateLimitResetMinutes > 0)
                    return _settings.GroqSettings.RateLimitResetMinutes;
                return _settings.GroqRateLimitResetMinutes > 0 ? _settings.GroqRateLimitResetMinutes : 60;
            }

            return 60;
        }

        private static string MaskKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return "";
            if (key.Length <= 4) return "****";
            return key.Substring(key.Length - 4);
        }
    }
}
