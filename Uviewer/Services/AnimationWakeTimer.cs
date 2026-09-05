using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace Uviewer.Services
{
    // One-shot waits: the UI arms the next deadline only after processing this one.
    // No periodic callbacks accumulate when the dispatcher/compositor is stalled.
    internal sealed class AnimationWakeTimer : IDisposable
    {
        private readonly object _gate = new();
        private readonly EventWaitHandle _timer;
        private readonly ManualResetEvent _stop = new(false);
        private bool _disposed;

        public AnimationWakeTimer(Action wake)
        {
            var handle = CreateWaitableTimerExW(IntPtr.Zero, IntPtr.Zero, 0x2, 0x00100002);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                _stop.Dispose();
                throw new Win32Exception(error);
            }

            _timer = new EventWaitHandle(false, EventResetMode.AutoReset);
            _timer.SafeWaitHandle.Dispose();
            _timer.SafeWaitHandle = handle;
            var thread = new Thread(() =>
            {
                try
                {
                    WaitHandle[] handles = { _stop, _timer };
                    while (WaitHandle.WaitAny(handles) == 1)
                    {
                        lock (_gate)
                        {
                            if (_disposed) return;
                            wake();
                        }
                    }
                }
                finally
                {
                    lock (_gate)
                    {
                        _disposed = true;
                        _timer.Dispose();
                        _stop.Dispose();
                    }
                }
            }) { IsBackground = true, Name = "WebP frame timer" };
            try { thread.Start(); }
            catch
            {
                _timer.Dispose();
                _stop.Dispose();
                throw;
            }
        }

        public bool TrySchedule(TimeSpan delay)
        {
            lock (_gate)
            {
                if (_disposed) return false;
                long dueTime = -Math.Max(1L, delay.Ticks);
                return SetWaitableTimer(_timer.SafeWaitHandle, ref dueTime, 0,
                    IntPtr.Zero, IntPtr.Zero, false);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _stop.Set();
            }
        }

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern SafeWaitHandle CreateWaitableTimerExW(
            IntPtr attributes, IntPtr name, uint flags, uint access);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime,
            int period, IntPtr callback, IntPtr argument, [MarshalAs(UnmanagedType.Bool)] bool resume);
    }
}
