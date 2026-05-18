using Exiled.API.Features;
using NVorbis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace EviAudio.API;

public static class PcmDecoder
{
    public const int TargetSampleRate = 48000;
    public const int TargetChannels = 1;

    private static readonly object FfmpegLock = new();
    private static readonly object YtDlpLock = new();
    private static string _cachedFfmpegPath;
    private static string _lastFfmpegSearch = string.Empty;
    private static string _cachedYtDlpPath;
    private static string _lastYtDlpSearch = string.Empty;

    public static Task<AudioClipData> DecodeFileAsync(string path, string name)
        => Task.Run(() => DecodeFile(path, name));

    public static AudioClipData DecodeFile(string path, string name, float pitchShift = 0f)
    {
        string input = ResolveExternalInput(path);
        string ext = IsUrl(input) ? string.Empty : Path.GetExtension(input).ToLowerInvariant();

        var decoded = ext switch
        {
            ".ogg" => DecodeOgg(input),
            ".wav" => DecodeWav(input),
            _ => DecodeFfmpeg(input),
        };

        float[] samples = decoded.samples;
        int sampleRate = decoded.rate;
        int channels = decoded.channels;

        if (channels != TargetChannels || sampleRate != TargetSampleRate)
            samples = Resample(samples, sampleRate, channels, TargetSampleRate, TargetChannels);

        int metadataSampleCount = samples.Length;

        if (Math.Abs(pitchShift) >= 0.01f)
            samples = PitchShifter.Shift(samples, pitchShift);

        Normalize(samples);

        AudioTrackMetadata metadata = ReadMetadata(path, metadataSampleCount, TargetSampleRate);
        return new AudioClipData(name, TargetSampleRate, TargetChannels, samples, metadata);
    }

    public static bool IsUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public static bool ShouldStream(string value) => IsUrl(value);

    public static AudioPcmStream OpenStream(string input)
        => OpenStream(input, TimeSpan.Zero, null);

    public static AudioPcmStream OpenStream(string input, TimeSpan startAt)
        => OpenStream(input, startAt, null);

    public static AudioPcmStream OpenStream(string input, TimeSpan startAt, Func<bool> shouldContinue)
    {
        string ffmpegPath = FindFfmpeg() ?? throw new FileNotFoundException(
            $"FFmpeg not found. Place ffmpeg in EXILED/Plugins/EviAudio/ffmpeg or in PATH. Checked: {_lastFfmpegSearch}");

        if (shouldContinue != null && !shouldContinue())
            throw new OperationCanceledException("Stream load was superseded before FFmpeg started.");

        string resolvedInput = ResolveExternalInput(input, shouldContinue);
        if (shouldContinue != null && !shouldContinue())
            throw new OperationCanceledException("Stream load was superseded before FFmpeg started.");

        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel",
            "error",
            "-reconnect",
            "1",
            "-reconnect_streamed",
            "1",
            "-reconnect_delay_max",
            "5",
        };

        if (startAt > TimeSpan.Zero)
        {
            args.Add("-ss");
            args.Add(startAt.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        }

        args.AddRange(["-i", resolvedInput, "-vn", "-ac", "1", "-ar", TargetSampleRate.ToString(), "-f", "f32le", "pipe:1"]);

        var psi = new ProcessStartInfo(ffmpegPath, JoinArguments(args))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        Process process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start FFmpeg.");
        return new AudioPcmStream(process);
    }

    public static float[] BytesToFloats(byte[] raw, int length)
    {
        int sampleCount = length / sizeof(float);
        var samples = new float[sampleCount];

        if (BitConverter.IsLittleEndian)
            Buffer.BlockCopy(raw, 0, samples, 0, sampleCount * sizeof(float));
        else
            for (int i = 0, o = 0; i < sampleCount; i++, o += 4)
                samples[i] = BitConverter.ToSingle(raw, o);

        return samples;
    }

    private static (float[] samples, int rate, int channels) DecodeOgg(string path)
    {
        using var reader = new VorbisReader(path);

        int rate = reader.SampleRate;
        int channels = reader.Channels;
        if (channels <= 0)
            throw new InvalidDataException("OGG: invalid channel count.");

        var chunks = new List<float[]>();
        var buffer = new float[Math.Max(4096, rate * channels)];

        while (true)
        {
            int read = reader.ReadSamples(buffer, 0, buffer.Length);
            if (read <= 0)
                break;

            var chunk = new float[read];
            Array.Copy(buffer, chunk, read);
            chunks.Add(chunk);
        }

        return (Flatten(chunks), rate, channels);
    }

