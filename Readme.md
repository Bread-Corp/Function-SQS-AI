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
- [📦 Deployment](#-deployment)
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

## 📦 Deployment

This section covers three deployment methods for the AI Lambda Function (Tender Summarization Service). Choose the method that best fits your workflow and infrastructure preferences.

### 🛠️ Prerequisites

Before deploying, ensure you have:
- AWS CLI configured with appropriate credentials 🔑
- .NET 8 SDK installed locally
- AWS SAM CLI installed (for SAM deployment)
- Access to AWS Lambda, SQS, Bedrock, Parameter Store, and CloudWatch services ☁️
- Visual Studio 2022 or VS Code with C# extensions (for AWS Toolkit deployment)
- Claude 3 Sonnet model access in AWS Bedrock

### 🎯 Method 1: AWS Toolkit Deployment

Deploy directly through Visual Studio using the AWS Toolkit extension.

#### Setup Steps:
1. **Install AWS Toolkit** for Visual Studio 2022
2. **Configure AWS Profile** with your credentials in Visual Studio
3. **Open Solution** containing `Sqs_AI_Lambda.csproj`

#### Deploy Process:
1. **Right-click** the project in Solution Explorer
2. **Select** "Publish to AWS Lambda" from the context menu
3. **Configure Lambda Settings**:
   - Function Name: `AILambda`
   - Runtime: `.NET 8`
   - Handler: `Sqs_AI_Lambda::Sqs_AI_Lambda.Function::FunctionHandler`
   - Memory: `512 MB`
   - Timeout: `900 seconds` (15 minutes)
4. **Set Environment Variables**:
   ```
   FAILED_QUEUE_URL=https://sqs.{region}.amazonaws.com/{account-id}/FailedQueue.fifo
   SOURCE_QUEUE_URL=https://sqs.{region}.amazonaws.com/{account-id}/AIQueue.fifo
   TAG_QUEUE_URL=https://sqs.{region}.amazonaws.com/{account-id}/TagQueue.fifo
   ```
5. **Configure IAM Role** with required permissions:
   - SQS: `ReceiveMessage`, `DeleteMessage`, `GetQueueAttributes`, `SendMessage`
   - Bedrock: `bedrock:InvokeModel` for Claude 3 Sonnet
   - Parameter Store: `ssm:GetParameter` on `/TenderSummary/Prompts/*`
   - CloudWatch Logs: Standard logging permissions
6. **Set up SQS Trigger** manually after deployment

#### Parameter Store Setup:
Create these parameters before deployment:
```bash
# System prompt for Claude 3 Sonnet
aws ssm put-parameter \
    --name "/TenderSummary/Prompts/System" \
    --value "You are an AI assistant specialized in analyzing South African tender documents..." \
    --type "String"

# Source-specific prompts
aws ssm put-parameter \
    --name "/TenderSummary/Prompts/eTenders" \
    --value "Focus on government procurement requirements..." \
    --type "String"

aws ssm put-parameter \
    --name "/TenderSummary/Prompts/Eskom" \
    --value "Emphasize power generation and electrical infrastructure..." \
    --type "String"

aws ssm put-parameter \
    --name "/TenderSummary/Prompts/Transnet" \
    --value "Highlight logistics and transportation aspects..." \
    --type "String"

aws ssm put-parameter \
    --name "/TenderSummary/Prompts/SARS" \
    --value "Focus on tax administration and revenue services..." \
    --type "String"

aws ssm put-parameter \
    --name "/TenderSummary/Prompts/SANRAL" \
    --value "Emphasize road infrastructure and highway projects..." \
    --type "String"
```

#### Post-Deployment:
- Test the function using the AWS Toolkit test feature
- Monitor logs through CloudWatch integration
- Verify Bedrock integration and prompt retrieval
- Test SQS message processing

### 🚀 Method 2: SAM Deployment

Use AWS SAM for infrastructure-as-code deployment with the provided template.

#### Initial Setup:
```bash
# Install AWS SAM CLI
pip install aws-sam-cli

# Install .NET 8 SDK
# Download from https://dotnet.microsoft.com/download/dotnet/8.0

# Verify installations
sam --version
dotnet --version
```

#### Build and Deploy:
```bash
# Build the .NET 8 application
dotnet build -c Release

# Build the SAM application
sam build

# Deploy with guided configuration (first time)
sam deploy --guided

# Follow the prompts:
# Stack Name: ai-lambda-stack
# AWS Region: us-east-1 (or your preferred region)
# Parameter FailedQueueURL: https://sqs.{region}.amazonaws.com/{account-id}/FailedQueue.fifo
# Parameter SourceQueueURL: https://sqs.{region}.amazonaws.com/{account-id}/AIQueue.fifo
# Parameter TagQueueURL: https://sqs.{region}.amazonaws.com/{account-id}/TagQueue.fifo
# Confirm changes before deploy: Y
# Allow SAM to create IAM roles: Y
# Save parameters to samconfig.toml: Y
```

#### Environment Variables Setup:
The template already includes the required environment variables:

```yaml
# Already configured in AILambda.yaml
Environment:
  Variables:
    FAILED_QUEUE_URL: https://sqs.us-east-1.amazonaws.com/{account-id}/FailedQueue.fifo
    SOURCE_QUEUE_URL: https://sqs.us-east-1.amazonaws.com/{account-id}/AIQueue.fifo
    TAG_QUEUE_URL: https://sqs.us-east-1.amazonaws.com/{account-id}/TagQueue.fifo
```

#### Pre-Deployment Parameter Store Setup:
```bash
# Create all required prompts before deploying
./scripts/setup-parameters.sh  # Create this script with parameter creation commands
```

#### Subsequent Deployments:
```bash
# Quick deployment after initial setup
dotnet build -c Release
sam build && sam deploy
```

#### Local Testing with SAM:
```bash
# Test function locally (requires Docker)
sam local invoke AILambda

# Start local SQS simulation
sam local start-api
```

#### SAM Deployment Advantages:
- ✅ Complete infrastructure management including SQS queues
- ✅ Environment variables defined in template
- ✅ IAM permissions automatically configured for Bedrock and Parameter Store
- ✅ Easy rollback capabilities
- ✅ CloudFormation integration
- ✅ SQS trigger automatically configured

### 🔄 Method 3: Workflow Deployment (CI/CD)

Automated deployment using GitHub Actions workflow for production environments.

#### Setup Requirements:
1. **GitHub Repository Secrets**:
   ```
   AWS_ACCESS_KEY_ID: Your AWS access key
   AWS_SECRET_ACCESS_KEY: Your AWS secret key
   AWS_REGION: us-east-1 (or your target region)
   ```

2. **Pre-existing Lambda Function**: The workflow updates an existing function, so deploy initially using Method 1 or 2.

3. **Parameter Store Setup**: Ensure all prompts are configured in Parameter Store.

#### Deployment Process:
1. **Create Release Branch**:
   ```bash
   # Create and switch to release branch
   git checkout -b release
   
   # Make your changes to the .NET code
   # Commit changes
   git add .
   git commit -m "feat: update AI prompt processing logic"
   
   # Push to trigger deployment
   git push origin release
   ```

2. **Automatic Deployment**: The workflow will:
   - Checkout the code
   - Set up .NET 8 SDK
   - Install AWS Lambda Tools
   - Build and package the Lambda function using `Sqs_AI_Lambda.csproj`
   - Configure AWS credentials
   - Update the existing Lambda function code
   - Maintain existing configuration (environment variables, triggers, etc.)

#### Manual Trigger:
You can also trigger deployment manually:
1. Go to **Actions** tab in your GitHub repository
2. Select **"Deploy .NET Lambda to AWS"** workflow
3. Click **"Run workflow"**
4. Choose the `release` branch
5. Click **"Run workflow"** button

#### Workflow Deployment Advantages:
- ✅ Automated CI/CD pipeline
- ✅ Consistent deployment process
- ✅ Audit trail of deployments
- ✅ Easy rollback to previous commits
- ✅ No local environment dependencies
- ✅ Automatic .NET build and packaging

### 🔧 Post-Deployment Configuration

Regardless of deployment method, verify the following:

#### Environment Variables Verification:
Ensure these environment variables are properly set:

```bash
# Verify environment variables via AWS CLI
aws lambda get-function-configuration \
    --function-name AILambda \
    --query 'Environment.Variables'
```

Expected output:
```json
{
    "FAILED_QUEUE_URL": "https://sqs.us-east-1.amazonaws.com/{account-id}/FailedQueue.fifo",
    "SOURCE_QUEUE_URL": "https://sqs.us-east-1.amazonaws.com/{account-id}/AIQueue.fifo",
    "TAG_QUEUE_URL": "https://sqs.us-east-1.amazonaws.com/{account-id}/TagQueue.fifo"
}
```

#### Parameter Store Verification:
Verify all required prompts exist:

```bash
# Check system prompt
aws ssm get-parameter --name "/TenderSummary/Prompts/System"

# Check source-specific prompts
aws ssm get-parameter --name "/TenderSummary/Prompts/eTenders"
aws ssm get-parameter --name "/TenderSummary/Prompts/Eskom"
aws ssm get-parameter --name "/TenderSummary/Prompts/Transnet"
aws ssm get-parameter --name "/TenderSummary/Prompts/SARS"
aws ssm get-parameter --name "/TenderSummary/Prompts/SANRAL"
```

#### Bedrock Model Access:
Verify Claude 3 Sonnet access:

```bash
# List available foundation models
aws bedrock list-foundation-models --region us-east-1

# Test model access (if needed)
aws bedrock invoke-model \
    --model-id anthropic.claude-3-sonnet-20240229-v1:0 \
    --body '{"messages":[{"role":"user","content":"Test"}],"max_tokens":100}' \
    --cli-binary-format raw-in-base64-out \
    response.json
```

#### SQS Trigger Configuration:
Ensure the SQS trigger is properly configured:

```bash
# List event source mappings
aws lambda list-event-source-mappings \
    --function-name AILambda

# Verify batch size and queue configuration
aws lambda get-event-source-mapping \
    --uuid [event-source-mapping-uuid]
```

### 🧪 Testing Your Deployment

After deployment, test the function thoroughly:

#### Test AI Processing:
```bash
# Send test message to source queue
aws sqs send-message \
    --queue-url https://sqs.us-east-1.amazonaws.com/{account-id}/AIQueue.fifo \
    --message-body '{
        "title": "Test Tender for Infrastructure Development",
        "description": "This is a test tender for AI processing",
        "tenderNumber": "TEST-001",
        "source": "eTenders"
    }' \
    --message-group-id "etenderscrape" \
    --message-deduplication-id "test-$(date +%s)"

# Monitor function execution
aws logs tail /aws/lambda/AILambda --follow
```

#### Test Parameter Store Integration:
```bash
# Invoke function to test prompt retrieval
aws lambda invoke \
    --function-name AILambda \
    --payload '{"Records":[]}' \
    response.json

# Check response for prompt loading logs
cat response.json
```

#### Expected Success Indicators:
- ✅ Function executes without errors
- ✅ CloudWatch logs show successful prompt retrieval from Parameter Store
- ✅ Bedrock integration working (AI summaries generated)
- ✅ Messages properly routed to TagQueue with AI summaries
- ✅ No timeout or memory errors
- ✅ Proper error handling for failed messages
- ✅ SQS batch processing functioning correctly

### 🔍 Monitoring and Maintenance

#### CloudWatch Metrics to Monitor:
- **Duration**: Function execution time for AI processing
- **Error Rate**: Failed AI generation operations
- **Memory Utilization**: RAM usage during Bedrock calls
- **SQS Metrics**: Message processing rates and queue depths
- **Bedrock Metrics**: Model invocation success rates and latencies
- **Parameter Store**: GetParameter API call rates

#### Log Analysis:
```bash
# View recent logs
aws logs tail /aws/lambda/AILambda --follow

# Search for AI processing statistics
aws logs filter-log-events \
    --log-group-name /aws/lambda/AILambda \
    --filter-pattern "AI-Enhanced"

# Search for prompt loading issues
aws logs filter-log-events \
    --log-group-name /aws/lambda/AILambda \
    --filter-pattern "Prompt loading"

# Monitor Bedrock integration
aws logs filter-log-events \
    --log-group-name /aws/lambda/AILambda \
    --filter-pattern "Bedrock"
```

### 🚨 Troubleshooting Deployments

<details>
<summary><strong>.NET 8 Runtime Issues</strong></summary>

**Issue**: Function fails to start or throws runtime errors

**Solution**: Ensure proper .NET 8 configuration:
- Verify the handler path: `Sqs_AI_Lambda::Sqs_AI_Lambda.Function::FunctionHandler`
- Check that all NuGet packages are compatible with .NET 8
- Ensure the project targets `net8.0` framework
- Verify all dependencies are included in the deployment package
</details>

<details>
<summary><strong>Bedrock Access Issues</strong></summary>

**Issue**: Cannot access Claude 3 Sonnet model

**Solution**: Verify Bedrock configuration:
- Ensure the IAM role has `bedrock:InvokeModel` permissions
- Check that Claude 3 Sonnet is available in your region
- Verify model ARN: `anthropic.claude-3-sonnet-20240229-v1:0`
- Test model access using AWS CLI
- Check Bedrock service quotas and limits
</details>

<details>
<summary><strong>Parameter Store Access Failures</strong></summary>

**Issue**: Cannot retrieve prompts from Parameter Store

**Solution**: Debug Parameter Store configuration:
- Verify IAM role has `ssm:GetParameter` permissions on `/TenderSummary/Prompts/*`
- Check that all required parameters exist
- Test parameter retrieval using AWS CLI
- Ensure parameter names match exactly (case-sensitive)
- Verify parameter type is "String"
</details>

<details>
<summary><strong>SQS Message Processing Issues</strong></summary>

**Issue**: Messages not being processed or routed incorrectly

**Solution**: Debug SQS configuration:
- Verify SQS trigger is configured with correct batch size (10)
- Check message format matches expected JSON structure
- Ensure FIFO queue attributes are properly set
- Verify MessageGroupId routing logic
- Monitor dead letter queue for failed messages
</details>

<details>
<summary><strong>Memory and Performance Issues</strong></summary>

**Issue**: Function runs out of memory or times out

**Solution**: Optimize function performance:
- Increase memory allocation (current: 512 MB)
- Optimize AI processing batch size
- Review Bedrock API call patterns
- Monitor prompt caching effectiveness
- Consider implementing connection pooling
</details>

<details>
<summary><strong>Environment Variables Missing</strong></summary>

**Issue**: Function cannot access required queue URLs

**Solution**: Set environment variables using AWS CLI:
```bash
aws lambda update-function-configuration \
    --function-name AILambda \
    --environment Variables='{
        "FAILED_QUEUE_URL":"https://sqs.{region}.amazonaws.com/{account-id}/FailedQueue.fifo",
        "SOURCE_QUEUE_URL":"https://sqs.{region}.amazonaws.com/{account-id}/AIQueue.fifo",
        "TAG_QUEUE_URL":"https://sqs.{region}.amazonaws.com/{account-id}/TagQueue.fifo"
    }'
```
</details>

<details>
<summary><strong>Workflow Deployment Fails</strong></summary>

**Issue**: GitHub Actions workflow errors

**Solution**: 
- Check repository secrets are correctly configured
- Verify .NET 8 SDK is properly installed in workflow
- Ensure AWS Lambda Tools installation succeeds
- Check that `Sqs_AI_Lambda.csproj` exists in repository
- Verify target Lambda function exists in AWS
- Monitor workflow logs for specific error messages
</details>

Choose the deployment method that best fits your development workflow and infrastructure requirements. SAM deployment is recommended for development environments, while workflow deployment excels for production systems requiring automated CI/CD pipelines with AI model integration.

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
