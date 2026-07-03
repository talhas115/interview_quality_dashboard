using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Azure.Identity;
using Azure.Core;
using InterviewAudit.Domain.Interfaces;
using InterviewAudit.Domain.Models;

namespace InterviewAudit.Infrastructure.Graph
{
    public class GraphSettings
    {
        public bool UseMock { get; set; } = true;
        public string TenantId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public string EventsEndpoint { get; set; } = string.Empty;
        public string OnlineMeetingsEndpoint { get; set; } = string.Empty;
        public string TranscriptsEndpoint { get; set; } = string.Empty;
        public string TokenScope { get; set; } = string.Empty;
        public string OrganizerUserId { get; set; } = string.Empty;
        public string OrganizerEmail { get; set; } = string.Empty;
    }

    public class MicrosoftGraphService : IGraphService
    {
        private readonly GraphSettings _settings;
        private readonly ILogger<MicrosoftGraphService> _logger;
        private static readonly HttpClient _httpClient = new HttpClient();
        private ClientSecretCredential? _credential;

        public MicrosoftGraphService(IOptions<GraphSettings> settings, ILogger<MicrosoftGraphService> logger)
        {
            _settings = settings.Value;
            _logger = logger;
        }

        private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            if (_credential == null)
            {
                if (string.IsNullOrEmpty(_settings.TenantId) || 
                    string.IsNullOrEmpty(_settings.ClientId) || 
                    string.IsNullOrEmpty(_settings.ClientSecret))
                {
                    throw new InvalidOperationException("Microsoft Graph configuration is missing client credentials.");
                }
                _credential = new ClientSecretCredential(_settings.TenantId, _settings.ClientId, _settings.ClientSecret);
            }
            string scope = string.IsNullOrEmpty(_settings.TokenScope) 
                ? "https://graph.microsoft.com/.default" 
                : _settings.TokenScope;

            var tokenRequestContext = new TokenRequestContext(new[] { scope });
            var tokenResult = await _credential.GetTokenAsync(tokenRequestContext, cancellationToken);
            return tokenResult.Token;
        }

        private async Task<string> SendGraphRequestAsync(string endpoint, CancellationToken cancellationToken)
        {
            string url = endpoint.StartsWith("http") 
                ? endpoint 
                : _settings.BaseUrl.TrimEnd('/') + "/" + endpoint.TrimStart('/');

            int maxRetries = 5;
            int delaySeconds = 2;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string token = await GetAccessTokenAsync(cancellationToken);
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                // ConsistencyLevel header is required for some $count=true queries
                request.Headers.Add("ConsistencyLevel", "eventual");

                _logger.LogInformation("  ➤  [GET] {Url} (Attempt {Attempt}/{MaxRetries})", url, attempt, maxRetries);

                try
                {
                    var response = await _httpClient.SendAsync(request, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        if (attempt == 1)
                            _logger.LogInformation("  ✓  [GET] {Url} → HTTP {StatusCode} {ReasonPhrase}", url, (int)response.StatusCode, response.ReasonPhrase);
                        else
                            _logger.LogInformation("  ✓  [GET] {Url} → HTTP {StatusCode} (Succeeded on attempt {Attempt})", url, (int)response.StatusCode, attempt);
                            
                        return await response.Content.ReadAsStringAsync(cancellationToken);
                    }

                    string error = await response.Content.ReadAsStringAsync(cancellationToken);
                    
                    if ((int)response.StatusCode == 429) // Too Many Requests
                    {
                        var retryAfterHeader = response.Headers.RetryAfter;
                        var delay = retryAfterHeader?.Delta ?? TimeSpan.FromSeconds(delaySeconds);
                        _logger.LogWarning("  ⚠  [429] Rate limited. Waiting {Delay}s before retry...", delay.TotalSeconds);
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    if ((int)response.StatusCode >= 500 && (int)response.StatusCode <= 599)
                    {
                        _logger.LogWarning("  ⚠  [5xx] Server error HTTP {StatusCode}. Waiting {Delay}s...", (int)response.StatusCode, delaySeconds);
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                        delaySeconds *= 2; // Exponential backoff
                        continue;
                    }

                    _logger.LogError("  ✗  [GET] {Url} → HTTP {StatusCode} {ReasonPhrase}", url, (int)response.StatusCode, response.ReasonPhrase);
                    _logger.LogError("      Graph API Response: {Error}", error);
                    response.EnsureSuccessStatusCode();
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning("  ⚠  [Network Error] {Message}. Waiting {Delay}s...", ex.Message, delaySeconds);
                    if (attempt == maxRetries) throw;
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                    delaySeconds *= 2;
                }
            }

            throw new InvalidOperationException($"Failed to retrieve data from Graph API after {maxRetries} attempts. URL: {url}");
        }

