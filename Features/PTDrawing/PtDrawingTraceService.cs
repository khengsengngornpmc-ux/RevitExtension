using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CamboBIM.Revit2024.Addin
{
    internal static class PtDrawingTraceService
    {
        private const string FeatureId = "DRAWING_PT";

        public static string GetLogsRootDirectory()
        {
            return ExtensionEnvironment.GetFeatureDirectory(FeatureId, "Logs", true);
        }

        public static void WriteTrace(
            string stage,
            string detail,
            string sourcePath = "",
            IEnumerable<string> extraLines = null)
        {
            try
            {
                string directory = ExtensionEnvironment.EnsureDirectory(GetLogsRootDirectory());
                if (string.IsNullOrWhiteSpace(directory))
                {
                    return;
                }

                string logPath = Path.Combine(
                    directory,
                    "pt-trace-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");

                var lines = new List<string>
                {
                    "Time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    "Stage=" + (stage ?? ""),
                    "Source=" + (sourcePath ?? ""),
                    "Detail=" + (detail ?? "")
                };

                foreach (string line in extraLines ?? Enumerable.Empty<string>())
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        lines.Add(line.Trim());
                    }
                }

                lines.Add("");
                File.AppendAllLines(logPath, lines, new UTF8Encoding(false));
            }
            catch
            {
            }
        }
    }
}
