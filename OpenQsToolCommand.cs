using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CamboBIM.Revit2024.Addin
{
    [Transaction(TransactionMode.Manual)]
    public class OpenQsToolCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return CamboBIMWindowCommand.ShowWindow(
                commandData,
                ref message,
                "QS TOOLS",
                "Quick Estimate (CBIM-QS)",
                true);
        }
    }
}
