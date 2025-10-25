using Microsoft.Extensions.Logging;
using Sqs_AI_Lambda.Interfaces;
using Sqs_AI_Lambda.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sqs_AI_Lambda.Services
{
    /// <summary>
    /// Message processor for handling tender messages from different sources
    /// </summary>
    public class MessageProcessor : IMessageProcessor
    {
        private readonly ILogger<MessageProcessor> _logger;
        private readonly IBedrockSummaryService _bedrockSummaryService;

        public MessageProcessor(ILogger<MessageProcessor> logger, IBedrockSummaryService bedrockSummaryService)
        {
            _logger = logger;
            _bedrockSummaryService = bedrockSummaryService;
        }

        /// <summary>
        /// Main processing method that handles different types of tender messages
        /// </summary>
        public async Task<TenderMessageBase> ProcessMessageAsync(TenderMessageBase message)
        {
            // Get message source type for logging and routing
            var sourceType = message.GetSourceType();
            var messageType = message.GetType().Name;
            var tenderNumber = message.TenderNumber ?? "Unknown";

            _logger.LogInformation("Starting message processing - Source: {SourceType}, Type: {MessageType}, TenderNumber: {TenderNumber}, Title: {Title}",
                sourceType, messageType, tenderNumber, message.Title);

            try
            {
                // Copy existing tags to preserve original data
                var originalTagCount = message.Tags?.Count ?? 0;

                // Add processing tags to track message lifecycle
                var processedTags = new List<string>(message.Tags ?? new List<string>())
                {
                    "Processed",
                    $"ProcessedBy{sourceType}Handler"
                };
                message.Tags = processedTags;

                _logger.LogDebug("Added processing tags - OriginalCount: {OriginalCount}, NewCount: {NewCount}, Source: {SourceType}",
                    originalTagCount, processedTags.Count, sourceType);

                // Route message to appropriate processor based on type
                switch (message)
                {
                    case ETenderMessage eTenderMessage:
                        _logger.LogDebug("Routing to eTender processor - ID: {TenderId}", eTenderMessage.TenderNumber);
                        await ProcessETenderMessage(eTenderMessage);
                        break;

                    case EskomTenderMessage eskomMessage:
                        _logger.LogDebug("Routing to Eskom processor - TenderNumber: {TenderNumber}", eskomMessage.TenderNumber);
                        await ProcessEskomTenderMessage(eskomMessage);
                        break;

                    case TransnetTenderMessage transnetMessage:
                        _logger.LogDebug("Routing to Transnet processor - TenderNumber: {TenderNumber}", transnetMessage.TenderNumber);
                        await ProcessTransnetTenderMessage(transnetMessage);
                        break;

                    case SarsTenderMessage sarsMessage:
                        _logger.LogDebug("Routing to SARS processor - TenderNumber: {TenderNumber}", sarsMessage.TenderNumber);
                        await ProcessSarsTenderMessage(sarsMessage);
                        break;

                    case SanralTenderMessage sanralMessage:
                        _logger.LogDebug("Routing to SANRAL processor - TenderNumber: {TenderNumber}", sanralMessage.TenderNumber);
                        await ProcessSanralTenderMessage(sanralMessage);
                        break;

                    default:
                        // Log unknown message types for monitoring
                        _logger.LogWarning("Unknown message type encountered - Type: {MessageType}, Source: {SourceType}, TenderNumber: {TenderNumber}",
                            messageType, sourceType, tenderNumber);
                        break;
                }

                _logger.LogInformation("Message processing completed successfully - Source: {SourceType}, TenderNumber: {TenderNumber}, FinalTagCount: {TagCount}",
                    sourceType, tenderNumber, message.Tags.Count);

                return message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Message processing failed - Source: {SourceType}, Type: {MessageType}, TenderNumber: {TenderNumber}, Title: {Title}",
                    sourceType, messageType, tenderNumber, message.Title);
                throw;
            }
        }

        /// <summary>
        /// Processes eTender-specific messages
        /// </summary>
        private async Task ProcessETenderMessage(ETenderMessage message)
        {
            var tenderId = message.TenderNumber;
            var status = message.Status ?? "Unknown";
            var docCount = message.SupportingDocs?.Count ?? 0;

            _logger.LogInformation("Processing eTender message - ID: {TenderId}, Status: {Status}, DocumentCount: {DocumentCount}",
                tenderId, status, docCount);

            // Log date information for tracking
            if (message.DatePublished != default)
            {
                _logger.LogDebug("eTender published date - ID: {TenderId}, PublishedDate: {PublishedDate}",
                    tenderId, message.DatePublished);
            }

            if (message.DateClosing != default)
            {
                _logger.LogDebug("eTender closing date - ID: {TenderId}, ClosingDate: {ClosingDate}",
                    tenderId, message.DateClosing);
            }

            // Generate AI summary as part of eTender processing
            await GenerateAndAttachSummary(message, "eTender");

            _logger.LogInformation("eTender processing completed - ID: {TenderId}, Status: {Status}, SummaryGenerated: {HasSummary}",
                tenderId, status, !string.IsNullOrEmpty(message.AISummary));


            await Task.CompletedTask;
        }

        /// <summary>
        /// Processes Eskom-specific messages
        /// </summary>
        private async Task ProcessEskomTenderMessage(EskomTenderMessage message)
        {
            var tenderNumber = message.TenderNumber ?? "Unknown";
            var source = message.Source ?? "Unknown";
            var hasPublishedDate = message.PublishedDate.HasValue;
            var hasClosingDate = message.ClosingDate.HasValue;

            _logger.LogInformation("Processing Eskom tender - Number: {TenderNumber}, Source: {Source}, HasPublishedDate: {HasPublishedDate}, HasClosingDate: {HasClosingDate}",
                tenderNumber, source, hasPublishedDate, hasClosingDate);

            // Log published date if available
            if (message.PublishedDate.HasValue)
            {
                _logger.LogDebug("Eskom tender published date - Number: {TenderNumber}, PublishedDate: {PublishedDate}",
                    tenderNumber, message.PublishedDate.Value);
            }
            else
            {
                _logger.LogDebug("Eskom tender missing published date - Number: {TenderNumber}", tenderNumber);
            }

            // Log closing date if available
            if (message.ClosingDate.HasValue)
            {
                _logger.LogDebug("Eskom tender closing date - Number: {TenderNumber}, ClosingDate: {ClosingDate}",
                    tenderNumber, message.ClosingDate.Value);
            }

            // Generate AI summary as part of Eskom processing
            await GenerateAndAttachSummary(message, "Eskom");

            _logger.LogInformation("Eskom processing completed - Number: {TenderNumber}, Source: {Source}, SummaryGenerated: {HasSummary}",
                tenderNumber, source, !string.IsNullOrEmpty(message.AISummary));


            await Task.CompletedTask;
        }

        /// <summary>
        /// Processes Transnet-specific messages
        /// </summary>
        private async Task ProcessTransnetTenderMessage(TransnetTenderMessage message)
        {
            var tenderNumber = message.TenderNumber ?? "Unknown";
            var source = message.Source ?? "Unknown";
            var institution = message.Institution ?? "Unknown";
            var category = message.Category ?? "Unknown";
            var location = message.Location ?? "Unknown";
            var hasPublishedDate = message.PublishedDate.HasValue;
            var hasClosingDate = message.ClosingDate.HasValue;

            _logger.LogInformation("Processing Transnet tender - Number: {TenderNumber}, Institution: {Institution}, Category: {Category}, Location: {Location}, HasPublishedDate: {HasPublishedDate}, HasClosingDate: {HasClosingDate}",
                tenderNumber, institution, category, location, hasPublishedDate, hasClosingDate);

            // Log published date if available
            if (message.PublishedDate.HasValue)
            {
                _logger.LogDebug("Transnet tender published date - Number: {TenderNumber}, PublishedDate: {PublishedDate}",
                    tenderNumber, message.PublishedDate.Value);
            }
            else
            {
                _logger.LogDebug("Transnet tender missing published date - Number: {TenderNumber}", tenderNumber);
            }

            // Log closing date if available
            if (message.ClosingDate.HasValue)
            {
                _logger.LogDebug("Transnet tender closing date - Number: {TenderNumber}, ClosingDate: {ClosingDate}",
                    tenderNumber, message.ClosingDate.Value);
            }

            // Log contact person if available
            if (!string.IsNullOrEmpty(message.ContactPerson))
            {
                _logger.LogDebug("Transnet tender contact person - Number: {TenderNumber}, ContactPerson: {ContactPerson}",
                    tenderNumber, message.ContactPerson);
            }

            // Generate AI summary as part of Transnet processing
            await GenerateAndAttachSummary(message, "Transnet");

            _logger.LogInformation("Transnet processing completed - Number: {TenderNumber}, Institution: {Institution}, Source: {Source}, SummaryGenerated: {HasSummary}",
                tenderNumber, institution, source, !string.IsNullOrEmpty(message.AISummary));


            await Task.CompletedTask;
        }

        /// <summary>
        /// Processes SARS-specific messages.
        /// </summary>
        private async Task ProcessSarsTenderMessage(SarsTenderMessage message)
        {
            var tenderNumber = message.TenderNumber ?? "Unknown";
            var hasBriefing = !string.IsNullOrEmpty(message.BriefingSession);

            _logger.LogInformation("Processing SARS tender - Number: {TenderNumber}, HasBriefingSession: {HasBriefing}",
                tenderNumber, hasBriefing);

            // Generate AI summary
            await GenerateAndAttachSummary(message, "SARS");

            _logger.LogInformation("SARS processing completed - Number: {TenderNumber}, SummaryGenerated: {HasSummary}",
                tenderNumber, !string.IsNullOrEmpty(message.AISummary));

            await Task.CompletedTask;
        }

        /// <summary>
        /// Processes SANRAL-specific messages.
        /// </summary>
        private async Task ProcessSanralTenderMessage(SanralTenderMessage message)
        {
            var tenderNumber = message.TenderNumber ?? "Unknown";
            var region = message.Region ?? "Unknown";
            var category = message.Category ?? "Unknown";

            _logger.LogInformation("Processing SANRAL tender - Number: {TenderNumber}, Region: {Region}, Category: {Category}",
                tenderNumber, region, category);

            // Generate AI summary
            await GenerateAndAttachSummary(message, "SANRAL");

            _logger.LogInformation("SANRAL processing completed - Number: {TenderNumber}, SummaryGenerated: {HasSummary}",
                tenderNumber, !string.IsNullOrEmpty(message.AISummary));

            await Task.CompletedTask;
        }

        /// <summary>
        /// Generates AI summary using Bedrock and attaches it to the message
        /// Now part of each tender processing workflow
        /// </summary>
        private async Task GenerateAndAttachSummary(TenderMessageBase message, string processingContext)
        {
            var tenderNumber = message.TenderNumber ?? "Unknown";

            try
            {
                _logger.LogDebug("Generating Bedrock summary - TenderNumber: {TenderNumber}, Context: {ProcessingContext}",
                    tenderNumber, processingContext);

                var summary = await _bedrockSummaryService.GenerateSummaryAsync(message);
                message.AISummary = summary; // This sets the Summary property on your base class

                // Add summary tag to track processing
                message.Tags.Add("SummaryGenerated");
                message.Tags.Add($"SummaryGeneratedBy{processingContext}");

                _logger.LogInformation("Bedrock summary generated and attached - TenderNumber: {TenderNumber}, Context: {ProcessingContext}, SummaryLength: {SummaryLength}",
                    tenderNumber, processingContext, summary?.Length ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate Bedrock summary - TenderNumber: {TenderNumber}, Context: {ProcessingContext}, ErrorType: {ErrorType}",
                    tenderNumber, processingContext, ex.GetType().Name);

                // Add failure tags but don't stop processing
                message.Tags.Add("SummaryGenerationFailed");
                message.Tags.Add($"SummaryFailedIn{processingContext}");

                // Set a fall back message indicating failure with context
                message.AISummary = $"Summary generation failed for {processingContext} tender {tenderNumber}. Manual review required.";
            }
        }
    }
}