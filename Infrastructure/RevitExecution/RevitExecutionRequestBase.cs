namespace CamboBIM.Revit2024.Addin
{
    internal abstract class RevitExecutionRequestBase : IRevitExecutionRequest
    {
        protected RevitExecutionRequestBase(string featureId, string name, bool showFailureDialog = true)
        {
            FeatureId = string.IsNullOrWhiteSpace(featureId) ? "GENERAL" : featureId.Trim();
            Name = string.IsNullOrWhiteSpace(name) ? GetType().Name : name.Trim();
            ShowFailureDialog = showFailureDialog;
        }

        public string FeatureId { get; private set; }

        public string Name { get; private set; }

        public bool ShowFailureDialog { get; private set; }

        public abstract OperationResult Execute(RevitExecutionContext context);
    }
}
