using Microsoft.UI.Input;
using System;
using System.Runtime.InteropServices;
using WinRT;

namespace Uviewer.Services
{
    internal static class TransparentInputCursorFactory
    {
        private const int SmCxCursor = 13;
        private const int SmCyCursor = 14;
        private const int CreateFromHCursorVtableIndex = 6;
        private static readonly Guid InputCursorStaticsInteropId =
            new("AC6F5065-90C4-46CE-BEB7-05E138E54117");

        internal static InputCursor? TryCreate()
        {
            IntPtr cursorHandle = CreateTransparentCursor();
            if (cursorHandle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                using var factory = ActivationFactory.Get(
                    "Microsoft.UI.Input.InputCursor",
                    InputCursorStaticsInteropId);
                IntPtr vtable = Marshal.ReadIntPtr(factory.ThisPtr);
                IntPtr method = Marshal.ReadIntPtr(
                    vtable,
                    CreateFromHCursorVtableIndex * IntPtr.Size);
                var createFromHCursor = Marshal.GetDelegateForFunctionPointer<CreateFromHCursorDelegate>(method);

                int result = createFromHCursor(factory.ThisPtr, cursorHandle, out IntPtr inputCursor);
                Marshal.ThrowExceptionForHR(result);
                try
                {
                    return MarshalInspectable<InputCursor>.FromAbi(inputCursor);
                }
                finally
                {
                    if (inputCursor != IntPtr.Zero)
                    {
                        Marshal.Release(inputCursor);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create the transparent XAML cursor: {ex.Message}");
                return null;
            }
            finally
            {
                DestroyCursor(cursorHandle);
            }
        }

        private static IntPtr CreateTransparentCursor()
        {
            int width = Math.Max(1, GetSystemMetrics(SmCxCursor));
            int height = Math.Max(1, GetSystemMetrics(SmCyCursor));
            int stride = ((width + 15) / 16) * 2;
            var andMask = new byte[stride * height];
            var xorMask = new byte[stride * height];
            Array.Fill(andMask, (byte)0xFF);

            return CreateCursor(
                IntPtr.Zero,
                0,
                0,
                width,
                height,
                andMask,
                xorMask);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateFromHCursorDelegate(
            IntPtr thisPtr,
            IntPtr cursor,
            out IntPtr result);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateCursor(
            IntPtr instance,
            int xHotSpot,
            int yHotSpot,
            int width,
            int height,
            byte[] andPlane,
            byte[] xorPlane);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyCursor(IntPtr cursor);
    }
}
