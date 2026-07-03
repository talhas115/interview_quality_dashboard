using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InterviewAudit.Infrastructure.Llm
{
    public class GroqApiKeyManager : IGroqApiKeyManager
    {
        private readonly LlmSettings _settings;
        private readonly ILogger<GroqApiKeyManager> _logger;
        private readonly ConcurrentDictionary<string, DateTimeOffset> _exhaustedKeys;

        public GroqApiKeyManager(IOptions<LlmSettings> options, ILogger<GroqApiKeyManager> logger)
        {
            _settings = options.Value;
            _logger = logger;
            _exhaustedKeys = new ConcurrentDictionary<string, DateTimeOffset>();
        }

        public string GetNextAvailableKey()
        {
            if (_settings.GroqApiKeys == null || !_settings.GroqApiKeys.Any())
            {
                // Fallback to legacy single key if no list provided
                if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
                {
                    return _settings.ApiKey;
                }
                return string.Empty;
            }

            foreach (var key in _settings.GroqApiKeys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;

                if (_exhaustedKeys.TryGetValue(key, out var resetTime))
                {
                    if (DateTimeOffset.UtcNow >= resetTime)
                    {
                        // The penalty time has passed. The key is available again.
                        _exhaustedKeys.TryRemove(key, out _);
                        _logger.LogInformation("Groq API Key ending in '...{Masked}' has passed its reset period and is available again.", MaskKey(key));
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

        public void MarkKeyExhausted(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;

            int resetMinutes = _settings.GroqRateLimitResetMinutes > 0 ? _settings.GroqRateLimitResetMinutes : 60;
            var resetTime = DateTimeOffset.UtcNow.AddMinutes(resetMinutes);

            _exhaustedKeys.AddOrUpdate(key, resetTime, (k, old) => resetTime);
            
            _logger.LogWarning("Groq API Key ending in '...{Masked}' is EXHAUSTED. Marked as unavailable until {ResetTime} (Local: {LocalResetTime}).", 
                MaskKey(key), resetTime, resetTime.ToLocalTime());
        }

        public bool HasAvailableKeys()
        {
            if (_settings.GroqApiKeys == null || !_settings.GroqApiKeys.Any())
            {
                return !string.IsNullOrWhiteSpace(_settings.ApiKey);
            }

            return _settings.GroqApiKeys.Any(key => 
                !string.IsNullOrWhiteSpace(key) && 
                (!_exhaustedKeys.TryGetValue(key, out var resetTime) || DateTimeOffset.UtcNow >= resetTime)
            );
        }

        private static string MaskKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return "";
            if (key.Length <= 4) return "****";
            return key.Substring(key.Length - 4);
        }
    }
}
