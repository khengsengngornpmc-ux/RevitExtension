using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CamboBIM.Revit2024.Addin
{
    [Transaction(TransactionMode.Manual)]
    public class OpenDrawingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return CamboBIMWindowCommand.ShowWindow(commandData, ref message, "REINFORCEMENT", null, true);
        }
    }
}
