using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace G321Studio;

internal sealed record BatteryReading(int? Percent, string? Detail = null);

internal static class BluetoothBattery
{
    public static async Task<BatteryReading> ReadAsync(CancellationToken cancellation)
    {
        var classicQuery = DeviceInformation.FindAllAsync(
            BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected))
            .AsTask(cancellation);
        var leQuery = DeviceInformation.FindAllAsync(
            BluetoothLEDevice.GetDeviceSelectorFromPairingState(true))
            .AsTask(cancellation);
        await Task.WhenAll(classicQuery, leQuery);

        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in classicQuery.Result.Where(info => IsG321(info.Name)))
        {
            using var device = await BluetoothDevice.FromIdAsync(info.Id).AsTask(cancellation);
            if (device?.ConnectionStatus != BluetoothConnectionStatus.Connected)
                continue;

            addresses.Add(device.BluetoothAddress.ToString("X12"));
        }

        if (addresses.Count > 0 && ReadWindowsBattery(addresses) is { } classicPercent)
            return new BatteryReading(classicPercent);

        foreach (var info in leQuery.Result.Where(info => IsG321(info.Name)))
        {
            using var device = await BluetoothLEDevice.FromIdAsync(info.Id).AsTask(cancellation);
            if (device is null)
                continue;

            var address = device.BluetoothAddress.ToString("X12");
            if (device.ConnectionStatus == BluetoothConnectionStatus.Connected)
            {
                addresses.Add(address);
                if (ReadWindowsBattery(addresses) is { } lePercent)
                    return new BatteryReading(lePercent);
            }
            else if (!addresses.Contains(address))
            {
                continue;
            }

            var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery)
                .AsTask(cancellation);
            foreach (var service in services.Services)
            {
                using (service)
                {
                    var characteristics = await service.GetCharacteristicsForUuidAsync(
                        GattCharacteristicUuids.BatteryLevel).AsTask(cancellation);
                    foreach (var characteristic in characteristics.Characteristics)
                    {
                        var value = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached)
                            .AsTask(cancellation);
                        if (value.Status != GattCommunicationStatus.Success || value.Value.Length != 1)
                            continue;

                        using var reader = DataReader.FromBuffer(value.Value);
                        int level = reader.ReadByte();
                        if (level <= 100)
                            return new BatteryReading(level);
                    }
                }
            }
        }

        return new BatteryReading(null, addresses.Count > 0
            ? StudioText.Get("RtBluetoothBatteryUnavailable")
            : StudioText.Get("RtConnectBluetoothBattery"));
    }

    internal static bool IsG321(string? name) =>
        name?.Contains("G321", StringComparison.OrdinalIgnoreCase) == true;

    private static int? ReadWindowsBattery(HashSet<string> addresses)
    {
        foreach (string bus in new[] { "BTHENUM", "BTHLE", "BTHLEDEVICE" })
        {
            var list = SetupDiGetClassDevsW(IntPtr.Zero, bus, IntPtr.Zero, 0x06); // Present, all classes
            if (list == new IntPtr(-1))
                continue;

            try
            {
                var data = new DeviceInfoData { Size = Marshal.SizeOf<DeviceInfoData>() };
                for (uint i = 0; SetupDiEnumDeviceInfo(list, i, ref data); i++)
                {
                    var id = new System.Text.StringBuilder(512);
                    if (CM_Get_Device_IDW(data.DevInst, id, id.Capacity, 0) != 0)
                        continue;

                    string instance = id.ToString();
                    if (!addresses.Any(address => instance.Contains(address, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var key = new PropertyKey
                    {
                        Format = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"),
                        Id = 2
                    };
                    var value = new byte[4];
                    if (!SetupDiGetDevicePropertyW(list, ref data, ref key, out uint type,
                            value, (uint)value.Length, out uint length, 0))
                        continue;

                    int level = (type, length) switch
                    {
                        (0x03, 1) => value[0], // DEVPROP_TYPE_BYTE
                        (0x07, 4) => BitConverter.ToInt32(value), // DEVPROP_TYPE_INT32
                        (0x08, 4) when BitConverter.ToUInt32(value) <= 100 => (int)BitConverter.ToUInt32(value),
                        _ => -1
                    };
                    if (level is >= 0 and <= 100)
                        return level;
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(list);
            }
        }

        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoData
    {
        public int Size;
        public Guid ClassGuid;
        public uint DevInst;
        public UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid Format;
        public uint Id;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string enumerator,
        IntPtr owner, uint flags);

    [DllImport("setupapi.dll")]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr list, uint index, ref DeviceInfoData device);

    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr list);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool SetupDiGetDevicePropertyW(IntPtr list, ref DeviceInfoData device,
        ref PropertyKey key, out uint type, [Out] byte[] buffer, uint size, out uint required, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_IDW(uint instance, System.Text.StringBuilder id,
        int size, uint flags);
}
