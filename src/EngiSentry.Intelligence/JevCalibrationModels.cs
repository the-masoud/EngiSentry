using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace EngiSentry.Intelligence
{
    [DataContract] public sealed class JevCalibrationPackage { [DataMember(Order=1)] public string SchemaVersion { get; set; } = "J-B-1"; [DataMember(Order=2)] public string CalibrationPackageId { get; set; } = string.Empty; [DataMember(Order=3)] public string DrawingName { get; set; } = string.Empty; [DataMember(Order=4)] public bool AdvisoryOnly { get; set; } = true; [DataMember(Order=5)] public bool ProductionThresholdSelected { get; set; } [DataMember(Order=6)] public List<JevCalibrationCase> Cases { get; set; } = new List<JevCalibrationCase>(); }
    [DataContract] public sealed class JevCalibrationCase { [DataMember(Order=1)] public string EvidenceId { get; set; } = string.Empty; [DataMember(Order=2)] public string InputProjectionSha256 { get; set; } = string.Empty; [DataMember(Order=3)] public string GeometryKind { get; set; } = string.Empty; [DataMember(Order=4, EmitDefaultValue=false)] public string? LayerName { get; set; } [DataMember(Order=5)] public string CurrentDeterministicState { get; set; } = "Ambiguous"; [DataMember(Order=6)] public List<string> ExistingCandidateKinds { get; set; } = new List<string>(); [DataMember(Order=7)] public List<string> NearbyTextFragments { get; set; } = new List<string>(); [DataMember(Order=8)] public List<string> SpatialContext { get; set; } = new List<string>(); [DataMember(Order=9)] public List<string> MeasurementBasisHints { get; set; } = new List<string>(); }
    [DataContract] public sealed class JevEngineerLabels { [DataMember(Order=1)] public string SchemaVersion { get; set; } = "J-B-1"; [DataMember(Order=2)] public string CalibrationPackageId { get; set; } = string.Empty; [DataMember(Order=3)] public List<JevEngineerLabel> Labels { get; set; } = new List<JevEngineerLabel>(); }
    [DataContract] public sealed class JevEngineerLabel { [DataMember(Order=1)] public string EvidenceId { get; set; } = string.Empty; [DataMember(Order=2)] public string InputProjectionSha256 { get; set; } = string.Empty; [DataMember(Order=3, EmitDefaultValue=false)] public string? EngineerLabel { get; set; } [DataMember(Order=4, EmitDefaultValue=false)] public string? Comment { get; set; } }
    public static class JevCalibrationStates { public const string AwaitingEngineerLabels="AWAITING_ENGINEER_LABELS"; public const string PackageContentMismatch="PACKAGE_CONTENT_MISMATCH"; public const string BenchmarkInputChanged="BENCHMARK_INPUT_CHANGED"; public const string BenchmarkComplete="BENCHMARK_COMPLETE"; }
    public sealed class JevCalibrationPackageStore
    {
        public const string SchemaVersion = "J-B-1";
        public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EngiSentry", "artifacts", "qa", "jev-calibration");
        private readonly string root;
        public JevCalibrationPackageStore() : this(Root) { }
        public JevCalibrationPackageStore(string rootOverride) { root = rootOverride ?? throw new ArgumentNullException(nameof(rootOverride)); }
        public JevCalibrationPackage CreateOrReuse(string drawingName, IReadOnlyList<SemanticAmbiguityCase> cases, out string directory)
        {
            JevCalibrationPackage package = Build(drawingName, cases); directory = Path.Combine(root, package.CalibrationPackageId); Directory.CreateDirectory(directory);
            CreateOrMatch(Path.Combine(directory, "calibration-cases.json"), package, package.CalibrationPackageId);
            JevEngineerLabels template = new JevEngineerLabels { CalibrationPackageId = package.CalibrationPackageId, Labels = package.Cases.Select(c => new JevEngineerLabel { EvidenceId=c.EvidenceId, InputProjectionSha256=c.InputProjectionSha256 }).ToList() };
            CreateOrMatch(Path.Combine(directory, "engineer-labels.template.json"), template, package.CalibrationPackageId);
            string instructions = "Copy engineer-labels.template.json to engineer-labels.json. Edit only EngineerLabel and Comment. Do not modify EvidenceId, InputProjectionSha256, or CalibrationPackageId. Labels may remain null. Only labeled rows participate. EngineerLabel and Comment stay local and are never sent to TypeSafe. This does not activate automatic semantic resolution.";
            CreateOrMatchText(Path.Combine(directory, "LABELING_INSTRUCTIONS.md"), instructions);
            return package;
        }
        public static JevCalibrationPackage Build(string drawingName, IReadOnlyList<SemanticAmbiguityCase> cases)
        {
            List<JevCalibrationCase> items=(cases??new List<SemanticAmbiguityCase>()).Select(c=>new JevCalibrationCase { EvidenceId=c.EvidenceId,InputProjectionSha256=JevSemanticAssistPayloadBuilder.ComputeSha256(c),GeometryKind=c.GeometryKind,LayerName=c.LayerName,CurrentDeterministicState=c.CurrentDeterministicState,ExistingCandidateKinds=c.ExistingCandidateKinds,NearbyTextFragments=c.NearbyTextFragments,SpatialContext=c.SpatialContext,MeasurementBasisHints=c.MeasurementBasisHints }).OrderBy(c=>c.EvidenceId,StringComparer.Ordinal).ToList();
            string canonical=SchemaVersion+"\n"+(drawingName??string.Empty)+"\n"+string.Join("\n",items.Select(c=>c.EvidenceId+"|"+c.InputProjectionSha256));
            return new JevCalibrationPackage { CalibrationPackageId=Hash(canonical),DrawingName=drawingName??string.Empty,Cases=items };
        }
        public static List<JevEngineerLabel> LoadValidLabels(string directory,JevCalibrationPackage package)
        {
            string path=Path.Combine(directory,"engineer-labels.json"); if(!File.Exists(path)) return new List<JevEngineerLabel>();
            JevEngineerLabels labels=Read<JevEngineerLabels>(path); if(labels.SchemaVersion!=SchemaVersion||labels.CalibrationPackageId!=package.CalibrationPackageId) throw new InvalidOperationException("PACKAGE_CONTENT_MISMATCH");
            if(labels.Labels.GroupBy(x=>x.EvidenceId,StringComparer.Ordinal).Any(g=>g.Count()!=1)) throw new InvalidOperationException("PACKAGE_CONTENT_MISMATCH");
            Dictionary<string,JevCalibrationCase> expected=package.Cases.ToDictionary(x=>x.EvidenceId,StringComparer.Ordinal);
            foreach(JevEngineerLabel label in labels.Labels){if(!expected.TryGetValue(label.EvidenceId,out JevCalibrationCase? item)||item.InputProjectionSha256!=label.InputProjectionSha256||(!string.IsNullOrWhiteSpace(label.EngineerLabel)&&!JevSemanticLabels.All.Contains(label.EngineerLabel))||(label.Comment?.Length??0)>500)throw new InvalidOperationException("PACKAGE_CONTENT_MISMATCH");}
            return labels.Labels.Where(x=>!string.IsNullOrWhiteSpace(x.EngineerLabel)).OrderBy(x=>x.EvidenceId,StringComparer.Ordinal).ToList();
        }
        private static void CreateOrMatch<T>(string path,T value,string identity){if(File.Exists(path)){T existing=Read<T>(path);if(!string.Equals(Serialize(existing),Serialize(value),StringComparison.Ordinal))throw new InvalidOperationException("PACKAGE_CONTENT_MISMATCH");return;}using(FileStream s=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))new DataContractJsonSerializer(typeof(T)).WriteObject(s,value);}
        private static void CreateOrMatchText(string path,string value){if(File.Exists(path)){if(File.ReadAllText(path)!=value)throw new InvalidOperationException("PACKAGE_CONTENT_MISMATCH");return;}using(FileStream s=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(StreamWriter w=new StreamWriter(s,new UTF8Encoding(false)))w.Write(value);}
        private static T Read<T>(string path){using(FileStream s=File.OpenRead(path))return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(s)!;}
        private static string Serialize<T>(T value){using(MemoryStream s=new MemoryStream()){new DataContractJsonSerializer(typeof(T)).WriteObject(s,value);return Convert.ToBase64String(s.ToArray());}}
        private static string Hash(string value){using(SHA256 h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-",string.Empty);}
    }
}
