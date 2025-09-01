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
                // SQS batch limit is 10 messages
                const int batchSize = 10;

                // Send messages in batches
                for (int i = 0; i < messages.Count; i += batchSize)
                {
                    // Create a batch of messages
                    var batch = messages.Skip(i).Take(batchSize).ToList();

                    // Prepare batch entries
                    var entries = batch.Select((message, index) => new SendMessageBatchRequestEntry
                    {
                        Id = $"msg_{i + index}",
                        MessageBody = JsonSerializer.Serialize(message, _jsonOptions)
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
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send message batch to {QueueUrl}", queueUrl);
                throw;
            }
        }
    }
}
