using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sqs_AI_Lambda.Interfaces
{
    /// <summary>
    /// Defines the contract for a service that retrieves Bedrock prompts,
    /// potentially caching them for performance.
    /// </summary>
    public interface IPromptService
    {
        /// <summary>
        /// Retrieves the combined system and source-specific prompt for a given tender source type.
        /// Fetches prompts from AWS Parameter Store and caches them locally.
        /// </summary>
        /// <param name="sourceType">The specific tender source (e.g., "Eskom", "SARS").</param>
        /// <returns>The combined prompt string to be used with Bedrock.</returns>
        /// <exception cref="ArgumentException">Thrown if sourceType is null or empty.</exception>
        /// <exception cref="KeyNotFoundException">Thrown if a required prompt parameter is not found in Parameter Store.</exception>
        Task<string> GetPromptAsync(string sourceType);
    }
}
