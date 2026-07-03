# Microsoft Teams Transcript Pipeline – LLM Guardrail

## 🎯 Purpose

This document defines the **strict end-to-end rules** for fetching Microsoft Graph Teams meeting transcripts and ensuring that the **LLM is only called when valid transcript content is available**.

This is designed to:
- Prevent unnecessary LLM calls
- Reduce token cost and API usage
- Avoid processing empty or invalid transcript data
- Ensure deterministic pipeline behavior

---

## 🚨 CORE RULE (NON-NEGOTIABLE)

❗ The LLM must NOT be called unless transcript content is successfully retrieved.

If transcript is missing or empty:
- STOP execution immediately
- LOG the reason
- DO NOT retry or fallback to LLM

---

## 🔄 REQUIRED EXECUTION FLOW

### Step 1 — Resolve Online Meeting

```http
GET /users/{userId}/onlineMeetings?$filter=JoinWebUrl eq '{joinUrl}'
Example: https://graph.microsoft.com/v1.0/users/876ab97f-5bf6-4162-9228-da10f1bc2d76/onlineMeetings?$filter=JoinWebUrl eq 'https://teams.microsoft.com/l/meetup-join/19%3ameeting_MDBmZGNiYTItNDJhNi00YjgyLWI3NzktNTA0NmY5ZDE1OTAw%40thread.v2/0?context=%7b%22Tid%22%3a%22b553c4ea-9255-4e9b-ab21-e65af26175cf%22%2c%22Oid%22%3a%22876ab97f-5bf6-4162-9228-da10f1bc2d76%22%7d'   

Extract:

onlineMeetingId = response.value[0].id
Step 2 — Fetch Transcript List
GET /users/{userId}/onlineMeetings/{onlineMeetingId}/transcripts
Example: https://graph.microsoft.com/v1.0/users/876ab97f-5bf6-4162-9228-da10f1bc2d76/onlineMeetings/MSo4NzZhYjk3Zi01YmY2LTQxNjItOTIyOC1kYTEwZjFiYzJkNzYqMCoqMTk6bWVldGluZ19NREJtWkdOaVlUSXROREpoTmkwMFlqZ3lMV0kzTnprdE5UQTBObVk1WkRFMU9UQXdAdGhyZWFkLnYy/transcripts
❌ CASE: NO TRANSCRIPTS FOUND

If response is:

"value": []
ACTION:
❌ DO NOT call LLM
❌ DO NOT retry immediately
✅ LOG:
Transcript not found. Skipping LLM invocation. meetingId={onlineMeetingId}
🛑 STOP execution
Step 3 — Extract Transcript ID

If transcripts exist:

transcriptId = response.value[0].id
Step 4 — Fetch Transcript Content
GET /users/{userId}/onlineMeetings/{onlineMeetingId}/transcripts/{transcriptId}/content
❌ CASE: EMPTY OR FAILED CONTENT

If transcript content is:

null
empty
failed response
ACTION:
❌ DO NOT call LLM
🛑 STOP execution
✅ LOG:
Transcript content not available. Skipping LLM invocation. meetingId={onlineMeetingId}
✅ SUCCESS CONDITION (ONLY VALID PATH)


# Proceed to LLM ONLY IF ALL CONDITIONS ARE MET:

onlineMeetingId exists
transcript list is NOT empty
transcriptId is valid
transcript content is successfully fetched
content length > 0
🤖 LLM INVOCATION RULE
CALL_LLM = TRUE ONLY IF valid transcript content exists

Otherwise:

CALL_LLM = FALSE


📉 COST OPTIMIZATION RULE

# To minimize token usage and API cost:

❌ NEVER:

Call LLM with empty transcript
Call LLM with partial transcript
Generate fallback or synthetic summaries
Retry LLM when transcript is missing

✔ ALWAYS:

Validate transcript before LLM invocation
Stop pipeline early if data is missing
📊 LOGGING STANDARD
✅ Success Logs
[INFO] Meeting resolved: onlineMeetingId={onlineMeetingId}
[INFO] Transcript found: transcriptId={transcriptId}
[INFO] Transcript content fetched successfully
[INFO] Proceeding to LLM invocation
⚠️ Failure Logs
[WARN] Transcript not found. Skipping LLM invocation. meetingId={onlineMeetingId}
[WARN] Transcript content missing. Skipping LLM invocation. meetingId={onlineMeetingId}
🔁 OPTIONAL RETRY STRATEGY (IF ENABLED)

If transcript is not available:

Retry every 2–5 minutes
Maximum retries: 5
After max retries → STOP permanently
Do NOT call LLM during retry phase
🎯 FINAL GOAL
Zero unnecessary LLM invocations
Strict transcript validation before processing
Reduced cost and token consumption
Reliable Microsoft Graph transcript pipeline