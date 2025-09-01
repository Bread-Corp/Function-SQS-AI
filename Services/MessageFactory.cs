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
    public class MessageFactory : IMessageFactory
    {
        private readonly ILogger<MessageFactory> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public MessageFactory(ILogger<MessageFactory> logger)
        {
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };
        }

        public TenderMessageBase? CreateMessage(string messageBody, string messageGroupId)
        {
            try
            {
                _logger.LogInformation("Creating message for MessageGroupId: {MessageGroupId}", messageGroupId);

                return messageGroupId?.ToLowerInvariant() switch
                {
                    "etenderscrape" or "etenderlambda" =>
                        JsonSerializer.Deserialize<ETenderMessage>(messageBody, _jsonOptions),

                    "eskomtenderscrape" or "eskomlambda" =>
                        JsonSerializer.Deserialize<EskomTenderMessage>(messageBody, _jsonOptions),

                    _ => throw new NotSupportedException($"Unsupported MessageGroupId: {messageGroupId}")
                };
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize message body for MessageGroupId: {MessageGroupId}", messageGroupId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error creating message for MessageGroupId: {MessageGroupId}", messageGroupId);
                return null;
            }
        }
    }
}
