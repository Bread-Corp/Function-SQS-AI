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
    public class MessageProcessor : IMessageProcessor
    {
        private readonly ILogger<MessageProcessor> _logger;

        public MessageProcessor(ILogger<MessageProcessor> logger)
        {
            _logger = logger;
        }

        public async Task<TenderMessageBase> ProcessMessageAsync(TenderMessageBase message)
        {
            try
            {
                var sourceType = message.GetSourceType();
                _logger.LogInformation("Processing {SourceType} message: {Title}", sourceType, message.Title);

                // Add a "Processed" tag to indicate the message has been processed
                var processedTags = new List<string>(message.Tags) { "Processed", $"ProcessedBy{sourceType}Handler" };
                message.Tags = processedTags;

                // Source-specific processing
                switch (message)
                {
                    case ETenderMessage eTenderMessage:
                        await ProcessETenderMessage(eTenderMessage);
                        break;

                    case EskomTenderMessage eskomMessage:
                        await ProcessEskomTenderMessage(eskomMessage);
                        break;

                    default:
                        _logger.LogWarning("Unknown message type: {MessageType}", message.GetType().Name);
                        break;
                }

                // Simulate some processing time
                await Task.Delay(100);

                _logger.LogInformation("Successfully processed {SourceType} message: {Title}", sourceType, message.Title);
                return message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process {SourceType} message: {Title}",
                    message.GetSourceType(), message.Title);
                throw;
            }
        }

        private async Task ProcessETenderMessage(ETenderMessage message)
        {
            _logger.LogInformation("Processing eTender message ID: {TenderId}, Status: {Status}",
                message.Id, message.Status);

            // Add eTender-specific processing logic here
            // For example: validate dates, enrich data, etc.

            await Task.CompletedTask;
        }

        private async Task ProcessEskomTenderMessage(EskomTenderMessage message)
        {
            _logger.LogInformation("Processing Eskom tender: {TenderNumber}, Source: {Source}",
                message.TenderNumber, message.Source);

            // Add Eskom-specific processing logic here
            // For example: validate Eskom-specific fields, etc.

            await Task.CompletedTask;
        }
    }
}
