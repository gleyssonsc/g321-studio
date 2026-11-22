using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Runtime.InteropServices;

namespace G321Studio;

internal sealed record Endpoint(string Id, string Name)
{
    public override string ToString() => Name;
}

internal static class AudioDevices
{
    public static Endpoint[] List(DataFlow flow, Func<string, bool> filter)
    {
        using var enumerator = new MMDeviceEnumerator();
        var endpoints = new List<Endpoint>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (device)
            {
                string name = device.FriendlyName;
                if (filter(name))
                    endpoints.Add(new Endpoint(device.ID, name));
            }
        }
        return endpoints.OrderBy(endpoint => endpoint.Name).ToArray();
    }

    public static Endpoint? GetDefaultPlayback()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return new Endpoint(device.ID, device.FriendlyName);
        }
        catch (COMException)
        {
            return null;
        }
    }
}

internal sealed class AudioRoute : IDisposable
{
    private readonly MMDevice input;
    private readonly MMDevice output;
    private readonly WasapiCapture? capture;
    private readonly VoicePlayback? playback;
    private bool disposed;

    public AudioRoute(string inputId, string outputId, Func<float> monitorLevel, Action<Exception> onFailure)
    {
        using var enumerator = new MMDeviceEnumerator();
        input = enumerator.GetDevice(inputId);
        try
        {
            output = enumerator.GetDevice(outputId);
        }
        catch
        {
            input.Dispose();
            throw;
        }

        try
        {
            capture = new WasapiCapture(input, useEventSync: true, audioBufferMillisecondsLength: 10);
            var inputFormat = capture.WaveFormat;
            var buffer = new BufferedWaveProvider(inputFormat, TimeSpan.FromMilliseconds(50))
            {
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };
            int queueLimit = inputFormat.ConvertLatencyToByteSize(30);
            var discard = new byte[queueLimit];

            capture.DataAvailable += (_, args) =>
            {
                // Keep the most recent frames when audio has built up.
                int length = Math.Min(args.BytesRecorded, queueLimit);
                int excess = buffer.BufferedBytes + length - queueLimit;
                if (excess > 0)
                    buffer.Read(discard, 0, excess);
                buffer.AddSamples(args.Buffer, args.BytesRecorded - length, length);
            };
            capture.RecordingStopped += (_, args) =>
            {
                if (args.Exception is { } error)
                    onFailure(error);
            };
            ISampleProvider samples = buffer.ToSampleProvider();
            using var formatClient = output.AudioClient;
            var outputFormat = formatClient.MixFormat;
            samples = (samples.WaveFormat.Channels, outputFormat.Channels) switch
            {
                (var source, var destination) when source == destination => samples,
                (1, 2) => new MonoToStereoSampleProvider(samples),
                (2, 1) => new StereoToMonoSampleProvider(samples),
                _ => throw new NotSupportedException(
                    "Use uma configuração mono ou estéreo nos dispositivos de áudio do Windows.")
            };
            if (samples.WaveFormat.SampleRate != outputFormat.SampleRate)
                samples = new WdlResamplingSampleProvider(samples, outputFormat.SampleRate);

            var monitoredSamples = new MonitorGain(samples, monitorLevel).ToWaveProvider();
            capture.StartRecording();
            playback = new VoicePlayback(outputId, monitoredSamples, onFailure);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        try
        {
            capture?.Dispose();
        }
        finally
        {
            try
            {
                playback?.Dispose();
            }
            finally
            {
                input.Dispose();
                output.Dispose();
            }
        }
    }
}

internal sealed record EqParameters(float[] Gains, float Preamp)
{
    public const float BandQ = 1.4f;
    public static readonly int[] Frequencies = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    public static readonly Preset[] Presets =
    [
        new("Plano", new float[Frequencies.Length], 0),
        new("Graves", [5, 5, 4, 2, 0, 0, 0, 0, 0, 0], -6),
        new("Voz clara", [-4, -3, -2, -1, 0, 2, 4, 3, 1, 0], -5),
        new("Jogos", [-3, -3, -2, -1, 0, 1, 3, 4, 2, 0], -6),
        new("Música", [3, 3, 2, 0, -1, 0, 1, 2, 3, 2], -5),
        new("Personalizado", new float[Frequencies.Length], 0)
    ];
}

internal sealed class MonitorGain(ISampleProvider source, Func<float> level) : ISampleProvider
{
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        float gain = level();
        for (int i = offset; i < offset + read; i++)
            buffer[i] = Math.Clamp(buffer[i] * gain, -1f, 1f);
        return read;
    }
}
