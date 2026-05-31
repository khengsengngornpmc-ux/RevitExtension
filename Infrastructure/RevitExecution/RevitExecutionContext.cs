using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class RevitExecutionContext
    {
        public RevitExecutionContext(UIApplication application)
        {
            Application = application;
            ActiveUIDocument = application == null ? null : application.ActiveUIDocument;
            Document = ActiveUIDocument == null ? null : ActiveUIDocument.Document;
        }

        public UIApplication Application { get; private set; }

        public UIDocument ActiveUIDocument { get; private set; }

        public Document Document { get; private set; }
    }
}
