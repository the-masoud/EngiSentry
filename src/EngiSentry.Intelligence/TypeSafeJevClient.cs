using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

namespace EngiSentry.Intelligence
{
    public sealed class TypeSafeJevClient : IJevSemanticDecisionClient
    {
        private static readonly Uri Endpoint = new Uri("https://api.typesafe.ai/v1/systemone");
        private static readonly DataContractJsonSerializerSettings JsonSettings = new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true };
        private readonly HttpClient client;

        public TypeSafeJevClient(HttpClient? httpClient = null)
        {
            client = httpClient ?? new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(15);
        }

        public async Task<JevSystemOneResponse> DecideAsync(JevSystemOneRequest request, string apiKey)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(JevWireRequest), JsonSettings).WriteObject(stream, new JevWireRequest { Model = "jev-latest", State = request.State, Questions = request.Questions });
                using (HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, Endpoint))
                {
                    message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                    message.Content = new StringContent(Encoding.UTF8.GetString(stream.ToArray()), Encoding.UTF8, "application/json");
                    HttpResponseMessage response;
                    try { response = await client.SendAsync(message).ConfigureAwait(false); }
                    catch (TaskCanceledException exception) { throw new JevTransportException("timeout", exception); }
                    catch (HttpRequestException exception) { throw new JevTransportException("network", exception); }

                    string? category = ErrorCategory(response.StatusCode);
                    if (category != null) throw new JevTransportException(category);
                    if (!response.IsSuccessStatusCode) throw new JevTransportException("network");

                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    try
                    {
                        using (MemoryStream input = new MemoryStream(Encoding.UTF8.GetBytes(body)))
                        {
                            JevWireResponse wire = (JevWireResponse)new DataContractJsonSerializer(typeof(JevWireResponse), JsonSettings).ReadObject(input)!;
                            return new JevSystemOneResponse { Model = wire.Model ?? string.Empty, RequestId = wire.RequestId, Answers = wire.Answers ?? new Dictionary<string, JevChoiceAnswer>() };
                        }
                    }
                    catch (Exception exception) { throw new JevInvalidResponseException(exception); }
                }
            }
        }

        private static string? ErrorCategory(HttpStatusCode status)
        {
            int code = (int)status;
            if (code == 401 || code == 403 || code == 422 || code == 429 || code == 529) return "http-" + code;
            return code >= 500 && code <= 599 ? "http-5xx" : null;
        }

        [DataContract]
        private sealed class JevWireRequest
        {
            [DataMember(Name = "model")] public string Model { get; set; } = string.Empty;
            [DataMember(Name = "state")] public Dictionary<string, SemanticAmbiguityCase> State { get; set; } = new Dictionary<string, SemanticAmbiguityCase>();
            [DataMember(Name = "questions")] public Dictionary<string, JevChoiceQuestion> Questions { get; set; } = new Dictionary<string, JevChoiceQuestion>();
        }

        [DataContract]
        private sealed class JevWireResponse
        {
            [DataMember(Name = "model")] public string? Model { get; set; }
            [DataMember(Name = "request_id")] public string? RequestId { get; set; }
            [DataMember(Name = "answers")] public Dictionary<string, JevChoiceAnswer>? Answers { get; set; }
        }
    }

    public sealed class JevTransportException : Exception
    {
        public JevTransportException(string category, Exception? inner = null) : base(category, inner) { Category = category; }
        public string Category { get; }
    }

    public sealed class JevInvalidResponseException : Exception
    {
        public JevInvalidResponseException(Exception inner) : base("invalid-response", inner) { }
    }
}
