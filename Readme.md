# ⚙️ SQS AI Lambda — Tender Summarization Service

[![AWS Lambda](https://img.shields.io/badge/AWS-Lambda-orange.svg)](https://aws.amazon.com/lambda/)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![SQS](https://img.shields.io/badge/AWS-SQS-yellow.svg)](https://aws.amazon.com/sqs/)
[![Bedrock](https://img.shields.io/badge/AWS-Bedrock-blueviolet.svg)](https://aws.amazon.com/bedrock/)
[![Parameter Store](https://img.shields.io/badge/AWS-Parameter%20Store-informational.svg)](https://aws.amazon.com/systems-manager/features/)

A production-ready, high-throughput AWS Lambda that processes South African tender messages from multiple sources via Amazon SQS (FIFO) and generates **intelligent summaries using Anthropic Claude 3 Sonnet via Amazon Bedrock**. It continuously polls the source queue, validates and enriches messages with AI-generated summaries, and publishes results to a write queue—while safely routing failures to a DLQ. Built for reliability, observability, and smart prompt management through AWS Parameter Store.

- Sources: eTenders, Eskom, Transnet, SARS, SANRAL (government and utility)
- AI Model: Anthropic Claude 3 Sonnet via Amazon Bedrock
- Prompts: Dynamic, source-specific prompts managed in AWS Parameter Store
- Queues: AIQueue (input), TagQueue (success), FailedQueue (errors)
- Focus: Quality AI summaries, speed, resilience, clear monitoring

---

## 📚 Table of Contents

- [✨ Features](#-features)
- [🧭 Architecture (Simple View)](#-architecture-simple-view)
- [💡 Why SQS + Lambda + Bedrock + Parameter Store?](#-why-sqs--lambda--bedrock--parameter-store)
- [🧩 What's Inside (Project Structure)](#-whats-inside-project-structure)
- [⚙️ Configuration](#️-configuration)
- [🚀 Quick Start](#-quick-start)
- [🧠 How It Works](#-how-it-works)
- [📦 Tech Stack](#-tech-stack)
- [🗂️ Message Models (At a Glance)](#️-message-models-at-a-glance)
- [📮 Queues](#-queues)
- [📈 Monitoring & Metrics](#-monitoring--metrics)
- [🛡️ Error Handling & Recovery](#️-error-handling--recovery)
- [⚡ Performance & Scaling](#-performance--scaling)
- [🔒 Security](#-security)
- [🧪 Testing](#-testing)
- [🧰 Troubleshooting](#-troubleshooting)
- [🗺️ Roadmap](#️-roadmap)

---

## ✨ Features

- 🚀 High throughput: continuous polling + batch processing (up to 10 messages)
- 🧠 **Advanced AI Summarization**: Uses Anthropic Claude 3 Sonnet via Amazon Bedrock
- 🎯 **Source-specific Prompts**: Fetches tailored instructions from AWS Parameter Store
- 🔄 Multi-source support: eTenders, Eskom, Transnet, SARS, SANRAL (easy to add more)
- ⚙️ **External Prompt Management**: Update AI instructions without code deployments
- ⚡ **Efficient Caching**: In-memory prompt caching reduces API calls
- 🧯 Safe-by-default: DLQ routing, partial-batch success, Bedrock throttling handling
- 🧭 Observable: structured logs, clear metrics, error context
- 🧩 JSON-friendly: case-insensitive and camelCase support

---

## 🧭 Architecture (Simple View)

```
Tender Scrapers
    ↓
TenderQueue (TenderQueue.fifo)
    ↓
Deduplication Lambda
    ├─ Check RDS Database (duplicate validation)
    └─ Route based on deduplication results
           ↓
AIQueue (AIQueue.fifo)
    ↓
AI Summary Lambda (AILambda)
    ├─ Message Factory (eTenders, Eskom, Transnet, SARS, SANRAL)
    ├─ Prompt Service ← AWS Parameter Store (dynamic prompts)
    ├─ Bedrock Service → Amazon Bedrock (Claude 3 Sonnet)
    ├─ Message Processor (validate → AI summarize)
    └─ SQS Service (I/O)
           ├─ TagQueue (TagQueue.fifo)        ← success + AI summaries
           └─ FailedQueue (FailedQueue.fifo)  ← errors/DLQ
                ↓
AI Tagging Lambda
    ├─ Apply relevant tags based on content
    └─ Route tagged messages
           ↓
WriteQueue (WriteQueue.fifo)
    ↓
Final Processing → RDS Database
```

---

## 💡 Why SQS + Lambda + Bedrock + Parameter Store?

- **Resilient by design**: retries, visibility timeouts, and DLQs
- **Pay-per-use**: scales with load, no idle cost
- **Intelligent Processing**: State-of-the-art AI summaries via Claude 3 Sonnet
- **Flexible Prompts**: External prompt management allows quick iterations
- **Simple ops model**: logs + metrics in CloudWatch, minimal moving parts
- **Easy extension**: add new sources via the Message Factory pattern

---

## 🧩 What's Inside (Project Structure)

```
Sqs_AI_Lambda/
├── Function.cs                     # Lambda entry point with continuous polling
├── Models/
│   ├── TenderMessageBase.cs        # Base model with AISummary property
│   ├── ETenderMessage.cs           # eTenders model
│   ├── EskomTenderMessage.cs       # Eskom model
│   ├── TransnetTenderMessage.cs    # Transnet model
│   ├── SarsTenderMessage.cs        # SARS model
│   ├── SanralTenderMessage.cs      # SANRAL model
│   ├── SupportingDocument.cs       # Attachments
│   └── QueueMessage.cs             # Internal SQS wrapper
├── Services/
│   ├── MessageProcessor.cs         # Business pipeline with AI integration
│   ├── MessageFactory.cs           # Creates typed messages by MessageGroupId
│   ├── BedrockSummaryService.cs    # Claude 3 Sonnet integration with retries
│   ├── PromptService.cs            # Parameter Store prompt management
│   └── SqsService.cs               # SQS operations
├── Interfaces/
│   ├── IMessageProcessor.cs
│   ├── IMessageFactory.cs
│   ├── IBedrockSummaryService.cs
│   ├── IPromptService.cs
│   └── ISqsService.cs
├── Converters/
│   └── StringOrNumberConverter.cs  # Flexible type conversion
├── aws-lambda-tools-defaults.json  # Deployment config
└── README.md
```

---

## ⚙️ Configuration

### AWS Parameter Store Setup
Create these **String** parameters in AWS Systems Manager Parameter Store:

```
/TenderSummary/Prompts/System      # General instructions for Claude 3
/TenderSummary/Prompts/eTenders    # eTenders-specific prompts
/TenderSummary/Prompts/Eskom       # Eskom-specific prompts
/TenderSummary/Prompts/Transnet    # Transnet-specific prompts
/TenderSummary/Prompts/SARS        # SARS-specific prompts
/TenderSummary/Prompts/SANRAL      # SANRAL-specific prompts
```

### Environment Variables
```bash
SOURCE_QUEUE_URL="https://sqs.us-east-1.amazonaws.com/211635102441/AIQueue.fifo"
TAG_QUEUE_URL="https://sqs.us-east-1.amazonaws.com/211635102441/TagQueue.fifo"
FAILED_QUEUE_URL="https://sqs.us-east-1.amazonaws.com/211635102441/FailedQueue.fifo"
```

### IAM Permissions Required
- SQS: `ReceiveMessage`, `DeleteMessage`, `GetQueueAttributes`, `SendMessage`
- Bedrock: `bedrock:InvokeModel` (Claude 3 Sonnet model ARN)
- Parameter Store: `ssm:GetParameter` (on `/TenderSummary/Prompts/*`)
- CloudWatch Logs: `logs:CreateLogGroup`, `logs:CreateLogStream`, `logs:PutLogEvents`

---

## 🧠 How It Works

1) **Ingest** (continuous polling, small time budget safety)
```csharp
while (context.RemainingTime > TimeSpan.FromSeconds(30))
{
    var messages = await PollMessagesFromQueue(10);
    if (!messages.Any()) break;
    await ProcessMessageBatch(messages);
}
```

2) **Route & Create** (by MessageGroupId, now supports 5 sources)
```csharp
var result = groupIdLower switch
{
    "etenderscrape" or "etenderlambda" => CreateETenderMessage(body, groupId),
    "eskomtenderscrape" or "eskomlambda" => CreateEskomTenderMessage(body, groupId),
    "transnettenderscrape" or "transnetlambda" => CreateTransnetTenderMessage(body, groupId),
    "sarstenderscrape" or "sarslambda" => CreateSarsTenderMessage(body, groupId),
    "sanraltenderscrape" or "sanrallambda" => CreateSanralTenderMessage(body, groupId),
    _ => HandleUnsupportedMessageGroup(groupId)
};
```

3) **Fetch Prompts** (cached from Parameter Store)
```csharp
var systemPrompt = await _promptService.GetPromptAsync("System");
var sourcePrompt = await _promptService.GetPromptAsync(sourceType);
```

4) **AI Summarization** (Claude 3 Sonnet via Bedrock)
```csharp
var summary = await _bedrockService.GenerateSummaryAsync(message, systemPrompt, sourcePrompt);
message.AISummary = summary;
```

5) **Process & Tag**
- Adds lifecycle tags (Processed, AI-Enhanced, handler name, UTC timestamp)

6) **Output** (transactional)
- Success → TagQueue + delete from AIQueue
- Error → FailedQueue (DLQ) + keep in AIQueue for retry

---

## 📦 Tech Stack

- Runtime: .NET 8 (LTS)
- Compute: AWS Lambda
- AI Model: Anthropic Claude 3 Sonnet (via Amazon Bedrock)
- Configuration: AWS Systems Manager Parameter Store
- Messaging: Amazon SQS (FIFO)
- Serialization: System.Text.Json
- Logging/DI: Microsoft.Extensions.*

NuGet (excerpt):
```xml
<PackageReference Include="Amazon.Lambda.Core" Version="2.5.0" />
<PackageReference Include="Amazon.Lambda.Serialization.SystemTextJson" Version="2.4.4" />
<PackageReference Include="Amazon.Lambda.SQSEvents" Version="2.2.0" />
<PackageReference Include="AWSSDK.SQS" Version="4.0.1.2" />
<PackageReference Include="AWSSDK.BedrockRuntime" Version="3.7.400.3" />
<PackageReference Include="AWSSDK.SimpleSystemsManagement" Version="3.7.400.3" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.8" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="9.0.8" />
```

---

## 🗂️ Message Models (At a Glance)

Shared fields: title, description, tenderNumber, reference, contact info, province, supportingDocs, tags, **AISummary**

- eTenders: id, status, datePublished, dateClosing, url
- Eskom: source, publishedDate?, closingDate?
- Transnet: institution, category, location, contactPerson
- SARS: briefingSession, dates
- SANRAL: category, region, fullNoticeText

Base model excerpt:
```csharp
public abstract class TenderMessageBase
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TenderNumber { get; set; } = string.Empty;
    public List<SupportingDocument> SupportingDocs { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string? AISummary { get; set; } = null; // Generated by Claude 3 Sonnet
    public abstract string GetSourceType();
}
```

---

## 📮 Queues

| Queue         | Purpose                 | URL                                 | Groups (examples)                              | Notes                         |
|---------------|-------------------------|--------------------------------------|-------------------------------------------------|-------------------------------|
| AIQueue       | Incoming tender messages| AIQueue.fifo                         | etenderscrape, eskomtenderscrape, transnettenderscrape, sarstenderscrape, sanraltenderscrape | Retention 5d |
| TagQueue      | AI-enhanced messages    | TagQueue.fifo                        | Source-based grouping                           | Includes AI summaries + tags  |
| FailedQueue   | Error recovery          | FailedQueue.fifo                     | Error-type grouping                             | Enriched error info            |

Full URLs:
```bash
SOURCE_QUEUE_URL="https://sqs.us-east-1.amazonaws.com/211635102441/AIQueue.fifo"
TAG_QUEUE_URL="https://sqs.us-east-1.amazonaws.com/211635102441/TagQueue.fifo"
FAILED_QUEUE_URL="https://sqs.us-east-1.amazonaws.com/211635102441/FailedQueue.fifo"
```

---

## 📈 Monitoring & Metrics

Watch these:
- Lambda: Invocations, Duration, Errors, Throttles
- SQS: ApproximateNumberOfMessages (queue depth)
- Bedrock: Model invocation metrics, throttling events
- Parameter Store: GetParameter API calls

Structured log example:
```json
{
  "level": "Information",
  "message": "AI summary generated",
  "properties": { 
    "SourceType": "SANRAL", 
    "TenderNumber": "RFP-2025-001",
    "SummaryLength": 247,
    "BedrockDuration": 1.2
  }
}
```

Custom metric-style logs:
```csharp
_logger.LogInformation("Batch done - Processed: {Processed}, AI-Enhanced: {Enhanced}, Failed: {Failed}, Duration: {Duration}ms",
  successCount, aiEnhancedCount, failedCount, processingDuration);
```

---

## 🛡️ Error Handling & Recovery

- **Bedrock throttling**: Automatic retries with exponential backoff
- **Prompt fetching failures**: Graceful degradation, detailed logging
- **AI processing errors**: Messages routed to DLQ with context
- **Automatic retries** via visibility timeout + Lambda retry behavior
- **Enriched error payloads** for fast diagnosis

Redrive example:
```bash
aws sqs start-message-move-task \
  --source-arn arn:aws:sqs:us-east-1:211635102441:FailedQueue.fifo \
  --destination-arn arn:aws:sqs:us-east-1:211635102441:AIQueue.fifo
```

---

## ⚡ Performance & Scaling

- **Prompt caching**: Reduces Parameter Store API calls
- **Bedrock concurrency control**: Prevents overwhelming the AI service
- **ReadyToRun** to reduce cold starts
- **Batch operations** for send/delete
- **Async/await** throughout the pipeline
- **Keep JSON compact** (camelCase, no indentation)

Scaling:
- Concurrency scales with demand and Bedrock capacity
- Typical: dozens of AI-enhanced messages/minute per Lambda
- Cost: pay-per-invocation + Bedrock token usage

---

## 🔒 Security

Data handling:
- TLS in transit, SSE at rest on SQS and Parameter Store
- Public tender data only
- **IAM least-privilege** for Bedrock and Parameter Store access
- Full audit trail via CloudWatch

Prompt security:
- Parameter Store provides secure configuration management
- Prompts can be updated without code deployments
- Version control for prompt changes

---

## 🧰 Troubleshooting

- **Missing prompts**
  - KeyNotFoundException → Check Parameter Store paths
- **Bedrock permission denied**
  - AccessDeniedException → Update IAM role for Bedrock
- **Model not available**
  - ValidationException → Verify Claude 3 Sonnet availability in region
- **Throttling issues**
  - ThrottlingException → Monitor Bedrock quotas, retry logic handles automatically
- **DLQ growth**
  - Investigate error patterns → fix root cause → redrive

Helpful commands:
```bash
aws logs filter-log-events --log-group-name "/aws/lambda/AILambda" --start-time $(date -d '1 hour ago' +%s)000
aws ssm get-parameter --name "/TenderSummary/Prompts/System"
aws bedrock list-foundation-models --region us-east-1
aws sqs get-queue-attributes --queue-url https://sqs.us-east-1.amazonaws.com/211635102441/AIQueue.fifo --attribute-names All
```

---

## 🗺️ Roadmap

- **Enhanced AI**: Fine-tuning models for even better summary quality
- **Smart tagging**: Implement content-based tagging using AI insights
- **Multi-language**: Support for Afrikaans and other SA languages
- **Analytics**: Summary quality metrics and feedback loops

---

> Built with love, bread, and code by **Bread Corporation** 🦆❤️💻