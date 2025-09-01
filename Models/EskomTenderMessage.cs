using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Sqs_AI_Lambda.Models
{
    public class EskomTenderMessage : TenderMessageBase
    {
        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;

        [JsonPropertyName("publishedDate")]
        public DateTime? PublishedDate { get; set; }

        [JsonPropertyName("closingDate")]
        public DateTime? ClosingDate { get; set; }

        public override string GetSourceType() => "Eskom";
    }
}
