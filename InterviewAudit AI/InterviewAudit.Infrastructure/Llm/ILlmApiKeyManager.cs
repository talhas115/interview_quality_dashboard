using System;

namespace InterviewAudit.Infrastructure.Llm
{
    public interface ILlmApiKeyManager
    {
        string GetNextAvailableKey(string providerName);
        void MarkKeyExhausted(string providerName, string key);
        bool HasAvailableKeys(string providerName);
    }
}
