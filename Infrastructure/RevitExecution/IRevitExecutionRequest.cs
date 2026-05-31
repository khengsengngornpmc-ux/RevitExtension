namespace CamboBIM.Revit2024.Addin
{
    internal interface IRevitExecutionRequest
    {
        string FeatureId { get; }

        string Name { get; }

        bool ShowFailureDialog { get; }

        OperationResult Execute(RevitExecutionContext context);
    }
}
