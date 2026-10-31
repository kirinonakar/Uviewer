using K4os.Compression.LZ4;
using System;
using System.Buffers.Binary;

namespace Uviewer.Services;

// Tightly packed, premultiplied BGRA pixels ready for a single Win2D upload.
internal sealed record PeleDiagramFrame(byte[] Pixels, int Width, int Height, float Dpi);

internal static class PeleDiagramFrameCodec
{
    private const int HeaderSize = 20;
    private const int Magic = 0x31454C50; // PLE1

    public static byte[] Encode(PeleDiagramFrame frame)
    {
        var data = GC.AllocateUninitializedArray<byte>(HeaderSize + LZ4Codec.MaximumOutputSize(frame.Pixels.Length));
        BinaryPrimitives.WriteInt32LittleEndian(data, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), frame.Width);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), frame.Height);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), BitConverter.SingleToInt32Bits(frame.Dpi));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), frame.Pixels.Length);
        int length = LZ4Codec.Encode(frame.Pixels.AsSpan(), data.AsSpan(HeaderSize), LZ4Level.L00_FAST);
        if (length <= 0) throw new InvalidOperationException("Could not cache the Pele diagram.");
        Array.Resize(ref data, HeaderSize + length);
        return data;
    }

    public static PeleDiagramFrame? Decode(byte[] data)
    {
        if (data.Length <= HeaderSize || BinaryPrimitives.ReadInt32LittleEndian(data) != Magic) return null;
        int width = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
        int height = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8));
        float dpi = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(12)));
        int length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16));
        if (width < 1 || height < 1 || width > 8192 || height > 8192 ||
            (long)width * height > 16 * 1024 * 1024 || length != (long)width * height * 4 ||
            !float.IsFinite(dpi) || dpi <= 0 || dpi > 192) return null;
        var pixels = GC.AllocateUninitializedArray<byte>(length);
        if (LZ4Codec.Decode(data.AsSpan(HeaderSize), pixels.AsSpan()) != length) return null;
        return new(pixels, width, height, dpi);
    }
}
