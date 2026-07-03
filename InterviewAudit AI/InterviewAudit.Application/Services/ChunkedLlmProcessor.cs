using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InterviewAudit.Domain.Interfaces;
using InterviewAudit.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InterviewAudit.Application.Services
{
    public class ChunkedLlmProcessor : IChunkedLlmProcessor
    {
        private readonly ILlmService _llmService;
        private readonly IJdSummarizerService _jdSummarizerService;
        private readonly ILogger<ChunkedLlmProcessor> _logger;
        
        // Token settings
        private const int GroqHardLimit = 12000;
        private const int SafetyBuffer = 500;
        private const int DefaultOutputReservation = 1500;
        private const int MaxInputTokens = GroqHardLimit - DefaultOutputReservation - SafetyBuffer; // 10000
        
        // Chunk sizes
        private const int ChunkTokenSize = 2500; // ~10000 chars
        private const int ChunkCharSize = ChunkTokenSize * 4;

        private readonly StorageOptions _storageOptions;

        public ChunkedLlmProcessor(
            ILlmService llmService, 
            IJdSummarizerService jdSummarizerService, 
            IOptions<StorageOptions> storageOptions,
            ILogger<ChunkedLlmProcessor> logger)
        {
            _llmService = llmService;
            _jdSummarizerService = jdSummarizerService;
            _storageOptions = storageOptions.Value;
            _logger = logger;
        }

        public async Task<string> ProcessAsync(MeetingTranscriptContext context, string promptTemplate, string jd, CancellationToken cancellationToken)
        {
            string combinedTranscript = context.CombinedTranscript;
            if (string.IsNullOrWhiteSpace(combinedTranscript))
            {
                _logger.LogWarning("[WARN] Combined transcript is empty. Cannot process.");
                return string.Empty;
            }

            int transcriptCharCount = combinedTranscript.Length;
            int transcriptTokens = transcriptCharCount / 4;

            _logger.LogInformation("");
            _logger.LogInformation("[LLM Processing Started]");
            _logger.LogInformation("MeetingId: {MeetingId}", context.MeetingId);
            _logger.LogInformation("Transcript Files: {TranscriptCount}", context.FetchedTranscriptCount);
            _logger.LogInformation("Transcript Length: {CharacterCount} chars", transcriptCharCount);
            _logger.LogInformation("Transcript Tokens (Estimated): {TokenCount}", transcriptTokens);
            _logger.LogInformation("Max Available Input Tokens: {MaxInput}", MaxInputTokens);
            _logger.LogInformation("");

            // 1. Get JD Summary
            _logger.LogInformation("Fetching/Generating JD Summary...");
            string jdSummary = await _jdSummarizerService.GetOrGenerateJdSummaryAsync(jd, cancellationToken);
            int jdTokens = jdSummary.Length / 4;
            _logger.LogInformation("JD Summary Tokens: {JdTokens}", jdTokens);

            // Calculate total tokens roughly
            int promptOverheadTokens = promptTemplate.Length / 4;
            int totalEstimatedInputTokens = transcriptTokens + jdTokens + promptOverheadTokens;

            _logger.LogInformation("Total Estimated Input Tokens (Transcript + JD + Prompt): {Total}", totalEstimatedInputTokens);

            // 2. Decide if chunking is needed
            string finalTranscriptContext = combinedTranscript;

            if (totalEstimatedInputTokens > MaxInputTokens)
            {
                _logger.LogWarning("Input tokens ({Total}) exceed available limit ({Max}). Initiating transcript chunking and summarization.", totalEstimatedInputTokens, MaxInputTokens);
                
                var chunks = SplitTranscript(combinedTranscript, ChunkCharSize);
                _logger.LogInformation("Split transcript into {TotalChunks} chunks.", chunks.Count);

                var chunkAnalyses = new List<string>();

                for (int i = 0; i < chunks.Count; i++)
                {
                    _logger.LogInformation("Summarizing chunk {ChunkIndex} of {TotalChunks} (Length: {Length})...", i + 1, chunks.Count, chunks[i].Length);
                    
                    string chunkPrompt = @"You are a meeting assistant analyzing a chunk of a larger interview transcript.
Please generate a concise chunk summary.
You MUST retain:
- Questions asked
- Candidate responses
- Technical topics discussed
- Strengths
- Weaknesses
- Communication observations
- Hiring signals

Do NOT discard any interview evidence unless it is:
- Greetings
- Small talk
- Scheduling discussions
- Repeated statements

Here is the Job Description summary for context:
" + jdSummary + @"

Here is the chunk of the transcript:
" + chunks[i];

                    // Reserve 1500 tokens for chunk summary output
                    string chunkAnalysis = await _llmService.GenerateTextAsync(chunkPrompt, 1500, cancellationToken);
                    chunkAnalyses.Add($"--- SUMMARY FOR CHUNK {i + 1} OF {chunks.Count} ---\n{chunkAnalysis}\n");
                }

                _logger.LogInformation("Analysis pass complete. Consolidating chunks...");
                finalTranscriptContext = string.Join("\n", chunkAnalyses);

                // Check again if the consolidated summaries somehow exceed the limits
                int consolidatedTokens = finalTranscriptContext.Length / 4;
                if (consolidatedTokens + jdTokens + promptOverheadTokens > MaxInputTokens)
                {
                    _logger.LogWarning("Consolidated summaries still exceed limit! Truncating to safe limits.");
                    int safeLength = (MaxInputTokens - jdTokens - promptOverheadTokens) * 4;
                    if (safeLength > 0 && safeLength < finalTranscriptContext.Length)
                    {
                        finalTranscriptContext = finalTranscriptContext.Substring(0, safeLength) + "... [TRUNCATED]";
                    }
                }
                
                // Save the consolidated summary to disk
                string transcriptsFolder = _storageOptions.TranscriptsFolder;
                if (string.IsNullOrWhiteSpace(transcriptsFolder))
                {
                    transcriptsFolder = Path.Combine(Path.GetDirectoryName(_storageOptions.ReportsFolder) ?? string.Empty, "Transcripts");
                }
                string summariesFolder = Path.Combine(Path.GetDirectoryName(transcriptsFolder) ?? string.Empty, "TranscriptSummaries");
                if (!Directory.Exists(summariesFolder)) Directory.CreateDirectory(summariesFolder);
                string summaryFileName = $"TranscriptSummary_{context.MeetingId}.txt";
                await File.WriteAllTextAsync(Path.Combine(summariesFolder, summaryFileName), finalTranscriptContext, cancellationToken);
                _logger.LogInformation("Saved intermediate transcript summary to {Path}", Path.Combine(summariesFolder, summaryFileName));
            }
            else
            {
                _logger.LogInformation("Transcript fits within safe limits. Executing single pass with full transcript.");
            }

            // 3. Final Report Generation
            _logger.LogInformation("Executing final report generation...");
            
            // GenerateReportAsync internally uses GroqLlmService which uses CallGroqApiAsync (which defaults to maxTokens based on the implementation, now 2500 for the report)
            string finalReport = await _llmService.GenerateReportAsync(promptTemplate, jdSummary, finalTranscriptContext, cancellationToken);

            _logger.LogInformation("");
            _logger.LogInformation("[LLM Processing Completed]");
            _logger.LogInformation("MeetingId: {MeetingId}", context.MeetingId);
            _logger.LogInformation("Response Status: Success");
            _logger.LogInformation("");

            return finalReport;
        }

        private List<string> SplitTranscript(string transcript, int maxLength)
        {
            var chunks = new List<string>();
            int currentIndex = 0;

            while (currentIndex < transcript.Length)
            {
                if (transcript.Length - currentIndex <= maxLength)
                {
                    chunks.Add(transcript.Substring(currentIndex));
                    break;
                }

                // Try to find a safe break point (newline or double newline) within the maxLength backwards
                int breakPoint = transcript.LastIndexOf("\n\n", currentIndex + maxLength, maxLength, StringComparison.Ordinal);
                if (breakPoint <= currentIndex)
                {
                    breakPoint = transcript.LastIndexOf('\n', currentIndex + maxLength, maxLength);
                }
                
                if (breakPoint <= currentIndex)
                {
                    // Fallback: hard split if no newlines found
                    breakPoint = currentIndex + maxLength;
                }

                chunks.Add(transcript.Substring(currentIndex, breakPoint - currentIndex).Trim());
                currentIndex = breakPoint;
            }

            return chunks;
        }
    }
}
