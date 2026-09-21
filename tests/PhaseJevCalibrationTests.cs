using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EngiSentry.Contracts;
using EngiSentry.Intelligence;

namespace EngiSentry.Tests
{
    internal static class PhaseJevCalibrationTests
    {
        private static int assertions;

        internal static void Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "EngiSentry-JB-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                List<SemanticAmbiguityCase> cases = Cases(5);
                JevCalibrationPackage package = JevCalibrationPackageStore.Build("DRAWING_SECRET_MARKER_JB.dwg", cases);
                TestPackageIdentity(package, cases);
                TestLabels(root, package);
                TestGeneratedReuse(root, cases);
                JevCalibrationBenchmarkReport report = TestMetrics(package, cases);
                TestModelHandling(package, cases);
                TestReportStore(root, report);
                TestExecution(root, package, cases);
                TestPrivacy(package, cases, report);
                TestIsolation(package, cases);
                Console.WriteLine("Phase J-B focused assertions passed: " + assertions);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static void TestPackageIdentity(JevCalibrationPackage package, List<SemanticAmbiguityCase> cases)
        {
            JevCalibrationPackage reversed = JevCalibrationPackageStore.Build(package.DrawingName, cases.AsEnumerable().Reverse().ToList());
            Assert(package.CalibrationPackageId == reversed.CalibrationPackageId, "Package identity depends on input order.");
            Assert(package.Cases.Select(x => x.EvidenceId).SequenceEqual(cases.Select(x => x.EvidenceId).OrderBy(x => x, StringComparer.Ordinal)), "Package cases are unordered.");
            SemanticAmbiguityCase changed = Cases(1)[0]; changed.GeometryKind = "Changed";
            Assert(package.CalibrationPackageId != JevCalibrationPackageStore.Build(package.DrawingName, new[] { changed }.Concat(cases.Skip(1)).ToList()).CalibrationPackageId, "Projection change retained package identity.");
        }

        private static void TestLabels(string root, JevCalibrationPackage package)
        {
            string directory = Path.Combine(root, "labels"); Directory.CreateDirectory(directory);
            foreach (string label in JevSemanticLabels.All)
            {
                WriteLabels(directory, package, new List<JevEngineerLabel> { Label(package.Cases[0], label) });
                Assert(JevCalibrationPackageStore.LoadValidLabels(directory, package).Single().EngineerLabel == label, "Allowed label rejected.");
            }
            WriteLabels(directory, package, new List<JevEngineerLabel> { Label(package.Cases[0], null), Label(package.Cases[1], JevSemanticLabels.Unknown) });
            Assert(JevCalibrationPackageStore.LoadValidLabels(directory, package).Count == 1, "Null/partial labels are invalid.");
            ExpectPackageMismatch(() => { WriteLabels(directory, package, new List<JevEngineerLabel> { Label(package.Cases[0], "BAD") }); JevCalibrationPackageStore.LoadValidLabels(directory, package); });
            ExpectPackageMismatch(() => { JevEngineerLabel row=Label(package.Cases[0],JevSemanticLabels.Unknown); WriteLabels(directory,package,new List<JevEngineerLabel>{row,row}); JevCalibrationPackageStore.LoadValidLabels(directory,package); });
            ExpectPackageMismatch(() => { JevEngineerLabel row=Label(package.Cases[0],JevSemanticLabels.Unknown);row.EvidenceId="unknown";WriteLabels(directory,package,new List<JevEngineerLabel>{row});JevCalibrationPackageStore.LoadValidLabels(directory,package); });
            ExpectPackageMismatch(() => { JevEngineerLabel row=Label(package.Cases[0],JevSemanticLabels.Unknown);row.InputProjectionSha256="bad";WriteLabels(directory,package,new List<JevEngineerLabel>{row});JevCalibrationPackageStore.LoadValidLabels(directory,package); });
            ExpectPackageMismatch(() => { WriteLabels(directory,package,new List<JevEngineerLabel>{Label(package.Cases[0],JevSemanticLabels.Unknown)},"wrong");JevCalibrationPackageStore.LoadValidLabels(directory,package); });
            ExpectPackageMismatch(() => { JevEngineerLabel row=Label(package.Cases[0],JevSemanticLabels.Unknown);row.Comment=new string('x',501);WriteLabels(directory,package,new List<JevEngineerLabel>{row});JevCalibrationPackageStore.LoadValidLabels(directory,package); });
        }

