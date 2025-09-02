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

namespace Sqs_AI_Lambda.Services
{
    /// <summary>
    /// Service for generating AI-powered tender summaries using AWS Bedrock
    /// </summary>
    public class BedrockSummaryService : IBedrockSummaryService
    {
        private readonly ILogger<BedrockSummaryService> _logger;
        private readonly AmazonBedrockRuntimeClient _bedrockClient;

        private const string SystemPrompt = @"You are a professional tender analyst. 
            Analyse the tender data and create a structured summary with these sections:

            **PURPOSE:** What the tender is for and scope
            **ELIGIBILITY:** Who can apply and requirements  
            **APPLICATION:** How to apply, deadlines, procedures
            **LOCATION/CONTACT:** Location details and contact info
            **DOCUMENTS:** Required/supporting documents
            **DETAILS:** Other important information

            Guidelines:
            - Use only provided information
            - State Information not provided for missing info
            - Be concise but comprehensive
            - Use bullet points for clarity";

        public BedrockSummaryService(ILogger<BedrockSummaryService> logger, AmazonBedrockRuntimeClient bedrockClient)
        {
            _logger = logger;
            _bedrockClient = bedrockClient;
        }

        /// <summary>
        /// Generates a comprehensive summary for the provided tender message using Amazon Nova Pro
        /// Optimized for minimal token usage with full model support
        /// </summary>
        public async Task<string> GenerateSummaryAsync(TenderMessageBase tenderMessage)
        {
            var startTime = DateTime.UtcNow;
            var tenderNumber = tenderMessage.TenderNumber ?? "Unknown";
            var sourceType = tenderMessage.GetSourceType();

            _logger.LogInformation("Starting Nova Pro summary - TenderNumber: {TenderNumber}, Source: {SourceType}",
                tenderNumber, sourceType);

            try
            {
                // Convert to compact JSON format to minimize tokens
                var tenderJson = ConvertTenderToCompactJson(tenderMessage);

                _logger.LogDebug("Tender JSON created - TenderNumber: {TenderNumber}, JsonLength: {JsonLength}",
                    tenderNumber, tenderJson.Length);

                // Optimised payload for token efficiency
                var payload = new
                {
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = new[]
                            {
                                new
                                {
                                    text = $"{SystemPrompt}\n\nTender: {tenderJson}"
                                }
                            }
                        }
                    },
                    inferenceConfig = new
                    {
                        max_new_tokens = 800,    // Efficient token usage
                        temperature = 0.1,       // Low for consistent output
                        top_p = 0.9
                    }
                };

                var request = new InvokeModelRequest
                {
                    ModelId = "amazon.nova-pro-v1:0",
                    ContentType = "application/json",
                    Accept = "application/json",
                    Body = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)))
                };

                _logger.LogDebug("Sending to Nova Pro - TenderNumber: {TenderNumber}", tenderNumber);

                var response = await _bedrockClient.InvokeModelAsync(request);

                using var responseStream = new StreamReader(response.Body);
                var responseText = await responseStream.ReadToEndAsync();

                var summary = ParseNovaResponse(responseText);

                var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogInformation("Nova Pro summary completed - TenderNumber: {TenderNumber}, Duration: {Duration}ms, Length: {Length}",
                    tenderNumber, duration, summary.Length);

                return summary;
            }
            catch (Exception ex)
            {
                var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogError(ex, "Nova Pro summary failed - TenderNumber: {TenderNumber}, Duration: {Duration}ms",
                    tenderNumber, duration);

                return GenerateFallbackSummary(tenderMessage);
            }
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
        /// Parses Amazon Nova Pro's response to extract the summary content
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

                _logger.LogWarning("Unexpected Nova Pro response format");
                return "Summary generated but response parsing failed.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse Nova Pro response");
                return "Summary generation completed but response parsing failed.";
            }
        }

        /// <summary>
        /// Generates a model-aware fall back summary when Bedrock fails
        /// </summary>
        private string GenerateFallbackSummary(TenderMessageBase tender)
        {
            var summary = new StringBuilder();

            summary.AppendLine("**AUTOMATED SUMMARY**");
            summary.AppendLine($"**Tender:** {tender.Title}");
            summary.AppendLine($"**Number:** {tender.TenderNumber}");
            summary.AppendLine($"**Source:** {tender.GetSourceType()}");

            if (!string.IsNullOrEmpty(tender.Description))
                summary.AppendLine($"**Purpose:** {tender.Description}");

            // Add model-specific fall back information
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
            }

            if (!string.IsNullOrEmpty(tender.Email))
                summary.AppendLine($"**Email:** {tender.Email}");

            if (!string.IsNullOrEmpty(tender.OfficeLocation) || !string.IsNullOrEmpty(tender.Province))
                summary.AppendLine($"**Location:** {tender.OfficeLocation} {tender.Province}".Trim());

            var supportingDocs = GetSupportingDocs(tender);
            if (supportingDocs?.Count > 0)
                summary.AppendLine($"**Documents:** {supportingDocs.Count} available");

            summary.AppendLine("*AI summary unavailable - manual review required*");

            _logger.LogInformation("Generated model-aware fallback summary - TenderNumber: {TenderNumber}, Type: {Type}",
                tender.TenderNumber, tender.GetType().Name);

            return summary.ToString();
        }
    }
}
