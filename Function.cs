using Amazon.BedrockRuntime;
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

/// <summary>
/// AWS Lambda function for processing tender messages from SQS with continuous queue polling
/// </summary>
public class Function
{
    private readonly ISqsService _sqsService;
    private readonly IMessageProcessor _messageProcessor;
    private readonly IMessageFactory _messageFactory;
    private readonly ILogger<Function> _logger;
    private readonly IAmazonSQS _sqsClient;
    private readonly JsonSerializerOptions _jsonOptions;

    // Queue URLs configured via environment variables for deployment flexibility
    private readonly string _writeQueueUrl;
    private readonly string _failedQueueUrl;
    private readonly string _sourceQueueUrl;

    /// <summary>
    /// Default constructor used by AWS Lambda runtime with automatic dependency injection setup
    /// </summary>
    public Function() : this(null, null, null, null, null) { }

    /// <summary>
    /// Constructor with dependency injection support for testing and custom service configuration
    /// </summary>
    public Function(ISqsService? sqsService, IMessageProcessor? messageProcessor, IMessageFactory? messageFactory, ILogger<Function>? logger, IAmazonSQS? sqsClient)
    {
        // Configure dependency injection container for production services
        var serviceProvider = ConfigureServices();

        // Initialize services with injected dependencies or defaults from service provider
        _sqsService = sqsService ?? serviceProvider.GetRequiredService<ISqsService>();
        _messageProcessor = messageProcessor ?? serviceProvider.GetRequiredService<IMessageProcessor>();
        _messageFactory = messageFactory ?? serviceProvider.GetRequiredService<IMessageFactory>();
        _logger = logger ?? serviceProvider.GetRequiredService<ILogger<Function>>();
        _sqsClient = sqsClient ?? serviceProvider.GetRequiredService<IAmazonSQS>();

        // Configure JSON serialization for consistent message handling
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        // Load and validate required environment variables for queue configuration
        var writeQueueEnv = Environment.GetEnvironmentVariable("WRITE_QUEUE_URL");
        var failedQueueEnv = Environment.GetEnvironmentVariable("FAILED_QUEUE_URL");
        var sourceQueueEnv = Environment.GetEnvironmentVariable("SOURCE_QUEUE_URL");

        _logger.LogInformation("Initializing Lambda function - WriteQueue: {WriteQueueSet}, FailedQueue: {FailedQueueSet}, SourceQueue: {SourceQueueSet}",
            !string.IsNullOrEmpty(writeQueueEnv), !string.IsNullOrEmpty(failedQueueEnv), !string.IsNullOrEmpty(sourceQueueEnv));

        // Validate and assign required queue URLs with descriptive error messages
        _writeQueueUrl = writeQueueEnv ??
            throw new InvalidOperationException("WRITE_QUEUE_URL environment variable is required for processed message output");
        _failedQueueUrl = failedQueueEnv ??
            throw new InvalidOperationException("FAILED_QUEUE_URL environment variable is required for error handling");
        _sourceQueueUrl = sourceQueueEnv ??
            throw new InvalidOperationException("SOURCE_QUEUE_URL environment variable is required for message input");

        _logger.LogInformation("Lambda function initialized successfully - Queues configured: Write, Failed, Source");
    }

