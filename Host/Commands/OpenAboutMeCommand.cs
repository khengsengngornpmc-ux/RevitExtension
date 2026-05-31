using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CamboBIM.Revit2024.Addin
{
    [Transaction(TransactionMode.Manual)]
    public class OpenAboutMeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            string content =
                "Product: MHNK Revit " + CamboBimRuntime.RevitYear + " Extension" + System.Environment.NewLine +
                "Version: " + version + System.Environment.NewLine +
                "Publisher: Mohanokor Engineering Construction" + System.Environment.NewLine +
                System.Environment.NewLine +
                "Contact info:" + System.Environment.NewLine +
                App.GetLicenseSupportContactText();

            var dialog = new TaskDialog("About Me");
            dialog.MainInstruction = "MHNK Information";
            dialog.MainContent = content;
            dialog.CommonButtons = TaskDialogCommonButtons.Close;
            dialog.Show();

            return Result.Succeeded;
        }
    }
}