        public async Task<GraphEventRetrievalResult> ProcessOrganizerMeetingsAsync(string userId, Func<List<Domain.Models.Meeting>, Task<bool>> pageProcessor, CancellationToken cancellationToken)
        {
            var result = new GraphEventRetrievalResult
            {
                ProcessingDuration = TimeSpan.Zero
            };
            var startTime = DateTime.UtcNow;

            try
            {
                _logger.LogInformation("Fetching calendar events for organizer {UserId}...", userId);
                
                string endpoint = string.Format(_settings.EventsEndpoint, userId);
                if (!endpoint.Contains("$top="))
                {
                    endpoint += (endpoint.Contains("?") ? "&" : "?") + "$top=100";
                }

                string? nextLink = endpoint;

                while (!string.IsNullOrEmpty(nextLink))
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    result.TotalPagesProcessed++;

                    string jsonResponse = await SendGraphRequestAsync(nextLink, cancellationToken);
                    
                    using var doc = JsonDocument.Parse(jsonResponse);
                    if (!doc.RootElement.TryGetProperty("value", out JsonElement valueElement))
                    {
                        _logger.LogWarning("No calendar events found in response for page {PageNumber}", result.TotalPagesProcessed);
                        break;
                    }

                    var domainMeetings = new List<Domain.Models.Meeting>();

                    foreach (var ev in valueElement.EnumerateArray())
                    {
                        string id = ev.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                        if (string.IsNullOrEmpty(id)) continue;

                        string subject = ev.TryGetProperty("subject", out var subProp) && subProp.ValueKind != JsonValueKind.Null ? subProp.GetString() ?? "" : "";
                        
                        string bodyPreview = ev.TryGetProperty("bodyPreview", out var bpProp) && bpProp.ValueKind != JsonValueKind.Null ? bpProp.GetString() ?? "" : "";

                        string bodyContent = "";
                        if (ev.TryGetProperty("body", out var bodyProp) && bodyProp.ValueKind == JsonValueKind.Object && bodyProp.TryGetProperty("content", out var contentProp))
                        {
                            bodyContent = contentProp.GetString() ?? "";
                        }

                        string organizerEmail = "";
                        string organizerName = "";
                        if (ev.TryGetProperty("organizer", out var orgProp) && orgProp.ValueKind == JsonValueKind.Object && orgProp.TryGetProperty("emailAddress", out var orgEmailProp) && orgEmailProp.ValueKind == JsonValueKind.Object)
                        {
                            organizerEmail = orgEmailProp.TryGetProperty("address", out var addrProp) && addrProp.ValueKind != JsonValueKind.Null ? addrProp.GetString() ?? "" : "";
                            organizerName = orgEmailProp.TryGetProperty("name", out var nameProp) && nameProp.ValueKind != JsonValueKind.Null ? nameProp.GetString() ?? "" : "";
                        }

                        var domainAttendees = new List<Domain.Models.Attendee>();
                        if (ev.TryGetProperty("attendees", out var attArray) && attArray.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var att in attArray.EnumerateArray())
                            {
                                if (att.TryGetProperty("emailAddress", out var attEmailProp) && attEmailProp.ValueKind == JsonValueKind.Object)
                                {
                                    string aEmail = attEmailProp.TryGetProperty("address", out var aAddrProp) && aAddrProp.ValueKind != JsonValueKind.Null ? aAddrProp.GetString() ?? "" : "";
                                    string aName = attEmailProp.TryGetProperty("name", out var aNameProp) && aNameProp.ValueKind != JsonValueKind.Null ? aNameProp.GetString() ?? "" : "";
                                    string aType = att.TryGetProperty("type", out var typeProp) && typeProp.ValueKind != JsonValueKind.Null ? typeProp.GetString() ?? "Required" : "Required";
                                    
                                    domainAttendees.Add(new Domain.Models.Attendee
                                    {
                                        Name = aName,
                                        Email = aEmail,
                                        Role = aType
                                    });
                                }
                            }
                        }

                        if (!domainAttendees.Any(a => a.Email.Equals(organizerEmail, StringComparison.OrdinalIgnoreCase)))
                        {
                            domainAttendees.Add(new Domain.Models.Attendee
                            {
                                Name = string.IsNullOrEmpty(organizerName) ? "Organizer" : organizerName,
                                Email = organizerEmail,
                                Role = "Organizer"
                            });
                        }

                        string joinUrl = "";
                        if (ev.TryGetProperty("onlineMeetingUrl", out var joinUrlProp) && joinUrlProp.ValueKind != JsonValueKind.Null)
                        {
                            joinUrl = joinUrlProp.GetString() ?? "";
                        }
                        else if (ev.TryGetProperty("onlineMeeting", out var onlineMeetingProp) && onlineMeetingProp.ValueKind == JsonValueKind.Object && onlineMeetingProp.TryGetProperty("joinUrl", out var omJoinUrlProp) && omJoinUrlProp.ValueKind != JsonValueKind.Null)
                        {
                            joinUrl = omJoinUrlProp.GetString() ?? "";
                        }

                        DateTimeOffset? startDt = null;
                        if (ev.TryGetProperty("start", out var startProp) && startProp.ValueKind == JsonValueKind.Object && startProp.TryGetProperty("dateTime", out var startDtProp) && startDtProp.ValueKind != JsonValueKind.Null)
                        {
                            if (DateTimeOffset.TryParse(startDtProp.GetString(), out var dt)) startDt = dt;
                        }

                        DateTimeOffset? endDt = null;
                        if (ev.TryGetProperty("end", out var endProp) && endProp.ValueKind == JsonValueKind.Object && endProp.TryGetProperty("dateTime", out var endDtProp) && endDtProp.ValueKind != JsonValueKind.Null)
                        {
                            if (DateTimeOffset.TryParse(endDtProp.GetString(), out var dt)) endDt = dt;
                        }

                        domainMeetings.Add(new Domain.Models.Meeting
                        {
                            Id = id,
                            Subject = subject,
                            Description = bodyContent,
                            BodyPreview = bodyPreview,
                            OrganizerName = organizerEmail,
                            Attendees = domainAttendees,
                            JoinUrl = joinUrl,
                            StartDateTime = startDt,
                            EndDateTime = endDt
                        });
                    }

                    result.TotalEventsRetrieved += domainMeetings.Count;
                    _logger.LogInformation("  📊  Page {Page}: Retrieved {PageCount} events. Running Total: {TotalCount}", result.TotalPagesProcessed, domainMeetings.Count, result.TotalEventsRetrieved);

                    if (domainMeetings.Any())
                    {
                        bool continueFetching = await pageProcessor(domainMeetings);
                        if (!continueFetching)
                        {
                            _logger.LogInformation("Page processor requested to stop fetching further pages.");
                            break;
                        }
                    }

                    if (doc.RootElement.TryGetProperty("@odata.nextLink", out JsonElement nextLinkElement) && nextLinkElement.ValueKind != JsonValueKind.Null)
                    {
                        nextLink = nextLinkElement.GetString();
                        _logger.LogInformation("  🔗  Pagination nextLink found. Preparing to fetch next page.");
                    }
                    else
                    {
                        nextLink = null;
                        _logger.LogInformation("  🏁  No further pages. Event retrieval complete.");
                    }
                }

                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to completely retrieve meetings from Microsoft Graph for user {UserId}", userId);
                result.IsSuccess = false;
                result.ErrorMessage = ex.Message;
            }
            finally
            {
                result.ProcessingDuration = DateTime.UtcNow - startTime;
            }

            return result;
        }

        public async Task<List<Domain.Models.Transcript>> GetTranscriptsAsync(string userId, string meetingId, string joinUrl, CancellationToken cancellationToken)
        {
            var resultList = new List<Domain.Models.Transcript>();
            try
            {
                if (string.IsNullOrWhiteSpace(joinUrl))
                {
                    _logger.LogWarning("  ⚠  No JoinUrl provided for meeting {MeetingId}. Cannot retrieve transcript.", meetingId);
                    return null;
                }

                // Step 1: Resolve Calendar Event → OnlineMeeting via JoinWebUrl filter
                string filterEndpoint = $"/users/{userId}/onlineMeetings?$filter=JoinWebUrl eq '{Uri.EscapeDataString(joinUrl)}'";
                string filterUrl = _settings.BaseUrl.TrimEnd('/') + "/" + filterEndpoint.TrimStart('/');
                _logger.LogInformation("  ➤  [GET] Resolve onlineMeeting via JoinWebUrl filter");
                _logger.LogInformation("      URL : {Url}", filterUrl);

                string meetingResponse = await SendGraphRequestAsync(filterEndpoint, cancellationToken);
                
                using var meetingDoc = JsonDocument.Parse(meetingResponse);
                if (!meetingDoc.RootElement.TryGetProperty("value", out JsonElement meetValueElement) || meetValueElement.GetArrayLength() == 0)
                {
                    _logger.LogWarning("  ✗  No onlineMeeting matched the JoinWebUrl for meeting {MeetingId} (User {UserId})", meetingId, userId);
                    return null;
                }

                var onlineMeeting = meetValueElement.EnumerateArray().First();
                string onlineMeetingId = onlineMeeting.TryGetProperty("id", out var omIdProp) ? omIdProp.GetString() ?? "" : "";
                
                if (string.IsNullOrEmpty(onlineMeetingId))
                {
                    _logger.LogWarning("  ✗  Failed to extract onlineMeetingId for meeting {MeetingId}", meetingId);
                    return null;
                }

                _logger.LogInformation("[INFO] Meeting resolved: onlineMeetingId={MeetingId}", onlineMeetingId);

                // Step 2: Fetch transcript list for the resolved onlineMeeting
                string transcriptsEndpoint = string.Format(_settings.TranscriptsEndpoint, userId, onlineMeetingId);
                string transcriptsUrl = _settings.BaseUrl.TrimEnd('/') + "/" + transcriptsEndpoint.TrimStart('/');
                _logger.LogInformation("  ➤  [GET] Fetch transcripts for onlineMeeting {OnlineMeetingId}", onlineMeetingId);
                _logger.LogInformation("      URL : {Url}", transcriptsUrl);
                
                string jsonResponse = await SendGraphRequestAsync(transcriptsEndpoint, cancellationToken);
                
                using var doc = JsonDocument.Parse(jsonResponse);
                if (!doc.RootElement.TryGetProperty("value", out JsonElement valueElement))
                {
                    _logger.LogWarning("  ✗  No transcripts property returned for onlineMeeting {OnlineMeetingId} (User {UserId})", onlineMeetingId, userId);
                    return null;
                }

                var transcripts = valueElement.EnumerateArray().ToList();
                if (!transcripts.Any())
                {
                    _logger.LogWarning("[WARN] Transcript not found. Skipping LLM invocation. meetingId={MeetingId}", onlineMeetingId);
                    return resultList;
                }

                string token = await GetAccessTokenAsync(cancellationToken);

                foreach (var transcriptElement in transcripts)
                {
                    string transcriptId = transcriptElement.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(transcriptId)) continue;

                    _logger.LogInformation("[INFO] Transcript found: transcriptId={TranscriptId}", transcriptId);

                    DateTimeOffset? createdDt = null;
                    if (transcriptElement.TryGetProperty("createdDateTime", out var createdDtProp))
                    {
                        if (DateTimeOffset.TryParse(createdDtProp.GetString(), out var dt)) createdDt = dt;
                    }

                    // Step 3: Download transcript content
                    string contentEndpoint = $"{transcriptsEndpoint}/{transcriptId}/content";
                    string contentUrl = _settings.BaseUrl.TrimEnd('/') + "/" + contentEndpoint.TrimStart('/');
                    _logger.LogInformation("  ➤  [GET] Download transcript content (ID: {TranscriptId})", transcriptId);
                    
                    var request = new HttpRequestMessage(HttpMethod.Get, contentUrl);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    
                    var response = await _httpClient.SendAsync(request, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("  ✗  [GET] {Url} → HTTP {StatusCode} {ReasonPhrase}", contentUrl, (int)response.StatusCode, response.ReasonPhrase);
                        continue;
                    }

                    string transcriptText = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogInformation("  ✓  [GET] Content fetched successfully for {TranscriptId}", transcriptId);

                    resultList.Add(new Domain.Models.Transcript
                    {
                        Id = transcriptId,
                        MeetingId = meetingId,
                        FileName = $"transcript_{transcriptId}.vtt",
                        Content = transcriptText,
                        CreatedDateTime = createdDt
                    });
                }

                return resultList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "  ✗  Failed to retrieve transcript for meeting {MeetingId} (User {UserId}) from Microsoft Graph", meetingId, userId);
                return resultList;
            }
        }
    }
}




