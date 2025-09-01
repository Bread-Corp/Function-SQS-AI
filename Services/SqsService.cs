using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Sqs_AI_Lambda.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sqs_AI_Lambda.Services
{
    public class SqsService : ISqsService
    {
        private readonly IAmazonSQS _sqsClient;
        private readonly ILogger<SqsService> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public SqsService(IAmazonSQS sqsClient, ILogger<SqsService> logger)
        {
            _sqsClient = sqsClient;
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
        }

        public async Task SendMessageAsync(string queueUrl, object message)
        {
            try
            {
                var messageBody = JsonSerializer.Serialize(message, _jsonOptions);
                var request = new SendMessageRequest
                {
                    QueueUrl = queueUrl,
                    MessageBody = messageBody
                };

                // Check if this is a FIFO queue and add required attributes
                if (queueUrl.EndsWith(".fifo"))
                {
                    var messageGroupId = GetMessageGroupId(message, messageBody);
                    request.MessageGroupId = messageGroupId;
                    request.MessageDeduplicationId = Guid.NewGuid().ToString();

                    _logger.LogInformation("Sending message to FIFO queue with MessageGroupId: {MessageGroupId}", messageGroupId);
                }

                var response = await _sqsClient.SendMessageAsync(request);
                _logger.LogInformation("Message sent successfully to {QueueUrl}. MessageId: {MessageId}",
                    queueUrl, response.MessageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send message to {QueueUrl}", queueUrl);
                throw;
            }
        }

        public async Task SendMessageBatchAsync(string queueUrl, List<object> messages)
        {
            if (!messages.Any()) return;

            try
            {
                _logger.LogInformation("Sending batch of {Count} messages to {QueueUrl}", messages.Count, queueUrl);

                // SQS batch limit is 10 messages
                const int batchSize = 10;

                // Send messages in batches
                for (int i = 0; i < messages.Count; i += batchSize)
                {
                    // Create a batch of messages
                    var batch = messages.Skip(i).Take(batchSize).ToList();

                    // Prepare batch entries
                    var entries = batch.Select((message, index) =>
                    {
                        var messageBody = JsonSerializer.Serialize(message, _jsonOptions);
                        var entry = new SendMessageBatchRequestEntry
                        {
                            Id = $"msg_{i + index}",
                            MessageBody = messageBody
                        };

                        // Check if this is a FIFO queue and add required attributes
                        if (queueUrl.EndsWith(".fifo"))
                        {
                            var messageGroupId = GetMessageGroupId(message, messageBody);
                            entry.MessageGroupId = messageGroupId;
                            entry.MessageDeduplicationId = Guid.NewGuid().ToString();

                            _logger.LogDebug("FIFO message {Id} using MessageGroupId: {MessageGroupId}",
                                entry.Id, messageGroupId);
                        }

                        return entry;
                    }).ToList();

                    // Create and send the batch request
                    var request = new SendMessageBatchRequest
                    {
                        QueueUrl = queueUrl,
                        Entries = entries
                    };

                    // Send the batch
                    var response = await _sqsClient.SendMessageBatchAsync(request);

                    // Log the results
                    _logger.LogInformation("Batch sent successfully to {QueueUrl}. Successful: {SuccessCount}, Failed: {FailCount}",
                        queueUrl, response.Successful.Count, response.Failed.Count);

                    // Log any failed messages
                    if (response.Failed.Any())
                    {
                        foreach (var failed in response.Failed)
                        {
                            _logger.LogWarning("Failed to send message {MessageId}: {ErrorCode} - {ErrorMessage}",
                                failed.Id, failed.Code, failed.Message);
                        }
                        throw new InvalidOperationException($"Failed to send {response.Failed.Count} messages to queue");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send message batch to {QueueUrl}", queueUrl);
                throw;
            }
        }

        public async Task DeleteMessageAsync(string queueUrl, string receiptHandle)
        {
            try
            {
                var request = new DeleteMessageRequest
                {
                    QueueUrl = queueUrl,
                    ReceiptHandle = receiptHandle
                };

                await _sqsClient.DeleteMessageAsync(request);
                _logger.LogInformation("Message deleted successfully from {QueueUrl}", queueUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete message from {QueueUrl}", queueUrl);
                throw;
            }
        }

        public async Task DeleteMessageBatchAsync(string queueUrl, List<(string id, string receiptHandle)> messages)
        {
            if (!messages.Any()) return;

            try
            {
                const int batchSize = 10;

                for (int i = 0; i < messages.Count; i += batchSize)
                {
                    var batch = messages.Skip(i).Take(batchSize).ToList();

                    var entries = batch.Select(m => new DeleteMessageBatchRequestEntry
                    {
                        Id = m.id,
                        ReceiptHandle = m.receiptHandle
                    }).ToList();

                    var request = new DeleteMessageBatchRequest
                    {
                        QueueUrl = queueUrl,
                        Entries = entries
                    };

                    var response = await _sqsClient.DeleteMessageBatchAsync(request);
                    _logger.LogInformation("Batch delete completed for {QueueUrl}. Successful: {SuccessCount}, Failed: {FailCount}",
                        queueUrl, response.Successful.Count, response.Failed.Count);

                    // Log any failed deletions
                    if (response.Failed.Any())
                    {
                        foreach (var failed in response.Failed)
                        {
                            _logger.LogWarning("Failed to delete message {MessageId}: {ErrorCode} - {ErrorMessage}",
                                failed.Id, failed.Code, failed.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete message batch from {QueueUrl}", queueUrl);
                throw;
            }
        }

        /// <summary>
        /// Extracts or generates MessageGroupId for FIFO queues
        /// </summary>
        private string GetMessageGroupId(object message, string messageBody)
        {
            try
            {
                _logger.LogDebug("Extracting MessageGroupId from message of type: {MessageType}", message.GetType().Name);

                // Strategy 1: Try to extract MessageGroupId from the message object if it has a GetSourceType method
                var getSourceTypeMethod = message.GetType().GetMethod("GetSourceType");
                if (getSourceTypeMethod != null)
                {
                    var sourceType = getSourceTypeMethod.Invoke(message, null)?.ToString();
                    if (!string.IsNullOrEmpty(sourceType))
                    {
                        _logger.LogDebug("Found MessageGroupId from GetSourceType(): {MessageGroupId}", sourceType);
                        return SanitizeMessageGroupId(sourceType);
                    }
                }

                // Strategy 2: Try to get MessageGroupId from object properties
                var messageGroupIdProperty = message.GetType().GetProperty("MessageGroupId");
                if (messageGroupIdProperty != null)
                {
                    var messageGroupId = messageGroupIdProperty.GetValue(message)?.ToString();
                    if (!string.IsNullOrEmpty(messageGroupId))
                    {
                        _logger.LogDebug("Found MessageGroupId from property: {MessageGroupId}", messageGroupId);
                        return SanitizeMessageGroupId(messageGroupId);
                    }
                }

                // Strategy 3: Parse JSON and look for common MessageGroupId fields
                using var document = JsonDocument.Parse(messageBody);
                var root = document.RootElement;

                // Check various possible field names for MessageGroupId in order of preference
                var possibleFields = new[] {
                    "MessageGroupId", "messageGroupId"
                };

                foreach (var field in possibleFields)
                {
                    if (root.TryGetProperty(field, out var property) && property.ValueKind == JsonValueKind.String)
                    {
                        var value = property.GetString();
                        if (!string.IsNullOrEmpty(value))
                        {
                            _logger.LogDebug("Found MessageGroupId from JSON field '{Field}': {MessageGroupId}", field, value);
                            return SanitizeMessageGroupId(value);
                        }
                    }
                }

                // Strategy 4: Look for Organization field specifically for tender messages
                if (root.TryGetProperty("Organization", out var orgProperty) && orgProperty.ValueKind == JsonValueKind.String)
                {
                    var organization = orgProperty.GetString();
                    if (!string.IsNullOrEmpty(organization))
                    {
                        var sanitizedOrg = SanitizeMessageGroupId(organization);
                        _logger.LogDebug("Using Organization as MessageGroupId: {MessageGroupId}", sanitizedOrg);
                        return sanitizedOrg;
                    }
                }

                // Strategy 5: Check if this is an anonymous object (like failed message metadata)
                var objectTypeName = message.GetType().Name;
                if (objectTypeName.Contains("AnonymousType") || objectTypeName.Contains("<>"))
                {
                    _logger.LogDebug("Anonymous object detected, using 'FailedMessages' as MessageGroupId");
                    return "FailedMessages";
                }

                // Fallback for processed messages
                _logger.LogWarning("Could not extract MessageGroupId from message, using default 'ProcessedMessages'");
                return "ProcessedMessages";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting MessageGroupId from message, using default 'DefaultGroup'");
                return "DefaultGroup";
            }
        }

        /// <summary>
        /// Sanitizes MessageGroupId to meet AWS requirements
        /// </summary>
        private string SanitizeMessageGroupId(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "DefaultGroup";

            // Remove spaces and special characters, keep only alphanumeric, hyphens, and underscores
            var sanitized = System.Text.RegularExpressions.Regex.Replace(input, @"[^a-zA-Z0-9\-_]", "");

            // Ensure it's not empty after sanitization
            if (string.IsNullOrEmpty(sanitized))
                return "DefaultGroup";

            // Ensure it doesn't exceed 128 characters (AWS limit)
            if (sanitized.Length > 128)
                sanitized = sanitized.Substring(0, 128);

            return sanitized;
        }
    }
}