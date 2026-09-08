using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class PlotPlanStandardsAuthorityReport
    {
        [DataMember(Order = 1)] public StandardAuthorityResolution AuthorityResolution { get; set; } = new StandardAuthorityResolution();
        [DataMember(Order = 2)] public StandardExecutionGateResolution ExecutionGate { get; set; } = new StandardExecutionGateResolution();
    }
}
