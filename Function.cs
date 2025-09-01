using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.SQS;
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
    private readonly JsonSerializerOptions _jsonOptions;

    // Queue URLs - configured via environment variables
    private readonly string _writeQueueUrl;
    private readonly string _failedQueueUrl;

    /// <summary>
    /// Default constructor. This constructor is used by Lambda to construct the instance. When invoked in a Lambda environment
    /// the AWS credentials will come from the IAM role associated with the function and the AWS region will be set to the
    /// region the Lambda function is executed in.
    /// </summary>
    public Function() : this(null, null, null, null) { }

    /// <summary>
    /// Constructor for dependency injection (used for testing)
    /// </summary>
    public Function(ISqsService? sqsService, IMessageProcessor? messageProcessor, IMessageFactory? messageFactory, ILogger<Function>? logger)
    {
        var serviceProvider = ConfigureServices();

        _sqsService = sqsService ?? serviceProvider.GetRequiredService<ISqsService>();
        _messageProcessor = messageProcessor ?? serviceProvider.GetRequiredService<IMessageProcessor>();
        _messageFactory = messageFactory ?? serviceProvider.GetRequiredService<IMessageFactory>();
        _logger = logger ?? serviceProvider.GetRequiredService<ILogger<Function>>();

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
    /// to respond to SQS messages. Processes messages in batches of up to 10.
    /// </summary>
    /// <param name="evnt">The event for the Lambda function handler to process.</param>
    /// <param name="context">The ILambdaContext that provides methods for logging and describing the Lambda environment.</param>
    /// <returns>Processing summary string</returns>
    public async Task<string> FunctionHandler(SQSEvent evnt, ILambdaContext context)
    {
        _logger.LogInformation("Processing {MessageCount} messages from SQS", evnt.Records.Count);

        var processedMessages = new List<TenderMessageBase>();
        var failedMessages = new List<(string originalMessage, string messageGroupId, Exception exception)>();

        foreach (var record in evnt.Records)
        {
            var messageGroupId = GetMessageGroupId(record);

            try
            {
                _logger.LogInformation("Processing message {MessageId} with MessageGroupId: {MessageGroupId}",
                    record.MessageId, messageGroupId);

                // Create the appropriate message type based on MessageGroupId
                var tenderMessage = _messageFactory.CreateMessage(record.Body, messageGroupId);

                if (tenderMessage == null)
                {
                    throw new InvalidOperationException($"Failed to create message for MessageGroupId: {messageGroupId}");
                }

                // Process the message
                var processedMessage = await _messageProcessor.ProcessMessageAsync(tenderMessage);
                processedMessages.Add(processedMessage);

                _logger.LogInformation("Successfully processed message {MessageId} of type {MessageType}",
                    record.MessageId, processedMessage.GetSourceType());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process message {MessageId} with MessageGroupId: {MessageGroupId}",
                    record.MessageId, messageGroupId);
                failedMessages.Add((record.Body, messageGroupId, ex));
            }
        }

        // Send processed messages to WriteQueue
        if (processedMessages.Any())
        {
            try
            {
                await _sqsService.SendMessageBatchAsync(_writeQueueUrl, processedMessages.Cast<object>().ToList());
                _logger.LogInformation("Sent {Count} processed messages to WriteQueue", processedMessages.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send processed messages to WriteQueue");
                // Add these to failed messages for DLQ processing
                foreach (var message in processedMessages)
                {
                    failedMessages.Add((JsonSerializer.Serialize(message, _jsonOptions), message.GetSourceType(), ex));
                }
            }
        }

        // Send failed messages to FailedQueue (Dead Letter Queue)
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
                    ProcessedBy = "SQS_AI_Lambda",
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

        var result = $"Processed: {processedMessages.Count}, Failed: {failedMessages.Count}";
        _logger.LogInformation("Function execution completed. {Result}", result);
        return result;
    }

    /// <summary>
    /// Extracts MessageGroupId from SQS message attributes
    /// </summary>
    /// <param name="record">SQS message record</param>
    /// <returns>MessageGroupId or "Unknown" if not found</returns>
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
}