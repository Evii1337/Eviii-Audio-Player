using Exiled.API.Features;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EviAudio.API;

public sealed class AudioPcmStream : IDisposable
{
    private const int MaxBufferedChunks = 256;

    private readonly Process _process;
    private readonly Task<string> _stderrTask;
    private readonly Task _readTask;
    private readonly Queue<float[]> _chunks = new();
    private readonly object _lock = new();
    private int _chunkOffset;
    private bool _completed;
    private volatile bool _disposed;
    private Exception _error;

    internal AudioPcmStream(Process process)
    {
        _process = process;
        _stderrTask = process.StandardError.ReadToEndAsync();
        _readTask = Task.Run(ReadLoop);
    }

    public bool IsCompleted
    {
        get
        {
            lock (_lock)
                return _completed && _chunks.Count == 0;
        }
    }

    public Exception ConsumeError()
    {
        lock (_lock)
        {
            var error = _error;
            _error = null;
            return error;
        }
    }

    public int Read(float[] destination, int offset, int count)
    {
        int written = 0;

        lock (_lock)
        {
            while (written < count && _chunks.Count > 0)
            {
                float[] chunk = _chunks.Peek();
                int available = chunk.Length - _chunkOffset;
                int copy = Math.Min(available, count - written);

                Array.Copy(chunk, _chunkOffset, destination, offset + written, copy);
                _chunkOffset += copy;
                written += copy;

                if (_chunkOffset < chunk.Length)
                    continue;

                _chunks.Dequeue();
                _chunkOffset = 0;
            }
        }

        return written;
    }

    public void Dispose()
    {
        _disposed = true;

        try
        {
            if (!_process.HasExited)
                _process.Kill();
        }
        catch { }

        try { _readTask.Wait(1000); } catch { }
        try { _process.Dispose(); } catch { }
    }

    private void ReadLoop()
    {
        byte[] raw = new byte[AudioClipPlayback.PacketSize * sizeof(float) * 16 + 4];
        int carry = 0;

        try
        {
            Stream stream = _process.StandardOutput.BaseStream;

            while (!_disposed)
            {
                WaitForBufferSpace();
                if (_disposed)
                    break;

                int read = stream.Read(raw, carry, raw.Length - carry);
                if (read <= 0)
                    break;

                int total = carry + read;
                int usable = total - total % sizeof(float);

                if (usable > 0)
                    Enqueue(PcmDecoder.BytesToFloats(raw, usable));

                carry = total - usable;
                if (carry > 0)
                    Buffer.BlockCopy(raw, usable, raw, 0, carry);
            }

            try { _process.WaitForExit(1000); } catch { }

            if (!_disposed && _process.ExitCode != 0)
            {
                string stderr = string.Empty;
                try { stderr = _stderrTask.Wait(1000) ? _stderrTask.Result : string.Empty; } catch { }
                throw new InvalidOperationException($"FFmpeg stream exited with code {_process.ExitCode}: {stderr}");
            }
        }
        catch (Exception ex)
        {
            lock (_lock)
                _error = ex;

            Log.Error($"Audio stream error: {ex.Message}");
        }
        finally
        {
            lock (_lock)
                _completed = true;
        }
    }

    private void Enqueue(float[] samples)
    {
        lock (_lock)
            _chunks.Enqueue(samples);
    }

    private void WaitForBufferSpace()
    {
        while (!_disposed)
        {
            lock (_lock)
            {
                if (_chunks.Count < MaxBufferedChunks)
                    return;
            }

            Thread.Sleep(1);
        }
    }
}
