using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
#if REVIT2027
[assembly: AssemblyTitle("MHNK Revit 2027 Extension")]
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]
#elif REVIT2026
[assembly: AssemblyTitle("MHNK Revit 2026 Extension")]
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]
#elif REVIT2025
[assembly: AssemblyTitle("MHNK Revit 2025 Extension")]
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]
#elif REVIT2024
[assembly: AssemblyTitle("MHNK Revit 2024 Extension")]
#elif REVIT2023
[assembly: AssemblyTitle("MHNK Revit 2023 Extension")]
#else
[assembly: AssemblyTitle("MHNK Revit Extension")]
#endif
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Mohanokor Engineering Construction")]
#if REVIT2027
[assembly: AssemblyProduct("MHNK Revit 2027 Extension")]
#elif REVIT2026
[assembly: AssemblyProduct("MHNK Revit 2026 Extension")]
#elif REVIT2025
[assembly: AssemblyProduct("MHNK Revit 2025 Extension")]
#elif REVIT2024
[assembly: AssemblyProduct("MHNK Revit 2024 Extension")]
#elif REVIT2023
[assembly: AssemblyProduct("MHNK Revit 2023 Extension")]
#else
[assembly: AssemblyProduct("MHNK Revit Extension")]
#endif
[assembly: AssemblyCopyright("Copyright ©  2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("d4265daa-0966-461a-8900-c48c10260677")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
