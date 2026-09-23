using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using EngiSentry.Intelligence;

namespace EngiSentry.Tests
{
    internal static class PhaseJevEngineerLabelingTests
    {
        private static int assertions;

        internal static void Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "EngiSentry-JD3-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                CreatePackage(root,"02","Zulu.dwg",2,null,false);
                string editableDirectory=CreatePackage(root,"01","Alpha.dwg",2,null,false);
                List<JevEngineerLabelingPackage> discovered=new JevEngineerLabelingService(root).Discover();
                Assert(discovered.Count==2&&discovered.Select(x=>x.Package.DrawingName).SequenceEqual(new[]{"Alpha.dwg","Zulu.dwg"}),"Package discovery/order failed.");
                JevEngineerLabelingPackage editable=discovered[0];Assert(editable.Status==JevEngineerLabelingStatuses.NotStarted&&editable.LabeledCount==0&&editable.Labels.All(x=>x.EngineerLabel==null),"NOT_STARTED/unset labels failed.");

                byte[] casesBefore=File.ReadAllBytes(Path.Combine(editableDirectory,"calibration-cases.json"));byte[] templateBefore=File.ReadAllBytes(Path.Combine(editableDirectory,"engineer-labels.template.json"));
                editable.Labels[0].EngineerLabel=JevSemanticLabels.Unknown;editable.Labels[0].Comment="human comment";new JevEngineerLabelingService(root).Save(editable);Assert(editable.Status==JevEngineerLabelingStatuses.InProgress&&editable.LabeledCount==1,"IN_PROGRESS or UNKNOWN counting failed.");
                Assert(casesBefore.SequenceEqual(File.ReadAllBytes(Path.Combine(editableDirectory,"calibration-cases.json")))&&templateBefore.SequenceEqual(File.ReadAllBytes(Path.Combine(editableDirectory,"engineer-labels.template.json"))),"Save mutated package evidence/template.");
                JevEngineerLabelingPackage loaded=new JevEngineerLabelingService(root).Discover().Single(x=>x.Package.DrawingName=="Alpha.dwg");Assert(loaded.Labels[0].EngineerLabel==JevSemanticLabels.Unknown&&loaded.Labels[0].Comment=="human comment"&&loaded.Labels[1].EngineerLabel==null,"Valid labels/comments did not reload.");
                foreach(string value in JevSemanticLabels.All){loaded.Labels[0].EngineerLabel=value;new JevEngineerLabelingService(root).Save(loaded);Assert(loaded.Labels[0].EngineerLabel==value,"Allowed label rejected: "+value);}loaded.Labels[1].EngineerLabel=JevSemanticLabels.Unrelated;new JevEngineerLabelingService(root).Save(loaded);Assert(loaded.Status==JevEngineerLabelingStatuses.Complete&&loaded.LabeledCount==2,"COMPLETE failed.");
                JevEngineerLabels saved=Read<JevEngineerLabels>(Path.Combine(editableDirectory,"engineer-labels.json"));Assert(saved.SchemaVersion=="J-D-1"&&saved.CalibrationPackageId==loaded.Package.CalibrationPackageId&&saved.Labels.Select(x=>x.EvidenceId).SequenceEqual(loaded.Package.Cases.Select(x=>x.EvidenceId))&&saved.Labels.Zip(loaded.Package.Cases,(l,c)=>l.InputProjectionSha256==c.InputProjectionSha256).All(x=>x),"Saved identity/order failed.");

                loaded.Labels[0].EngineerLabel="BAD";ExpectInvalid(()=>new JevEngineerLabelingService(root).Save(loaded),"Invalid label accepted.");loaded.Labels[0].EngineerLabel=JevSemanticLabels.Unknown;
                string evidence=loaded.Labels[0].EvidenceId;loaded.Labels[0].EvidenceId="missing";ExpectInvalid(()=>new JevEngineerLabelingService(root).Save(loaded),"Evidence mismatch accepted.");loaded.Labels[0].EvidenceId=evidence;
                string projection=loaded.Labels[0].InputProjectionSha256;loaded.Labels[0].InputProjectionSha256="bad";ExpectInvalid(()=>new JevEngineerLabelingService(root).Save(loaded),"Projection mismatch accepted.");loaded.Labels[0].InputProjectionSha256=projection;
                string secondEvidence=loaded.Labels[1].EvidenceId;loaded.Labels[1].EvidenceId=loaded.Labels[0].EvidenceId;ExpectInvalid(()=>new JevEngineerLabelingService(root).Save(loaded),"Duplicate EvidenceId accepted.");loaded.Labels[1].EvidenceId=secondEvidence;

