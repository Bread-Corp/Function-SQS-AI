using Sqs_AI_Lambda.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sqs_AI_Lambda.Interfaces
{
    /// <summary>
    /// Defines the contract for processing tender-related messages in the SQS AI Lambda system.
    /// This interface provides a standardized way to handle and transform tender messages,
    /// allowing for different processing implementations while maintaining a consistent API.
    /// </summary>
    public interface IMessageProcessor
    {
        /// <summary>
        /// Asynchronously processes a tender message and returns the processed result.
        /// This method serves as the main entry point for message processing operations,
        /// handling the business logic required to transform or validate tender messages.
        /// </summary>
        /// <param name="message">
        /// The tender message to process. This should be a valid instance of TenderMessageBase
        /// or one of its derived types containing the data that needs to be processed.
        /// </param>
        /// <returns>
        /// A Task containing the processed TenderMessageBase. The returned message may be:
        /// - The same message with modified properties (e.g., validation flags, calculated fields)
        /// - A transformed version of the original message
        /// - An enriched message with additional data
        /// The specific processing behavior depends on the implementation.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// May be thrown by implementations if the message parameter is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// May be thrown by implementations if the message is in an invalid state for processing.
        /// </exception>
        Task<TenderMessageBase> ProcessMessageAsync(TenderMessageBase message);
    }
}