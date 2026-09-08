using System;
using System.Collections.Generic;
using EngiSentry.Contracts;
using EngiSentry.Standards;

namespace EngiSentry.AutoCAD2021.Scanning
{
    /// <summary>Creates the compact report graph without changing the live Plot Plan model used by QA.</summary>
    public static class PlotPlanReportCompactor
    {
        public static PlotPlanQaReport CreatePlotPlanQaReport(PlotPlanModel plotPlan, QaReport qa)
        {
            if (plotPlan == null) throw new ArgumentNullException(nameof(plotPlan));
            if (qa == null) throw new ArgumentNullException(nameof(qa));

            PlotPlanStandardsAuthorityRuntimeContext authorityContext = PlotPlanStandardsAuthorityRuntimeContextCatalog.Create();
            return new PlotPlanQaReport
            {
                PlotPlan = plotPlan,
                Qa = qa,
                StandardsAuthority = new PlotPlanStandardsAuthorityReport
                {
                    AuthorityResolution = authorityContext.AuthorityResolution,
                    ExecutionGate = authorityContext.ExecutionGate
                },
                ReviewProjection = authorityContext.ReviewProjection
            };
        }

        public static PlotPlanQaReport Create(PlotPlanQaReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (report.PlotPlan == null) throw new ArgumentException("Plot Plan report requires a PlotPlan model.", nameof(report));

            PlotPlanModel source = report.PlotPlan;
            return new PlotPlanQaReport
            {
                Qa = report.Qa,
                StandardsAuthority = report.StandardsAuthority,
                ReviewProjection = report.ReviewProjection,
                PlotPlan = new PlotPlanModel
                {
                    Drawing = source.Drawing,
                    EquipmentPlacements = source.EquipmentPlacements,
                    Statistics = source.Statistics,
                    Measurements = source.Measurements,
                    Geometry = new PlotGeometryModel
                    {
                        Primitives = new List<PlotGeometryPrimitive>(),
                        Associations = source.Geometry.Associations,
                        Statistics = source.Geometry.Statistics,
                        SpatialConstraints = source.Geometry.SpatialConstraints,
                        EngineeringSpatialSubjects = source.Geometry.EngineeringSpatialSubjects
                    }
                }
            };
        }
    }
}
