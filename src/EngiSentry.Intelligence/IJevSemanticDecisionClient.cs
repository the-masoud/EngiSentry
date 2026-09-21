using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace EngiSentry.Intelligence
{
    public interface IJevSemanticDecisionClient
    {
        Task<JevSystemOneResponse> DecideAsync(JevSystemOneRequest request, string apiKey);
    }

    [DataContract]
    public sealed class JevSystemOneRequest
    {
        [DataMember(Name = "state")]
        public Dictionary<string, SemanticAmbiguityCase> State { get; set; } = new Dictionary<string, SemanticAmbiguityCase>();

        [DataMember(Name = "questions")]
        public Dictionary<string, JevChoiceQuestion> Questions { get; set; } = new Dictionary<string, JevChoiceQuestion>();
    }

    [DataContract]
    public sealed class JevChoiceQuestion
    {
        [DataMember(Name = "type")]
        public string Type { get; set; } = "choice";

        [DataMember(Name = "instructions")]
        public string Instructions { get; set; } = string.Empty;

        [DataMember(Name = "criteria")]
        public Dictionary<string, string> Criteria { get; set; } = new Dictionary<string, string>();
    }

    [DataContract]
    public sealed class JevSystemOneResponse
    {
        [DataMember(Name = "model")]
        public string Model { get; set; } = string.Empty;

        [DataMember(Name = "request_id")]
        public string? RequestId { get; set; }

        [DataMember(Name = "answers")]
        public Dictionary<string, JevChoiceAnswer> Answers { get; set; } = new Dictionary<string, JevChoiceAnswer>();
    }

    [DataContract]
    public sealed class JevChoiceAnswer
    {
        [DataMember(Name = "type")]
        public string Type { get; set; } = string.Empty;

        [DataMember(Name = "choice")]
        public string Choice { get; set; } = string.Empty;

        [DataMember(Name = "probabilities")]
        public Dictionary<string, double> Probabilities { get; set; } = new Dictionary<string, double>();

        [DataMember(Name = "confidence")]
        public double Confidence { get; set; }
    }
}
