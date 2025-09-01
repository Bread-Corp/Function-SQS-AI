using Sqs_AI_Lambda.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sqs_AI_Lambda.Interfaces
{
    public interface IMessageProcessor
    {
        Task<TenderMessageBase> ProcessMessageAsync(TenderMessageBase message);
    }
}
