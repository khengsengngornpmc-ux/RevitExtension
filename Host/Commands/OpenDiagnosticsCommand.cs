using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CamboBIM.Revit2024.Addin
{
    [Transaction(TransactionMode.ReadOnly)]
    public class OpenDiagnosticsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                string report = MhnkDiagnostics.BuildReport(commandData);
                MhnkLogger.Info("Diagnostics opened." + Environment.NewLine + report);
                FeatureTraceWriter.WriteStage("DIAGNOSTICS", "OpenReport", "Diagnostics report opened.");

                var dialog = new TaskDialog("MHNK Diagnostics");
                dialog.MainInstruction = "MHNK diagnostics";
                dialog.MainContent = report;
                dialog.CommonButtons = TaskDialogCommonButtons.Close;
                dialog.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                MhnkLogger.Error("Diagnostics failed.", ex);
                message = ex.Message;
                TaskDialog.Show("MHNK Diagnostics", "Diagnostics failed." + Environment.NewLine + ex.Message);
                return Result.Failed;
            }
        }
    }
}
