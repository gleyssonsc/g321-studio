using Microsoft.Win32;
using NAudio.CoreAudioApi;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace G321Studio;

internal sealed class DirectEqualizer
{
    private const string ApoRegistryPath = @"SOFTWARE\EqualizerAPO";
    private const string RenderRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render";
    private const string DisableEffectsKey = "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5";
    private const string EffectKey = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d}";
    private const string ManagedFile = "g321-studio.txt";
    private const string ManagedInclude = "Include: " + ManagedFile;
    private const string BeginBlock = "# BEGIN G321 Studio";
    private const string EndBlock = "# END G321 Studio";
    private const string InactiveFilters = "# G321 Studio EQ desativado\r\n";
    private static readonly Guid PostMixClsid = new("EC1CC9CE-FAED-4822-828A-82A81A6F018F");
    private static readonly Regex IncludeBlock = new(
        @"^# BEGIN G321 Studio\r?\nDevice: all\r?\nInclude: g321-studio\.txt\r?\n# END G321 Studio(?:\r?\n|$)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private readonly string endpointGuid;
    private readonly string configPath;
    private readonly string filtersPath;

    private DirectEqualizer(string directory, Guid endpointGuid)
    {
        this.endpointGuid = endpointGuid.ToString("B");
        configPath = Path.Combine(directory, "config.txt");
        filtersPath = Path.Combine(directory, ManagedFile);
    }

    public static DirectEqualizer? For(Endpoint endpoint)
    {
        using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var apo = registry.OpenSubKey(ApoRegistryPath);
        if (apo?.GetValue("ConfigPath") is not string directory || !Path.IsPathFullyQualified(directory) ||
            !File.Exists(Path.Combine(directory, "config.txt")))
            return null;

        try
        {
            using var endpoints = new MMDeviceEnumerator();
            using var device = endpoints.GetDevice(endpoint.Id);
            if (device.DataFlow != DataFlow.Render || device.State != DeviceState.Active)
                return null;

            var properties = device.Properties;
            var guidKey = PropertyKeys.PKEY_AudioEndpoint_GUID;
            if (!properties.Contains(guidKey) || !Guid.TryParse(properties[guidKey].Value as string, out Guid guid))
                return null;

            var disabledKey = PropertyKeys.PKEY_AudioEndpoint_Disable_SysFx;
            if (properties.Contains(disabledKey) && IsDisabled(properties[disabledKey].Value))
                return null;

            using var effects = registry.OpenSubKey($@"{RenderRegistryPath}\{guid:B}\FxProperties");
            if (effects is null || IsDisabled(effects.GetValue(DisableEffectsKey)))
                return null;

            // The Equalizer APO Device Selector uses GFX, MFX or EFX for its post-mix class.
            foreach (int property in new[] { 2, 6, 7 })
            {
                if (Guid.TryParse(effects.GetValue($"{EffectKey},{property}") as string, out Guid clsid) && clsid == PostMixClsid)
                    return new DirectEqualizer(directory, guid);
            }
        }
        catch (COMException)
        {
            // The render endpoint can disappear while it is being inspected.
        }

        return null;
    }

    public void Apply(EqParameters parameters)
    {
        var filters = new StringBuilder().Append("# G321 Studio\r\n")
            .Append(FormattableString.Invariant($"Device: {endpointGuid}\r\nStage: post-mix\r\nChannel: all\r\nPreamp: {parameters.Preamp} dB\r\n"));
        for (int band = 0; band < EqParameters.Frequencies.Length; band++)
        {
            float gain = parameters.Gains[band];
            if (gain != 0)
                filters.Append(FormattableString.Invariant($"Filter: ON PK Fc {EqParameters.Frequencies[band]} Hz Gain {gain} dB Q {EqParameters.BandQ}\r\n"));
        }

        byte[] originalConfig = File.ReadAllBytes(configPath);
        byte[] configured = AddInclude(originalConfig);
        byte[]? originalFilters = ReadOptional(filtersPath);
        byte[] updatedFilters = Encoding.UTF8.GetBytes(filters.ToString());
        bool filtersChanged = originalFilters is null || !originalFilters.AsSpan().SequenceEqual(updatedFilters);

        if (filtersChanged)
            WriteAtomically(filtersPath, updatedFilters, originalFilters is not null);
        if (ReferenceEquals(configured, originalConfig))
            return;

        try
        {
            if (!File.ReadAllBytes(configPath).AsSpan().SequenceEqual(originalConfig))
                throw new IOException(StudioText.Get("RtApoChanged"));
            WriteAtomically(configPath, configured, destinationExists: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (filtersChanged)
            {
                try
                {
                    if (originalFilters is null)
                        File.Delete(filtersPath);
                    else
                        WriteAtomically(filtersPath, originalFilters, destinationExists: true);
                }
                catch (Exception rollbackError) when (rollbackError is IOException or UnauthorizedAccessException)
                {
                    throw new IOException(StudioText.Get("RtApoRestoreFailed"),
                        new AggregateException(error, rollbackError));
                }
            }
            throw;
        }
    }

    public void Clear()
    {
        byte[]? current = ReadOptional(filtersPath);
        byte[] inactive = Encoding.UTF8.GetBytes(InactiveFilters);
        if (current is not null && !current.AsSpan().SequenceEqual(inactive))
            WriteAtomically(filtersPath, inactive, destinationExists: true);
    }

    private static bool IsDisabled(object? value) => value switch
    {
        int number => number != 0,
        uint number => number != 0,
        _ => false
    };

    private static byte[] AddInclude(byte[] original)
    {
        // APO reads UTF-8 and falls back to the Windows code page. Latin-1 is used here
        // to find ASCII markers without converting any bytes in the user's file.
        if (Array.IndexOf(original, (byte)0) >= 0)
            throw new IOException(StudioText.Get("RtApoConfigEncoding"));
        string text = Encoding.Latin1.GetString(original);
        var blocks = IncludeBlock.Matches(text);
        if (blocks.Count == 1 && text.AsSpan(blocks[0].Index + blocks[0].Length).Trim().IsEmpty)
            return original;

        string newLine = text.Contains('\n') && !text.Contains("\r\n", StringComparison.Ordinal) ? "\n" : "\r\n";
        string userConfig = IncludeBlock.Replace(text, "");
        if (userConfig.Split('\n').Any(line => line.Trim().Equals(ManagedInclude, StringComparison.OrdinalIgnoreCase)))
            throw new IOException(StudioText.Get("RtApoManagedIncludeConflict"));
        string separator = userConfig.Length != 0 && !userConfig.EndsWith('\n') ? newLine : "";
        string block = string.Join(newLine, BeginBlock, "Device: all", ManagedInclude, EndBlock, "");
        return Encoding.Latin1.GetBytes(userConfig + separator + block);
    }

    private static byte[]? ReadOptional(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static void WriteAtomically(string path, byte[] data, bool destinationExists)
    {
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, data);
            if (destinationExists)
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
