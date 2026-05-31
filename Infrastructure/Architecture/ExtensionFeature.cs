using System;
using System.Collections.Generic;

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class ExtensionFeature
    {
        private readonly string[] _legacyEntryPoints;

        public ExtensionFeature(
            string id,
            string displayName,
            string workspace,
            string description,
            params string[] legacyEntryPoints)
        {
            Id = string.IsNullOrWhiteSpace(id) ? "UNKNOWN" : id.Trim();
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName.Trim();
            Workspace = string.IsNullOrWhiteSpace(workspace) ? "General" : workspace.Trim();
            Description = string.IsNullOrWhiteSpace(description) ? string.Empty : description.Trim();
            _legacyEntryPoints = legacyEntryPoints ?? Array.Empty<string>();
        }

        public string Id { get; private set; }

        public string DisplayName { get; private set; }

        public string Workspace { get; private set; }

        public string Description { get; private set; }

        public IReadOnlyList<string> LegacyEntryPoints
        {
            get { return _legacyEntryPoints; }
        }
    }
}
