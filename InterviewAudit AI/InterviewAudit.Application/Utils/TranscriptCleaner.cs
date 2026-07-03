using System;
using System.Text;
using System.Text.RegularExpressions;

namespace InterviewAudit.Application.Utils
{
    public class TranscriptCleaner
    {
        // Matches WebVTT timestamps: e.g., 00:00:01.000 --> 00:00:04.000
        private static readonly Regex VttTimestampRegex = new Regex(
            @"\d{2}:\d{2}:\d{2}\.\d{3}\s+-->\s+\d{2}:\d{2}:\d{2}\.\d{3}",
            RegexOptions.Compiled);

        // Matches SRT/other timestamps: e.g., 00:00:01,000 --> 00:00:04,000 or simple 00:00:00
        private static readonly Regex SrtTimestampRegex = new Regex(
            @"\d{2}:\d{2}:\d{2}(?:[.,]\d{3})?",
            RegexOptions.Compiled);

        // Matches WebVTT speaker tags like <v Speaker Name>Text</v>
        private static readonly Regex VttSpeakerRegex = new Regex(
            @"<v\s+([^>]+)>(.*?)</v>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Matches generic HTML/XML tags
        private static readonly Regex GenericTagRegex = new Regex(
            @"<[^>]+>",
            RegexOptions.Compiled);

        // Matches multiple spaces
        private static readonly Regex MultipleSpacesRegex = new Regex(
            @"[ \t]+",
            RegexOptions.Compiled);

        // Matches repeated empty lines (2 or more consecutive newlines)
        private static readonly Regex MultipleNewlinesRegex = new Regex(
            @"(\r?\n){2,}",
            RegexOptions.Compiled);

        public static string Clean(string rawTranscript)
        {
            if (string.IsNullOrWhiteSpace(rawTranscript))
            {
                return string.Empty;
            }

            // 1. If it looks like a WebVTT file (contains "WEBVTT" or timestamps), process line-by-line
            string[] lines = rawTranscript.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var sb = new StringBuilder();

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();

                // Skip WebVTT header, metadata or empty lines
                if (string.Equals(line, "WEBVTT", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("NOTE ", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Language:", StringComparison.OrdinalIgnoreCase) ||
                    VttTimestampRegex.IsMatch(line))
                {
                    continue;
                }

                // If it's just a number (like SRT line numbers), skip it
                if (int.TryParse(line, out _))
                {
                    continue;
                }

                // Remove SRT-like timestamps
                if (line.Contains("-->"))
                {
                    continue;
                }

                // Extract speaker and text from WebVTT tags if present
                var match = VttSpeakerRegex.Match(line);
                if (match.Success)
                {
                    string speaker = match.Groups[1].Value.Trim();
                    string text = match.Groups[2].Value.Trim();
                    sb.AppendLine($"{speaker}: {text}");
                    continue;
                }

                // Otherwise, clean generic tags and append
                string cleanedLine = GenericTagRegex.Replace(line, "").Trim();

                // Clean timestamps that might be embedded in text
                cleanedLine = SrtTimestampRegex.Replace(cleanedLine, "").Trim();

                if (!string.IsNullOrEmpty(cleanedLine))
                {
                    sb.AppendLine(cleanedLine);
                }
            }

            string result = sb.ToString();

            // 2. Collapse multiple spaces within lines
            result = MultipleSpacesRegex.Replace(result, " ");

            // 3. Collapse multiple consecutive newlines (2 or more) to a single newline to remove empty lines
            result = MultipleNewlinesRegex.Replace(result, Environment.NewLine);

            return result.Trim();
        }
    }
}
