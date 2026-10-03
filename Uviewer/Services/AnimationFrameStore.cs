using System;
using System.IO;
using K4os.Compression.LZ4;

namespace Uviewer.Services;

// Composited frames can be gigabytes even when the source is only a few MB.
// Keep lossless frames in a private, delete-on-close spool instead of the LOH.
internal sealed class AnimationFrameStore : IDisposable
{
    private readonly object _gate = new();
    private readonly object _writeGate = new();
    private readonly object _readGate = new();
    private FileStream? _stream;
    // Per-animation scratch buffers: reuse without retaining large arrays in a
    // process-wide pool after Stop. Size is bounded by the largest single frame.
    private byte[] _writeBuffer = Array.Empty<byte>();
    private byte[] _readBuffer = Array.Empty<byte>();
    private byte[] _pixelBuffer = Array.Empty<byte>();

    public AnimationFrameStore()
    {
        _stream = new FileStream(Path.Combine(Path.GetTempPath(), $"Uviewer-animation-{Guid.NewGuid():N}.tmp"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536,
            FileOptions.DeleteOnClose | FileOptions.RandomAccess);
    }

    public readonly record struct Frame(long Offset, int Length, int StoredLength, bool Compressed);

    public Frame Write(byte[] pixels)
    {
        lock (_writeGate)
        {
            ObjectDisposedException.ThrowIf(_stream == null, this);
            int capacity = LZ4Codec.MaximumOutputSize(pixels.Length);
            if (_writeBuffer.Length < capacity)
                _writeBuffer = GC.AllocateUninitializedArray<byte>(capacity);
            int encoded = LZ4Codec.Encode(pixels.AsSpan(), _writeBuffer.AsSpan(), LZ4Level.L00_FAST);
            bool compressed = encoded > 0 && encoded < pixels.Length;
            int storedLength = compressed ? encoded : pixels.Length;
            lock (_gate)
            {
                _stream.Position = _stream.Length;
                var frame = new Frame(_stream.Position, pixels.Length, storedLength, compressed);
                _stream.Write(compressed ? _writeBuffer.AsSpan(0, storedLength) : pixels.AsSpan());
                return frame;
            }
        }
    }

    // Upload while holding the read lease so repeated loops reuse one exact-sized
    // pixel buffer instead of allocating a large managed array for every frame.
    public TResult Read<TResult>(Frame frame, Func<byte[], TResult> usePixels)
    {
        lock (_readGate)
        {
            ObjectDisposedException.ThrowIf(_stream == null, this);
            if (_pixelBuffer.Length != frame.Length)
                _pixelBuffer = GC.AllocateUninitializedArray<byte>(frame.Length);
            byte[] pixels = _pixelBuffer;
            if (!frame.Compressed)
            {
                lock (_gate)
                {
                    _stream.Position = frame.Offset;
                    _stream.ReadExactly(pixels);
                }
                return usePixels(pixels);
            }
            if (_readBuffer.Length < frame.StoredLength)
                _readBuffer = GC.AllocateUninitializedArray<byte>(frame.StoredLength);
            lock (_gate)
            {
                _stream.Position = frame.Offset;
                _stream.ReadExactly(_readBuffer.AsSpan(0, frame.StoredLength));
            }
            if (LZ4Codec.Decode(_readBuffer.AsSpan(0, frame.StoredLength), pixels.AsSpan()) != frame.Length)
                throw new InvalidDataException("Incomplete animation frame in temporary storage.");
            return usePixels(pixels);
        }
    }

    public void Dispose()
    {
        lock (_writeGate)
        lock (_readGate)
        lock (_gate)
        {
            _stream?.Dispose();
            _stream = null;
            _writeBuffer = Array.Empty<byte>();
            _readBuffer = Array.Empty<byte>();
            _pixelBuffer = Array.Empty<byte>();
        }
    }
}
