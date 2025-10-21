using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Logging;
using Sqs_AI_Lambda.Interfaces;
using Sqs_AI_Lambda.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;

namespace Sqs_AI_Lambda.Services
{
    /// <summary>
    /// Service for generating AI-powered tender summaries using AWS Bedrock.
    /// It now dynamically fetches prompts from a dedicated prompt service.
    /// It now uses Anthropic's Claude 3 Sonnet model.
    /// </summary>
    public class BedrockSummaryService : IBedrockSummaryService
    {
        private readonly ILogger<BedrockSummaryService> _logger;
        private readonly AmazonBedrockRuntimeClient _bedrockClient;
        private readonly IPromptService _promptService;

        // Rate limiting and retry configuration
        private static readonly SemaphoreSlim _rateLimitSemaphore = new(3, 3); // Max 3 concurrent requests
        private const int MaxRetryAttempts = 5;
        private const int BaseDelayMs = 1000; // 1 second base delay

        
        public BedrockSummaryService(ILogger<BedrockSummaryService> logger, AmazonBedrockRuntimeClient bedrockClient, IPromptService promptService)
        {
            _logger = logger;
            _bedrockClient = bedrockClient;
            _promptService = promptService;
        }

        /// <summary>
        /// Generates a comprehensive summary for the provided tender message using Amazon Nova Pro
        /// Enhanced with retry logic and rate limiting to handle throttling
        /// </summary>
        public async Task<string> GenerateSummaryAsync(TenderMessageBase tenderMessage)
        {
            var startTime = DateTime.UtcNow;
            var tenderNumber = tenderMessage.TenderNumber ?? "Unknown";
            var sourceType = tenderMessage.GetSourceType();

            _logger.LogInformation("Starting summary generation - TenderNumber: {TenderNumber}, Source: {SourceType}",
                tenderNumber, sourceType);

            // Wait for rate limit semaphore to control concurrent requests
            await _rateLimitSemaphore.WaitAsync();

            try
            {
                // Fetch the dynamic, combined prompt for the specific source
                var combinedPrompt = await _promptService.GetPromptAsync(sourceType);

                // Convert to compact JSON format to minimize tokens
                var tenderJson = ConvertTenderToCompactJson(tenderMessage);

                _logger.LogDebug("Tender JSON created - TenderNumber: {TenderNumber}, JsonLength: {JsonLength}",
                    tenderNumber, tenderJson.Length);

                // Pass the combined prompt to the execution method
                var summary = await ExecuteWithRetryAsync(combinedPrompt, tenderJson, tenderNumber);

                var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;

                _logger.LogInformation("Summary completed successfully - TenderNumber: {TenderNumber}, Duration: {Duration}ms",
                    tenderNumber, duration);

                return summary;
            }
            catch (Exception ex)
            {
                var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogError(ex, "Summary generation failed after all retries - TenderNumber: {TenderNumber}, Duration: {Duration}ms, ErrorType: {ErrorType}",
                    tenderNumber, duration, ex.GetType().Name);

                return GenerateFallbackSummary(tenderMessage);
            }
            finally
            {
                // Always release the semaphore to allow other requests
                _rateLimitSemaphore.Release();
            }
        }

        /// <summary>
        /// Executes Bedrock request with retry logic, now using a dynamic prompt.
        /// </summary>
        private async Task<string> ExecuteWithRetryAsync(string combinedPrompt, string tenderJson, string tenderNumber)
        {
            var attempt = 0;

            while (attempt < MaxRetryAttempts)
            {
                attempt++;

                try
                {
                    _logger.LogDebug("Bedrock request attempt {Attempt}/{MaxAttempts} - TenderNumber: {TenderNumber}",
                        attempt, MaxRetryAttempts, tenderNumber);

                    // Payload structured for Anthropic Claude 3 models
                    var payload = new
                    {
                        anthropic_version = "bedrock-2023-05-31", // Required for Claude 3
                        max_tokens = 900,
                        temperature = 0.3,
                        messages = new[]
                        {
                            new
                            {
                                role = "user",
                                content = new[]
                                {
                                    new
                                    {
                                        type = "text",
                                        text = $"{combinedPrompt}\n\nTender Data:\n{tenderJson}"
                                    }
                                }
                            }
                        }
                    };

                    var request = new InvokeModelRequest
                    {
                        ModelId = "anthropic.claude-3-sonnet-20240229-v1:0", // Model ID changed to Claude 3 Sonnet
                        ContentType = "application/json",
                        Accept = "application/json",
                        Body = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)))
                    };

