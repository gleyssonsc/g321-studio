using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace G321Studio;

internal sealed class VoicePlayback : IDisposable
{
    private static readonly long RequestedDuration = TimeSpan.FromMilliseconds(10).Ticks;
    private static readonly long SchedulingHeadroom = TimeSpan.FromMilliseconds(5).Ticks;
    private static readonly long RecoveryStep = TimeSpan.FromMilliseconds(2).Ticks;

    private readonly ManualResetEvent stop = new(false);
    private readonly Thread worker;

    public VoicePlayback(string outputId, IWaveProvider source, Action<Exception> onFailure)
    {
        worker = new Thread(() => Play(outputId, source, onFailure))
        {
            IsBackground = true,
            Name = "G321 voice playback"
        };
        try
        {
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
        }
        catch
        {
            stop.Dispose();
            throw;
        }
    }

    private void Play(string outputId, IWaveProvider source, Action<Exception> onFailure)
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var output = devices.GetDevice(outputId);
            using var ready = new AutoResetEvent(false);
            using var client = output.AudioClient;
            var format = source.WaveFormat;
            client.Initialize(AudioClientShareMode.Shared, AudioClientStreamFlags.EventCallback |
                AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality,
                RequestedDuration, 0, format, Guid.Empty);
            client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle());

            var renderer = client.AudioRenderClient;
            int capacity = client.BufferSize;
            long period = Math.Max(client.DefaultDevicePeriod, RequestedDuration);
            int target = Math.Min(capacity, FramesIn(period + SchedulingHeadroom, format.SampleRate));
            int recoveryFrames = FramesIn(RecoveryStep, format.SampleRate);
            int timeout = (int)Math.Ceiling(period / (double)TimeSpan.TicksPerMillisecond);
            var scratch = new byte[capacity * format.BlockAlign];
            WaitHandle[] signals = [stop, ready];

            uint taskIndex = 0;
            IntPtr priority = AvSetMmThreadCharacteristics("Pro Audio", ref taskIndex);
            try
            {
                if (stop.WaitOne(0))
                    return;

                Write(renderer, source, scratch, target);
                client.Start();
                try
                {
                    while (WaitHandle.WaitAny(signals, timeout) != 0)
                    {
                        int padding = client.CurrentPadding;
                        // Leave extra headroom if the queue drains under load.
                        if (padding == 0)
                            target = Math.Min(capacity, target + recoveryFrames);
                        if (padding < target)
                            Write(renderer, source, scratch, target - padding);
                    }
                }
                finally
                {
                    client.Stop();
                }
            }
            finally
            {
                if (priority != IntPtr.Zero)
                    AvRevertMmThreadCharacteristics(priority);
            }
        }
        catch (Exception error)
        {
            if (!stop.WaitOne(0))
                onFailure(error);
        }
    }

    private static int FramesIn(long duration, int sampleRate) =>
        (int)Math.Ceiling(duration * (double)sampleRate / TimeSpan.TicksPerSecond);

    private static void Write(AudioRenderClient renderer, IWaveProvider source, byte[] scratch, int frames)
    {
        int bytes = frames * source.WaveFormat.BlockAlign;
        int read = source.Read(scratch, 0, bytes);
        if (read > 0 && read < bytes)
            Array.Clear(scratch, read, bytes - read);

        IntPtr target = renderer.GetBuffer(frames);
        var flags = AudioClientBufferFlags.Silent;
        try
        {
            if (read > 0)
            {
                Marshal.Copy(scratch, 0, target, bytes);
                flags = AudioClientBufferFlags.None;
            }
        }
        finally
        {
            renderer.ReleaseBuffer(frames, flags);
        }
    }

    public void Dispose()
    {
        stop.Set();
        worker.Join();
        stop.Dispose();
    }

    [DllImport("avrt.dll", EntryPoint = "AvSetMmThreadCharacteristicsW", CharSet = CharSet.Unicode)]
    private static extern IntPtr AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);

    [DllImport("avrt.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AvRevertMmThreadCharacteristics(IntPtr taskHandle);
}
