using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using EngiSentry.Contracts;

namespace EngiSentry.AutoCAD2021.Scanning
{
    /// <summary>
    /// Phase E / Phase A architecture K.3 — the read-only placement-proposal sidecar. Emits the accepted Phase D
    /// <see cref="PlacementProposal"/> set as a separate artifact alongside the accepted Plot Plan QA report and
    /// the standards review package, without adding any field to <see cref="PlotPlanQaReport"/> or altering its
    /// serialized shape.
    ///
    /// The artifact is a pure projection: it is written after the CHECK+SUGGEST pipeline has completed, it never
    /// opens a document, and it never mutates a DWG. The proposals themselves remain advisory evidence —
    /// <see cref="PlacementProposal.EngineeringReviewRequired"/> is always true in v1.
    /// </summary>
    public static class PlacementProposalSnapshotWriter
    {
        /// <summary>Writes the ordered proposal set as
        /// <c>&lt;drawing&gt;.&lt;snapshot timestamp&gt;.placement-proposal.json</c> and returns the path.</summary>
        public static string Write(IReadOnlyList<PlacementProposal> proposals, string? drawingName, DateTime timestampUtc)
        {
            if (proposals == null) throw new ArgumentNullException(nameof(proposals));

            string directory = Path.Combine(SnapshotFileWriter.ResolveArtifactsRoot(), "qa", "plotplan");
            Directory.CreateDirectory(directory);
            string drawing = SnapshotFileWriter.SanitizeFileName(drawingName ?? "unnamed");
            string timestamp = timestampUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, drawing + "." + timestamp + ".placement-proposal.json");

            // Deterministic ordering, independent of caller construction order: the same evidence always yields
            // the same byte-identical artifact.
            List<PlacementProposal> ordered = proposals
                .Where(x => x != null)
                .OrderBy(x => x.ProposalId, StringComparer.Ordinal)
                .ToList();

            var serializer = new DataContractJsonSerializer(typeof(List<PlacementProposal>));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                serializer.WriteObject(stream, ordered);
            }
            return path;
        }

        /// <summary>Reads a proposal sidecar back, in the deterministic order it was written.</summary>
        public static List<PlacementProposal> Read(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            var serializer = new DataContractJsonSerializer(typeof(List<PlacementProposal>));
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return (List<PlacementProposal>)serializer.ReadObject(stream);
            }
        }

        /// <summary>Loads the most recent immutable proposal sidecar for the exact drawing revision.</summary>
        public static List<PlacementProposal> ReadCurrentForDrawing(string drawingName, string drawingSha256)
        {
            string directory = Path.Combine(SnapshotFileWriter.ResolveArtifactsRoot(), "qa", "plotplan");
            if (!Directory.Exists(directory)) return new List<PlacementProposal>();
            string prefix = SnapshotFileWriter.SanitizeFileName(drawingName ?? "unnamed") + ".";
            string[] files = Directory.GetFiles(directory, prefix + "*.placement-proposal.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToArray();
            foreach (string file in files)
            {
                List<PlacementProposal> proposals;
                try { proposals = Read(file); }
                catch (Exception exception) when (exception is IOException || exception is System.Runtime.Serialization.SerializationException) { continue; }
                List<PlacementProposal> current = proposals.Where(proposal =>
                    string.Equals(proposal.SourceDrawingSha256, drawingSha256, StringComparison.Ordinal)
                    && string.Equals(proposal.ProposalFingerprint, EngiSentry.Standards.PlacementProposalFingerprint.Compute(proposal), StringComparison.Ordinal))
                    .OrderBy(proposal => proposal.ProposalId, StringComparer.Ordinal).ToList();
                if (current.Count > 0) return current;
            }
            return new List<PlacementProposal>();
        }
    }
}
