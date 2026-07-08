using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using InterviewAudit.Domain.Interfaces;
using InterviewAudit.Domain.Models;
using InterviewAudit.Application.Utils;

namespace InterviewAudit.Application.Services
{
    public class SchedulerOptions
    {
        public int IntervalSeconds { get; set; }
    }

    public class StorageOptions
    {
        public string ReportsFolder { get; set; } = string.Empty;
        public string TranscriptsFolder { get; set; } = string.Empty;
        public string StateFilePath { get; set; } = string.Empty;
        public string MockDataFolder { get; set; } = string.Empty;
        public string JdFolder { get; set; } = string.Empty;
    }

    public class PromptOptions
    {
        public string PromptFilePath { get; set; } = string.Empty;
    }

    public class OrganizerOptions
    {
        public string OrganizerUserId { get; set; } = string.Empty;
    }

    public class ParserOptions
    {
        public List<string> CandidatePatterns { get; set; } = new List<string>();
        public List<string> InterviewerPatterns { get; set; } = new List<string>();
        public List<string> JdPatterns { get; set; } = new List<string>();
        public List<string> GroupIdPatterns { get; set; } = new List<string>();
        public List<string> CandidateIdPatterns { get; set; } = new List<string>();
    }

    public class AuditScheduler
    {
        private readonly IGraphService _graphService;
        private readonly ILlmService _llmService;
        private readonly IStateRepository _stateRepository;
        private readonly IReportRepository _reportRepository;
        private readonly ILogger<AuditScheduler> _logger;
        private readonly StorageOptions _storageOptions;
        private readonly PromptOptions _promptOptions;
        private readonly ParserOptions _parserOptions;
        private readonly OrganizerOptions _organizerOptions;
        private readonly ITranscriptAggregator _transcriptAggregator;
        private readonly IChunkedLlmProcessor _chunkedLlmProcessor;
        private readonly InterviewFilterSettings _filterSettings;