        private static void TestGeneratedReuse(string root, List<SemanticAmbiguityCase> cases)
        {
            string storeRoot=Path.Combine(root,"reuse");var store=new JevCalibrationPackageStore(storeRoot);store.CreateOrReuse("drawing.dwg",cases,out string directory);byte[] before=File.ReadAllBytes(Path.Combine(directory,"calibration-cases.json"));store.CreateOrReuse("drawing.dwg",cases,out _);Assert(before.SequenceEqual(File.ReadAllBytes(Path.Combine(directory,"calibration-cases.json"))),"Exact package was rewritten.");
            string casesPath=Path.Combine(directory,"calibration-cases.json");JevCalibrationPackage altered=JevCalibrationPackageStore.Build("drawing.dwg",cases);altered.DrawingName="changed";Write(casesPath,altered);ExpectPackageMismatch(()=>store.CreateOrReuse("drawing.dwg",cases,out _));Write(casesPath,JevCalibrationPackageStore.Build("drawing.dwg",cases));
            string templatePath=Path.Combine(directory,"engineer-labels.template.json");JevEngineerLabels template=Read<JevEngineerLabels>(templatePath);template.Labels[0].Comment="changed";Write(templatePath,template);ExpectPackageMismatch(()=>store.CreateOrReuse("drawing.dwg",cases,out _));template.Labels[0].Comment=null;Write(templatePath,template);
            string instructions=Path.Combine(directory,"LABELING_INSTRUCTIONS.md");File.AppendAllText(instructions,"changed");ExpectPackageMismatch(()=>store.CreateOrReuse("drawing.dwg",cases,out _));
        }

        private static JevCalibrationBenchmarkReport TestMetrics(JevCalibrationPackage package,List<SemanticAmbiguityCase> cases)
        {
            string[] engineer={JevSemanticLabels.AccessRoadEdge,JevSemanticLabels.SecurityFence,JevSemanticLabels.UnitBoundary,JevSemanticLabels.Unrelated,JevSemanticLabels.Unknown};
            var labels=package.Cases.Select((x,i)=>Label(x,engineer[i])).ToList();
            var results=new List<JevSemanticAssistResult>{Result("e0",JevSemanticAdvisoryStates.Classified,JevSemanticLabels.AccessRoadEdge,.49,"model-a"),Result("e1",JevSemanticAdvisoryStates.Classified,JevSemanticLabels.UnitBoundary,.50,"model-a"),Result("e2",JevSemanticAdvisoryStates.Classified,JevSemanticLabels.Unknown,.70,"model-a"),Result("e3",JevSemanticAdvisoryStates.Unavailable,null,null,null),Result("e4",JevSemanticAdvisoryStates.InvalidResponse,null,null,null)};
            var report=JevCalibrationBenchmarkRunner.Create(package,labels,new JevSemanticAdvisorySidecar{Results=results},"cases","labels");
            Assert(report.LabeledSampleCount==5,"Labeled count wrong.");Assert(report.ValidPredictionCount==3,"Valid count wrong.");Assert(report.UnavailablePredictionCount==1,"Unavailable count wrong.");Assert(report.InvalidPredictionCount==1,"Invalid count wrong.");Assert(report.ExactMatchCount==1,"Match count wrong.");Assert(report.IncorrectCount==2,"Incorrect count wrong.");Assert(report.UnknownPredictionCount==1,"Unknown count wrong.");Assert(report.Coverage==.6,"Coverage wrong.");Assert(report.ExactMatchRate==.2,"Match rate wrong.");Assert(report.ClassifiedAccuracy==1d/3d,"Accuracy wrong.");
            Assert(report.ConfusionMatrix.Count==5&&report.ConfusionMatrix.Select(x=>x.EngineerLabel).SequenceEqual(JevSemanticLabels.All)&&report.ConfusionMatrix.All(x=>x.Counts.Count==5),"Matrix shape/order wrong.");Assert(report.ConfusionMatrix.Sum(x=>x.Counts.Sum())==3,"Non-classified result entered matrix.");
            Assert(report.ConfidenceBuckets.Single(x=>x.Range=="[0.00, 0.50)").SampleCount==1,"0.49 bucket wrong.");Assert(report.ConfidenceBuckets.Single(x=>x.Range=="[0.50, 0.70)").SampleCount==1,"0.50 bucket wrong.");Assert(report.ConfidenceBuckets.Single(x=>x.Range=="[0.70, 0.80)").SampleCount==1,"0.70 bucket wrong.");Assert(report.ConfidenceBuckets.Any(x=>x.SampleCount==0&&x.Accuracy==null),"Empty bucket accuracy wrong.");
            Assert(report.ThresholdObservations.Select(x=>x.Threshold).SequenceEqual(new[]{.5,.6,.7,.8,.9,.95}),"Threshold order wrong.");Assert(report.ThresholdObservations.Single(x=>x.Threshold==.5).EligibleCount==2,"0.50 observation wrong.");Assert(report.ThresholdObservations.Single(x=>x.Threshold==.95).EligibleAccuracy==null,"0.95 empty accuracy wrong.");Assert(!report.ProductionThresholdSelected&&report.ThresholdStatus=="NO_PRODUCTION_THRESHOLD_SELECTED","Threshold selected.");
            Assert(report.PerLabelSummary.Count==5&&report.PerLabelSummary.Select(x=>x.Label).SequenceEqual(JevSemanticLabels.All),"Per-label order wrong.");Assert(report.PerLabelSummary[0].Support==1&&report.PerLabelSummary[0].PredictedCount==1&&report.PerLabelSummary[0].CorrectCount==1,"Per-label counts wrong.");
            JevCalibrationBenchmarkReport zero=JevCalibrationBenchmarkRunner.Create(package,new List<JevEngineerLabel>(),new JevSemanticAdvisorySidecar(),"c","l");Assert(zero.Coverage==null&&zero.ExactMatchRate==null&&zero.ClassifiedAccuracy==null,"Zero denominator is not null.");Assert(AllFinite(report),"Metric contains NaN/Infinity.");
            return report;
        }

