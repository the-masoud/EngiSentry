import json, os

ROOT = r"C:\Users\M.Zeynali\Documents\EngiSentry"
source = os.path.join(ROOT, "artifacts", "qa", "plotplan", "m22-manual-acceptance.dwg.20260904T100824032Z.plotplan.qa.json")
with open(source, encoding="utf-8") as f:
    report = json.load(f)
qa = report["Qa"]
plot = report["PlotPlan"]
geo = plot["Geometry"]

def slim_finding(f):
    e = f.get("Evidence") or {}
    return {
        "id": f["FindingId"], "rule": f["RuleId"], "severity": f["Severity"],
        "category": f["Category"], "subject": f["SubjectId"], "message": f["Message"],
        "handles": f.get("SourceHandles") or [], "tag": e.get("NormalizedTag"),
        "description": e.get("Description"), "reason": e.get("Reason")
    }
data = {
    "product": "EngiSentry",
    "version": "Technical Preview v0.1",
    "subtitle": "Engineering QA & Drawing Intelligence",
    "drawing": qa["Drawing"]["FileName"],
    "execution": qa["ExecutionStatus"],
    "overall": qa["OverallStatus"],
    "qaStats": qa["Statistics"],
    "placementStats": plot["Statistics"],
    "geometryStats": geo["Statistics"],
    "rules": [{"id": r["RuleId"], "status": r["Status"], "count": r["FindingCount"]}
              for r in qa["RuleResults"]],
    "findings": [slim_finding(f) for f in qa["Findings"]],
    "spatial": geo.get("SpatialConstraints") or [],
    "provenance": {
        "sourceArtifact": os.path.basename(source),
        "commit": "f267d6cdf41f02cb19dce347bab39d53daeba45a",
        "readOnly": True,
        "fixtureSha256": "B1D1B99054C08AF00BAFF7FC6CDD0A8EDF3EFD35774F733A3942F2490142246B"
    }
}
out = os.path.join(os.path.dirname(__file__), "data.js")
with open(out, "w", encoding="utf-8") as f:
    f.write("window.ENGISENTRY_PREVIEW = ")
    json.dump(data, f, ensure_ascii=False, separators=(",", ":"))
    f.write(";\n")
print(out)
