# ⚙️ SQS AI Lambda — Tender Message Processing System

[![AWS Lambda](https://img.shields.io/badge/AWS-Lambda-orange.svg)](https://aws.amazon.com/lambda/)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![SQS](https://img.shields.io/badge/AWS-SQS-yellow.svg)](https://aws.amazon.com/sqs/)

A production-ready, high-throughput AWS Lambda that processes South African tender messages from multiple sources via Amazon SQS (FIFO). It continuously polls the source queue, validates and enriches messages, and publishes results to a write queue—while safely routing failures to a DLQ. Built for reliability, observability, and easy extension.

- Sources: eTenders (government) and Eskom (utility)
- Queues: Source (input), Write (success), Failed/DLQ (errors)
- Focus: Speed, resilience, clear monitoring—and future AI enhancements

---

## 📚 Table of Contents

- [✨ Features](#-features)
- [🧭 Architecture (Simple View)](#-architecture-simple-view)
- [💡 Why SQS + Lambda?](#-why-sqs--lambda)
- [🧩 What’s Inside (Project Structure)](#-whats-inside-project-structure)
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
- 🔄 Multi-source support: eTenders and Eskom (easy to add more)
- 🧯 Safe-by-default: DLQ routing, partial-batch success, automatic retries
- 🧭 Observable: structured logs, clear metrics, error context
- ⚡ Fast cold starts: ReadyToRun publishing for quick spin-up
- 🧩 JSON-friendly: case-insensitive and camelCase support

---

## 🧭 Architecture (Simple View)

```
Tender Scrapers
    ↓
Source Queue (AIQueue.fifo)
    ↓
AWS Lambda (AILambda)
    ├─ Message Factory (eTenders, Eskom, …)
    ├─ Message Processor (validate → enrich → tag)
    └─ SQS Service (I/O)
           ├─ Write Queue (WriteQueue.fifo)      ← success
           └─ Failed Queue (FailedQueue.fifo)    ← errors/DLQ
```

---

## 💡 Why SQS + Lambda?

- Resilient by design: retries, visibility timeouts, and DLQs
- Pay-per-use: scales with load, no idle cost
- Simple ops model: logs + metrics in CloudWatch, minimal moving parts
- Easy extension: add new sources via the Message Factory pattern

---

## 🧩 What’s Inside (Project Structure)

```
Sqs_AI_Lambda/
├── Function.cs                     # Lambda entry point with continuous polling
├── Models/
│   ├── TenderMessageBase.cs        # Base model
│   ├── ETenderMessage.cs           # eTenders model
│   ├── EskomTenderMessage.cs       # Eskom model
│   ├── SupportingDocument.cs       # Attachments
│   └── QueueMessage.cs             # Internal SQS wrapper
├── Services/
│   ├── MessageProcessor.cs         # Business pipeline
│   ├── MessageFactory.cs           # Creates typed messages by MessageGroupId
│   └── SqsService.cs               # SQS operations
├── Interfaces/
│   ├── IMessageProcessor.cs
│   ├── IMessageFactory.cs
│   └── ISqsService.cs
├── Converters/
│   └── StringOrNumberConverter.cs  # Flexible type conversion
├── aws-lambda-tools-defaults.json  # Deployment config
└── README.md
```

---

## ⚙️ Configuration

Environment variables:
```bash
SOURCE_QUEUE_URL="https://sqs.us-east-1.amazonaws.com/211635102441/AIQueue.fifo"
WRITE_QUEUE_URL="https://sqs.us-east-1.amazonaws.com/211635102441/WriteQueue.fifo"
FAILED_QUEUE_URL="https://sqs.us-east-1.amazonaws.com/211635102441/FailedQueue.fifo"
```

---

## 🧠 How It Works

1) Ingest (continuous polling, small time budget safety)
```csharp
while (context.RemainingTime > TimeSpan.FromSeconds(30))
{
    var messages = await PollMessagesFromQueue(10);
    if (!messages.Any()) break;
    await ProcessMessageBatch(messages);
}
```

2) Route & Create (by MessageGroupId)
```csharp
var result = groupIdLower switch
{
    "etenderscrape" or "etenderlambda" => CreateETenderMessage(body, groupId),
    "eskomtenderscrape" or "eskomlambda" => CreateEskomTenderMessage(body, groupId),
    _ => HandleUnsupportedMessageGroup(groupId)
};
```

3) Process & Tag
- Adds lifecycle tags (Processed, handler name, UTC timestamp)

4) Output (transactional)
- Success → Write queue + delete from Source
- Error → Failed queue (DLQ) + keep in Source for retry

---

## 📦 Tech Stack

- Runtime: .NET 8 (LTS)
- Compute: AWS Lambda
- Messaging: Amazon SQS (FIFO)
- Serialization: System.Text.Json
- Logging/DI: Microsoft.Extensions.*

NuGet (excerpt):
```xml
<PackageReference Include="Amazon.Lambda.Core" Version="2.5.0" />
<PackageReference Include="Amazon.Lambda.Serialization.SystemTextJson" Version="2.4.4" />
<PackageReference Include="Amazon.Lambda.SQSEvents" Version="2.2.0" />
<PackageReference Include="AWSSDK.SQS" Version="4.0.1.2" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.8" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="9.0.8" />
<PackageReference Include="Microsoft.Extensions.Logging.Console" Version="9.0.8" />
```

---

## 🗂️ Message Models (At a Glance)

Shared fields: title, description, tenderNumber, reference, contact info, province, supportingDocs, tags

- eTenders: id, status, datePublished, dateClosing, url
- Eskom: source, publishedDate?, closingDate?

Base model excerpt:
```csharp
public abstract class TenderMessageBase
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TenderNumber { get; set; } = string.Empty;
    public List<SupportingDocument> SupportingDocs { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public abstract string GetSourceType();
}
```

---

## 📮 Queues

| Queue         | Purpose                 | URL                                 | Groups (examples)                              | Notes                         |
|---------------|-------------------------|--------------------------------------|-------------------------------------------------|-------------------------------|
| Source        | Incoming tender messages| AIQueue.fifo                         | etenderscrape, etenderlambda, eskomtenderscrape, eskomlambda | Retention 5d |
| Write         | Processed messages      | WriteQueue.fifo                      | Source-based grouping                           | Includes processing tags       |
| Failed (DLQ)  | Error recovery          | FailedQueue.fifo                     | Error-type grouping                             | Enriched error info            |

Full URLs:
```bash
SOURCE_QUEUE_URL="https://sqs.us-east-1.amazonaws.com/211635102441/AIQueue.fifo"
WRITE_QUEUE_URL="https://sqs.us-east-1.amazonaws.com/211635102441/WriteQueue.fifo"
FAILED_QUEUE_URL="https://sqs.us-east-1.amazonaws.com/211635102441/FailedQueue.fifo"
```

---

## 📈 Monitoring & Metrics

Watch these:
- Lambda: Invocations, Duration, Errors, Throttles
- SQS: ApproximateNumberOfMessages (queue depth)

Structured log example:
```json
{
  "level": "Information",
  "message": "Processing complete",
  "properties": { "SourceType": "eTenders", "TenderNumber": "RFP-2025-001" }
}
```

Custom metric-style logs:
```csharp
_logger.LogInformation("Batch done - Processed: {Processed}, Failed: {Failed}, Duration: {Duration}ms",
  successCount, failedCount, processingDuration);
```

---

## 🛡️ Error Handling & Recovery

- Automatic retries via visibility timeout + Lambda retry behavior
- DLQ on exhausted retries or non-recoverable failures
- Enriched error payloads for fast diagnosis

Redrive example:
```bash
aws sqs start-message-move-task \
  --source-arn arn:aws:sqs:us-east-1:211635102441:FailedQueue.fifo \
  --destination-arn arn:aws:sqs:us-east-1:211635102441:AIQueue.fifo
```

---

## ⚡ Performance & Scaling

- ReadyToRun to reduce cold starts
- Batch operations for send/delete
- Async/await throughout the pipeline
- Keep JSON compact (camelCase, no indentation)

Scaling:
- Concurrency scales with demand
- Typical: hundreds of messages/minute per Lambda (workload dependent)
- Cost: pay-per-invocation, sub-second billing

---

## 🔒 Security

Data handling:
- TLS in transit, SSE at rest on SQS
- Public tender data only
- Full audit trail via CloudWatch

---

## 🧰 Troubleshooting

- Missing env var
  - Error: SOURCE_QUEUE_URL is required → Set all three queue URLs
- Permission denied
  - AccessDeniedException on SQS → Update IAM role
- Timeouts
  - TaskCanceledException → Increase Lambda timeout or optimize processing
- DLQ growth
  - Investigate error patterns → fix root cause → redrive

Helpful commands:
```bash
aws logs filter-log-events --log-group-name "/aws/lambda/AILambda" --start-time $(date -d '1 hour ago' +%s)000
aws sqs get-queue-attributes --queue-url https://sqs.us-east-1.amazonaws.com/211635102441/AIQueue.fifo --attribute-names All
aws lambda get-function --function-name AILambda
```

---

## 🗺️ Roadmap

- AI: intelligent categorization and summarization

---

> Built with love, bread, and code by **Bread Corporation** 🦆❤️💻