                string invalidDirectory=CreatePackage(root,"03","Invalid.dwg",1,null,false);Write(Path.Combine(invalidDirectory,"engineer-labels.json"),new JevEngineerLabels{SchemaVersion="J-D-1",CalibrationPackageId="wrong",Labels=new List<JevEngineerLabel>()});byte[] invalidBefore=File.ReadAllBytes(Path.Combine(invalidDirectory,"engineer-labels.json"));JevEngineerLabelingPackage invalid=new JevEngineerLabelingService(root).Discover().Single(x=>x.Package.DrawingName=="Invalid.dwg");Assert(invalid.Status==JevEngineerLabelingStatuses.InvalidLabelFile&&invalid.IsReadOnly,"Invalid label file was not protected.");ExpectInvalid(()=>new JevEngineerLabelingService(root).Save(invalid),"Invalid label file was overwritten.");Assert(invalidBefore.SequenceEqual(File.ReadAllBytes(Path.Combine(invalidDirectory,"engineer-labels.json"))),"Invalid label file changed.");

                string lockedDirectory=CreatePackage(root,"04","Locked.dwg",1,JevSemanticLabels.Unknown,true);byte[] benchmarkBefore=File.ReadAllBytes(Path.Combine(lockedDirectory,"benchmark-report.json"));JevEngineerLabelingPackage locked=new JevEngineerLabelingService(root).Discover().Single(x=>x.Package.DrawingName=="Locked.dwg");Assert(locked.Status==JevEngineerLabelingStatuses.LockedBenchmarked&&locked.IsReadOnly,"Benchmarked package not locked.");ExpectInvalid(()=>new JevEngineerLabelingService(root).Save(locked),"Benchmarked package saved.");Assert(benchmarkBefore.SequenceEqual(File.ReadAllBytes(Path.Combine(lockedDirectory,"benchmark-report.json"))),"Benchmark report changed.");
                string emptyDirectory=CreatePackage(root,"05","Empty.dwg",0,null,false);JevEngineerLabelingPackage empty=new JevEngineerLabelingService(root).Discover().Single(x=>x.Package.DrawingName=="Empty.dwg");Assert(empty.Package.Cases.Count==0&&empty.Status==JevEngineerLabelingStatuses.NotStarted,"Empty package was not handled safely.");

                Assert(!typeof(JevEngineerLabelingService).GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Any(x=>typeof(IJevSemanticDecisionClient).IsAssignableFrom(x.FieldType)),"Labeling service constructs a Jev client.");
                Assert(!typeof(JevEngineerLabelingService).GetMethods().Any(x=>x.Name.IndexOf("Benchmark",StringComparison.OrdinalIgnoreCase)>=0),"Labeling service exposes benchmark execution.");
                Console.WriteLine("Phase J-D3 focused assertions passed: "+assertions);
            }
            finally { if(Directory.Exists(root))Directory.Delete(root,true); }
        }

        private static string CreatePackage(string root,string id,string drawing,int count,string? initialLabel,bool benchmarked)
        {
            string directory=Path.Combine(root,id);Directory.CreateDirectory(directory);var package=new JevCalibrationPackage{SchemaVersion="J-D-1",CalibrationPackageId="package-"+id,DrawingName=drawing,Cases=Enumerable.Range(0,count).Select(i=>new JevCalibrationCase{EvidenceId=id+"-e"+i,InputProjectionSha256=id+"-p"+i,GeometryKind="Line",CurrentDeterministicState="Ambiguous"}).ToList()};var labels=new JevEngineerLabels{SchemaVersion=package.SchemaVersion,CalibrationPackageId=package.CalibrationPackageId,Labels=package.Cases.Select((c,i)=>new JevEngineerLabel{EvidenceId=c.EvidenceId,InputProjectionSha256=c.InputProjectionSha256,EngineerLabel=i==0?initialLabel:null}).ToList()};Write(Path.Combine(directory,"calibration-cases.json"),package);Write(Path.Combine(directory,"engineer-labels.template.json"),new JevEngineerLabels{SchemaVersion=labels.SchemaVersion,CalibrationPackageId=labels.CalibrationPackageId,Labels=labels.Labels.Select(x=>new JevEngineerLabel{EvidenceId=x.EvidenceId,InputProjectionSha256=x.InputProjectionSha256}).ToList()});if(initialLabel!=null)Write(Path.Combine(directory,"engineer-labels.json"),labels);if(benchmarked)File.WriteAllText(Path.Combine(directory,"benchmark-report.json"),"synthetic benchmark marker");return directory;
        }
        private static void Write<T>(string path,T value){using(FileStream stream=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None))new DataContractJsonSerializer(typeof(T)).WriteObject(stream,value);}
        private static T Read<T>(string path){using(FileStream stream=File.OpenRead(path))return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream)!;}
        private static void ExpectInvalid(Action action,string message){bool failed=false;try{action();}catch(InvalidOperationException){failed=true;}Assert(failed,message);}
        private static void Assert(bool value,string message){assertions++;if(!value)throw new InvalidOperationException(message);}
    }
}
