using System;

namespace CamboBIM.Revit2024.Addin
{
    internal static class CamboBimRuntime
    {
#if REVIT2025
        public const string RevitYear = "2025";
        public const string AddInName = "CamboBIM.Revit2025.Addin";
        public const string DefaultProductCode = "CBIM_RVT2025_EXTENSION";
#else
        public const string RevitYear = "2024";
        public const string AddInName = "CamboBIM.Revit2024.Addin";
        public const string DefaultProductCode = "CBIM_RVT2024_EXTENSION";
#endif

        public static string AssemblyName
        {
            get { return typeof(CamboBimRuntime).Assembly.GetName().Name; }
        }

        public static string GetCommandClassName(Type commandType)
        {
            return commandType == null ? "" : (commandType.FullName ?? commandType.Name);
        }

        public static string GetPackUri(string relativeUri)
        {
            string cleanRelativeUri = (relativeUri ?? "").TrimStart('/', '\\');
            return "pack://application:,,,/" + AssemblyName + ";component/" + cleanRelativeUri;
        }
    }
}
