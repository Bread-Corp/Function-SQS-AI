using Sqs_AI_Lambda.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sqs_AI_Lambda.Interfaces
{
    /// <summary>
    /// Service interface for generating AI-powered tender summaries using AWS Bedrock
    /// </summary>
    public interface IBedrockSummaryService
    {
        /// <summary>
        /// Generates a comprehensive summary for the provided tender message using AWS Bedrock
        /// </summary>
        /// <param name="tenderMessage">The tender message to summarize</param>
        /// <returns>A formatted summary string containing key tender information</returns>
        Task<string> GenerateSummaryAsync(TenderMessageBase tenderMessage);
    }
}
