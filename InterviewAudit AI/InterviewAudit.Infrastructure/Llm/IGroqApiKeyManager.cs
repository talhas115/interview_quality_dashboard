using System;
using System.Collections.Generic;

namespace InterviewAudit.Infrastructure.Llm
{
    public interface IGroqApiKeyManager
    {
        string GetNextAvailableKey();
        void MarkKeyExhausted(string key);
        bool HasAvailableKeys();
    }
}
