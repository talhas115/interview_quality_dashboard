# PROJECT NAME

Interview Audit Automation

---

# BUSINESS OBJECTIVE

Automate the complete interview audit process by retrieving Microsoft Teams interview transcripts, extracting Job Descriptions from meeting details, sending data to LLM providers, generating professional interview audit reports, and tracking processed meetings.

The application must run without human intervention.

---

# HIGH LEVEL FLOW

Teams Interview
→ Auto Recording
→ Auto Transcript
→ Scheduler
→ Graph API
→ Meeting Details
→ Transcript
→ Master Prompt
→ LLM
→ Audit Report
→ Processed Meeting Tracking

---

# FUNCTIONAL REQUIREMENTS

## Scheduler

The system shall:

- Run continuously as a Worker Service.
- Execute every configurable interval.
- Load interval from configuration.
- Support graceful shutdown.
- Support cancellation tokens.

---

## Graph API Integration

The system shall:

- Authenticate using Azure App Registration.
- Load credentials from configuration.
- Retrieve configured Teams Groups.
- Retrieve meetings.
- Retrieve meeting attendees. (If candidate/interviewer name is not available then AI should detect from transcript file and attendees record and map it correctly without any assumption with thorough checks.)
- Retrieve meeting descriptions.
- Retrieve transcripts.
- Retrieve organizer details.
- Retrieve participant details when available.

---

## Meeting Processing

The system shall:

- Detect newly generated transcripts.
- Skip already processed meetings.
- Use processed-meetings.json.
- Prevent duplicate report generation.

---

## Meeting Description Parsing

The system shall parse:

Candidate Name:
Interviewer Name:
JD:

from meeting description if provided else AI should detect from transcript file and attendees record retrieve from meeting and map it correctly without any assumption with thorough checks.

Parsing rules must be configurable.

---

## Prompt Management

The system shall:

- Read prompt from file.
- Support prompt replacement.
- Support future prompt versioning.
- Never hardcode prompts.

Prompt Variables:

{{JD}}

{{TRANSCRIPT}}

---

## LLM Integration

The system shall support:

- OpenAI
- Claude

Future providers must be pluggable.

Provider selection must come from configuration.

Model selection must come from configuration.

API keys must come from configuration.

---

## Audit Report Generation

Generate:

AuditReport.md

Naming Convention:

GroupId_CandidateName_InterviewerName.md

Example:

12345_Zeeshan_John.md

---

## Report Storage

Store reports in configurable folders.

Folder path must come from configuration.

No hardcoded paths allowed.

---

## Processed Meeting Tracking

Store processed meetings inside:

processed-meetings.json

Structure:

{
  "group_id": [
    "meeting_id_1",
    "meeting_id_2"
  ]
}

---

## Logging

The system shall:

- Log startup
- Log shutdown
- Log Graph API calls
- Log transcript retrieval
- Log AI requests
- Log report generation
- Log failures

Use structured logging.

---

## MSI Setup

Generate MSI installer supporting:

- Silent installation
- Upgrade installation
- Repair installation
- Uninstall

Installer must:

- Install Worker Service
- Create folders
- Register Windows Service
- Install configuration files
- Create logs folder
- Create reports folder

---

# NON FUNCTIONAL REQUIREMENTS

Performance:
- Support 1000+ interviews

Reliability:
- Auto recovery

Maintainability:
- Configuration driven

Security:
- Secrets externalized

Scalability:
- Multiple Teams Groups

Extensibility:
- Future AI providers

Availability:
- 24x7