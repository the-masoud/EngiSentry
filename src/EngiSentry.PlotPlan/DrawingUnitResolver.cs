using System;
using System.Collections.Generic;
using EngiSentry.Contracts;

namespace EngiSentry.PlotPlan
{
    public sealed class DrawingUnitResolutionException : Exception
    {
        public DrawingUnitResolutionException(string message) : base(message) { }
    }

    public static class DrawingUnitResolver
    {
        private const string AutoCadInsunits = "AutoCAD.Insunits";
        private static readonly Dictionary<string, UnitDefinition> SupportedUnits = new Dictionary<string, UnitDefinition>(StringComparer.Ordinal)
        {
            { "Millimeters", new UnitDefinition("mm", 0.001d) },
            { "Centimeters", new UnitDefinition("cm", 0.01d) },
            { "Meters", new UnitDefinition("m", 1.0d) },
            { "Kilometers", new UnitDefinition("km", 1000.0d) },
            { "Inches", new UnitDefinition("in", 0.0254d) },
            { "Feet", new UnitDefinition("ft", 0.3048d) },
            { "Yards", new UnitDefinition("yd", 0.9144d) },
            { "Miles", new UnitDefinition("mi", 1609.344d) }
        };

        public static DrawingUnitResolution Resolve(string insertionUnits)
        {
            if (string.IsNullOrWhiteSpace(insertionUnits))
            {
                throw new DrawingUnitResolutionException("drawing insertion units are required");
            }

            UnitDefinition unit;
            if (!SupportedUnits.TryGetValue(insertionUnits, out unit))
            {
                throw new DrawingUnitResolutionException("unsupported drawing insertion units: " + insertionUnits);
            }

            return new DrawingUnitResolution
            {
                InsertionUnits = insertionUnits,
                CanonicalUnit = unit.CanonicalUnit,
                MetersPerDrawingUnit = unit.MetersPerDrawingUnit,
                ResolutionSource = AutoCadInsunits
            };
        }

        public static double DrawingUnitsToMeters(double drawingDistance, DrawingUnitResolution resolution)
        {
            if (resolution == null) throw new ArgumentNullException(nameof(resolution));
            if (!IsFinite(drawingDistance) || drawingDistance < 0d)
            {
                throw new DrawingUnitResolutionException("drawing distance must be finite and non-negative");
            }

            UnitDefinition unit = Validate(resolution);
            double converted = drawingDistance * unit.MetersPerDrawingUnit;
            if (!IsFinite(converted)) throw new DrawingUnitResolutionException("converted distance is not finite");
            return converted;
        }

        public static double MetersToDrawingUnits(double meters, DrawingUnitResolution resolution)
        {
            if (resolution == null) throw new ArgumentNullException(nameof(resolution));
            if (!IsFinite(meters) || meters < 0d)
            {
                throw new DrawingUnitResolutionException("metre distance must be finite and non-negative");
            }

            UnitDefinition unit = Validate(resolution);
            double converted = meters / unit.MetersPerDrawingUnit;
            if (!IsFinite(converted)) throw new DrawingUnitResolutionException("converted distance is not finite");
            return converted;
        }

        private static UnitDefinition Validate(DrawingUnitResolution resolution)
        {
            UnitDefinition unit;
            if (!SupportedUnits.TryGetValue(resolution.InsertionUnits ?? string.Empty, out unit)
                || !string.Equals(resolution.CanonicalUnit, unit.CanonicalUnit, StringComparison.Ordinal)
                || resolution.MetersPerDrawingUnit != unit.MetersPerDrawingUnit
                || !string.Equals(resolution.ResolutionSource, AutoCadInsunits, StringComparison.Ordinal))
            {
                throw new DrawingUnitResolutionException("drawing unit resolution is invalid: " + (resolution.InsertionUnits ?? string.Empty));
            }

            return unit;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private sealed class UnitDefinition
        {
            public UnitDefinition(string canonicalUnit, double metersPerDrawingUnit)
            {
                CanonicalUnit = canonicalUnit;
                MetersPerDrawingUnit = metersPerDrawingUnit;
            }

            public string CanonicalUnit { get; }
            public double MetersPerDrawingUnit { get; }
        }
    }
}
