using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Collections.Generic;
using System.IO;

namespace Uviewer
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    public interface IShellItem
    {
        void BindToHandler(IntPtr pbc, [In] ref Guid dbh, [In] ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
    public interface IShellItemArray
    {
        void BindToHandler(IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, out IntPtr ppv);
        void GetPropertyStore(uint flags, [In] ref Guid riid, out IntPtr ppv);
        void GetPropertyDescriptionList([In] ref Guid keyType, [In] ref Guid riid, out IntPtr ppv);
        void GetAttributes(uint AttribFlags, uint sfgaoMask, out uint psfgaoAttribs);
        void GetCount(out uint pdwNumItems);
        void GetItemAt(uint dwIndex, out IShellItem ppsi);
        void EnumItems(out IntPtr ppenumShellItems);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9")]
    public interface IExplorerCommand
    {
        void GetTitle(IShellItemArray psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetIcon(IShellItemArray psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszIcon);
        void GetToolTip(IShellItemArray psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszInfotip);
        void GetCanonicalName(out Guid pguidCommandName);
        void GetState(IShellItemArray psiItemArray, bool fOkToBeSlow, out uint pCommandState);
        void Invoke(IShellItemArray psiItemArray, IntPtr pbc);
        void GetFlags(out uint pdwFlags);
        void EnumSubCommands(out IntPtr ppEnum);
    }

    [Guid("D9614E4F-E02D-4E3F-8C3B-76C1B323E0B9")]
    [ComVisible(true)]
    public class UviewerExplorerCommand : IExplorerCommand
    {
        public void GetTitle(IShellItemArray psiItemArray, out string ppszName)
        {
            ppszName = "Open in Uviewer";
        }

        public void GetIcon(IShellItemArray psiItemArray, out string ppszIcon)
        {
            // Use the executable itself as the icon source (resource index 0)
            // This is more reliable for shell extensions to resolve the icon
            ppszIcon = Path.Combine(AppContext.BaseDirectory, "Uviewer.exe");
        }

        public void GetToolTip(IShellItemArray psiItemArray, out string ppszInfotip)
        {
            ppszInfotip = "Open the selected item in Uviewer";
        }

        public void GetCanonicalName(out Guid pguidCommandName)
        {
            pguidCommandName = Guid.Parse("D9614E4F-E02D-4E3F-8C3B-76C1B323E0B9");
        }

        public void GetState(IShellItemArray psiItemArray, bool fOkToBeSlow, out uint pCommandState)
        {
            pCommandState = 0; // ECS_ENABLED
        }

        public void Invoke(IShellItemArray psiItemArray, IntPtr pbc)
        {
            if (psiItemArray != null)
            {
                psiItemArray.GetCount(out uint count);
                if (count > 0)
                {
                    psiItemArray.GetItemAt(0, out IShellItem item);
                    item.GetDisplayName(0x80058000, out string path); // SIGDN_FILESYSPATH

                    // Launch Uviewer.exe with the path
                    // Since we are already in Uviewer.exe (running as COM server), 
                    // we can either start a new process or use the existing logic.
                    // The simplest is to start a new instance or let the App handle it.
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = Path.Combine(AppContext.BaseDirectory, "Uviewer.exe"),
                        Arguments = $"\"{path}\"",
                        UseShellExecute = true
                    });
                }
            }
        }

        public void GetFlags(out uint pdwFlags)
        {
            pdwFlags = 0;
        }

        public void EnumSubCommands(out IntPtr ppEnum)
        {
            ppEnum = IntPtr.Zero;
        }
    }

    [ComImport]
    [Guid("00000001-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IClassFactory
    {
        void CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);
        void LockServer(bool fLock);
    }

    public class UviewerExplorerCommandFactory : IClassFactory
    {
        public void CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
        {
            if (pUnkOuter != IntPtr.Zero)
                throw new COMException("Not supported", -2147221232); // CLASS_E_NOAGGREGATION

            if (riid == typeof(IExplorerCommand).GUID || riid == Guid.Parse("00000000-0000-0000-C000-000000000046")) // IUnknown
            {
                var cmd = new UviewerExplorerCommand();
                ppvObject = Marshal.GetComInterfaceForObject(cmd, typeof(IExplorerCommand));
            }
            else
            {
                throw new COMException("No such interface", -2147467262); // E_NOINTERFACE
            }
        }

        public void LockServer(bool fLock) { }
    }
}
