using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class RevitExecutionExternalEventHandler : IExternalEventHandler
    {
        private readonly RevitExecutionQueue _queue;

        public RevitExecutionExternalEventHandler(RevitExecutionQueue queue)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        }

        public void Execute(UIApplication app)
        {
            var context = new RevitExecutionContext(app);

            while (_queue.TryDequeue(out IRevitExecutionRequest request))
            {
                ExecuteRequest(context, request);
            }
        }

        public string GetName()
        {
            return "MHNK Revit execution boundary";
        }

        private static void ExecuteRequest(RevitExecutionContext context, IRevitExecutionRequest request)
        {
            string featureId = string.IsNullOrWhiteSpace(request.FeatureId) ? "GENERAL" : request.FeatureId;
            string requestName = string.IsNullOrWhiteSpace(request.Name) ? request.GetType().Name : request.Name;

            var traceContext = new Dictionary<string, object>
            {
                { "request", requestName }
            };

            FeatureTraceWriter.WriteStage(featureId, "RevitExecuteStart", "Revit execution request started.", traceContext);

            try
            {
                OperationResult result = request.Execute(context) ?? OperationResult.Success();
                string stage = result.Succeeded ? "RevitExecuteSucceeded" : "RevitExecuteFailed";
                FeatureTraceWriter.WriteStage(featureId, stage, result.ToString(), traceContext);

                if (!result.Succeeded && request.ShowFailureDialog)
                {
                    TaskDialog.Show("MHNK " + featureId, result.ToString());
                }
            }
            catch (Exception ex)
            {
                MhnkLogger.Error("Revit execution request failed: " + requestName, ex);
                FeatureTraceWriter.WriteStage(featureId, "RevitExecuteException", ex.Message, traceContext);

                if (request.ShowFailureDialog)
                {
                    TaskDialog.Show("MHNK " + featureId, "Request failed." + Environment.NewLine + ex.Message);
                }
            }
        }
    }
}
