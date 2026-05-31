using System;
using System.IO;
using System.Text;

namespace CamboBIM.Revit2024.Addin
{
    internal static class JsonFileStore
    {
        public static OperationResult<T> Read<T>(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return OperationResult<T>.Failure("JSON path is empty.", "JSON_PATH_EMPTY");
                }

                if (!File.Exists(path))
                {
                    return OperationResult<T>.Failure("JSON file was not found: " + path, "JSON_NOT_FOUND");
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                T value = CamboBimJson.Deserialize<T>(json);
                return OperationResult<T>.Success(value, "Loaded JSON: " + path);
            }
            catch (Exception ex)
            {
                return OperationResult<T>.Failure("Could not read JSON file: " + path, "JSON_READ_FAILED", ex);
            }
        }

        public static bool TryRead<T>(string path, out T value, out string failureMessage)
        {
            OperationResult<T> result = Read<T>(path);
            value = result.Value;
            failureMessage = result.Succeeded ? string.Empty : result.ToString();
            return result.Succeeded;
        }

        public static OperationResult Write(string path, object value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return OperationResult.Failure("JSON path is empty.", "JSON_PATH_EMPTY");
                }

                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    ExtensionEnvironment.EnsureDirectory(directory);
                }

                string json = CamboBimJson.Serialize(value) ?? "{}";
                File.WriteAllText(path, json, new UTF8Encoding(false));
                return OperationResult.Success("Saved JSON: " + path);
            }
            catch (Exception ex)
            {
                return OperationResult.Failure("Could not write JSON file: " + path, "JSON_WRITE_FAILED", ex);
            }
        }

        public static bool TryWrite(string path, object value, out string failureMessage)
        {
            OperationResult result = Write(path, value);
            failureMessage = result.Succeeded ? string.Empty : result.ToString();
            return result.Succeeded;
        }
    }
}