        private static void TestModelHandling(JevCalibrationPackage package,List<SemanticAmbiguityCase> cases)
        {
            var labels=new List<JevEngineerLabel>{Label(package.Cases[0],JevSemanticLabels.Unknown)};
            Assert(JevCalibrationBenchmarkRunner.Create(package,labels,new JevSemanticAdvisorySidecar{Results=new List<JevSemanticAssistResult>{Result("e0",JevSemanticAdvisoryStates.Classified,JevSemanticLabels.Unknown,1,"one")}},"c","l").Model=="one","Single model lost.");
            Assert(JevCalibrationBenchmarkRunner.Create(package,labels,new JevSemanticAdvisorySidecar{Results=new List<JevSemanticAssistResult>{Result("e0",JevSemanticAdvisoryStates.Unavailable,null,null,null)}},"c","l").Model==null,"Absent model invented.");
            bool mismatch=false;try{JevCalibrationBenchmarkRunner.Create(package,new[]{labels[0],Label(package.Cases[1],JevSemanticLabels.Unknown)},new JevSemanticAdvisorySidecar{Results=new List<JevSemanticAssistResult>{Result("e0",JevSemanticAdvisoryStates.Classified,JevSemanticLabels.Unknown,1,"one"),Result("e1",JevSemanticAdvisoryStates.Classified,JevSemanticLabels.Unknown,1,"two")}},"c","l");}catch(InvalidOperationException e){mismatch=e.Message=="BENCHMARK_MODEL_MISMATCH";}Assert(mismatch,"Model mismatch accepted.");
        }

