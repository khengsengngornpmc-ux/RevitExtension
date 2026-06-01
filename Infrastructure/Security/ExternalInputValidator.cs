using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CamboBIM.Revit2024.Addin
{
    internal static class ExternalInputValidator
    {
        private static readonly Regex MultiWhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly HashSet<string> ReservedDeviceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "COM1",
            "COM2",
            "COM3",
            "COM4",
            "COM5",
            "COM6",
            "COM7",
            "COM8",
            "COM9",
            "LPT1",
            "LPT2",
            "LPT3",
            "LPT4",
            "LPT5",
            "LPT6",
            "LPT7",
            "LPT8",
            "LPT9"
        };

        public static OperationResult<string> ValidateReadableFile(
            string path,
            string featureId,
            params string[] allowedExtensions)
        {
            string normalizedFeatureId = string.IsNullOrWhiteSpace(featureId) ? "GENERAL" : featureId.Trim();

            if (string.IsNullOrWhiteSpace(path))
            {
                return OperationResult<string>.Failure("File path is empty.", "INPUT_PATH_EMPTY");
            }

            if (ContainsControlCharacter(path))
            {
                return OperationResult<string>.Failure("File path contains invalid control characters.", "INPUT_PATH_CONTROL_CHAR");
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path.Trim());
            }
            catch (Exception ex)
            {
                return OperationResult<string>.Failure("File path is invalid: " + path, "INPUT_PATH_INVALID", ex);
            }

            OperationResult fileNameResult = ValidateFileName(fullPath);
            if (!fileNameResult.Succeeded)
            {
                return OperationResult<string>.Failure(fileNameResult.Message, fileNameResult.ErrorCode, fileNameResult.Exception);
            }

            if (!File.Exists(fullPath))
            {
                return OperationResult<string>.Failure("File was not found: " + fullPath, "INPUT_FILE_NOT_FOUND");
            }

            string extension = (Path.GetExtension(fullPath) ?? string.Empty).ToLowerInvariant();
            if (allowedExtensions != null && allowedExtensions.Length > 0)
            {
                bool allowed = false;
                for (int i = 0; i < allowedExtensions.Length; i++)
                {
                    string allowedExtension = NormalizeExtension(allowedExtensions[i]);
                    if (string.Equals(extension, allowedExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    return OperationResult<string>.Failure(
                        "File type is not allowed for " + normalizedFeatureId + ": " + extension,
                        "INPUT_EXTENSION_NOT_ALLOWED");
                }
            }

            return OperationResult<string>.Success(fullPath, "Validated file: " + fullPath);
        }

        public static OperationResult<string> ValidateDirectory(string path, string featureId)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return OperationResult<string>.Failure("Directory path is empty.", "INPUT_DIRECTORY_EMPTY");
            }

            if (ContainsControlCharacter(path))
            {
                return OperationResult<string>.Failure("Directory path contains invalid control characters.", "INPUT_DIRECTORY_CONTROL_CHAR");
            }

            try
            {
                string fullPath = Path.GetFullPath(path.Trim());
                if (!Directory.Exists(fullPath))
                {
                    return OperationResult<string>.Failure("Directory was not found: " + fullPath, "INPUT_DIRECTORY_NOT_FOUND");
                }

                return OperationResult<string>.Success(fullPath, "Validated directory: " + fullPath);
            }
            catch (Exception ex)
            {
                string normalizedFeatureId = string.IsNullOrWhiteSpace(featureId) ? "GENERAL" : featureId.Trim();
                return OperationResult<string>.Failure(
                    "Directory path is invalid for " + normalizedFeatureId + ": " + path,
                    "INPUT_DIRECTORY_INVALID",
                    ex);
            }
        }

        public static string SanitizeImportedLabel(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (char.IsControl(current))
                {
                    continue;
                }

                builder.Append(current);
            }

            string text = MultiWhitespaceRegex.Replace(builder.ToString(), " ").Trim();
            if (maxLength > 0 && text.Length > maxLength)
            {
                text = text.Substring(0, maxLength).Trim();
            }

            return text;
        }

        private static OperationResult ValidateFileName(string fullPath)
        {
            string fileName = Path.GetFileNameWithoutExtension(fullPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return OperationResult.Failure("File name is empty.", "INPUT_FILE_NAME_EMPTY");
            }

            if (ReservedDeviceNames.Contains(fileName.Trim()))
            {
                return OperationResult.Failure("Reserved Windows device name is not allowed: " + fileName, "INPUT_RESERVED_DEVICE_NAME");
            }

            return OperationResult.Success();
        }

        private static bool ContainsControlCharacter(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsControl(value[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                return string.Empty;
            }

            string text = extension.Trim().ToLowerInvariant();
            return text.StartsWith(".", StringComparison.Ordinal) ? text : "." + text;
        }
    }
}
