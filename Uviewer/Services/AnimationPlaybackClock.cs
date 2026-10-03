using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Uviewer.Services;

// A monotonic media clock. Callers serialize AddFrame/Advance with frame publication.
internal sealed class AnimationPlaybackClock
{
    private readonly List<long> _frameEnds = new();
    private readonly long _frequency;
    private long _cycleStart;
    private bool _waitingForFrame;

    public AnimationPlaybackClock(long frequency = 0)
    {
        _frequency = frequency > 0 ? frequency : Stopwatch.Frequency;
    }

    public int FrameIndex { get; private set; }
    public long NextDeadline { get; private set; }

    public void Reset()
    {
        _frameEnds.Clear();
        FrameIndex = 0;
        NextDeadline = 0;
        _cycleStart = 0;
        _waitingForFrame = false;
    }

    public void AddFrame(int delayMs)
    {
        long duration = Math.Max(1L, (long)Math.Ceiling(delayMs * (double)_frequency / 1000.0));
        _frameEnds.Add((_frameEnds.Count == 0 ? 0 : _frameEnds[^1]) + duration);
    }

    public void Start(long now)
    {
        FrameIndex = 0;
        _cycleStart = now;
        _waitingForFrame = false;
        NextDeadline = now + _frameEnds[0];
    }

    public bool Advance(long now, bool decodingComplete)
    {
        if (_frameEnds.Count == 0 || now < NextDeadline) return false;

        if (_waitingForFrame)
        {
            if (!decodingComplete && FrameIndex + 1 >= _frameEnds.Count)
            {
                ScheduleDecodeRetry(now);
                return false;
            }

            // Resume at the next frame without charging decode stalls to its duration.
            _cycleStart = now - _frameEnds[FrameIndex];
            _waitingForFrame = false;
        }

        long elapsed = now - _cycleStart;
        long cycle = _frameEnds[^1];
        if (decodingComplete && elapsed >= cycle)
        {
            // Handles sleep/resume and busy dispatchers without iterating over missed loops.
            _cycleStart += (elapsed / cycle) * cycle;
            elapsed %= cycle;
        }
        else if (!decodingComplete && elapsed >= cycle)
        {
            int last = _frameEnds.Count - 1;
            bool changed = FrameIndex != last;
            FrameIndex = last;
            _waitingForFrame = true;
            ScheduleDecodeRetry(now);
            return changed;
        }

        int index = _frameEnds.BinarySearch(elapsed);
        index = index >= 0 ? index + 1 : ~index;
        bool advanced = FrameIndex != index;
        FrameIndex = index;
        NextDeadline = _cycleStart + _frameEnds[index];
        return advanced;
    }

    private void ScheduleDecodeRetry(long now) =>
        NextDeadline = now + Math.Max(1L, _frequency / 500); // 2 ms, not another full frame delay.
}
