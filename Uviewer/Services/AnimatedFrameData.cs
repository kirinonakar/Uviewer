using System;

namespace Uviewer.Services;

internal sealed class AnimatedFrameData
{
    public AnimatedFrameData(AnimationFrameStore store, byte[] pixels, int width, int height, bool isDisplayReady)
    {
        _store = store;
        _pixels = pixels;
        Width = width;
        Height = height;
        IsDisplayReady = isDisplayReady;
    }

    private AnimatedFrameData(AnimationFrameStore store, AnimationFrameStore.Frame storedFrame,
        int width, int height)
    {
        _store = store;
        _storedFrame = storedFrame;
        Width = width;
        Height = height;
        IsDisplayReady = true;
    }

    private readonly AnimationFrameStore _store;
    private readonly object _pixelGate = new();
    private AnimationFrameStore.Frame _storedFrame;
    private byte[]? _pixels;

    public TResult WithPixels<TResult>(Func<byte[], TResult> usePixels)
    {
        byte[]? pixels;
        AnimationFrameStore.Frame storedFrame;
        lock (_pixelGate)
        {
            pixels = _pixels;
            storedFrame = _storedFrame;
        }
        return pixels != null ? usePixels(pixels) : _store.Read(storedFrame, usePixels);
    }

    public void Persist()
    {
        byte[]? pixels;
        lock (_pixelGate) pixels = _pixels;
        if (pixels == null) return;

        // Compression never holds the pixel lease: the first pass can upload
        // the decoded bytes immediately while its lossless cache is written.
        var storedFrame = _store.Write(pixels);
        lock (_pixelGate)
        {
            _storedFrame = storedFrame;
            _pixels = null;
        }
    }

    public AnimatedFrameData StoreProcessedPixels(AnimationFrameStore store, byte[] pixels, int width, int height) =>
        new(store, store.Write(pixels), width, height);
    public int Width { get; }
    public int Height { get; }
    public bool IsDisplayReady { get; }
}