        public AuditScheduler(
            IGraphService graphService,
            ILlmService llmService,
            IStateRepository stateRepository,
            IReportRepository reportRepository,
            ILogger<AuditScheduler> logger,
            IOptions<StorageOptions> storageOptions,
            IOptions<PromptOptions> promptOptions,
            IOptions<ParserOptions> parserOptions,
            IOptions<OrganizerOptions> organizerOptions,
            ITranscriptAggregator transcriptAggregator,
            IChunkedLlmProcessor chunkedLlmProcessor,
            IOptions<InterviewFilterSettings> filterOptions)
        {
            _graphService = graphService;
            _llmService = llmService;
            _stateRepository = stateRepository;
            _reportRepository = reportRepository;
            _logger = logger;
            _storageOptions = storageOptions.Value;
            _promptOptions = promptOptions.Value;
            _parserOptions = parserOptions.Value;
            _organizerOptions = organizerOptions.Value;
            _transcriptAggregator = transcriptAggregator;
            _chunkedLlmProcessor = chunkedLlmProcessor;
            _filterSettings = filterOptions.Value;
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("");
            _logger.LogInformation("══════════════════════════════════════════════════════════");
            _logger.LogInformation("  Interview Audit Scheduler — Execution Started");
            _logger.LogInformation("  Time   : {Time}", DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            _logger.LogInformation("══════════════════════════════════════════════════════════");

            string organizerUserId = _organizerOptions.OrganizerUserId;
            if (string.IsNullOrEmpty(organizerUserId))
            {
                _logger.LogWarning("No OrganizerUserId configured for auditing.");
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (_filterSettings.EnableFilterMode && _filterSettings.StopSchedulerAfterCompletion)
            {
                if (await _stateRepository.IsFilterExecutionCompletedAsync(cancellationToken))
                {
                    _logger.LogInformation("  ??   [SKIP] Filter execution already completed. Waiting for next config change.");
                    return;
                }
            }

            try
            {
                _logger.LogInformation("Checking meetings for Organizer: {OrganizerUserId}", organizerUserId);
                await ProcessOrganizerMeetingsAsync(organizerUserId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process meetings for organizer {OrganizerUserId}", organizerUserId);
            }

            _logger.LogInformation("");
            _logger.LogInformation("══════════════════════════════════════════════════════════");
            _logger.LogInformation("  Interview Audit Scheduler — Execution Completed");
            _logger.LogInformation("  Time   : {Time}", DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            _logger.LogInformation("══════════════════════════════════════════════════════════");
            _logger.LogInformation("");
        }

        private async Task ProcessOrganizerMeetingsAsync(string organizerUserId, CancellationToken cancellationToken)
        {
            _logger.LogInformation("  🔍  Parser: {CandidateCount} CandidatePatterns | {InterviewerCount} InterviewerPatterns",
                _parserOptions.CandidatePatterns.Count, _parserOptions.InterviewerPatterns.Count);

            // Initialize description parser
            var parser = new MeetingDescriptionParser(
                _parserOptions.CandidatePatterns,
                _parserOptions.InterviewerPatterns,
                _parserOptions.JdPatterns,
                _parserOptions.GroupIdPatterns,
                _parserOptions.CandidateIdPatterns);

            int globalIndex = 0;

            // Retrieve meetings from Graph API using streaming pagination
            var retrievalResult = await _graphService.ProcessOrganizerMeetingsAsync(organizerUserId, async (meetingsPage) => 
            {
                bool shouldContinueFetching = true;
                int oldMeetingsCount = 0;

                foreach (var meeting in meetingsPage)
                {
                    if (cancellationToken.IsCancellationRequested) 
                    {
                        shouldContinueFetching = false;
                        break;
                    }

                    try
                    {
                        globalIndex++;
                        _logger.LogInformation("");
                        _logger.LogInformation("──────────────────────────────────────────────────────────");
                        _logger.LogInformation("  📌  Meeting [{Index}]", globalIndex);
                        _logger.LogInformation("  Subject  : {Subject}", meeting.Subject);
                        _logger.LogInformation("  ID       : {MeetingId}", meeting.Id);
                        _logger.LogInformation("──────────────────────────────────────────────────────────");

                        // Parse description from ONLY Description (HTML content) since BodyPreview is truncated by Graph API
                        string combinedText = meeting.Description;
                            
                        var (candidateName, interviewerName, jd, groupId, candidateId) = parser.Parse(combinedText);

                        // Fallback: Resolve CandidateId from Attendees list if missing from description
                        if (string.IsNullOrWhiteSpace(candidateId))
                        {
                            var candidateAttendee = meeting.Attendees?.FirstOrDefault(a => 
                                !string.IsNullOrWhiteSpace(candidateName) && 
                                a.Name.Contains(candidateName.Replace("_", " "), StringComparison.OrdinalIgnoreCase));
                            
                            if (candidateAttendee == null && meeting.Attendees != null)
                            {
                                candidateAttendee = meeting.Attendees.FirstOrDefault(a => 
                                    !a.Name.Contains(interviewerName.Replace("_", " "), StringComparison.OrdinalIgnoreCase) &&
                                    !a.Email.Equals(meeting.OrganizerName, StringComparison.OrdinalIgnoreCase));
                            }

                            if (candidateAttendee != null && !string.IsNullOrWhiteSpace(candidateAttendee.Email))
                            {
                                int atIndex = candidateAttendee.Email.IndexOf('@');
                                candidateId = atIndex > 0 ? candidateAttendee.Email.Substring(0, atIndex) : candidateAttendee.Email;
                            }
                        }

                        // --- FLEXIBLE FILTERING LOGIC ---
                        // If user provided a specific target name in settings, strictly filter by it
                        if (!string.IsNullOrWhiteSpace(_filterSettings.InterviewName))
                        {
                            bool matchesInterviewer = !string.IsNullOrWhiteSpace(interviewerName) && interviewerName.Contains(_filterSettings.InterviewName, StringComparison.OrdinalIgnoreCase);
                            bool matchesCandidate = !string.IsNullOrWhiteSpace(candidateName) && candidateName.Contains(_filterSettings.InterviewName, StringComparison.OrdinalIgnoreCase);
                            bool matchesSubject = meeting.Subject != null && meeting.Subject.Contains(_filterSettings.InterviewName, StringComparison.OrdinalIgnoreCase);

                            if (!matchesInterviewer && !matchesCandidate && !matchesSubject)
                            {
                                _logger.LogInformation("  ⏭️   [SKIP] Target name '{Target}' did not match Interviewer ({Interviewer}), Candidate ({Candidate}), or Subject.", 
                                    _filterSettings.InterviewName, interviewerName, candidateName);
                                continue;
                            }
                        }

                        // Date filter logic (if enabled)
                        if (_filterSettings.EnableFilterMode && _filterSettings.RecentDays > 0)
                        {
                            var limitDate = DateTimeOffset.UtcNow.AddDays(-_filterSettings.RecentDays);
                            if ((meeting.StartDateTime == null || meeting.StartDateTime < limitDate) && 
                                (meeting.EndDateTime == null || meeting.EndDateTime < limitDate))
                            {
                                oldMeetingsCount++;
                                _logger.LogDebug("  ⏭️   [SKIP] Meeting is older than the configured limit of {Days} days.", _filterSettings.RecentDays);
                                continue;
                            }
                        }
                        // --------------------------------

                        // JD is mandatory. Skip if JD is not found.
                        if (string.IsNullOrWhiteSpace(jd))
                        {
                            _logger.LogError("  ✗  [SKIP] No Job Description found. This meeting will not be audited.");
                            continue;
                        }

                        _logger.LogInformation("  👤  Candidate   : {Candidate} ({CID})", candidateName, candidateId);
                        _logger.LogInformation("  🧑‍💼  Interviewer  : {Interviewer}", interviewerName);
                        _logger.LogInformation("  🗂️   Group ID     : {GID}", groupId);
                        
                        string displayJd = jd.Length > 300 ? jd.Substring(0, 300).Replace("\n", " ") + "..." : jd.Replace("\n", " ");
                        _logger.LogInformation("  📄  JD           : [{JDLength} chars] {JDValue}", jd.Length, displayJd);

                        string effectiveGroupId = string.IsNullOrWhiteSpace(groupId) ? "UnknownGroup" : groupId;

                        // 1. Get processed meetings state
                        var processedMeetings = await _stateRepository.GetProcessedMeetingIdsAsync(effectiveGroupId, cancellationToken);

                        // Check if already processed
                        if (processedMeetings.Contains(meeting.Id))
                        {
                            _logger.LogInformation("  ⏭️   [SKIP] Already processed — Group: {GroupId} | Candidate: {CandidateId}", effectiveGroupId, candidateId);
                            _logger.LogInformation("       No duplicate report will be generated.");
                            continue;
                        }

                        // Retrieve transcript using the organizer's user ID and joinUrl
                        _logger.LogInformation("  🎙️   [Transcript Fetch Started]");
                        var transcripts = await _graphService.GetTranscriptsAsync(organizerUserId, meeting.Id, meeting.JoinUrl, cancellationToken);

                        if (transcripts == null || !transcripts.Any())
                        {
                            _logger.LogWarning("  ✗  [Transcript Fetch Failed] No transcript content found. Skipping LLM invocation. meetingId={MeetingId}", meeting.Id);
                            continue;
                        }

                        _logger.LogInformation("  ✓  [Transcript Fetch Completed] Found {Count} transcript files.", transcripts.Count);

                        // Clean transcripts to optimize tokens
                        _logger.LogInformation("  🧹  Cleaning transcripts for token optimization...");
                        foreach (var transcript in transcripts)
                        {
                            transcript.Content = TranscriptCleaner.Clean(transcript.Content);
                        }

                        // Aggregate Transcripts
                        _logger.LogInformation("  🔄  Aggregating transcripts...");
                        var transcriptContext = _transcriptAggregator.Aggregate(meeting.Id, meeting.Subject, transcripts.Count, transcripts);
                        
                        if (string.IsNullOrWhiteSpace(transcriptContext.CombinedTranscript))
                        {
                            _logger.LogWarning("  ⚠️   Transcript became empty after cleaning. Skipping.");
                            continue;
                        }

                        _logger.LogInformation("");
                        _logger.LogInformation("[Transcript Fetch]");
                        _logger.LogInformation("InterviewId:\n{Id}", meeting.Id);
                        _logger.LogInformation("Expected Transcript Files:\n{Expected}", transcriptContext.FetchedTranscriptCount);
                        _logger.LogInformation("Fetched Transcript Files:\n{Fetched}", transcriptContext.TranscriptFiles.Count);
                        _logger.LogInformation("");

                        // Normalize names for output
                        candidateName = string.IsNullOrWhiteSpace(candidateName) ? "UnknownCandidate" : SanitizeName(candidateName);
                        interviewerName = string.IsNullOrWhiteSpace(interviewerName) ? "UnknownInterviewer" : SanitizeName(interviewerName);
                        string finalCandidateId = string.IsNullOrWhiteSpace(candidateId) ? "" : $"_{SanitizeName(candidateId)}";
                        
                        // Save Transcript
                        string transcriptFolder = string.IsNullOrWhiteSpace(_storageOptions.TranscriptsFolder) 
                            ? Path.Combine(Path.GetDirectoryName(_storageOptions.ReportsFolder) ?? string.Empty, "Transcripts")
                            : _storageOptions.TranscriptsFolder;

                        if (!Directory.Exists(transcriptFolder))
                        {
                            Directory.CreateDirectory(transcriptFolder);
                        }

                        string transcriptFileName = $"{effectiveGroupId}{finalCandidateId}_{candidateName}_{interviewerName}.txt";
                        string transcriptFilePath = Path.Combine(transcriptFolder, transcriptFileName);
                        await File.WriteAllTextAsync(transcriptFilePath, transcriptContext.CombinedTranscript, cancellationToken);

                        // Load Master Prompt template
                        if (!File.Exists(_promptOptions.PromptFilePath))
                        {
                            throw new FileNotFoundException($"Master Prompt file not found at: {_promptOptions.PromptFilePath}");
                        }

                        _logger.LogInformation("  📝  Loading master prompt template...");
                        string promptTemplate = await File.ReadAllTextAsync(_promptOptions.PromptFilePath, cancellationToken);

                        // Call LLM using ChunkedLlmProcessor
                        string auditContent = await _chunkedLlmProcessor.ProcessAsync(transcriptContext, promptTemplate, jd, cancellationToken);

                        if (string.IsNullOrWhiteSpace(auditContent))
                        {
                            _logger.LogWarning("  ✗  LLM returned empty content. Skipping report generation.");
                            continue;
                        }

                        // Append transcript info to report
                        auditContent += $"\n\n---\n**Source Transcript:** `{transcriptFileName}`";

                        // Create Audit Report
                        string reportFileName = $"{effectiveGroupId}{finalCandidateId}_{candidateName}_{interviewerName}.md";
                        var report = new AuditReport
                        {
                            GroupId = effectiveGroupId,
                            MeetingId = meeting.Id,
                            FileName = reportFileName,
                            Content = auditContent
                        };

                        // Save Report
                        _logger.LogInformation("  💾  Saving audit report: {FileName}", reportFileName);
                        await _reportRepository.SaveReportAsync(report, cancellationToken);

                        // Extract scores from generated audit report
                        int? candidateScore = ExtractCandidateScore(auditContent);
                        int? interviewerScore = ExtractInterviewerScore(auditContent);
                        int? jdAlignmentScore = ExtractJdAlignmentScore(auditContent);

                        var processedMeetingInfo = new ProcessedMeetingInfo
                        {
                            MeetingId = meeting.Id,
                            CandidateId = candidateId,
                            CandidateName = candidateName ?? "",
                            InterviewerName = interviewerName ?? "",
                            StartDateTime = meeting.StartDateTime?.ToString("dddd, dd MMMM yyyy h:mm tt") ?? "",
                            EndDateTime = meeting.EndDateTime?.ToString("dddd, dd MMMM yyyy h:mm tt") ?? "",
                            CandidateScore = candidateScore,
                            InterviewerScore = interviewerScore,
                            JdAlignmentScore = jdAlignmentScore
                        };

                        // Save state as processed with metadata
                        await _stateRepository.AddProcessedMeetingIdAsync(effectiveGroupId, processedMeetingInfo, cancellationToken);

                        _logger.LogInformation("  ✅  [SUCCESS] Meeting fully audited and report saved.");
                        _logger.LogInformation("       Report  : {FileName}", reportFileName);
                        _logger.LogInformation("       Group   : {GroupId} | Candidate: {CandidateId}", effectiveGroupId, candidateId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "  ✗  [ERROR] Unhandled exception while processing meeting {MeetingId}", meeting.Id);
                    }
                }
                return shouldContinueFetching;
            }, cancellationToken);

            // Log Pagination Stats
            _logger.LogInformation("");
            _logger.LogInformation("📊  Retrieval Statistics");
            _logger.LogInformation("    Total Events Retrieved : {TotalEvents}", retrievalResult.TotalEventsRetrieved);
            _logger.LogInformation("    Total Pages Processed  : {TotalPages}", retrievalResult.TotalPagesProcessed);
            _logger.LogInformation("    Processing Duration    : {Duration}", retrievalResult.ProcessingDuration);
            _logger.LogInformation("    Retrieval Status       : {Status}", retrievalResult.IsSuccess ? "Success" : "Failed");
            
            if (!retrievalResult.IsSuccess)
            {
                _logger.LogError("    Error Message          : {Error}", retrievalResult.ErrorMessage);
            }
        }

        private string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Unknown";
            }

            // Replace spaces and invalid filename characters with underscores
            string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            string invalidRegStr = string.Format(@"([{0}]|\s)+", invalidChars);
            string sanitized = Regex.Replace(name, invalidRegStr, "_");
            return sanitized.Trim('_');
        }

        private int? ExtractCandidateScore(string report)
        {
            if (string.IsNullOrEmpty(report)) return null;
            var match = Regex.Match(report, @"Overall Candidate Score:\s*(\d+)", RegexOptions.IgnoreCase);
            return match.Success && int.TryParse(match.Groups[1].Value, out int score) ? score : null;
        }

        private int? ExtractInterviewerScore(string report)
        {
            if (string.IsNullOrEmpty(report)) return null;
            var match = Regex.Match(report, @"Overall Interviewer Score:\s*(\d+)", RegexOptions.IgnoreCase);
            return match.Success && int.TryParse(match.Groups[1].Value, out int score) ? score : null;
        }

        private int? ExtractJdAlignmentScore(string report)
        {
            if (string.IsNullOrEmpty(report)) return null;
            
            // Try matching "JD Alignment Score: X%" or "JD Alignment Score: X/100"
            var match1 = Regex.Match(report, @"JD Alignment Score[^:]*:\s*(\d+)", RegexOptions.IgnoreCase);
            if (match1.Success && int.TryParse(match1.Groups[1].Value, out int score1)) return score1;
            
            // Try matching "JD Alignment | X" or "JD Alignment | X/100"
            var match2 = Regex.Match(report, @"JD Alignment\s*\|\s*(\d+)", RegexOptions.IgnoreCase);
            if (match2.Success && int.TryParse(match2.Groups[1].Value, out int score2)) return score2;

            return null;
        }
    }
}