    /// <summary>
    /// Configures dependency injection container with all required services for production use
    /// </summary>
    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Configure structured logging for CloudWatch integration
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
        });

        // Register AWS SQS client with default configuration for Lambda environment
        services.AddSingleton<IAmazonSQS>(provider => new AmazonSQSClient());
        services.AddSingleton<AmazonBedrockRuntimeClient>(provider => new AmazonBedrockRuntimeClient());

        // Register application services as transient for per-invocation isolation
        services.AddTransient<ISqsService, SqsService>();
        services.AddTransient<IMessageProcessor, MessageProcessor>();
        services.AddTransient<IMessageFactory, MessageFactory>();
        services.AddTransient<IBedrockSummaryService, BedrockSummaryService>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Main Lambda entry point that processes SQS events and continues polling until queue is empty
    /// Implements continuous processing strategy to maximize message throughput per invocation
    /// </summary>
    public async Task<string> FunctionHandler(SQSEvent evnt, ILambdaContext context)
    {
        var functionStart = DateTime.UtcNow;
        var totalProcessed = 0;
        var totalFailed = 0;
        var totalDeleted = 0;
        var batchCount = 0;

        _logger.LogInformation("Lambda invocation started - InitialEventMessages: {InitialMessageCount}, RemainingTime: {RemainingTimeMs}ms",
            evnt.Records.Count, context.RemainingTime.TotalMilliseconds);

        try
        {
            // Process initial event messages first if any were provided in the SQS event
            if (evnt.Records.Any())
            {
                _logger.LogInformation("Processing initial SQS event batch - MessageCount: {MessageCount}", evnt.Records.Count);

                // Convert SQS event records to internal message format
                var initialMessages = evnt.Records.Select(ConvertSqsEventToMessage).ToList();
                var initialResult = await ProcessMessageBatch(initialMessages);

                totalProcessed += initialResult.processed;
                totalFailed += initialResult.failed;
                totalDeleted += initialResult.deleted;
                batchCount++;

                _logger.LogInformation("Initial batch completed - Processed: {Processed}, Failed: {Failed}, Deleted: {Deleted}",
                    initialResult.processed, initialResult.failed, initialResult.deleted);
            }

            // Continue polling for additional messages until time limit or queue empty
            while (context.RemainingTime > TimeSpan.FromSeconds(30)) // Reserve 30 seconds for clean up and response
            {
                // Poll for more messages from the source queue
                var messages = await PollMessagesFromQueue(10); // AWS SQS batch limit

                if (!messages.Any())
                {
                    _logger.LogInformation("Queue polling completed - No additional messages found");
                    break;
                }

                batchCount++;
                _logger.LogInformation("Continuous polling batch {BatchNumber} - MessageCount: {MessageCount}, RemainingTime: {RemainingTimeMs}ms",
                    batchCount, messages.Count, context.RemainingTime.TotalMilliseconds);

                // Process the polled messages using the same batch processing logic
                var batchResult = await ProcessMessageBatch(messages);
                totalProcessed += batchResult.processed;
                totalFailed += batchResult.failed;
                totalDeleted += batchResult.deleted;

                _logger.LogInformation("Batch {BatchNumber} completed - Processed: {Processed}, Failed: {Failed}, Deleted: {Deleted}",
                    batchCount, batchResult.processed, batchResult.failed, batchResult.deleted);

                // Brief delay to prevent aggressive polling and allow other processes
                await Task.Delay(100);
            }

            var totalDuration = (DateTime.UtcNow - functionStart).TotalMilliseconds;
            var result = $"Batches: {batchCount}, Processed: {totalProcessed}, Failed: {totalFailed}, Deleted: {totalDeleted}, Duration: {totalDuration:F0}ms";

            _logger.LogInformation("Lambda execution completed successfully - {ExecutionSummary}", result);
            return result;
        }
        catch (Exception ex)
        {
            var errorDuration = (DateTime.UtcNow - functionStart).TotalMilliseconds;
            _logger.LogError(ex, "Lambda execution failed - Duration: {Duration}ms, BatchesCompleted: {BatchCount}, TotalProcessed: {TotalProcessed}",
                errorDuration, batchCount, totalProcessed);
            throw;
        }
    }

    /// <summary>
    /// Polls messages directly from the configured source SQS queue using short polling
    /// </summary>
    private async Task<List<QueueMessage>> PollMessagesFromQueue(int maxMessages)
    {
        try
        {
            _logger.LogDebug("Polling messages from source queue - MaxMessages: {MaxMessages}, QueueUrl: {QueueUrl}",
                maxMessages, _sourceQueueUrl);

            // Configure receive message request with optimized settings
            var request = new ReceiveMessageRequest
            {
                QueueUrl = _sourceQueueUrl,
                MaxNumberOfMessages = maxMessages,           // Batch size for efficiency
                WaitTimeSeconds = 2,                         // Short polling for responsiveness
                VisibilityTimeout = 300,                     // 5 minutes processing window
                MessageSystemAttributeNames = new List<string> { "All" },  // Get all system metadata
                MessageAttributeNames = new List<string> { "All" }         // Get all custom attributes
            };

            var response = await _sqsClient.ReceiveMessageAsync(request);
            var messageCount = response.Messages?.Count ?? 0;

            _logger.LogDebug("Queue polling completed - ReceivedMessages: {MessageCount}", messageCount);

            // Convert AWS SQS messages to internal queue message format
            return response.Messages?.Select(msg => new QueueMessage
            {
                MessageId = msg.MessageId,
                Body = msg.Body,
                ReceiptHandle = msg.ReceiptHandle,
                MessageGroupId = GetMessageGroupIdFromSqsMessage(msg),
                Attributes = msg.Attributes,
                MessageAttributes = msg.MessageAttributes
            }).ToList() ?? new List<QueueMessage>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to poll messages from source queue - QueueUrl: {QueueUrl}", _sourceQueueUrl);
            return new List<QueueMessage>(); // Return empty list to continue processing
        }
    }

    /// <summary>
    /// Processes a batch of messages through the complete pipeline: parse, process, route, and cleanup
    /// Implements transactional processing pattern to ensure data consistency
    /// </summary>
    private async Task<(int processed, int failed, int deleted)> ProcessMessageBatch(List<QueueMessage> messages)
    {
        var batchStart = DateTime.UtcNow;
        var processedMessages = new List<(TenderMessageBase message, QueueMessage record)>();
        var failedMessages = new List<(string originalMessage, string messageGroupId, Exception exception, QueueMessage record)>();

        _logger.LogInformation("Starting batch processing - MessageCount: {MessageCount}", messages.Count);

        // Phase 1: Parse and process all messages in the batch
        foreach (var message in messages)
        {
            try
            {
                _logger.LogDebug("Processing individual message - MessageId: {MessageId}, GroupId: {MessageGroupId}, BodyLength: {BodyLength}",
                    message.MessageId, message.MessageGroupId, message.Body?.Length ?? 0);

                // Validate message body is not null before processing
                if (string.IsNullOrEmpty(message.Body))
                {
                    throw new InvalidOperationException($"Message body is null or empty for MessageId: {message.MessageId}");
                }

                // Create typed message object from JSON body using factory pattern
                var tenderMessage = _messageFactory.CreateMessage(message.Body, message.MessageGroupId);

                if (tenderMessage == null)
                {
                    throw new InvalidOperationException($"Message factory returned null for GroupId: {message.MessageGroupId}");
                }

                // Process the message through business logic pipeline
                var processedMessage = await _messageProcessor.ProcessMessageAsync(tenderMessage);
                processedMessages.Add((processedMessage, message));

                _logger.LogDebug("Message processed successfully - MessageId: {MessageId}, MessageType: {MessageType}, Source: {SourceType}",
                    message.MessageId, processedMessage.GetType().Name, processedMessage.GetSourceType());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Message processing failed - MessageId: {MessageId}, GroupId: {MessageGroupId}, Error: {ErrorType}",
                    message.MessageId, message.MessageGroupId, ex.GetType().Name);

                // Collect failed messages for DLQ routing
                failedMessages.Add((message.Body ?? string.Empty, message.MessageGroupId, ex, message));
            }
        }

        // Phase 2: Send successfully processed messages to write queue
        var successfullySentToWriteQueue = new List<(TenderMessageBase message, QueueMessage record)>();

        if (processedMessages.Any())
        {
            try
            {
                _logger.LogDebug("Sending processed messages to write queue - Count: {Count}, QueueUrl: {WriteQueueUrl}",
                    processedMessages.Count, _writeQueueUrl);

                // Convert to object list for SQS service batch operation
                var messagesToSend = processedMessages.Select(pm => pm.message).Cast<object>().ToList();
                await _sqsService.SendMessageBatchAsync(_writeQueueUrl, messagesToSend);

                successfullySentToWriteQueue.AddRange(processedMessages);
                _logger.LogInformation("Successfully sent to write queue - Count: {Count}", processedMessages.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send processed messages to write queue - Count: {Count}", processedMessages.Count);

                // Move failed sends to DLQ processing
                foreach (var (message, record) in processedMessages)
                {
                    failedMessages.Add((JsonSerializer.Serialize(message, _jsonOptions), message.GetSourceType(), ex, record));
                }
            }
        }

        // Phase 3: Send failed messages to dead letter queue with error metadata
        if (failedMessages.Any())
        {
            try
            {
                _logger.LogDebug("Sending failed messages to DLQ - Count: {Count}, QueueUrl: {FailedQueueUrl}",
                    failedMessages.Count, _failedQueueUrl);

                // Create enriched error messages with processing metadata
                var dlqMessages = failedMessages.Select(f => new
                {
                    OriginalMessage = f.originalMessage,
                    MessageGroupId = f.messageGroupId,
                    ErrorMessage = f.exception.Message,
                    ErrorType = f.exception.GetType().Name,
                    ProcessingTime = DateTime.UtcNow,
                    StackTrace = f.exception.StackTrace,
                    ProcessedBy = "Sqs_AI_Lambda",
                    ProcessedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                }).Cast<object>().ToList();

                await _sqsService.SendMessageBatchAsync(_failedQueueUrl, dlqMessages);
                _logger.LogInformation("Successfully sent to DLQ - Count: {Count}", failedMessages.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical: Failed to send messages to DLQ - Count: {Count}", failedMessages.Count);
                throw; // Re-throw to trigger Lambda retry mechanism and prevent message loss
            }
        }

        // Phase 4: Clean up successfully processed messages from source queue
        var deletedCount = 0;
        if (successfullySentToWriteQueue.Any())
        {
            try
            {
                _logger.LogDebug("Deleting processed messages from source queue - Count: {Count}", successfullySentToWriteQueue.Count);

                // Prepare batch delete request with message IDs and receipt handles
                var messagesToDelete = successfullySentToWriteQueue
                    .Select(pm => (pm.record.MessageId, pm.record.ReceiptHandle))
                    .ToList();

                await _sqsService.DeleteMessageBatchAsync(_sourceQueueUrl, messagesToDelete);
                deletedCount = messagesToDelete.Count;

                _logger.LogInformation("Successfully deleted from source queue - Count: {Count}", deletedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete processed messages from source queue - Count: {Count}, Impact: Messages may be reprocessed",
                    successfullySentToWriteQueue.Count);

                // Log individual message details for operational troubleshooting
                foreach (var (message, record) in successfullySentToWriteQueue)
                {
                    _logger.LogWarning("Delete failed for MessageId: {MessageId}, ReceiptHandle: {ReceiptHandle}, TenderNumber: {TenderNumber}",
                        record.MessageId, record.ReceiptHandle, message.TenderNumber ?? "Unknown");
                }
            }
        }

        var batchDuration = (DateTime.UtcNow - batchStart).TotalMilliseconds;
        _logger.LogInformation("Batch processing completed - Processed: {Processed}, Failed: {Failed}, Deleted: {Deleted}, Duration: {Duration}ms",
            successfullySentToWriteQueue.Count, failedMessages.Count, deletedCount, batchDuration);

        return (successfullySentToWriteQueue.Count, failedMessages.Count, deletedCount);
    }

    /// <summary>
    /// Converts SQS Event record to internal QueueMessage format for consistent processing
    /// </summary>
    private QueueMessage ConvertSqsEventToMessage(SQSEvent.SQSMessage record)
    {
        // Convert SQS event message attributes to internal format
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

        // Create internal message representation with all required fields
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
    /// Extracts MessageGroupId from SQS event message using multiple fallback strategies
    /// </summary>
    private static string GetMessageGroupId(SQSEvent.SQSMessage record)
    {
        // Primary: Check system attributes for FIFO queue MessageGroupId
        if (record.Attributes?.TryGetValue("MessageGroupId", out var groupId) == true && !string.IsNullOrEmpty(groupId))
        {
            return groupId;
        }

        // Fall back: Check custom message attributes
        if (record.MessageAttributes?.TryGetValue("MessageGroupId", out var msgAttr) == true && !string.IsNullOrEmpty(msgAttr.StringValue))
        {
            return msgAttr.StringValue;
        }

        // Default fall back for messages without group ID
        return "Unknown";
    }

    /// <summary>
    /// Extracts MessageGroupId from direct SQS message using multiple fall back strategies
    /// </summary>
    private static string GetMessageGroupIdFromSqsMessage(Message message)
    {
        // Primary: Check system attributes for FIFO queue MessageGroupId
        if (message.Attributes?.TryGetValue("MessageGroupId", out var groupId) == true && !string.IsNullOrEmpty(groupId))
        {
            return groupId;
        }

        // Fall back: Check custom message attributes
        if (message.MessageAttributes?.TryGetValue("MessageGroupId", out var msgAttr) == true && !string.IsNullOrEmpty(msgAttr.StringValue))
        {
            return msgAttr.StringValue;
        }

        // Default fall back for messages without group ID
        return "Unknown";
    }
}