Act as a Senior Fullstack .NET Developer (10+ years of experience).

Update the existing code to implement Clean Architecture pattern and SOLID principle with the following specifications:

**Configuration Management:**
- Don't write everything fetching events,  meetings, transcript inside the Program.cs, Instead create a clean service class strictly follow SOLID principles.
- Retrieve the base URL from `app settings.json` configuration file
- Store additional API endpoints as variables in the configuration
- Use the following Graph API endpoint to fetch events: `https://graph.microsoft.com/v1.0/users/876ab97f-5bf6-4162-9228-da10f1bc2d76/events?$count=true`


**User/Organizer Configuration:**
- Store the organizer user ID: `876ab97f-5bf6-4162-9228-da10f1bc2d76`
- Store the organizer email: `Interview.net@neosofttech.com`
- This user will be responsible for organizing all Teams meetings


**Meeting Data Fetching Logic:**
- Fetch all events from the provided URL
- Iterate through all retrieved events
- For each event/meeting, extract the meeting ID, group ID, Candidate Name, nInterviewer and description
- Verify along with groupid and meeting_id from "processed-meetings.json" and don't again generate the transcript for the processed meeting 
- Use the `onlineMeetings` endpoint to get meeting details
- Use the transcript file endpoint to fetch transcripts for each meeting


**Transcript Processing:**
- Fetch transcripts for all meetings/events organized by the specified user ID
- Pass each transcript to the existing LLM implementation for analysis
- Generate the analysis report for each meeting


**Duplicate Prevention:**
- Do NOT duplicate fetch meeting details and transcripts
- Utilize the existing logic from previous implementation
- The file `processed-meetings.json` should continue functioning as it did in the previous code - maintain the same structure and behavior for tracking processed meetings


**Requirements:**
- Ensure all API calls use the base URL from configuration
- Maintain the existing code structure for LLM integration and report analysis
- Preserve all other functionality that was working in the previous implementation
- Handle the processed-meetings.json file to track which meetings have already been processed to avoid duplicates