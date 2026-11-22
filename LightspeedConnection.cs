using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace G321Studio;

internal static class LightspeedConnection
{
    private const uint ReadAccess = 0x80000000;
    private const uint WriteAccess = 0x40000000;
    private const uint Overlapped = 0x40000000;
    private const int FeatureReportLength = 67;
    private const int InputReportLength = 61;
    private const string ReceiverUsbPrefix = @"USB\VID_046D&PID_0B39\";
    private const string AudioInterfacePrefix = @"USB\VID_046D&PID_0B39&MI_00\";
    private const string HidInterfacePrefix = @"USB\VID_046D&PID_0B39&MI_03\";
    private const string DiagnosticCollectionPrefix = @"HID\VID_046D&PID_0B39&MI_03&COL02\";

    public static bool IsReceiverEndpoint(string endpointId) => TryGetReceiver(endpointId, out _);

    public static bool ShareReceiver(string inputId, string outputId) =>
        TryGetReceiver(inputId, out uint inputReceiver) &&
        TryGetReceiver(outputId, out uint outputReceiver) && inputReceiver == outputReceiver;

    public static bool ShareBluetoothAudioDevice(string inputId, string outputId) =>
        FindBluetoothContainer(inputId) is { } input && input == FindBluetoothContainer(outputId);

    public static bool HeadsetResponds(string endpointId)
    {
        if (!TryGetReceiver(endpointId, out uint receiver))
            return false;

        foreach (string path in ReceiverHidPathsFor(receiver))
        {
            using var handle = OpenHid(path, ReadAccess);
            if (handle.IsInvalid)
                continue;

            // The updater reads this CFU report to enumerate the remote headset's components.
            var report = new byte[FeatureReportLength];
            report[0] = 0x3A;
            if (HidD_GetFeature(handle, report, report.Length) &&
                report[0] == 0x3A && report[1] is >= 1 and <= 7 && report[4] != 0 &&
                TryGetReceiver(endpointId, out uint currentReceiver) && currentReceiver == receiver)
                return true;
        }
        return false;
    }

