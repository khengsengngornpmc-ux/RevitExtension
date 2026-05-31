using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CamboBIM.Revit2024.Addin
{
    internal static class PtDrawingSnapshotStore
    {
        private const string FeatureId = "DRAWING_PT";

        public static string GetSnapshotsRootDirectory()
        {
            return ExtensionEnvironment.GetFeatureDirectory(FeatureId, "Snapshots", true);
        }

        public static List<PtDrawingSnapshotRecord> LoadSnapshots()
        {
            var snapshots = new List<PtDrawingSnapshotRecord>();
            string rootDirectory = GetSnapshotsRootDirectory();
            if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
            {
                return snapshots;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(rootDirectory, "pt-import-*.json", SearchOption.AllDirectories);
            }
            catch
            {
                return snapshots;
            }

            foreach (string path in files)
            {
                try
                {
                    OperationResult<PtImportJsonDocument> result = JsonFileStore.Read<PtImportJsonDocument>(path);
                    if (!result.Succeeded || result.Value == null)
                    {
                        continue;
                    }

                    FileInfo info = new FileInfo(path);
                    snapshots.Add(new PtDrawingSnapshotRecord
                    {
                        JsonPath = path,
                        SnapshotTimeLocal = info.Exists ? info.LastWriteTime : DateTime.MinValue,
                        Document = result.Value
                    });
                }
                catch
                {
                }
            }

            return snapshots
                .OrderByDescending(item => item.SnapshotTimeLocal)
                .ThenByDescending(item => item.JsonPath ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static PtDrawingSnapshotRecord FindLatestSnapshotForSourcePath(
            string sourcePath,
            IEnumerable<PtDrawingSnapshotRecord> snapshots)
        {
            string normalizedSourcePath = NormalizeComparablePath(sourcePath);
            string sourceToken = NormalizeFileToken(Path.GetFileNameWithoutExtension(sourcePath) ?? "");

            return (snapshots ?? Enumerable.Empty<PtDrawingSnapshotRecord>())
                .Where(item => item != null)
                .Select(item => new
                {
                    Snapshot = item,
                    Score = GetSnapshotMatchScore(item, normalizedSourcePath, sourceToken)
                })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.Snapshot.SnapshotTimeLocal)
                .Select(item => item.Snapshot)
                .FirstOrDefault();
        }

        private static int GetSnapshotMatchScore(
            PtDrawingSnapshotRecord snapshot,
            string normalizedSourcePath,
            string sourceToken)
        {
            if (snapshot == null)
            {
                return 0;
            }

            string snapshotSourcePath = NormalizeComparablePath(snapshot.SourcePath);
            if (!string.IsNullOrWhiteSpace(normalizedSourcePath) &&
                !string.IsNullOrWhiteSpace(snapshotSourcePath) &&
                string.Equals(snapshotSourcePath, normalizedSourcePath, StringComparison.OrdinalIgnoreCase))
            {
                return 4;
            }

            if (string.IsNullOrWhiteSpace(sourceToken))
            {
                return 0;
            }

            string snapshotToken = NormalizeFileToken(Path.GetFileNameWithoutExtension(snapshot.SourcePath) ?? "");
            if (!string.IsNullOrWhiteSpace(snapshotToken) &&
                string.Equals(snapshotToken, sourceToken, StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            string displayToken = NormalizeFileToken(snapshot.DisplayName);
            if (!string.IsNullOrWhiteSpace(displayToken) &&
                string.Equals(displayToken, sourceToken, StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            string projectToken = NormalizeFileToken(snapshot.Document?.Source?.ProjectName ?? "");
            if (!string.IsNullOrWhiteSpace(projectToken) &&
                string.Equals(projectToken, sourceToken, StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            return 0;
        }

        private static string NormalizeComparablePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            try
            {
                return Path.GetFullPath(path).Trim();
            }
            catch
            {
                return path.Trim();
            }
        }

        private static string NormalizeFileToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            return Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "");
        }
    }
}
