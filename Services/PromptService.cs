using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Microsoft.Extensions.Logging;
using Sqs_AI_Lambda.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sqs_AI_Lambda.Services
{
    /// <summary>
    /// Service responsible for fetching and caching Bedrock prompts from AWS Parameter Store.
    /// Uses a static cache to minimize API calls during warm Lambda invocations.
    /// </summary>
    public class PromptService : IPromptService
    {
        private readonly IAmazonSimpleSystemsManagement _ssmClient;
        private readonly ILogger<PromptService> _logger;

        // Base path for prompt parameters in Parameter Store
        private const string PromptBasePath = "/TenderSummary/Prompts/";
        private const string SystemPromptKey = "System"; // Special key for the system prompt

        // Static cache using ConcurrentDictionary for thread safety during parallel fetches if needed
        private static readonly ConcurrentDictionary<string, string> _promptCache = new();

        public PromptService(IAmazonSimpleSystemsManagement ssmClient, ILogger<PromptService> logger)
        {
            _ssmClient = ssmClient ?? throw new ArgumentNullException(nameof(ssmClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public async Task<string> GetPromptAsync(string sourceType)
        {
            if (string.IsNullOrWhiteSpace(sourceType))
            {
                throw new ArgumentException("Source type cannot be null or empty.", nameof(sourceType));
            }

            // Ensure System prompt is cached
            string systemPrompt = await EnsurePromptCachedAsync(SystemPromptKey);

            // Ensure source-specific prompt is cached
            string sourcePrompt = await EnsurePromptCachedAsync(sourceType);

            // Combine prompts
            string combinedPrompt = $"{systemPrompt}\n\n{sourcePrompt}";

            _logger.LogDebug("Combined prompt retrieved for source type: {SourceType}", sourceType);
            return combinedPrompt;
        }

        /// <summary>
        /// Helper method to fetch a specific prompt from cache or Parameter Store.
        /// </summary>
        /// <param name="promptKey">The key of the prompt (e.g., "System", "Eskom").</param>
        /// <returns>The prompt text.</returns>
        private async Task<string> EnsurePromptCachedAsync(string promptKey)
        {
            // Check cache first (case-insensitive key comparison)
            if (_promptCache.TryGetValue(promptKey, out var cachedPrompt))
            {
                _logger.LogDebug("Prompt found in cache for key: {PromptKey}", promptKey);
                return cachedPrompt;
            }

            // Construct the full parameter name
            string parameterName = $"{PromptBasePath}{promptKey}";
            _logger.LogInformation("Prompt not found in cache for key: {PromptKey}. Fetching from Parameter Store: {ParameterName}", promptKey, parameterName);

            try
            {
                var request = new GetParameterRequest
                {
                    Name = parameterName,
                    WithDecryption = false
                };

                var response = await _ssmClient.GetParameterAsync(request);

                if (response.Parameter == null || string.IsNullOrEmpty(response.Parameter.Value))
                {
                    _logger.LogError("Parameter {ParameterName} was found but has no value.", parameterName);
                    throw new KeyNotFoundException($"Parameter '{parameterName}' retrieved from Parameter Store has no value.");
                }

                string promptValue = response.Parameter.Value;

                // Add to cache
                _promptCache.AddOrUpdate(promptKey, promptValue, (key, oldValue) => promptValue);

                _logger.LogInformation("Successfully fetched and cached prompt for key: {PromptKey}", promptKey);
                return promptValue;
            }
            catch (ParameterNotFoundException ex)
            {
                _logger.LogError(ex, "Required prompt parameter not found in Parameter Store: {ParameterName}", parameterName);
                throw new KeyNotFoundException($"Required prompt parameter '{parameterName}' not found in AWS Parameter Store.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch parameter {ParameterName} from Parameter Store.", parameterName);
                throw;
            }
        }
    }
}