    private static (float[] samples, int rate, int channels) DecodeWav(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);

        string riffTag = ReadTag(br);
        if (riffTag != "RIFF" && riffTag != "RF64")
            throw new InvalidDataException("Not a RIFF WAV file.");

        br.ReadUInt32();

        if (ReadTag(br) != "WAVE")
            throw new InvalidDataException("Not a WAVE file.");

        int audioFormat = 0;
        int channels = 0;
        int rate = 0;
        int bitsPerSample = 0;
        long dataStart = 0;
        long dataLength = 0;
        long riff64DataLength = -1;

        while (fs.Position <= fs.Length - 8)
        {
            string chunkId = ReadTag(br);
            uint chunkSizeRaw = br.ReadUInt32();
            long chunkSize = chunkSizeRaw == uint.MaxValue ? -1 : chunkSizeRaw;
            long chunkEnd = chunkSize >= 0 ? fs.Position + chunkSize : -1;

            if (chunkEnd > fs.Length)
                throw new InvalidDataException($"WAV: chunk '{chunkId}' exceeds file length.");

            if (chunkId == "fmt ")
            {
                if (chunkSize < 16)
                    throw new InvalidDataException("WAV: invalid fmt chunk.");

                audioFormat = br.ReadUInt16();
                channels = br.ReadUInt16();
                rate = br.ReadInt32();
                br.ReadUInt32();
                br.ReadUInt16();
                bitsPerSample = br.ReadUInt16();

                if (fs.Position < chunkEnd)
                    fs.Seek(chunkEnd - fs.Position, SeekOrigin.Current);
            }
            else if (chunkId == "ds64")
            {
                if (chunkSize < 28)
                    throw new InvalidDataException("WAV: invalid ds64 chunk.");

                br.ReadUInt64();
                riff64DataLength = checked((long)br.ReadUInt64());
                br.ReadUInt64();

                if (fs.Position < chunkEnd)
                    fs.Seek(chunkEnd - fs.Position, SeekOrigin.Current);
            }
            else if (chunkId == "data")
            {
                dataStart = fs.Position;
                dataLength = chunkSize >= 0 ? chunkSize : riff64DataLength >= 0 ? riff64DataLength : fs.Length - fs.Position;
                break;
            }
            else
            {
                if (chunkSize < 0)
                    throw new NotSupportedException($"WAV: chunk '{chunkId}' uses RF64 placeholder size without supported ds64 metadata.");

                fs.Seek(chunkSize, SeekOrigin.Current);
            }

            if (chunkSize >= 0 && (chunkSize & 1) == 1 && fs.Position < fs.Length)
                fs.Seek(1, SeekOrigin.Current);
        }

        if (dataStart == 0)
            throw new InvalidDataException("WAV: no data chunk found.");

        if (audioFormat != 1 && audioFormat != 3)
            throw new NotSupportedException($"WAV: format {audioFormat} not supported (need PCM=1 or IEEE_FLOAT=3).");

        if (channels <= 0)
            throw new InvalidDataException("WAV: invalid channel count.");

        if (dataLength > int.MaxValue)
            throw new NotSupportedException("WAV files over 2GB are not decoded in-memory.");

        fs.Seek(dataStart, SeekOrigin.Begin);

        int length = (int)dataLength;
        var raw = new byte[length];
        int bytesRead = 0;

        while (bytesRead < length)
        {
            int read = fs.Read(raw, bytesRead, length - bytesRead);
            if (read == 0)
                break;

            bytesRead += read;
        }

        int bytesPerSample = bitsPerSample / 8;
        if (bytesPerSample <= 0)
            throw new InvalidDataException("WAV: invalid bits per sample.");