        private static void TestReportStore(string root,JevCalibrationBenchmarkReport report)
        {
            string dir=Path.Combine(root,"reports");Directory.CreateDirectory(dir);Assert(JevCalibrationBenchmarkStore.Check(dir,report.CalibrationPackageId,report.CalibrationCasesSha256,report.EngineerLabelsSha256).Status=="NO_EXISTING_REPORT","Absent report status wrong.");JevCalibrationBenchmarkStore.ReadOrWrite(dir,report);string path=Path.Combine(dir,"benchmark-report.json");byte[] before=File.ReadAllBytes(path);Assert(JevCalibrationBenchmarkStore.Check(dir,report.CalibrationPackageId,report.CalibrationCasesSha256,report.EngineerLabelsSha256).Status=="EXISTING_REPORT_MATCH","Matching report rejected.");JevCalibrationBenchmarkStore.ReadOrWrite(dir,report);Assert(before.SequenceEqual(File.ReadAllBytes(path)),"Matching report rewritten.");ExpectInputChanged(()=>JevCalibrationBenchmarkStore.Check(dir,report.CalibrationPackageId,report.CalibrationCasesSha256,"changed"));ExpectInputChanged(()=>JevCalibrationBenchmarkStore.Check(dir,report.CalibrationPackageId,"changed",report.EngineerLabelsSha256));ExpectInputChanged(()=>JevCalibrationBenchmarkStore.Check(dir,"changed",report.CalibrationCasesSha256,report.EngineerLabelsSha256));
        }

        private static void TestExecution(string root,JevCalibrationPackage package,List<SemanticAmbiguityCase> cases)
        {
            string dir=Path.Combine(root,"execution");Directory.CreateDirectory(dir);Write(Path.Combine(dir,"calibration-cases.json"),package);var labels=package.Cases.Select(x=>Label(x,JevSemanticLabels.Unknown)).ToList();WriteLabels(dir,package,labels);var service=new JevCalibrationBenchmarkExecutionService();var fake=new CountingClient();Assert(service.Execute(dir,package,labels,cases,JevSemanticAssistPolicy.ForTests(false,true),()=>fake).Status==JevSemanticAdvisoryStates.Disabled&&fake.CallCount==0,"Disabled policy called transport.");Assert(service.Execute(dir,package,labels,cases,JevSemanticAssistPolicy.ForTests(true,false),()=>fake).Status==JevSemanticAdvisoryStates.NotConfigured&&fake.CallCount==0,"Missing key called transport.");var complete=service.Execute(dir,package,labels,cases,JevSemanticAssistPolicy.ForTests(true,true),()=>fake);Assert(complete.Status==JevCalibrationStates.BenchmarkComplete&&fake.CallCount==1,"Configured execution did not call once.");Assert(service.Execute(dir,package,labels,cases,JevSemanticAssistPolicy.ForTests(true,true),()=>fake).Status=="EXISTING_REPORT_MATCH"&&fake.CallCount==1,"Matching report called transport.");File.AppendAllText(Path.Combine(dir,"engineer-labels.json")," ");Assert(service.Execute(dir,package,labels,cases,JevSemanticAssistPolicy.ForTests(true,true),()=>fake).Status==JevCalibrationStates.BenchmarkInputChanged&&fake.CallCount==1,"Changed inputs called transport.");
        }

        private static void TestPrivacy(JevCalibrationPackage package,List<SemanticAmbiguityCase> cases,JevCalibrationBenchmarkReport report)
        {
            CaptureHandler handler=new CaptureHandler();new TypeSafeJevClient(new HttpClient(handler)).DecideAsync(JevSemanticAssistPayloadBuilder.Build(new[]{cases[0]}),"APIKEY_SECRET_MARKER_JB").GetAwaiter().GetResult();foreach(string marker in new[]{"COMMENT_SECRET_MARKER_JB","DRAWING_SECRET_MARKER_JB","PROJECT_SECRET_MARKER_JB","STANDARD_SECRET_MARKER_JB","APPROVAL_SECRET_MARKER_JB","POLICY_SECRET_MARKER_JB","TRUST_SECRET_MARKER_JB","APIKEY_SECRET_MARKER_JB"})Assert(handler.Body.IndexOf(marker,StringComparison.Ordinal)<0,"Private marker entered HTTP body.");string artifacts=Json(package)+Json(report);Assert(!artifacts.Contains("APIKEY_SECRET_MARKER_JB")&&!artifacts.Contains("Authorization: Bearer"),"Credential entered artifact.");
        }

