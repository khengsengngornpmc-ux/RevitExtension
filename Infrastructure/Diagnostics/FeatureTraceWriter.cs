using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CamboBIM.Revit2024.Addin
{
    internal static class FeatureTraceWriter
    {
        private static readonly object SyncRoot = new object();

        public static string WriteStage(string featureId, string stage, string message, IDictionary<string, object> context = null)
        {
            try
            {
                string directory = ExtensionEnvironment.EnsureDirectory(
                    ExtensionEnvironment.GetFeatureLogDirectory(featureId));
                string path = Path.Combine(
                    directory,
                    "trace-" + DateTime.Now.ToString("yyyyMMdd") + ".log");

                var line = new StringBuilder();
                line.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                line.Append(" [");
                line.Append(string.IsNullOrWhiteSpace(stage) ? "Trace" : stage.Trim());
                line.Append("] ");
                line.Append(message ?? string.Empty);

                if (context != null && context.Count > 0)
                {
                    line.Append(" | ");
                    line.Append(CamboBimJson.Serialize(context));
                }

                lock (SyncRoot)
                {
                    File.AppendAllText(path, line.ToString() + Environment.NewLine, new UTF8Encoding(false));
                }

                return path;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
