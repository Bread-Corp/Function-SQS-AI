using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sqs_AI_Lambda.Interfaces
{
    public interface ISqsService
    {
        Task SendMessageAsync(string queueUrl, object message);
        Task SendMessageBatchAsync(string queueUrl, List<object> messages);
        Task DeleteMessageAsync(string queueUrl, string receiptHandle);
        Task DeleteMessageBatchAsync(string queueUrl, List<(string id, string receiptHandle)> messages);
    }
}
