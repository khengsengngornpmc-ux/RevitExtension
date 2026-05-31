using System;
using Autodesk.Revit.UI;

namespace CamboBIM.Revit2024.Addin
{
    internal static class RevitExecutionBoundary
    {
        private static readonly object SyncRoot = new object();
        private static RevitExecutionQueue _queue;
        private static ExternalEvent _externalEvent;
        private static RevitExecutionExternalEventHandler _handler;

        public static bool IsInitialized
        {
            get
            {
                lock (SyncRoot)
                {
                    return _externalEvent != null && _queue != null;
                }
            }
        }

        public static int PendingRequestCount
        {
            get
            {
                lock (SyncRoot)
                {
                    return _queue == null ? 0 : _queue.Count;
                }
            }
        }

        public static OperationResult Initialize()
        {
            lock (SyncRoot)
            {
                if (_externalEvent != null && _queue != null)
                {
                    return OperationResult.Success("Revit execution boundary already initialized.");
                }

                try
                {
                    if (!ExtensionServiceRegistry.TryResolve(out _queue) || _queue == null)
                    {
                        _queue = new RevitExecutionQueue();
                        ExtensionServiceRegistry.RegisterSingleton(_queue);
                    }

                    _handler = new RevitExecutionExternalEventHandler(_queue);
                    _externalEvent = ExternalEvent.Create(_handler);

                    FeatureTraceWriter.WriteStage(
                        "CORE",
                        "RevitBoundaryStart",
                        "Revit external-event execution boundary initialized.");

                    return OperationResult.Success("Revit execution boundary initialized.");
                }
                catch (Exception ex)
                {
                    _externalEvent = null;
                    _handler = null;
                    MhnkLogger.Error("Revit execution boundary initialization failed.", ex);
                    return OperationResult.Failure(
                        "Revit execution boundary initialization failed.",
                        "REVIT_BOUNDARY_INIT_FAILED",
                        ex);
                }
            }
        }

        public static OperationResult Raise(IRevitExecutionRequest request)
        {
            if (request == null)
            {
                return OperationResult.Failure("Request is empty.", "REVIT_REQUEST_EMPTY");
            }

            lock (SyncRoot)
            {
                if (_externalEvent == null || _queue == null)
                {
                    return OperationResult.Failure(
                        "Revit execution boundary is not initialized.",
                        "REVIT_BOUNDARY_NOT_INITIALIZED");
                }

                try
                {
                    _queue.Enqueue(request);
                    ExternalEventRequest status = _externalEvent.Raise();
                    FeatureTraceWriter.WriteStage(
                        request.FeatureId,
                        "RevitBoundaryRaise",
                        "Revit execution request raised: " + status,
                        new System.Collections.Generic.Dictionary<string, object>
                        {
                            { "request", request.Name },
                            { "pending", _queue.Count }
                        });

                    return OperationResult.Success("Revit execution request raised: " + status);
                }
                catch (Exception ex)
                {
                    MhnkLogger.Error("Could not raise Revit execution request.", ex);
                    return OperationResult.Failure(
                        "Could not raise Revit execution request.",
                        "REVIT_BOUNDARY_RAISE_FAILED",
                        ex);
                }
            }
        }

        public static void Shutdown()
        {
            lock (SyncRoot)
            {
                try
                {
                    _queue?.Clear();
                    _externalEvent?.Dispose();
                }
                catch
                {
                }

                _externalEvent = null;
                _handler = null;
                _queue = null;
            }
        }
    }
}
