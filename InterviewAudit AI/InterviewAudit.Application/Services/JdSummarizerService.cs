using System.Security.Cryptography;
using System.Text;
using InterviewAudit.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace InterviewAudit.Application.Services
{
    public interface IJdSummarizerService
    {
        Task<string> GetOrGenerateJdSummaryAsync(string jd, CancellationToken cancellationToken);
    }

    public class JdSummarizerService : IJdSummarizerService
    {
        private readonly ILlmService _llmService;
        private readonly ILogger<JdSummarizerService> _logger;
        private readonly string _jdStoragePath;

        public JdSummarizerService(ILlmService llmService, Microsoft.Extensions.Options.IOptions<StorageOptions> storageOptions, ILogger<JdSummarizerService> logger)
        {
            _llmService = llmService;
            _logger = logger;
            _jdStoragePath = storageOptions.Value.JdFolder;
            
            if (string.IsNullOrWhiteSpace(_jdStoragePath))
            {
                _jdStoragePath = @"C:\InterviewAudit\Data\JD";
            }
            
            if (!Directory.Exists(_jdStoragePath))
            {
                Directory.CreateDirectory(_jdStoragePath);
            }
        }

        public async Task<string> GetOrGenerateJdSummaryAsync(string jd, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(jd)) return string.Empty;

            string sha256Hash = ComputeSha256Hash(jd);
            string originalJdPath = Path.Combine(_jdStoragePath, $"Original_{sha256Hash}.txt");
            string summaryJdPath = Path.Combine(_jdStoragePath, $"Summary_{sha256Hash}.summary.txt");

            if (File.Exists(summaryJdPath))
            {
                _logger.LogInformation("Found existing JD summary for hash {Hash}.", sha256Hash);
                return await File.ReadAllTextAsync(summaryJdPath, cancellationToken);
            }

            _logger.LogInformation("JD summary not found for hash {Hash}. Generating new summary...", sha256Hash);

            string prompt = @"You are an expert HR and technical recruiter. Please summarize the following Job Description (JD).
Your summary MUST retain and clearly list the following information:
- Job title
- Experience requirements
- Mandatory skills
- Preferred skills
- Responsibilities
- Evaluation criteria

Here is the original JD:
" + jd;

            // Generate summary using 1000 output tokens since it should be concise
            string summary = await _llmService.GenerateTextAsync(prompt, 1000, cancellationToken);

            if (string.IsNullOrWhiteSpace(summary))
            {
                _logger.LogWarning("Failed to generate JD summary. Returning original JD.");
                return jd; // fallback
            }

            // Atomic writes
            await AtomicWriteFileAsync(originalJdPath, jd, cancellationToken);
            await AtomicWriteFileAsync(summaryJdPath, summary, cancellationToken);

            return summary;
        }

        private async Task AtomicWriteFileAsync(string filePath, string content, CancellationToken cancellationToken)
        {
            string tempFilePath = filePath + ".tmp";
            await File.WriteAllTextAsync(tempFilePath, content, cancellationToken);
            if (File.Exists(filePath))
            {
                // In a highly concurrent environment, might want to just skip or overwrite,
                // but File.Move with overwrite = true works in newer .NET, or File.Replace.
                File.Move(tempFilePath, filePath, overwrite: true);
            }
            else
            {
                File.Move(tempFilePath, filePath);
            }
        }

        private string ComputeSha256Hash(string rawData)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}



