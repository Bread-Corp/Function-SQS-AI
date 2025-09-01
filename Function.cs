using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sqs_AI_Lambda.Interfaces;
using Sqs_AI_Lambda.Models;
using Sqs_AI_Lambda.Services;
using System.Text.Json;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Sqs_AI_Lambda;

public class Function
{
    private readonly ISqsService _sqsService;
    private readonly IMessageProcessor _messageProcessor;
    private readonly IMessageFactory _messageFactory;
    private readonly ILogger<Function> _logger;
    private readonly IAmazonSQS _sqsClient;
    private readonly JsonSerializerOptions _jsonOptions;

    // Queue URLs - configured via environment variables
    private readonly string _writeQueueUrl;
    private readonly string _failedQueueUrl;
    private readonly string _sourceQueueUrl;

    /// <summary>
    /// Default constructor. This constructor is used by Lambda to construct the instance. When invoked in a Lambda environment
    /// the AWS credentials will come from the IAM role associated with the function and the AWS region will be set to the
    /// region the Lambda function is executed in.
    /// </summary>
    public Function() : this(null, null, null, null, null) { }

    /// <summary>
    /// Constructor for dependency injection (used for testing)
    /// </summary>
    public Function(ISqsService? sqsService, IMessageProcessor? messageProcessor, IMessageFactory? messageFactory, ILogger<Function>? logger, IAmazonSQS? sqsClient)
    {
        var serviceProvider = ConfigureServices();

        _sqsService = sqsService ?? serviceProvider.GetRequiredService<ISqsService>();
        _messageProcessor = messageProcessor ?? serviceProvider.GetRequiredService<IMessageProcessor>();
        _messageFactory = messageFactory ?? serviceProvider.GetRequiredService<IMessageFactory>();
        _logger = logger ?? serviceProvider.GetRequiredService<ILogger<Function>>();
        _sqsClient = sqsClient ?? serviceProvider.GetRequiredService<IAmazonSQS>();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        // Configure queue URLs from environment variables
        _writeQueueUrl = Environment.GetEnvironmentVariable("WRITE_QUEUE_URL") ??
            throw new InvalidOperationException("WRITE_QUEUE_URL environment variable is required");
        _failedQueueUrl = Environment.GetEnvironmentVariable("FAILED_QUEUE_URL") ??
            throw new InvalidOperationException("FAILED_QUEUE_URL environment variable is required");
        _sourceQueueUrl = Environment.GetEnvironmentVariable("SOURCE_QUEUE_URL") ??
            throw new InvalidOperationException("SOURCE_QUEUE_URL environment variable is required");
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
        });

        // Add AWS SQS client directly (without extensions)
        services.AddSingleton<IAmazonSQS>(provider => new AmazonSQSClient());

        // Add application services
        services.AddTransient<ISqsService, SqsService>();
        services.AddTransient<IMessageProcessor, MessageProcessor>();
        services.AddTransient<IMessageFactory, MessageFactory>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// This method is called for every Lambda invocation. This method takes in an SQS event object and can be used 
    /// to respond to SQS messages. Processes ALL messages in the queue continuously in batches of 10.
    /// </summary>
    /// <param name="evnt">The event for the Lambda function handler to process.</param>
    /// <param name="context">The ILambdaContext that provides methods for logging and describing the Lambda environment.</param>
    /// <returns>Processing summary string</returns>
    public async Task<string> FunctionHandler(SQSEvent evnt, ILambdaContext context)
    {
        _logger.LogInformation("Starting continuous processing of all messages in queue");

        var totalProcessed = 0;
        var totalFailed = 0;
        var totalDeleted = 0;
        var batchCount = 0;

        try
        {
            // Process the initial event messages first
            if (evnt.Records.Any())
            {
                _logger.LogInformation("Processing initial event with {MessageCount} messages", evnt.Records.Count);
                var initialResult = await ProcessMessageBatch(evnt.Records.Select(ConvertSqsEventToMessage).ToList());
                totalProcessed += initialResult.processed;
                totalFailed += initialResult.failed;
                totalDeleted += initialResult.deleted;
                batchCount++;
            }

            // Continue polling for more messages until queue is empty
            while (context.RemainingTime > TimeSpan.FromSeconds(30)) // Leave 30 seconds buffer for cleanup
            {
                var messages = await PollMessagesFromQueue(10);

                if (!messages.Any())
                {
                    _logger.LogInformation("No more messages in queue. Processing complete.");
                    break;
                }

                _logger.LogInformation("Batch {BatchNumber}: Processing {MessageCount} messages from continuous poll",
                    batchCount + 1, messages.Count);

                var batchResult = await ProcessMessageBatch(messages);
                totalProcessed += batchResult.processed;
                totalFailed += batchResult.failed;
                totalDeleted += batchResult.deleted;
                batchCount++;

                // Small delay to prevent aggressive polling
                await Task.Delay(100);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during continuous processing");
            throw;
        }

        var result = $"Batches: {batchCount}, Total Processed: {totalProcessed}, Total Failed: {totalFailed}, Total Deleted: {totalDeleted}";
        _logger.LogInformation("Continuous processing completed. {Result}", result);
        return result;
    }

    /// <summary>
    /// Polls messages directly from the SQS queue
    /// </summary>
    private async Task<List<QueueMessage>> PollMessagesFromQueue(int maxMessages)
    {
        try
        {
            var request = new ReceiveMessageRequest
            {
                QueueUrl = _sourceQueueUrl,
                MaxNumberOfMessages = maxMessages,
                WaitTimeSeconds = 2, // Short polling
                VisibilityTimeout = 300, // 5 minutes
                MessageSystemAttributeNames = new List<string> { "All" },
                MessageAttributeNames = new List<string> { "All" }
            };

            var response = await _sqsClient.ReceiveMessageAsync(request);

            return response.Messages.Select(msg => new QueueMessage
            {
                MessageId = msg.MessageId,
                Body = msg.Body,
                ReceiptHandle = msg.ReceiptHandle,
                MessageGroupId = GetMessageGroupIdFromSqsMessage(msg),
                Attributes = msg.Attributes,
                MessageAttributes = msg.MessageAttributes
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to poll messages from queue");
            return new List<QueueMessage>();
        }
    }

    /// <summary>
    /// Processes a batch of messages
    /// </summary>
    private async Task<(int processed, int failed, int deleted)> ProcessMessageBatch(List<QueueMessage> messages)
    {
        var processedMessages = new List<(TenderMessageBase message, QueueMessage record)>();
        var failedMessages = new List<(string originalMessage, string messageGroupId, Exception exception, QueueMessage record)>();

        // Phase 1: Process all messages
        foreach (var message in messages)
        {
            try
            {
                _logger.LogInformation("Processing message {MessageId} with MessageGroupId: {MessageGroupId}",
                    message.MessageId, message.MessageGroupId);

                // Create the appropriate message type based on MessageGroupId
                var tenderMessage = _messageFactory.CreateMessage(message.Body, message.MessageGroupId);

                if (tenderMessage == null)
                {
                    throw new InvalidOperationException($"Failed to create message for MessageGroupId: {message.MessageGroupId}");
                }

                // Process the message
                var processedMessage = await _messageProcessor.ProcessMessageAsync(tenderMessage);
                processedMessages.Add((processedMessage, message));

                _logger.LogInformation("Successfully processed message {MessageId} of type {MessageType}",
                    message.MessageId, processedMessage.GetSourceType());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process message {MessageId} with MessageGroupId: {MessageGroupId}",
                    message.MessageId, message.MessageGroupId);
                failedMessages.Add((message.Body, message.MessageGroupId, ex, message));
            }
        }

        // Phase 2: Send processed messages to WriteQueue
        var successfullySentToWriteQueue = new List<(TenderMessageBase message, QueueMessage record)>();

        if (processedMessages.Any())
        {
            try
            {
                await _sqsService.SendMessageBatchAsync(_writeQueueUrl, processedMessages.Select(pm => pm.message).Cast<object>().ToList());
                _logger.LogInformation("Sent {Count} processed messages to WriteQueue", processedMessages.Count);
                successfullySentToWriteQueue.AddRange(processedMessages);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send processed messages to WriteQueue");
                // Move these to failed messages for DLQ processing
                foreach (var (message, record) in processedMessages)
                {
                    failedMessages.Add((JsonSerializer.Serialize(message, _jsonOptions), message.GetSourceType(), ex, record));
                }
            }
        }

        // Phase 3: Send failed messages to FailedQueue
        if (failedMessages.Any())
        {
            try
            {
                var dlqMessages = failedMessages.Select(f => new
                {
                    OriginalMessage = f.originalMessage,
                    MessageGroupId = f.messageGroupId,
                    ErrorMessage = f.exception.Message,
                    ErrorType = f.exception.GetType().Name,
                    ProcessingTime = DateTime.UtcNow,
                    StackTrace = f.exception.StackTrace,
                    ProcessedBy = "AI_Lambda",
                    ProcessedAt = DateTime.UtcNow
                }).Cast<object>().ToList();

                await _sqsService.SendMessageBatchAsync(_failedQueueUrl, dlqMessages);
                _logger.LogInformation("Sent {Count} failed messages to FailedQueue", failedMessages.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send failed messages to FailedQueue");
                throw; // Re-throw to trigger Lambda retry mechanism
            }
        }

        // Phase 4: Delete successfully processed messages from source queue
        var deletedCount = 0;
        if (successfullySentToWriteQueue.Any())
        {
            try
            {
                var messagesToDelete = successfullySentToWriteQueue
                    .Select(pm => (pm.record.MessageId, pm.record.ReceiptHandle))
                    .ToList();

                await _sqsService.DeleteMessageBatchAsync(_sourceQueueUrl, messagesToDelete);
                deletedCount = messagesToDelete.Count;
                _logger.LogInformation("Deleted {Count} successfully processed messages from source queue", deletedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete {Count} messages from source queue. Messages may be reprocessed.",
                    successfullySentToWriteQueue.Count);

                // Log individual message details for troubleshooting
                foreach (var (message, record) in successfullySentToWriteQueue)
                {
                    _logger.LogWarning("Message {MessageId} with ReceiptHandle {ReceiptHandle} could not be deleted",
                        record.MessageId, record.ReceiptHandle);
                }
            }
        }

        return (successfullySentToWriteQueue.Count, failedMessages.Count, deletedCount);
    }

    /// <summary>
    /// Converts SQS Event record to QueueMessage
    /// </summary>
    private QueueMessage ConvertSqsEventToMessage(SQSEvent.SQSMessage record)
    {
        // Convert SQSEvent.MessageAttribute to MessageAttributeValue
        Dictionary<string, MessageAttributeValue>? convertedMessageAttributes = null;
        if (record.MessageAttributes != null)
        {
            convertedMessageAttributes = record.MessageAttributes.ToDictionary(
                kvp => kvp.Key,
                kvp => new MessageAttributeValue
                {
                    StringValue = kvp.Value.StringValue,
                    BinaryValue = kvp.Value.BinaryValue,
                    DataType = kvp.Value.DataType
                });
        }

        return new QueueMessage
        {
            MessageId = record.MessageId,
            Body = record.Body,
            ReceiptHandle = record.ReceiptHandle,
            MessageGroupId = GetMessageGroupId(record),
            Attributes = record.Attributes,
            MessageAttributes = convertedMessageAttributes
        };
    }

    /// <summary>
    /// Extracts MessageGroupId from SQS event message attributes
    /// </summary>
    private static string GetMessageGroupId(SQSEvent.SQSMessage record)
    {
        // Try to get MessageGroupId from attributes
        if (record.Attributes?.TryGetValue("MessageGroupId", out var groupId) == true)
        {
            return groupId;
        }

        // Fall back: try to get from message attributes
        if (record.MessageAttributes?.TryGetValue("MessageGroupId", out var msgAttr) == true)
        {
            return msgAttr.StringValue ?? "Unknown";
        }

        return "Unknown";
    }

    /// <summary>
    /// Extracts MessageGroupId from direct SQS message
    /// </summary>
    private static string GetMessageGroupIdFromSqsMessage(Message message)
    {
        // Try to get MessageGroupId from attributes
        if (message.Attributes?.TryGetValue("MessageGroupId", out var groupId) == true)
        {
            return groupId;
        }

        // Fall back: try to get from message attributes
        if (message.MessageAttributes?.TryGetValue("MessageGroupId", out var msgAttr) == true)
        {
            return msgAttr.StringValue ?? "Unknown";
        }

        return "Unknown";
    }
}

/// <summary>
/// Internal class to represent queue messages
/// </summary>
internal class QueueMessage
{
    public string MessageId { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string ReceiptHandle { get; set; } = string.Empty;
    public string MessageGroupId { get; set; } = string.Empty;
    public Dictionary<string, string>? Attributes { get; set; }
    public Dictionary<string, MessageAttributeValue>? MessageAttributes { get; set; }
}