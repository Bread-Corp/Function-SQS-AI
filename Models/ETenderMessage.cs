using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Sqs_AI_Lambda.Models
{
    public class ETenderMessage : TenderMessageBase
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("datePublished")]
        public DateTime DatePublished { get; set; }

        [JsonPropertyName("dateClosing")]
        public DateTime DateClosing { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("supportingDocs")]
        public new List<SupportingDocument> SupportingDocs { get; set; } = new();

        public override string GetSourceType() => "eTenders";
    }
}
