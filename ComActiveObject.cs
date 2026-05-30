using System;
using System.Runtime.InteropServices;

namespace CamboBIM.Revit2024.Addin
{
    internal static class ComActiveObject
    {
        public static object GetActiveObject(string progId)
        {
            if (string.IsNullOrWhiteSpace(progId))
            {
                return null;
            }

#if NETFRAMEWORK
            return Marshal.GetActiveObject(progId);
#else
            Guid clsid;
            int hr = CLSIDFromProgID(progId, out clsid);
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            object activeObject;
            GetActiveObject(ref clsid, IntPtr.Zero, out activeObject);
            return activeObject;
#endif
        }

#if !NETFRAMEWORK
        [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
        private static extern int CLSIDFromProgID(string progId, out Guid clsid);

        [DllImport("oleaut32.dll", PreserveSig = false)]
        private static extern void GetActiveObject(
            ref Guid clsid,
            IntPtr reserved,
            [MarshalAs(UnmanagedType.IUnknown)] out object activeObject);
#endif
    }
}