        private static void TestIsolation(JevCalibrationPackage package,List<SemanticAmbiguityCase> cases)
        {
            PlotPlanModel plan=new PlotPlanModel();string before=Json(plan);JevCalibrationBenchmarkRunner.Create(package,new List<JevEngineerLabel>(),new JevSemanticAdvisorySidecar(),"c","l");Assert(before==Json(plan),"PlotPlanModel changed.");Assert(typeof(JevCalibrationBenchmarkReport).GetProperty("AutoExecutionAllowed")==null&&typeof(JevCalibrationBenchmarkReport).GetProperty("PlacementProposal")==null,"Activation surface exposed.");var report=new JevCalibrationBenchmarkReport();Assert(report.AdvisoryOnly&&!report.ProductionThresholdSelected,"Advisory flags changed.");
        }

        private static List<SemanticAmbiguityCase> Cases(int count)=>Enumerable.Range(0,count).Select(i=>new SemanticAmbiguityCase{EvidenceId="e"+i,GeometryKind="Polyline",LayerName="L"+i,ExistingCandidateKinds=JevSemanticLabels.All.ToList(),NearbyTextFragments=new List<string>{"TEXT"+i}}).ToList();
        private static JevEngineerLabel Label(JevCalibrationCase c,string? label)=>new JevEngineerLabel{EvidenceId=c.EvidenceId,InputProjectionSha256=c.InputProjectionSha256,EngineerLabel=label};
        private static JevSemanticAssistResult Result(string id,string status,string? choice,double? confidence,string? model)=>new JevSemanticAssistResult{EvidenceId=id,AdvisoryStatus=status,Choice=choice,Confidence=confidence,Model=model};
        private static bool AllFinite(JevCalibrationBenchmarkReport r)=>new[]{r.Coverage,r.ExactMatchRate,r.ClassifiedAccuracy}.Where(x=>x.HasValue).All(x=>!double.IsNaN(x!.Value)&&!double.IsInfinity(x.Value));
        private static void WriteLabels(string dir,JevCalibrationPackage package,List<JevEngineerLabel> labels,string? id=null)=>Write(Path.Combine(dir,"engineer-labels.json"),new JevEngineerLabels{CalibrationPackageId=id??package.CalibrationPackageId,Labels=labels});
        private static void Write<T>(string path,T value){using(FileStream s=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None))new DataContractJsonSerializer(typeof(T)).WriteObject(s,value);}
        private static T Read<T>(string path){using(FileStream s=File.OpenRead(path))return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(s)!;}
        private static string Json(object value){using(MemoryStream s=new MemoryStream()){new DataContractJsonSerializer(value.GetType()).WriteObject(s,value);return Encoding.UTF8.GetString(s.ToArray());}}
        private static void ExpectPackageMismatch(Action action){bool ok=false;try{action();}catch(InvalidOperationException e){ok=e.Message==JevCalibrationStates.PackageContentMismatch;}Assert(ok,"Expected PACKAGE_CONTENT_MISMATCH.");}
        private static void ExpectInputChanged(Action action){bool ok=false;try{action();}catch(InvalidOperationException e){ok=e.Message==JevCalibrationStates.BenchmarkInputChanged;}Assert(ok,"Expected BENCHMARK_INPUT_CHANGED.");}
        private static void Assert(bool value,string message){assertions++;if(!value)throw new InvalidOperationException(message);}

        private sealed class CountingClient:IJevSemanticDecisionClient { public int CallCount; public Task<JevSystemOneResponse> DecideAsync(JevSystemOneRequest request,string apiKey){CallCount++;var response=new JevSystemOneResponse{Model="fake-model"};foreach(string id in request.Questions.Keys)response.Answers[id]=new JevChoiceAnswer{Type="choice",Choice=JevSemanticLabels.Unknown,Probabilities=JevSemanticLabels.All.ToDictionary(x=>x,x=>x==JevSemanticLabels.Unknown?1d:0d),Confidence=1};return Task.FromResult(response);} }
        private sealed class CaptureHandler:HttpMessageHandler { public string Body=string.Empty; protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Body=await request.Content.ReadAsStringAsync();return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"model\":\"fake\",\"answers\":{\"e0\":{\"type\":\"choice\",\"choice\":\"UNKNOWN\",\"probabilities\":{\"ACCESS_ROAD_EDGE\":0,\"SECURITY_FENCE\":0,\"UNIT_BOUNDARY\":0,\"UNRELATED\":0,\"UNKNOWN\":1},\"confidence\":1}}}")};} }
    }
}
