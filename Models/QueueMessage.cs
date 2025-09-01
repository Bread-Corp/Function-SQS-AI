using Amazon.SQS.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sqs_AI_Lambda.Models
{
    /// <summary>
    /// Class to represent queue messages
    /// </summary>
    public class QueueMessage
    {
        public string MessageId { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string ReceiptHandle { get; set; } = string.Empty;
        public string MessageGroupId { get; set; } = string.Empty;
        public Dictionary<string, string>? Attributes { get; set; }
        public Dictionary<string, MessageAttributeValue>? MessageAttributes { get; set; }
    }
}
