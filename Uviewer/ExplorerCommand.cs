using System;
using System.IO;
using System.Runtime.InteropServices;

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
        // 마샬링 크래시를 막기 위해 IShellItemArray 대신 IntPtr 사용
        [PreserveSig]
        int GetTitle([In] IntPtr psiItemArray, out IntPtr ppszName);

        [PreserveSig]
        int GetIcon([In] IntPtr psiItemArray, out IntPtr ppszIcon);

        [PreserveSig]
        int GetToolTip([In] IntPtr psiItemArray, out IntPtr ppszInfotip);

        [PreserveSig]
        int GetCanonicalName(out Guid pguidCommandName);

        [PreserveSig]
        int GetState([In] IntPtr psiItemArray, [MarshalAs(UnmanagedType.Bool)] bool fOkToBeSlow, out uint pCommandState);

        [PreserveSig]
        int Invoke([In] IntPtr psiItemArray, [In] IntPtr pbc);

        [PreserveSig]
        int GetFlags(out uint pdwFlags);

        [PreserveSig]
        int EnumSubCommands(out IntPtr ppEnum);
    }

    [Guid("D9614E4F-E02D-4E3F-8C3B-76C1B323E0B9")]
    [ComVisible(true)]
    public class UviewerExplorerCommand : IExplorerCommand
    {
        public int GetTitle(IntPtr psiItemArray, out IntPtr ppszName)
        {
            App.EnterComCall();
            ppszName = IntPtr.Zero;
            try
            {
                ppszName = Marshal.StringToCoTaskMemUni("Open in Uviewer");
                return 0; // S_OK
            }
            catch
            {
                return unchecked((int)0x80004005); // E_FAIL
            }
            finally
            {
                App.LeaveComCall();
            }
        }

        public int GetIcon(IntPtr psiItemArray, out IntPtr ppszIcon)
        {
            App.EnterComCall();
            ppszIcon = IntPtr.Zero;
            try
            {
                string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Uviewer.ico");
                ppszIcon = Marshal.StringToCoTaskMemUni(iconPath);
                return 0; // S_OK
            }
            catch
            {
                return unchecked((int)0x80004005); // E_FAIL
            }
            finally
            {
                App.LeaveComCall();
            }
        }

        public int GetToolTip(IntPtr psiItemArray, out IntPtr ppszInfotip)
        {
            App.EnterComCall();
            ppszInfotip = IntPtr.Zero;
            try
            {
                return unchecked((int)0x80004001); // E_NOTIMPL
            }
            finally
            {
                App.LeaveComCall();
            }
        }

        public int GetCanonicalName(out Guid pguidCommandName)
        {
            App.EnterComCall();
            pguidCommandName = Guid.Empty;
            try
            {
                return unchecked((int)0x80004001); // E_NOTIMPL
            }
            finally
            {
                App.LeaveComCall();
            }
        }

        public int GetState(IntPtr psiItemArray, bool fOkToBeSlow, out uint pCommandState)
        {
            App.EnterComCall();
            pCommandState = 0; // ECS_ENABLED
            try
            {
                return 0; // S_OK
            }
            catch
            {
                return unchecked((int)0x80004005); // E_FAIL
            }
            finally
            {
                App.LeaveComCall();
            }
        }

        public int Invoke(IntPtr psiItemArray, IntPtr pbc)
        {
            App.EnterComCall();
            try
            {
                if (psiItemArray != IntPtr.Zero)
                {
                    IShellItemArray? array = null;
                    IShellItem? item = null;

                    try
                    {
                        array = (IShellItemArray)Marshal.GetObjectForIUnknown(psiItemArray);
                        array.GetCount(out uint count);
                        if (count > 0)
                        {
                            array.GetItemAt(0, out item);
                            item.GetDisplayName(0x80058000, out string path); // SIGDN_FILESYSPATH

                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = Path.Combine(AppContext.BaseDirectory, "Uviewer.exe"),
                                Arguments = $"\"{path}\"",
                                UseShellExecute = true
                            });
                        }
                    }
                    finally
                    {
                        if (item != null) Marshal.ReleaseComObject(item);
                        if (array != null) Marshal.ReleaseComObject(array);
                    }
                }

                return 0; // S_OK
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Invoke error: {ex.Message}");
                return unchecked((int)0x80004005); // E_FAIL
            }
            finally
            {
                App.LeaveComCall();
                App.NotifyComInvokeCompleted();
            }
        }

        public int GetFlags(out uint pdwFlags)
        {
            App.EnterComCall();
            pdwFlags = 0;
            try
            {
                return 0; // S_OK
            }
            finally
            {
                App.LeaveComCall();
            }
        }

        public int EnumSubCommands(out IntPtr ppEnum)
        {
            App.EnterComCall();
            ppEnum = IntPtr.Zero;
            try
            {
                return unchecked((int)0x80004001); // E_NOTIMPL
            }
            finally
            {
                App.LeaveComCall();
            }
        }
    }

    [ComImport]
    [Guid("00000001-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IClassFactory
    {
        [PreserveSig]
        int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);

        [PreserveSig]
        int LockServer(bool fLock);
    }

    // 외부 COM에서 접근 가능하도록 ComVisible 속성 추가
    [ComVisible(true)]
    public class UviewerExplorerCommandFactory : IClassFactory
    {
        public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
        {
            App.EnterComCall();
            ppvObject = IntPtr.Zero;

            if (pUnkOuter != IntPtr.Zero)
            {
                App.LeaveComCall();
                return unchecked((int)0x80040110); // CLASS_E_NOAGGREGATION
            }

            try
            {
                var cmd = new UviewerExplorerCommand();
                IntPtr pUnk = Marshal.GetIUnknownForObject(cmd);
                int hr = Marshal.QueryInterface(pUnk, in riid, out ppvObject);
                Marshal.Release(pUnk);
                return hr;
            }
            catch
            {
                return unchecked((int)0x80004005); // E_FAIL
            }
            finally
            {
                App.LeaveComCall();
            }
        }

        public int LockServer(bool fLock)
        {
            App.SetComServerLock(fLock);
            return 0; // S_OK
        }
    }
}
