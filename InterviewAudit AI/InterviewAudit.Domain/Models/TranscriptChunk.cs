namespace InterviewAudit.Domain.Models
{
    public class TranscriptChunk
    {
        public int ChunkIndex { get; set; }
        public int TotalChunks { get; set; }
        public string SourceTranscriptFile { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public int CharacterCount { get; set; }
        public int EstimatedTokens { get; set; }
    }
}
