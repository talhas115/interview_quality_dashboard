# TOKEN OPTIMIZATION STRATEGY

The objective is to minimize LLM cost while maintaining audit quality.

---

# RULES

Never send unnecessary data.

Never send:

- Graph API responses
- Meeting metadata not used by prompt
- Internal logs
- Processing status
- Group information
- Service configuration

Only send:

1. JD
2. Transcript

---

# PROMPT STRATEGY

Load prompt template from:

Prompts/MasterPrompt.txt

Inject only:

{{JD}}

{{TRANSCRIPT}}

Do not generate prompts dynamically.

Do not rewrite prompts.

Do not optimize prompts at runtime.

---

# CONTEXT MANAGEMENT

Before calling LLM:

Remove:

- Empty lines
- Duplicate spaces
- Repeated metadata
- Transcript artifacts

Keep:

- Interview Questions
- Candidate Answers
- Technical Discussion

---

# MODEL SETTINGS

Load from configuration.

Example:

{
  "Provider": "Claude",
  "Model": "claude-sonnet-4"
}

or

{
  "Provider": "OpenAI",
  "Model": "gpt-5"
}

No model names may be hardcoded.

---

# REPORT GENERATION

Generate only:

Markdown Report

Avoid:

- Additional summaries
- Secondary analysis
- Duplicate outputs

---

# FAILURE HANDLING

If LLM fails:

- Retry using configured policy.
- Log failure.
- Preserve transcript.
- Mark meeting as unprocessed.

Never lose data.

---

# FUTURE EXTENSIBILITY

The token strategy must allow:

- GPT Models
- Claude Models
- Gemini Models
- Future Providers

without changing business logic.

Use provider abstraction through interfaces.