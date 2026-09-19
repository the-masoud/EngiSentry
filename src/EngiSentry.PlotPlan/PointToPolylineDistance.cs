using System;
using System.Collections.Generic;
using EngiSentry.Contracts;

namespace EngiSentry.PlotPlan
{
    /// <summary>
    /// Pure, host-independent point-to-reference-geometry distance mathematics, extracted verbatim from the
    /// accepted <c>PlotSpatialMeasurementBuilder.BuildEngineeringSpatial</c> algorithm. The existing
    /// production measurement behaviour remains authoritative; this helper reproduces it exactly so that
    /// candidate re-evaluation (Phase D) uses the same numerical semantics, and so that
    /// <c>PlotSpatialMeasurementBuilder</c> may delegate to it with provably identical behaviour.
    ///
    /// Semantics preserved: iterate the primitive's segments (Closed-aware), clamp the projection parameter
    /// t to [0,1], compute the point-to-segment distance, and keep the minimum with the deterministic
    /// tie-break of lowest distance then lowest segment index. Units are drawing units throughout, exactly
    /// as the accepted pipeline; conversion to metres remains the caller's responsibility via
    /// <see cref="DrawingUnitResolver"/>.
    /// </summary>
    public static class PointToPolylineDistance
    {
        /// <summary>
        /// Minimum distance from a point to a polyline, in drawing units. Throws when the primitive has no
        /// positive-length XY segment, mirroring the accepted invariant.
        /// </summary>
        public static double PointToPolyline(double pointX, double pointY, PlotGeometryPrimitive primitive)
        {
            if (primitive == null) throw new ArgumentNullException(nameof(primitive));
            SegmentEvidence? governing = null;
            int count = primitive.Vertices.Count;
            int segmentCount = primitive.Closed ? count : count - 1;
            for (int index = 0; index < segmentCount; index++)
            {
                PlotGeometryVertex start = primitive.Vertices[index];
                PlotGeometryVertex end = primitive.Vertices[(index + 1) % count];
                double dx = end.X - start.X;
                double dy = end.Y - start.Y;
                double lengthSquared = dx * dx + dy * dy;
                if (!IsFinite(lengthSquared)) throw new InvalidOperationException("Engineering spatial primitive contains non-finite segment evidence: " + primitive.SourceHandle);
                if (lengthSquared <= 0d) continue;
                double t = ((pointX - start.X) * dx + (pointY - start.Y) * dy) / lengthSquared;
                t = Math.Max(0d, Math.Min(1d, t));
                double closestX = start.X + t * dx;
                double closestY = start.Y + t * dy;
                double distance = Distance(pointX, pointY, closestX, closestY);
                if (governing == null || distance < governing.Value.Distance || (distance == governing.Value.Distance && index < governing.Value.Index)) governing = new SegmentEvidence(index, distance, closestX, closestY);
            }
            if (governing == null) throw new InvalidOperationException("Engineering spatial primitive has no positive-length XY segment: " + primitive.SourceHandle);
            return governing.Value.Distance;
        }

        /// <summary>
        /// Point-to-segment distance with endpoint clamping: the perpendicular projection when it lands
        /// within the segment, otherwise the distance to the nearer endpoint. Degenerate (zero-length)
        /// segments fall back to the point-to-point distance deterministically.
        /// </summary>
        public static double PointToSegment(double pointX, double pointY, double startX, double startY, double endX, double endY)
        {
            double dx = endX - startX;
            double dy = endY - startY;
            double lengthSquared = dx * dx + dy * dy;
            if (!IsFinite(lengthSquared)) throw new InvalidOperationException("Segment evidence is non-finite.");
            if (lengthSquared <= 0d) return Distance(pointX, pointY, startX, startY);
            double t = ((pointX - startX) * dx + (pointY - startY) * dy) / lengthSquared;
            t = Math.Max(0d, Math.Min(1d, t));
            double closestX = startX + t * dx;
            double closestY = startY + t * dy;
            return Distance(pointX, pointY, closestX, closestY);
        }

        /// <summary>Reports the governing segment for a point against a primitive, exposing the segment
        /// index, distance and closest point that the accepted measurement pipeline records.</summary>
        public static SegmentEvidence PointToPolylineEvidence(double pointX, double pointY, PlotGeometryPrimitive primitive)
        {
            if (primitive == null) throw new ArgumentNullException(nameof(primitive));
            SegmentEvidence? governing = null;
            int count = primitive.Vertices.Count;
            int segmentCount = primitive.Closed ? count : count - 1;
            for (int index = 0; index < segmentCount; index++)
            {
                PlotGeometryVertex start = primitive.Vertices[index];
                PlotGeometryVertex end = primitive.Vertices[(index + 1) % count];
                double dx = end.X - start.X;
                double dy = end.Y - start.Y;
                double lengthSquared = dx * dx + dy * dy;
                if (!IsFinite(lengthSquared)) throw new InvalidOperationException("Engineering spatial primitive contains non-finite segment evidence: " + primitive.SourceHandle);
                if (lengthSquared <= 0d) continue;
                double t = ((pointX - start.X) * dx + (pointY - start.Y) * dy) / lengthSquared;
                t = Math.Max(0d, Math.Min(1d, t));
                double closestX = start.X + t * dx;
                double closestY = start.Y + t * dy;
                double distance = Distance(pointX, pointY, closestX, closestY);
                if (governing == null || distance < governing.Value.Distance || (distance == governing.Value.Distance && index < governing.Value.Index)) governing = new SegmentEvidence(index, distance, closestX, closestY);
            }
            if (governing == null) throw new InvalidOperationException("Engineering spatial primitive has no positive-length XY segment: " + primitive.SourceHandle);
            return governing.Value;
        }

        private static double Distance(double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1;
            double dy = y2 - y1;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>Governing segment evidence: index, distance, and closest point, with the accepted
        /// lowest-distance-then-lowest-index tie-break already applied.</summary>
        public readonly struct SegmentEvidence
        {
            public SegmentEvidence(int index, double distance, double closestPointX, double closestPointY)
            {
                Index = index;
                Distance = distance;
                ClosestPointX = closestPointX;
                ClosestPointY = closestPointY;
            }

            public int Index { get; }
            public double Distance { get; }
            public double ClosestPointX { get; }
            public double ClosestPointY { get; }
        }
    }
}