        int sampleCount = bytesRead / bytesPerSample;
        var samples = new float[sampleCount];
        ConvertToFloat(raw, samples, sampleCount, audioFormat, bitsPerSample);
        return (samples, rate, channels);
    }

    private static (float[] samples, int rate, int channels) DecodeFfmpeg(string path)
    {
        string ffmpegPath = FindFfmpeg() ?? throw new FileNotFoundException(
            $"FFmpeg not found. Place ffmpeg in EXILED/Plugins/EviAudio/ffmpeg or in PATH. Checked: {_lastFfmpegSearch}");

        string args = JoinArguments(["-hide_banner", "-loglevel", "error", "-i", path, "-vn", "-ac", "1", "-ar", TargetSampleRate.ToString(), "-f", "f32le", "pipe:1"]);
        var psi = new ProcessStartInfo(ffmpegPath, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start FFmpeg.");
        Task<string> stderrTask = proc.StandardError.ReadToEndAsync();
        Task<float[]> stdoutTask = Task.Run(() => ReadF32Output(proc.StandardOutput.BaseStream));

        if (!proc.WaitForExit(120_000))
        {
            try { proc.Kill(); } catch { }
            throw new InvalidOperationException("FFmpeg timed out after 120 seconds.");
        }

        if (!stdoutTask.Wait(5000))
            throw new InvalidOperationException("FFmpeg stdout reader did not finish.");

        string stderr = string.Empty;
        try { stderr = stderrTask.Wait(5000) ? stderrTask.Result : string.Empty; } catch { }

        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg exited with code {proc.ExitCode}: {stderr}");

        return (stdoutTask.Result, TargetSampleRate, TargetChannels);
    }

    private static float[] ReadF32Output(Stream stream)
    {
        var chunks = new List<float[]>();
        var raw = new byte[AudioClipPlayback.PacketSize * sizeof(float) * 32 + 4];
        int carry = 0;

        while (true)
        {
            int read = stream.Read(raw, carry, raw.Length - carry);
            if (read <= 0)
                break;

            int total = carry + read;
            int usable = total - total % sizeof(float);

            if (usable > 0)
                chunks.Add(BytesToFloats(raw, usable));

            carry = total - usable;
            if (carry > 0)
                Buffer.BlockCopy(raw, usable, raw, 0, carry);
        }

        return Flatten(chunks);
    }

    private static void ConvertToFloat(byte[] raw, float[] samples, int count, int audioFormat, int bitsPerSample)
    {
        if (audioFormat == 3)
        {
            if (bitsPerSample != 32)
                throw new NotSupportedException($"WAV: float format with {bitsPerSample} bits is not supported.");

            if (BitConverter.IsLittleEndian)
                Buffer.BlockCopy(raw, 0, samples, 0, count * sizeof(float));
            else
                for (int i = 0, o = 0; i < count; i++, o += 4)
                    samples[i] = BitConverter.ToSingle(raw, o);

            return;
        }

        switch (bitsPerSample)
        {
            case 8:
                for (int i = 0; i < count; i++)
                    samples[i] = (raw[i] - 128) * (1f / 128f);
                break;

            case 16:
                for (int i = 0, o = 0; i < count; i++, o += 2)
                {
                    short value = (short)(raw[o] | raw[o + 1] << 8);
                    samples[i] = value * (1f / 32768f);
                }
                break;

            case 24:
                for (int i = 0, o = 0; i < count; i++, o += 3)
                {
                    int value = raw[o] | raw[o + 1] << 8 | (sbyte)raw[o + 2] << 16;
                    samples[i] = value * (1f / 8388608f);
                }
                break;

            case 32:
                for (int i = 0, o = 0; i < count; i++, o += 4)
                {
                    int value = raw[o] | raw[o + 1] << 8 | raw[o + 2] << 16 | raw[o + 3] << 24;
                    samples[i] = value * (1f / 2147483648f);
                }
                break;

            default:
                throw new NotSupportedException($"WAV: {bitsPerSample}-bit PCM is not supported.");
        }
    }

    private static string ReadTag(BinaryReader br)
    {
        byte[] bytes = br.ReadBytes(4);
        if (bytes.Length != 4)
            throw new EndOfStreamException();

        return Encoding.ASCII.GetString(bytes);
    }

    public static string ResolveExternalInput(string input)
        => ResolveExternalInput(input, null);

    public static string ResolveExternalInput(string input, Func<bool> shouldContinue)
    {
        if (!IsYtDlpCandidate(input))
            return input;

        if (shouldContinue != null && !shouldContinue())
            return input;

        string ytDlp = FindYtDlp();
        if (string.IsNullOrEmpty(ytDlp))
            return input;

        if (shouldContinue != null && !shouldContinue())
            return input;

        try
        {
            var psi = new ProcessStartInfo(ytDlp, JoinArguments(["--get-url", "-f", "bestaudio/best", "--no-playlist", input]))
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                return input;

            Task<string> stderrTask = proc.StandardError.ReadToEndAsync();
            Task<string> stdoutTask = proc.StandardOutput.ReadLineAsync();
            var timeout = Stopwatch.StartNew();

            while (!proc.WaitForExit(250))
            {
                if (shouldContinue != null && !shouldContinue())
                {
                    try { proc.Kill(); } catch { }
                    return input;
                }

                if (timeout.ElapsedMilliseconds < 30_000)
                    continue;

                try { proc.Kill(); } catch { }
                return input;
            }

            string directUrl = stdoutTask.Wait(1000) ? stdoutTask.Result?.Trim() : string.Empty;

            if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(directUrl))
            {
                string err = stderrTask.Wait(1000) ? stderrTask.Result : string.Empty;
                Log.Warn($"yt-dlp failed for '{input}': {err}");
                return input;
            }

            return directUrl;
        }
        catch (Exception ex)
        {
            Log.Warn($"yt-dlp resolve failed for '{input}': {ex.Message}");
            return input;
        }
    }

    public static string FindFfmpeg()
    {
        lock (FfmpegLock)
        {
            if (!string.IsNullOrEmpty(_cachedFfmpegPath) && File.Exists(_cachedFfmpegPath))
                return _cachedFfmpegPath;

            _cachedFfmpegPath = FindExecutable(["ffmpeg"], ["ffmpeg"], out _lastFfmpegSearch);
            return _cachedFfmpegPath;
        }
    }

    public static string FindYtDlp()
    {
        lock (YtDlpLock)
        {
            if (!string.IsNullOrEmpty(_cachedYtDlpPath) && File.Exists(_cachedYtDlpPath))
                return _cachedYtDlpPath;

            _cachedYtDlpPath = FindExecutable(["yt-dlp", "ytdlp"], ["yt-dlp", "ytdlp"], out _lastYtDlpSearch);
            return _cachedYtDlpPath;
        }
    }

    private static string FindExecutable(string[] folderNames, string[] baseNames, out string searched)
    {
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var candidates = new List<string>();

        foreach (string baseName in baseNames)
        {
            string exe = isWindows ? baseName + ".exe" : baseName;

            if (Plugin.Instance != null)
            {
                foreach (string folderName in folderNames)
                {
                    candidates.Add(Path.Combine(Plugin.Instance.PluginFolder, folderName, exe));
                    candidates.Add(Path.Combine(Plugin.Instance.PluginFolder, folderName, "bin", exe));
                }

                candidates.Add(Path.Combine(Plugin.Instance.PluginFolder, exe));
            }

            foreach (string folderName in folderNames)
            {
                candidates.Add(Path.Combine(AppContext.BaseDirectory, folderName, exe));
                candidates.Add(Path.Combine(AppContext.BaseDirectory, folderName, "bin", exe));
            }

            candidates.Add(Path.Combine(AppContext.BaseDirectory, exe));
        }

        foreach (string candidate in candidates)
            if (File.Exists(candidate))
            {
                searched = string.Join("; ", candidates);
                return candidate;
            }

        try
        {
            foreach (string baseName in baseNames)
            {
                string exe = isWindows ? baseName + ".exe" : baseName;
                var psi = new ProcessStartInfo(isWindows ? "where" : "which", exe)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var proc = Process.Start(psi);
                string result = proc?.StandardOutput.ReadLine()?.Trim();
                proc?.WaitForExit(3000);

                if (!string.IsNullOrEmpty(result) && File.Exists(result))
                {
                    candidates.Add($"PATH:{result}");
                    searched = string.Join("; ", candidates);
                    return result;
                }

                candidates.Add(isWindows ? $"PATH:where {exe}" : $"PATH:which {exe}");
            }
        }
        catch { }

        searched = string.Join("; ", candidates);
        return null;
    }

    private static bool IsYtDlpCandidate(string input)
    {
        if (!IsUrl(input))
            return false;

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri))
            return false;

        string host = uri.Host.ToLowerInvariant();
        return host.Contains("youtube.com")
               || host.Contains("youtu.be")
               || host.Contains("soundcloud.com")
               || host.Contains("bandcamp.com")
               || host.Contains("twitch.tv")
               || host.Contains("vimeo.com");
    }

    private static float[] Resample(float[] input, int fromRate, int fromChannels, int toRate, int toChannels)
    {
        if (input.Length == 0)
            return input;

        float[] mono = input;
        int monoLen = input.Length / fromChannels;

        if (fromChannels > 1)
        {
            mono = new float[monoLen];
            float invChannels = 1f / fromChannels;

            for (int i = 0; i < monoLen; i++)
            {
                float sum = 0f;
                int baseIndex = i * fromChannels;

                for (int channel = 0; channel < fromChannels; channel++)
                    sum += input[baseIndex + channel];

                mono[i] = sum * invChannels;
            }
        }

        if (fromRate == toRate)
        {
            if (ReferenceEquals(mono, input))
                return input;

            var exact = new float[monoLen];
            Array.Copy(mono, exact, monoLen);
            return exact;
        }

        double ratio = (double)fromRate / toRate;
        int outLen = Math.Max(1, (int)(monoLen / ratio));
        var output = new float[outLen];

        for (int i = 0; i < outLen; i++)
        {
            double src = i * ratio;
            int x1 = (int)src;
            double t = src - x1;

            float y0 = mono[ClampIndex(x1 - 1, monoLen)];
            float y1 = mono[ClampIndex(x1, monoLen)];
            float y2 = mono[ClampIndex(x1 + 1, monoLen)];
            float y3 = mono[ClampIndex(x1 + 2, monoLen)];

            output[i] = Hermite(y0, y1, y2, y3, (float)t);
        }

        return output;
    }

    private static int ClampIndex(int index, int length)
    {
        if (index < 0) return 0;
        if (index >= length) return length - 1;
        return index;
    }

    private static float Hermite(float y0, float y1, float y2, float y3, float t)
    {
        float c0 = y1;
        float c1 = 0.5f * (y2 - y0);
        float c2 = y0 - 2.5f * y1 + 2f * y2 - 0.5f * y3;
        float c3 = 0.5f * (y3 - y0) + 1.5f * (y1 - y2);
        return ((c3 * t + c2) * t + c1) * t + c0;
    }

    private static void Normalize(float[] samples)
    {
        if (samples.Length == 0)
            return;

        double sum = 0;
        float peak = 0f;

        for (int i = 0; i < samples.Length; i++)
        {
            float abs = Math.Abs(samples[i]);
            peak = Math.Max(peak, abs);
            sum += samples[i] * samples[i];
        }

        double rms = Math.Sqrt(sum / samples.Length);
        if (rms <= 0.0001 || peak <= 0.0001f)
            return;

        float target = 0.18f;
        float gain = (float)(target / rms);
        gain = AudioMath.Clamp(gain, 0.25f, 3f);

        if (peak * gain > 0.98f)
            gain = 0.98f / peak;

        for (int i = 0; i < samples.Length; i++)
            samples[i] = AudioMath.SoftLimit(samples[i] * gain);
    }

    private static AudioTrackMetadata ReadMetadata(string path, int sampleCount, int sampleRate)
    {
        var metadata = new AudioTrackMetadata
        {
            Duration = sampleRate > 0 ? TimeSpan.FromSeconds((double)sampleCount / sampleRate) : TimeSpan.Zero,
        };

        if (IsUrl(path) || !File.Exists(path))
            return metadata;

        string ext = Path.GetExtension(path).ToLowerInvariant();

        try
        {
            if (ext == ".ogg")
                ReadVorbisMetadata(path, metadata);
            else if (ext == ".mp3")
                ReadMp3Metadata(path, metadata);
        }
        catch (Exception ex)
        {
            Log.Debug($"Metadata read failed for '{path}': {ex.Message}");
        }

        return metadata;
    }

    private static void ReadVorbisMetadata(string path, AudioTrackMetadata metadata)
    {
        using var reader = new VorbisReader(path);
        metadata.Title = FirstTag(reader, "TITLE");
        metadata.Artist = FirstTag(reader, "ARTIST");
        metadata.Album = FirstTag(reader, "ALBUM");
    }

    private static string FirstTag(VorbisReader reader, string tag)
    {
        try
        {
            return reader.Tags.GetTagMulti(tag)?.FirstOrDefault() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void ReadMp3Metadata(string path, AudioTrackMetadata metadata)
    {
        using var fs = File.OpenRead(path);

        if (fs.Length >= 128)
        {
            fs.Seek(-128, SeekOrigin.End);
            byte[] id3v1 = new byte[128];
            fs.Read(id3v1, 0, id3v1.Length);

            if (Encoding.ASCII.GetString(id3v1, 0, 3) == "TAG")
            {
                metadata.Title = ReadLatin1(id3v1, 3, 30);
                metadata.Artist = ReadLatin1(id3v1, 33, 30);
                metadata.Album = ReadLatin1(id3v1, 63, 30);
            }
        }

        fs.Seek(0, SeekOrigin.Begin);
        byte[] header = new byte[10];
        if (fs.Read(header, 0, header.Length) != header.Length)
            return;

        if (Encoding.ASCII.GetString(header, 0, 3) != "ID3")
            return;

        int tagSize = Synchsafe(header, 6);
        long tagEnd = Math.Min(fs.Length, 10L + tagSize);

        while (fs.Position + 10 <= tagEnd)
        {
            byte[] frameHeader = new byte[10];
            if (fs.Read(frameHeader, 0, 10) != 10)
                break;

            string id = Encoding.ASCII.GetString(frameHeader, 0, 4);
            int size = frameHeader[4] << 24 | frameHeader[5] << 16 | frameHeader[6] << 8 | frameHeader[7];

            if (string.IsNullOrWhiteSpace(id) || size <= 0 || fs.Position + size > tagEnd)
                break;

            byte[] data = new byte[size];
            fs.Read(data, 0, data.Length);

            string text = DecodeId3Text(data);
            if (id == "TIT2" && string.IsNullOrWhiteSpace(metadata.Title)) metadata.Title = text;
            else if (id == "TPE1" && string.IsNullOrWhiteSpace(metadata.Artist)) metadata.Artist = text;
            else if (id == "TALB" && string.IsNullOrWhiteSpace(metadata.Album)) metadata.Album = text;
        }
    }

    private static int Synchsafe(byte[] data, int offset)
        => data[offset] << 21 | data[offset + 1] << 14 | data[offset + 2] << 7 | data[offset + 3];

    private static string DecodeId3Text(byte[] data)
    {
        if (data.Length <= 1)
            return string.Empty;

        Encoding encoding = data[0] switch
        {
            1 => Encoding.Unicode,
            2 => Encoding.BigEndianUnicode,
            3 => Encoding.UTF8,
            _ => Encoding.GetEncoding("ISO-8859-1"),
        };

        return encoding.GetString(data, 1, data.Length - 1).TrimEnd('\0').Trim();
    }

    private static string ReadLatin1(byte[] data, int offset, int count)
        => Encoding.GetEncoding("ISO-8859-1").GetString(data, offset, count).TrimEnd('\0').Trim();

    private static float[] Flatten(List<float[]> chunks)
    {
        int length = 0;
        foreach (var chunk in chunks)
            length += chunk.Length;

        var result = new float[length];
        int offset = 0;

        foreach (var chunk in chunks)
        {
            Array.Copy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length;
        }

        return result;
    }

    private static string JoinArguments(IEnumerable<string> arguments)
        => string.Join(" ", arguments.Select(Quote));

    private static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        bool needsQuotes = value.Any(char.IsWhiteSpace) || value.IndexOfAny(new[] { '"', '\\', '$', '`', '\n', '\r' }) >= 0;
        if (!needsQuotes)
            return value;

        var sb = new StringBuilder();
        sb.Append('"');
        int slashes = 0;

        foreach (char c in value)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }

            if (c == '"')
            {
                sb.Append('\\', slashes * 2 + 1);
                sb.Append('"');
                slashes = 0;
                continue;
            }

            if (slashes > 0)
            {
                sb.Append('\\', slashes);
                slashes = 0;
            }

            sb.Append(c);
        }

        if (slashes > 0)
            sb.Append('\\', slashes * 2);

        sb.Append('"');
        return sb.ToString();
    }
}