    public static async Task<int?> ReadBatteryPercentAsync(CancellationToken cancellation)
    {
        foreach (string path in ReceiverHidPaths())
        {
            cancellation.ThrowIfCancellationRequested();
            using var reader = OpenHid(path, ReadAccess, Overlapped);
            using var writer = OpenHid(path, ReadAccess | WriteAccess);
            if (reader.IsInvalid || writer.IsInvalid)
                continue;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(800));
            try
            {
                using var input = new FileStream(reader, FileAccess.Read, bufferSize: 1, isAsync: true);
                var request = new byte[FeatureReportLength];
                request[0] = 0x24;
                request[1] = 0x2B;

                // Start the input read before sending the request. The receiver answers on interrupt IN.
                var response = ReadBatteryReplyAsync(input, timeout.Token);
                bool sent = HidD_SetFeature(writer, request, request.Length);
                if (!sent)
                    timeout.Cancel();

                int? percent = await response.ConfigureAwait(false);
                if (sent && percent is not null)
                    return percent;
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
            }
            catch (IOException)
            {
                // The receiver can be unplugged during a pending HID read.
            }
        }
        return null;
    }

    private static async Task<int?> ReadBatteryReplyAsync(FileStream input, CancellationToken cancellation)
    {
        var reply = new byte[InputReportLength];
        int count;
        while ((count = await input.ReadAsync(reply.AsMemory(), cancellation).ConfigureAwait(false)) != 0)
        {
            // The payload contains six peer-address bytes and four version bytes before the percentage.
            if (count == reply.Length && reply is [0x27, 0x2B, 0x00, 0x0B, ..] && reply[14] <= 100)
                return reply[14];
        }
        return null;
    }

    private static SafeFileHandle OpenHid(string path, uint access, uint flags = 0) =>
        CreateFileW(path, access, 3, IntPtr.Zero, 3, flags, IntPtr.Zero);

    private static bool TryGetReceiver(string endpointId, out uint receiver)
    {
        receiver = 0;
        return CM_Locate_DevNodeW(out uint endpoint, $@"SWD\MMDEVAPI\{endpointId}", 0) == 0 &&
            CM_Get_Parent(out uint audioInterface, endpoint, 0) == 0 &&
            GetDeviceId(audioInterface)?.StartsWith(AudioInterfacePrefix, StringComparison.OrdinalIgnoreCase) == true &&
            CM_Get_Parent(out receiver, audioInterface, 0) == 0 &&
            GetDeviceId(receiver)?.StartsWith(ReceiverUsbPrefix, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static IEnumerable<string> ReceiverHidPathsFor(uint receiver) =>
        MatchingChildren(receiver, HidInterfacePrefix)
            .SelectMany(hid => MatchingChildren(hid.Node, DiagnosticCollectionPrefix))
            .SelectMany(collection => ReceiverHidPaths(collection.Id));

    private static IEnumerable<(uint Node, string Id)> MatchingChildren(uint parent, string prefix)
    {
        if (CM_Get_Child(out uint child, parent, 0) != 0)
            yield break;

        do
        {
            if (GetDeviceId(child) is { } id && id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                yield return (child, id);
        } while (CM_Get_Sibling(out child, child, 0) == 0);
    }

    private static string? GetDeviceId(uint node)
    {
        var id = new StringBuilder(256);
        return CM_Get_Device_IDW(node, id, id.Capacity, 0) == 0 ? id.ToString() : null;
    }

    private static Guid? FindBluetoothContainer(string endpointId)
    {
        if (CM_Locate_DevNodeW(out uint node, $@"SWD\MMDEVAPI\{endpointId}", 0) != 0)
            return null;

        while (CM_Get_Parent(out uint parent, node, 0) == 0)
        {
            node = parent;
            string? id = GetDeviceId(node);
            if (id is null || !(id.StartsWith(@"BTHENUM\", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith(@"BTHHFENUM\", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith(@"BTHLE\", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith(@"BTHLEDEVICE\", StringComparison.OrdinalIgnoreCase)))
                continue;

            // Read the physical Bluetooth grouping supplied by Windows, not the audio display name.
            var key = new DevicePropertyKey(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);
            uint size = 16;
            if (CM_Get_DevNode_PropertyW(node, in key, out uint type, out Guid container, ref size, 0) == 0 &&
                type == 0x0D && size == 16 && container != Guid.Empty &&
                container != new Guid("00000000-0000-0000-FFFF-FFFFFFFFFFFF"))
                return container;
        }

        return null;
    }

    private static IEnumerable<string> ReceiverHidPaths(string? deviceId = null)
    {
        Guid hidClass = new("4D1E55B2-F16F-11CF-88CB-001111000030");
        if (CM_Get_Device_Interface_List_SizeW(out uint length, ref hidClass, deviceId, 0) != 0 || length < 2)
            yield break;

        var interfaces = new char[length];
        if (CM_Get_Device_Interface_ListW(ref hidClass, deviceId, interfaces, length, 0) != 0)
            yield break;

        foreach (string path in new string(interfaces).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (path.Contains("VID_046D&PID_0B39&MI_03&COL02#", StringComparison.OrdinalIgnoreCase))
                yield return path;
        }
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Locate_DevNodeW(out uint node, string id, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_Child(out uint child, uint node, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_Sibling(out uint sibling, uint node, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_IDW(uint node, StringBuilder id, int size, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DevicePropertyKey(Guid format, uint propertyId)
    {
        private readonly Guid formatId = format;
        private readonly uint id = propertyId;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_DevNode_PropertyW(uint node, in DevicePropertyKey key,
        out uint type, out Guid value, ref uint size, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_Interface_List_SizeW(out uint length, ref Guid classGuid,
        string? deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_Interface_ListW(ref Guid classGuid, string? deviceId,
        [Out] char[] interfaces, uint length, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint sharing,
        IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetFeature(SafeFileHandle handle, [In, Out] byte[] report, int length);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_SetFeature(SafeFileHandle handle, [In] byte[] report, int length);
}
