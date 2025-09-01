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

        public MessageProcessor(ILogger<MessageProcessor> logger)
        {
            _logger = logger;
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
                        _logger.LogDebug("Routing to eTender processor - ID: {TenderId}", eTenderMessage.Id);
                        await ProcessETenderMessage(eTenderMessage);
                        break;

                    case EskomTenderMessage eskomMessage:
                        _logger.LogDebug("Routing to Eskom processor - TenderNumber: {TenderNumber}", eskomMessage.TenderNumber);
                        await ProcessEskomTenderMessage(eskomMessage);
                        break;

                    default:
                        // Log unknown message types for monitoring
                        _logger.LogWarning("Unknown message type encountered - Type: {MessageType}, Source: {SourceType}, TenderNumber: {TenderNumber}",
                            messageType, sourceType, tenderNumber);
                        break;
                }

                // Simulate processing time for demo purposes
                _logger.LogTrace("Simulating processing delay for message: {TenderNumber}", tenderNumber);
                await Task.Delay(100);

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
            var tenderId = message.Id;
            var status = message.Status ?? "Unknown";
            var hasUrl = !string.IsNullOrWhiteSpace(message.Url);
            var docCount = message.SupportingDocs?.Count ?? 0;

            _logger.LogInformation("Processing eTender message - ID: {TenderId}, Status: {Status}, HasUrl: {HasUrl}, DocumentCount: {DocumentCount}",
                tenderId, status, hasUrl, docCount);

            // Log date information for tracking
            if (message.DatePublished != default)
            {
                _logger.LogDebug("eTender published date - ID: {TenderId}, PublishedDate: {PublishedDate}",
                    tenderId, message.DatePublished);
            }

            _logger.LogInformation("eTender processing completed - ID: {TenderId}, Status: {Status}", tenderId, status);

            // Placeholder for future eTender-specific processing logic
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

            _logger.LogInformation("Processing Eskom tender - Number: {TenderNumber}, Source: {Source}, HasPublishedDate: {HasPublishedDate}",
                tenderNumber, source, hasPublishedDate);

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

            _logger.LogInformation("Eskom processing completed - Number: {TenderNumber}, Source: {Source}", tenderNumber, source);

            // Placeholder for future Eskom-specific processing logic
            await Task.CompletedTask;
        }
    }
}