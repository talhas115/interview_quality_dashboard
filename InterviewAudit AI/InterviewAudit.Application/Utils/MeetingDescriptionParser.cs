using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace InterviewAudit.Application.Utils
{
    public class MeetingDescriptionParser
    {
        private readonly List<string> _candidatePatterns;
        private readonly List<string> _interviewerPatterns;
        private readonly List<string> _jdPatterns;
        private readonly List<string> _groupIdPatterns;
        private readonly List<string> _candidateIdPatterns;

        public MeetingDescriptionParser(
            List<string>? candidatePatterns = null,
            List<string>? interviewerPatterns = null,
            List<string>? jdPatterns = null,
            List<string>? groupIdPatterns = null,
            List<string>? candidateIdPatterns = null)
        {
            // NOTE: The meeting description format uses space before colon:
            //   "Candidate Name : Amit Singh"
            //   "Interviewer : Ramsingh Chauhan"
            // Patterns must account for optional whitespace both before AND after the colon.

            _candidatePatterns = (candidatePatterns != null && candidatePatterns.Count > 0)
                ? candidatePatterns
                : new List<string>
                {
                    // Handles: "Candidate Name : Amit Singh"
                    @"Candidate\s+Name\s*:\s*([^\r\n]+)",
                    // Handles: "Candidate : Amit Singh"
                    @"Candidate\s*:\s*([^\r\n]+)",
                    // Handles: "Applicant : Amit Singh"
                    @"Applicant\s*:\s*([^\r\n]+)",
                };

            _interviewerPatterns = (interviewerPatterns != null && interviewerPatterns.Count > 0)
                ? interviewerPatterns
                : new List<string>
                {
                    // Handles: "Interviewer : Ramsingh Chauhan"
                    @"Interviewer\s*Name\s*:\s*([^\r\n]+)",
                    @"Interviewer\s*:\s*([^\r\n]+)",
                    @"Evaluator\s*:\s*([^\r\n]+)",
                    // Handles "👤 Panel Name: ..."
                    @"Panel\s*Name\s*:\s*([^\r\n]+)",
                };

            _jdPatterns = (jdPatterns != null && jdPatterns.Count > 0)
                ? jdPatterns
                : new List<string>
                {
                    // Matches "Job Description" (with optional colon/dash) and captures everything after it,
                    // including "Position :" if it's there.
                    @"Job\s+Description\s*(?:[:\-]+)?\s*([\s\S]*?)(?=\s*(?:GID|CID)\s*:|\z)",
                    @"JD\s*(?:[:\-]+)?\s*([\s\S]*?)(?=\s*(?:GID|CID)\s*:|\z)",
                    @"Position\s*:\s*([\s\S]*?)(?=\s*(?:GID|CID)\s*:|\z)",
                };

            _groupIdPatterns = (groupIdPatterns != null && groupIdPatterns.Count > 0)
                ? groupIdPatterns
                : new List<string>
                {
                    @"GID\s*:\s*([^\r\n]+)",
                    @"Group\s*ID\s*:\s*([^\r\n]+)",
                };

            _candidateIdPatterns = (candidateIdPatterns != null && candidateIdPatterns.Count > 0)
                ? candidateIdPatterns
                : new List<string>
                {
                    @"CID\s*:\s*([^\r\n]+)",
                    @"Candidate\s*ID\s*:\s*([^\r\n]+)",
                };
        }

        public (string CandidateName, string InterviewerName, string JobDescription, string GroupId, string CandidateId) Parse(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return (string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
            }

            // Remove style and script tags and their content
            string cleanDescription = Regex.Replace(description, @"<style[^>]*>[\s\S]*?</style>", string.Empty, RegexOptions.IgnoreCase);
            cleanDescription = Regex.Replace(cleanDescription, @"<script[^>]*>[\s\S]*?</script>", string.Empty, RegexOptions.IgnoreCase);
            
            // Strip remaining HTML tags
            cleanDescription = Regex.Replace(cleanDescription, "<.*?>", string.Empty);
            cleanDescription = System.Net.WebUtility.HtmlDecode(cleanDescription);

            string candidateName = ExtractValue(cleanDescription, _candidatePatterns);
            string interviewerName = ExtractValue(cleanDescription, _interviewerPatterns);
            string jd = ExtractValue(cleanDescription, _jdPatterns);
            string groupId = ExtractValue(cleanDescription, _groupIdPatterns);
            string candidateId = ExtractValue(cleanDescription, _candidateIdPatterns);

            return (candidateName, interviewerName, jd, groupId, candidateId);
        }

        private string ExtractValue(string text, List<string> patterns)
        {
            foreach (var pattern in patterns)
            {
                try
                {
                    var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    if (match.Success)
                    {
                        string val = string.Empty;
                        // Try named group "value" first, then fallback to group 1
                        if (match.Groups["value"] != null && match.Groups["value"].Success)
                        {
                            val = match.Groups["value"].Value.Trim();
                        }
                        else if (match.Groups.Count > 1)
                        {
                            val = match.Groups[1].Value.Trim();
                        }

                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            return val;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PARSER] Pattern exception for '{pattern}': {ex.Message}");
                }
            }

            return string.Empty;
        }
    }
}