                    var response = await _bedrockClient.InvokeModelAsync(request);

                    using var responseStream = new StreamReader(response.Body);
                    var responseText = await responseStream.ReadToEndAsync();

                    // Call the new response parser for Claude
                    var summary = ParseClaudeResponse(responseText);

                    _logger.LogDebug("Bedrock request successful on attempt {Attempt} - TenderNumber: {TenderNumber}",
                        attempt, tenderNumber);

                    return summary;
                }
                catch (ThrottlingException)
                {
                    if (attempt == MaxRetryAttempts)
                    {
                        _logger.LogError("Bedrock throttling - Max retries exceeded - TenderNumber: {TenderNumber}, Attempt: {Attempt}",
                            tenderNumber, attempt);
                        throw;
                    }

                    // Calculate exponential back off delay with jitter
                    var delay = CalculateBackoffDelay(attempt);

                    _logger.LogWarning("Bedrock throttling detected - Attempt {Attempt}/{MaxAttempts}, Retrying in {Delay}ms - TenderNumber: {TenderNumber}",
                        attempt, MaxRetryAttempts, delay, tenderNumber);

                    await Task.Delay(delay);
                }
                catch (Amazon.Runtime.Internal.HttpErrorResponseException httpEx) when (httpEx.Message.Contains("Too many requests"))
                {
                    if (attempt == MaxRetryAttempts)
                    {
                        _logger.LogError("HTTP rate limit - Max retries exceeded - TenderNumber: {TenderNumber}, Attempt: {Attempt}",
                            tenderNumber, attempt);
                        throw;
                    }

                    // Handle HTTP-level throttling
                    var delay = CalculateBackoffDelay(attempt);

                    _logger.LogWarning("HTTP rate limit detected - Attempt {Attempt}/{MaxAttempts}, Retrying in {Delay}ms - TenderNumber: {TenderNumber}",
                        attempt, MaxRetryAttempts, delay, tenderNumber);

                    await Task.Delay(delay);
                }
                catch (Exception ex)
                {
                    // For non-throttling exceptions, don't retry
                    _logger.LogError(ex, "Bedrock request failed with non-retryable error - TenderNumber: {TenderNumber}, Attempt: {Attempt}, ErrorType: {ErrorType}",
                        tenderNumber, attempt, ex.GetType().Name);
                    throw;
                }
            }

            throw new InvalidOperationException($"Max retry attempts exceeded for tender {tenderNumber}");
        }

        /// <summary>
        /// Calculates exponential back off delay with jitter to avoid thundering herd problem
        /// </summary>
        private int CalculateBackoffDelay(int attempt)
        {
            // Exponential backoff: 1s, 2s, 4s, 8s, 16s
            var exponentialDelay = BaseDelayMs * Math.Pow(2, attempt - 1);

            // Add jitter (±25% randomization) to prevent thundering herd
            var random = new Random();
            var jitter = random.NextDouble() * 0.5 + 0.75; // 0.75 to 1.25 multiplier

            var finalDelay = (int)(exponentialDelay * jitter);

            // Cap at 30 seconds maximum to prevent excessive delays
            return Math.Min(finalDelay, 30000);
        }

        /// <summary>
        /// Converts tender to compact JSON format with full model support
        /// Only includes non-empty fields to minimize token usage
        /// </summary>
        private string ConvertTenderToCompactJson(TenderMessageBase tender)
        {
            var compactTender = new Dictionary<string, object>();

            // Base class properties - only add if not empty
            if (!string.IsNullOrEmpty(tender.TenderNumber)) compactTender["number"] = tender.TenderNumber;
            if (!string.IsNullOrEmpty(tender.Title)) compactTender["title"] = tender.Title;
            if (!string.IsNullOrEmpty(tender.Description)) compactTender["description"] = tender.Description;
            if (!string.IsNullOrEmpty(tender.Reference)) compactTender["reference"] = tender.Reference;
            if (!string.IsNullOrEmpty(tender.Audience)) compactTender["audience"] = tender.Audience;
            if (!string.IsNullOrEmpty(tender.OfficeLocation)) compactTender["office"] = tender.OfficeLocation;
            if (!string.IsNullOrEmpty(tender.Address)) compactTender["address"] = tender.Address;
            if (!string.IsNullOrEmpty(tender.Province)) compactTender["province"] = tender.Province;
            if (!string.IsNullOrEmpty(tender.Email)) compactTender["email"] = tender.Email;

            // Add source type
            compactTender["source"] = tender.GetSourceType();

            // Handle supporting documents from the correct property based on type
            var supportingDocs = GetSupportingDocs(tender);
            if (supportingDocs?.Count > 0)
            {
                compactTender["docs"] = supportingDocs.Select(d => new { name = d.Name, url = d.Url }).ToArray();
            }

            // Add model-specific properties
            switch (tender)
            {
                case ETenderMessage eTender:
                    AddETenderSpecificFields(compactTender, eTender);
                    break;

                case TransnetTenderMessage transnetTender:
                    AddTransnetSpecificFields(compactTender, transnetTender);
                    break;

                case EskomTenderMessage eskomTender:
                    AddEskomSpecificFields(compactTender, eskomTender);
                    break;
                case SarsTenderMessage sarsTender:
                    AddSarsSpecificFields(compactTender, sarsTender);
                    break;
                case SanralTenderMessage sanralTender:
                    AddSanralSpecificFields(compactTender, sanralTender);
                    break;
            }

            return JsonSerializer.Serialize(compactTender, new JsonSerializerOptions { WriteIndented = false });
        }

        /// <summary>
        /// Gets the correct supporting documents list based on tender type
        /// </summary>
        private List<SupportingDocument> GetSupportingDocs(TenderMessageBase tender)
        {
            return tender switch
            {
                ETenderMessage eTender => eTender.SupportingDocs,
                TransnetTenderMessage transnetTender => transnetTender.SupportingDocs,
                _ => tender.SupportingDocs
            };
        }

        /// <summary>
        /// Adds eTender-specific fields to compact tender object
        /// </summary>
        private void AddETenderSpecificFields(Dictionary<string, object> compactTender, ETenderMessage eTender)
        {
            if (eTender.Id > 0) compactTender["id"] = eTender.Id;
            if (!string.IsNullOrEmpty(eTender.Status)) compactTender["status"] = eTender.Status;
            if (!string.IsNullOrEmpty(eTender.Url)) compactTender["url"] = eTender.Url;

            if (eTender.DatePublished != default)
                compactTender["published"] = eTender.DatePublished.ToString("yyyy-MM-dd HH:mm");

            if (eTender.DateClosing != default)
                compactTender["closing"] = eTender.DateClosing.ToString("yyyy-MM-dd HH:mm");
        }

        /// <summary>
        /// Adds Transnet-specific fields to compact tender object
        /// </summary>
        private void AddTransnetSpecificFields(Dictionary<string, object> compactTender, TransnetTenderMessage transnetTender)
        {
            if (!string.IsNullOrEmpty(transnetTender.Institution)) compactTender["institution"] = transnetTender.Institution;
            if (!string.IsNullOrEmpty(transnetTender.Category)) compactTender["category"] = transnetTender.Category;
            if (!string.IsNullOrEmpty(transnetTender.TenderType)) compactTender["type"] = transnetTender.TenderType;
            if (!string.IsNullOrEmpty(transnetTender.Location)) compactTender["location"] = transnetTender.Location;
            if (!string.IsNullOrEmpty(transnetTender.ContactPerson)) compactTender["contact"] = transnetTender.ContactPerson;
            if (!string.IsNullOrEmpty(transnetTender.Source)) compactTender["sourceDetail"] = transnetTender.Source;

            if (transnetTender.PublishedDate.HasValue)
                compactTender["published"] = transnetTender.PublishedDate.Value.ToString("yyyy-MM-dd HH:mm");

            if (transnetTender.ClosingDate.HasValue)
                compactTender["closing"] = transnetTender.ClosingDate.Value.ToString("yyyy-MM-dd HH:mm");
        }

        /// <summary>
        /// Adds Eskom-specific fields to compact tender object
        /// </summary>
        private void AddEskomSpecificFields(Dictionary<string, object> compactTender, EskomTenderMessage eskomTender)
        {
            if (!string.IsNullOrEmpty(eskomTender.Source)) compactTender["sourceDetail"] = eskomTender.Source;

            if (eskomTender.PublishedDate.HasValue)
                compactTender["published"] = eskomTender.PublishedDate.Value.ToString("yyyy-MM-dd HH:mm");

            if (eskomTender.ClosingDate.HasValue)
                compactTender["closing"] = eskomTender.ClosingDate.Value.ToString("yyyy-MM-dd HH:mm");
        }

        /// <summary>
        /// Adds SARS-specific fields to the compact tender object for Bedrock processing.
        /// </summary>
        private void AddSarsSpecificFields(Dictionary<string, object> compactTender, SarsTenderMessage sarsTender)
        {
            if (!string.IsNullOrEmpty(sarsTender.BriefingSession)) compactTender["briefingSession"] = sarsTender.BriefingSession;
            if (sarsTender.PublishedDate.HasValue) compactTender["published"] = sarsTender.PublishedDate.Value.ToString("yyyy-MM-dd HH:mm");
            if (sarsTender.ClosingDate.HasValue) compactTender["closing"] = sarsTender.ClosingDate.Value.ToString("yyyy-MM-dd HH:mm");
        }

        /// <summary>
        /// Adds SANRAL-specific fields to the compact tender object for Bedrock processing.
        /// </summary>
        private void AddSanralSpecificFields(Dictionary<string, object> compactTender, SanralTenderMessage sanralTender)
        {
            if (!string.IsNullOrEmpty(sanralTender.Category)) compactTender["category"] = sanralTender.Category;
            if (!string.IsNullOrEmpty(sanralTender.Region)) compactTender["region"] = sanralTender.Region;
            if (!string.IsNullOrEmpty(sanralTender.FullNoticeText)) compactTender["fullNoticeText"] = sanralTender.FullNoticeText;
            if (sanralTender.PublishedDate.HasValue) compactTender["published"] = sanralTender.PublishedDate.Value.ToString("yyyy-MM-dd HH:mm");
            if (sanralTender.ClosingDate.HasValue) compactTender["closing"] = sanralTender.ClosingDate.Value.ToString("yyyy-MM-dd HH:mm");
        }

        /// <summary>
        /// Parses Amazon Nova's response to extract the summary content
        /// </summary>
        private string ParseNovaResponse(string responseJson)
        {
            try
            {
                using var document = JsonDocument.Parse(responseJson);

                if (document.RootElement.TryGetProperty("output", out var outputProperty) &&
                    outputProperty.TryGetProperty("message", out var messageProperty) &&
                    messageProperty.TryGetProperty("content", out var contentArray) &&
                    contentArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var contentItem in contentArray.EnumerateArray())
                    {
                        if (contentItem.TryGetProperty("text", out var textProperty))
                        {
                            return textProperty.GetString() ?? "Summary generated but content extraction failed.";
                        }
                    }
                }

                _logger.LogWarning("Unexpected Nova Pro response format - TenderNumber: Context not available");
                return "Summary generated but response parsing failed.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse Nova Pro response");
                return "Summary generation completed but response parsing failed.";
            }
        }


        /// <summary>
        /// Parses the JSON response from an Anthropic Claude 3 model to extract the summary content.
        /// </summary>
        private string ParseClaudeResponse(string responseJson)
        {
            try
            {
                using var document = JsonDocument.Parse(responseJson);
                var contentArray = document.RootElement.GetProperty("content");

                if (contentArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var contentItem in contentArray.EnumerateArray())
                    {
                        if (contentItem.TryGetProperty("type", out var typeProperty) && typeProperty.GetString() == "text" &&
                            contentItem.TryGetProperty("text", out var textProperty))
                        {
                            return textProperty.GetString() ?? "Summary generated but content extraction failed.";
                        }
                    }
                }
                _logger.LogWarning("Unexpected Claude 3 response format: 'text' field not found in content array.");
                return "Summary generated but response parsing failed (unexpected format).";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse Claude 3 response JSON: {ResponseJson}", responseJson);
                return "Summary generation completed but response parsing failed (exception).";
            }
        }

        /// <summary>
        /// Generates a model-aware fallback summary when Bedrock fails
        /// Enhanced with throttling context information
        /// </summary>
        private string GenerateFallbackSummary(TenderMessageBase tender)
        {
            var summary = new StringBuilder();

            summary.AppendLine("**AUTOMATED SUMMARY (Fallback)**");
            summary.AppendLine($"**Tender:** {tender.Title}");
            summary.AppendLine($"**Number:** {tender.TenderNumber}");
            summary.AppendLine($"**Source:** {tender.GetSourceType()}");

            if (!string.IsNullOrEmpty(tender.Description))
                summary.AppendLine($"**Purpose:** {tender.Description}");

            // Add model-specific fallback information
            switch (tender)
            {
                case ETenderMessage eTender:
                    if (!string.IsNullOrEmpty(eTender.Status))
                        summary.AppendLine($"**Status:** {eTender.Status}");
                    if (eTender.DateClosing != default)
                        summary.AppendLine($"**Closing:** {eTender.DateClosing:yyyy-MM-dd HH:mm}");
                    if (!string.IsNullOrEmpty(eTender.Url))
                        summary.AppendLine($"**URL:** {eTender.Url}");
                    break;

                case TransnetTenderMessage transnetTender:
                    if (!string.IsNullOrEmpty(transnetTender.Institution))
                        summary.AppendLine($"**Institution:** {transnetTender.Institution}");
                    if (!string.IsNullOrEmpty(transnetTender.Category))
                        summary.AppendLine($"**Category:** {transnetTender.Category}");
                    if (!string.IsNullOrEmpty(transnetTender.Location))
                        summary.AppendLine($"**Location:** {transnetTender.Location}");
                    if (!string.IsNullOrEmpty(transnetTender.ContactPerson))
                        summary.AppendLine($"**Contact:** {transnetTender.ContactPerson}");
                    if (transnetTender.ClosingDate.HasValue)
                        summary.AppendLine($"**Closing:** {transnetTender.ClosingDate.Value:yyyy-MM-dd HH:mm}");
                    break;

                case EskomTenderMessage eskomTender:
                    if (!string.IsNullOrEmpty(eskomTender.Source))
                        summary.AppendLine($"**Source Detail:** {eskomTender.Source}");
                    if (eskomTender.ClosingDate.HasValue)
                        summary.AppendLine($"**Closing:** {eskomTender.ClosingDate.Value:yyyy-MM-dd HH:mm}");
                    break;

                case SarsTenderMessage sarsTender:
                    if (sarsTender.ClosingDate.HasValue) summary.AppendLine($"**Closing:** {sarsTender.ClosingDate.Value:yyyy-MM-dd HH:mm}");
                    if (!string.IsNullOrEmpty(sarsTender.BriefingSession)) summary.AppendLine($"**Briefing Session:** {sarsTender.BriefingSession}");
                    break;

                case SanralTenderMessage sanralTender:
                    if (sanralTender.ClosingDate.HasValue) summary.AppendLine($"**Closing:** {sanralTender.ClosingDate.Value:yyyy-MM-dd HH:mm}");
                    if (!string.IsNullOrEmpty(sanralTender.Category)) summary.AppendLine($"**Category:** {sanralTender.Category}");
                    if (!string.IsNullOrEmpty(sanralTender.Region)) summary.AppendLine($"**Region:** {sanralTender.Region}");
                    break;
            }

            if (!string.IsNullOrEmpty(tender.Email))
                summary.AppendLine($"**Email:** {tender.Email}");

            if (!string.IsNullOrEmpty(tender.OfficeLocation) || !string.IsNullOrEmpty(tender.Province))
                summary.AppendLine($"**Location:** {tender.OfficeLocation} {tender.Province}".Trim());

            var supportingDocs = GetSupportingDocs(tender);
            if (supportingDocs?.Count > 0)
                summary.AppendLine($"**Documents:** {supportingDocs.Count} available");

            summary.AppendLine("*AI summary unavailable due to service limitations - manual review required*");

            _logger.LogInformation("Generated enhanced fallback summary - TenderNumber: {TenderNumber}, Type: {Type}",
                tender.TenderNumber, tender.GetType().Name);

            return summary.ToString();
        }
    }
}