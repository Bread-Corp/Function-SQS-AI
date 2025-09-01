using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sqs_AI_Lambda.Interfaces
{
    /// <summary>
    /// Defines the contract for Amazon SQS (Simple Queue Service) operations.
    /// This interface provides asynchronous methods for sending and deleting messages
    /// from SQS queues, supporting both single message and batch operations for efficiency.
    /// </summary>
    public interface ISqsService
    {
        /// <summary>
        /// Asynchronously sends a single message to the specified SQS queue.
        /// The message object will be serialized (typically to JSON) before being sent to the queue.
        /// </summary>
        /// <param name="queueUrl">The URL of the SQS queue to send the message to. This is the full queue URL provided by AWS.</param>
        /// <param name="message">The message object to send. This will be serialized into a format suitable for SQS (usually JSON).</param>
        /// <returns>A Task representing the asynchronous send operation. The task completes when the message has been successfully sent to SQS.</returns>
        Task SendMessageAsync(string queueUrl, object message);

        /// <summary>
        /// Asynchronously sends multiple messages to the specified SQS queue in a single batch operation.
        /// This method is more efficient than sending individual messages when you need to send multiple messages,
        /// as it reduces the number of API calls and can lower costs.
        /// </summary>
        /// <param name="queueUrl">The URL of the SQS queue to send the messages to. This is the full queue URL provided by AWS.</param>
        /// <param name="messages">A list of message objects to send. Each object will be serialized into a format suitable for SQS (usually JSON). AWS SQS supports up to 10 messages per batch.</param>
        /// <returns>A Task representing the asynchronous batch send operation. The task completes when all messages have been processed by SQS.</returns>
        Task SendMessageBatchAsync(string queueUrl, List<object> messages);

        /// <summary>
        /// Asynchronously deletes a single message from the specified SQS queue.
        /// This operation is typically performed after successfully processing a message received from the queue
        /// to prevent it from being delivered again.
        /// </summary>
        /// <param name="queueUrl">The URL of the SQS queue containing the message to delete. This is the full queue URL provided by AWS.</param>
        /// <param name="receiptHandle">The receipt handle of the message to delete. This is provided when a message is received from SQS and acts as a temporary identifier for the message.</param>
        /// <returns>A Task representing the asynchronous delete operation. The task completes when the message has been successfully deleted from SQS.</returns>
        Task DeleteMessageAsync(string queueUrl, string receiptHandle);

        /// <summary>
        /// Asynchronously deletes multiple messages from the specified SQS queue in a single batch operation.
        /// This method is more efficient than deleting individual messages when you need to delete multiple messages,
        /// as it reduces the number of API calls and can improve performance.
        /// </summary>
        /// <param name="queueUrl">The URL of the SQS queue containing the messages to delete. This is the full queue URL provided by AWS.</param>
        /// <param name="messages">A list of tuples containing the message ID and receipt handle for each message to delete. The ID can be any identifier you choose, while the receipt handle must be the one provided by SQS when the message was received. AWS SQS supports up to 10 messages per batch delete.</param>
        /// <returns>A Task representing the asynchronous batch delete operation. The task completes when all specified messages have been processed for deletion by SQS.</returns>
        Task DeleteMessageBatchAsync(string queueUrl, List<(string id, string receiptHandle)> messages);
    }
}