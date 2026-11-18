using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Uviewer.Services;

internal sealed class LibJxlDecoder : IDisposable
{
    private const string LibraryName = @"Libs\libjxl\UviewerJxl.dll";
    private readonly DecoderHandle _decoder;
    private readonly ImageInfo _info;
    private byte[]? _frameBuffer;
    private bool _finished;

    public LibJxlDecoder(byte[] bytes, int maxDimension)
    {
        if (bytes.Length == 0) throw new InvalidDataException("The JPEG XL input is empty.");

        _decoder = new DecoderHandle(bytes, out _info);
        if (_decoder.IsInvalid)
        {
            _decoder.Dispose();
            throw new InvalidDataException("Could not read the JPEG XL image header.");
        }
        if (_info.Width == 0 || _info.Height == 0 ||
            _info.Width > maxDimension || _info.Height > maxDimension)
        {
            _decoder.Dispose();
            throw new InvalidDataException("The JPEG XL image exceeds the supported dimensions.");
        }
    }

    public int Width => (int)_info.Width;
    public int Height => (int)_info.Height;
    public bool IsAnimated => _info.Animated != 0;
    public uint LoopCount => _info.Loops;

    public DecodedFrame? ReadFrame(float hdrDisplayPeak, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_finished) return null;

        int bytesPerPixel = _info.PeakNits > 0 ? 16 : 4;
        var buffer = _frameBuffer ??= new byte[checked(Width * Height * bytesPerPixel)];
        int result = UviewerJxlReadFrame(_decoder, buffer, (nuint)buffer.Length,
            out uint duration, out double millisecondsPerTick, out int isLast);
        _finished = isLast != 0 || result <= 0;
        if (result < 0) throw new InvalidDataException("Could not decode the JPEG XL frame.");
        if (result == 0) return null;

        double milliseconds = duration * millisecondsPerTick;
        int delayMs = (int)Math.Clamp(Math.Round(milliseconds), 1, int.MaxValue);
        var pixels = JxlFramePixels.Convert(buffer, _info.PeakNits, hdrDisplayPeak, token);
        return new DecodedFrame(pixels, delayMs, _finished,
            _info.PeakNits > 0 && hdrDisplayPeak > 0 ? hdrDisplayPeak / 80f : 0);
    }

    public void Dispose() => _decoder.Dispose();

    internal readonly record struct DecodedFrame(
        byte[] Pixels, int DelayMs, bool IsLast, float HdrNormalizationScale);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct ImageInfo
    {
        public readonly uint Width;
        public readonly uint Height;
        public readonly uint Animated;
        public readonly uint Loops;
        public readonly float PeakNits;
    }

    private sealed class DecoderHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private GCHandle _input;

        public DecoderHandle(byte[] bytes, out ImageInfo info) : base(ownsHandle: true)
        {
            _input = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                SetHandle(UviewerJxlOpen(_input.AddrOfPinnedObject(), (nuint)bytes.Length, out info));
            }
            finally
            {
                if (IsInvalid) _input.Free();
            }
        }

        protected override bool ReleaseHandle()
        {
            UviewerJxlClose(handle);
            _input.Free();
            return true;
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    [DllImport(LibraryName, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr UviewerJxlOpen(IntPtr bytes, nuint size, out ImageInfo info);

    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    [DllImport(LibraryName, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern void UviewerJxlClose(IntPtr decoder);

    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    [DllImport(LibraryName, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int UviewerJxlReadFrame(DecoderHandle decoder, [Out] byte[] pixels, nuint size,
        out uint duration, out double millisecondsPerTick, out int isLast);
}
