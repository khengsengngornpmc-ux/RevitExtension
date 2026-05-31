using System;
using System.IO;

namespace CamboBIM.Revit2024.Addin
{
    internal static class MhnkLogger
    {
        private static readonly object SyncRoot = new object();

        public static string LogDirectory
        {
            get
            {
                return ExtensionEnvironment.GetLogsDirectory();
            }
        }

        public static string CurrentLogPath
        {
            get
            {
                return ExtensionEnvironment.GetCurrentLogFilePath("MHNK-Revit" + CamboBimRuntime.RevitYear);
            }
        }

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Warn(string message)
        {
            Write("WARN", message, null);
        }

        public static void Error(string message, Exception exception)
        {
            Write("ERROR", message, exception);
        }

        private static void Write(string level, string message, Exception exception)
        {
            try
            {
                lock (SyncRoot)
                {
                    ExtensionEnvironment.EnsureDirectory(LogDirectory);
                    string line =
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                        " [" + (level ?? "INFO") + "] " +
                        (message ?? "");

                    if (exception != null)
                    {
                        line += Environment.NewLine + exception;
                    }

                    File.AppendAllText(CurrentLogPath, line + Environment.NewLine);
                }
            }
            catch
            {
                // Logging must never break Revit command execution.
            }
        }
    }
}
