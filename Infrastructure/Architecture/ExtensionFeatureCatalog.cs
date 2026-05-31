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
                "Host\\App\\App.cs",
                "Host\\Commands\\OpenAboutMeCommand.cs",
                "Host\\Commands\\OpenUserGuideCommand.cs",
                "Host\\Shell\\CamboBIMWindow.xaml.cs",
                "Host\\Shell\\CamboBIMWindow.xaml",
                "Host\\Shell\\CamboBIMWindowCommand.cs"),
            new ExtensionFeature(
                "CAD_TO_MODEL",
                "CAD To Model",
                "Modeling",
                "CAD import, source registry, model creation requests, and drafting package helpers.",
                "Features\\CadToModel\\CadToModelExternalEventHandler.cs",
                "Features\\CadToModel\\CadToModelRequest.cs",
                "Features\\CadToModel\\Cad2ModelToolRegistry.cs",
                "Features\\CadToModel\\OpenCad2ModelCommand.cs"),
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
                "Features\\QSBoq\\CBIM_QS.cs",
                "Features\\QSBoq\\CBIM_BOQ.cs",
                "Features\\QSBoq\\QsMeasurementSettings.cs",
                "Features\\QSBoq\\QsMeasurementRules.cs",
                "Features\\QSBoq\\CamboBIMWindow.TasExport.cs"),
            new ExtensionFeature(
                "REBAR",
                "Rebar Tools",
                "Structure",
                "Column rebar, beam rebar, reinforcement previews, and Naviate-style rebar workflows.",
                "Features\\Rebar\\CamboBIMWindow.BeamNaviate.cs",
                "Features\\Rebar\\CamboBIMWindow.RebarColumn.cs"),
            new ExtensionFeature(
                "BORED_PILE",
                "Bored Pile",
                "Foundation",
                "Bored pile import selection, layer filtering, and pile generation workflows.",
                "Features\\BoredPile\\BoredPileToolExternalEventHandler.cs",
                "Features\\BoredPile\\BoredPileToolWindow.xaml"),
            new ExtensionFeature(
                "SITE_PROGRESS",
                "Site Progress",
                "Monitoring",
                "Progress reporting, dashboards, and site tracking outputs.",
                "Features\\SiteProgress\\CBIM_SITE_PROGRESS.cs",
                "Features\\SiteProgress\\CamboBIMWindow.SiteProgress.cs"),
            new ExtensionFeature(
                "BOREY",
                "Borey",
                "Project Tools",
                "Borey workflows, data preparation, and exports.",
                "Features\\Borey\\CBIM_BOREY.cs",
                "Features\\Borey\\CamboBIMWindow.Borey.cs"),
            new ExtensionFeature(
                "PM_DASHBOARD",
                "PM Dashboard",
                "Monitoring",
                "Project management dashboard views and supporting transforms.",
                "Features\\PmDashboard\\CamboBIMWindow.PmDashboard.cs",
                "Features\\PmDashboard\\OpenPmDashboardCommand.cs",
                "Features\\PmDashboard\\OpenSCurveCommand.cs"),
            new ExtensionFeature(
                "ARCHITECTURE_TOOLS",
                "Architecture Tools",
                "Architecture",
                "ARC workspace logic, validation, mapping, previews, and specialized architecture workflows.",
                "Features\\ArchitectureTools\\OpenMhnkArchitectureToolCommand.cs",
                "Features\\ArchitectureTools\\MhnkArcCommandRuntime.cs",
                "Features\\ArchitectureTools\\MhnkArcToolsWindow.cs",
                "Features\\ArchitectureTools\\MhnkArcValidationWindow.cs"),
            new ExtensionFeature(
                "LINKSHEET",
                "Linksheet",
                "Review",
                "Review tables and data structures that can support PT audit, review, and coordination workflows.",
                "Features\\Linksheet\\LinksheetModels.cs",
                "Features\\Linksheet\\OpenLinksheetCommand.cs"),
            new ExtensionFeature(
                "LICENSING",
                "Licensing",
                "Security",
                "Online license client, cached sessions, and login flows.",
                "Features\\Licensing\\OnlineLicenseService.cs",
                "Features\\Licensing\\OnlineLicenseClient.cs"),
            new ExtensionFeature(
                "DIAGNOSTICS",
                "Diagnostics",
                "Support",
                "Health reports, logs, trace files, and support diagnostics.",
                "Host\\Commands\\OpenDiagnosticsCommand.cs",
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
