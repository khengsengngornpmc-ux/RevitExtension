using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CamboBIM.Revit2024.Addin
{
    [Transaction(TransactionMode.Manual)]
    public class OpenDrawingPtCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Result result = CamboBIMWindowCommand.ShowWindow(commandData, ref message, "IDENTIFY", null, true);
            if (result == Result.Succeeded)
            {
                CamboBIMWindowCommand.CurrentWindow?.StartAdaptImportFromRibbon();
            }

            return result;
        }
    }
}
