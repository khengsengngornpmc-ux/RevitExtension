using System;
using System.Collections.Generic;

namespace CamboBIM.Revit2024.Addin
{
    internal static class ExtensionFeatureCatalog
    {
        private static readonly List<ExtensionFeature> Features = new List<ExtensionFeature>
        {
            new ExtensionFeature(
                "CORE",
                "Core Shell",
                "Shell",
                "Ribbon, startup, shell window, shared runtime, and cross-feature coordination.",
                "App.cs",
                "CamboBIMWindow.xaml.cs",
                "CamboBIMWindow.xaml"),
            new ExtensionFeature(
                "CAD_TO_MODEL",
                "CAD To Model",
                "Modeling",
                "CAD import, source registry, model creation requests, and drafting package helpers.",
                "CadToModelExternalEventHandler.cs",
                "CadToModelRequest.cs",
                "Cad2ModelToolRegistry.cs"),
            new ExtensionFeature(
                "PT_DRAWING",
                "PT Drawing",
                "Shop Drawing",
                "ADAPT import, DWG or DXF fallback import, tendon normalization, profiles, sheets, and audit flows.",
                "CamboBIMWindow.AdaptTendonImport.cs",
                "PtJsonModels.cs",
                "PtJsonMapper.cs"),
            new ExtensionFeature(
                "QS_BOQ",
                "QS And BOQ",
                "Quantity",
                "Measurement rules, quantity settings, QS exports, and BOQ outputs.",
                "CBIM_QS.cs",
                "CBIM_BOQ.cs",
                "QsMeasurementSettings.cs",
                "QsMeasurementRules.cs"),
            new ExtensionFeature(
                "SITE_PROGRESS",
                "Site Progress",
                "Monitoring",
                "Progress reporting, dashboards, and site tracking outputs.",
                "CBIM_SITE_PROGRESS.cs",
                "CamboBIMWindow.SiteProgress.cs"),
            new ExtensionFeature(
                "BOREY",
                "Borey",
                "Project Tools",
                "Borey workflows, data preparation, and exports.",
                "CBIM_BOREY.cs",
                "CamboBIMWindow.Borey.cs"),
            new ExtensionFeature(
                "PM_DASHBOARD",
                "PM Dashboard",
                "Monitoring",
                "Project management dashboard views and supporting transforms.",
                "CamboBIMWindow.PmDashboard.cs"),
            new ExtensionFeature(
                "ARCHITECTURE_TOOLS",
                "Architecture Tools",
                "Architecture",
                "ARC workspace logic, validation, mapping, previews, and specialized architecture workflows.",
                "OpenMhnkArchitectureToolCommand.cs",
                "MhnkArcCommandRuntime.cs",
                "MhnkArcToolsWindow.cs"),
            new ExtensionFeature(
                "LINKSHEET",
                "Linksheet",
                "Review",
                "Review tables and data structures that can support PT audit, review, and coordination workflows.",
                "LinksheetModels.cs"),
            new ExtensionFeature(
                "LICENSING",
                "Licensing",
                "Security",
                "Online license client, cached sessions, and login flows.",
                "Licensing\\OnlineLicenseService.cs",
                "Licensing\\OnlineLicenseClient.cs"),
            new ExtensionFeature(
                "DIAGNOSTICS",
                "Diagnostics",
                "Support",
                "Health reports, logs, trace files, and support diagnostics.",
                "Infrastructure\\MhnkDiagnostics.cs",
                "Infrastructure\\MhnkLogger.cs")
        };

        public static IReadOnlyList<ExtensionFeature> All
        {
            get { return Features; }
        }

        public static bool TryGet(string featureId, out ExtensionFeature feature)
        {
            feature = null;
            if (string.IsNullOrWhiteSpace(featureId))
            {
                return false;
            }

            for (int i = 0; i < Features.Count; i++)
            {
                if (string.Equals(Features[i].Id, featureId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    feature = Features[i];
                    return true;
                }
            }

            return false;
        }
    }
}
