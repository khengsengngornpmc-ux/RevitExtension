using System;

namespace CamboBIM.Revit2024.Addin
{
    internal static class ExtensionServiceBootstrapper
    {
        private static bool _started;

        public static bool IsStarted
        {
            get { return _started; }
        }

        public static OperationResult Start()
        {
            try
            {
                ExtensionServiceRegistry.Clear();

                var queue = new RevitExecutionQueue();
                ExtensionServiceRegistry.RegisterSingleton(queue);

                _started = true;
                FeatureTraceWriter.WriteStage(
                    "CORE",
                    "CompositionStart",
                    "Extension service registry started.");

                return OperationResult.Success("Extension service registry started.");
            }
            catch (Exception ex)
            {
                _started = false;
                MhnkLogger.Error("Extension service registry startup failed.", ex);
                return OperationResult.Failure(
                    "Extension service registry startup failed.",
                    "COMPOSITION_START_FAILED",
                    ex);
            }
        }

        public static void Stop()
        {
            RevitExecutionBoundary.Shutdown();
            ExtensionServiceRegistry.Clear();
            _started = false;
            FeatureTraceWriter.WriteStage(
                "CORE",
                "CompositionStop",
                "Extension service registry stopped.");
        }
    }
}
