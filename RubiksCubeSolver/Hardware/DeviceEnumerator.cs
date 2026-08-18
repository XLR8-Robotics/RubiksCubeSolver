using System.IO.Ports;
using System.Management;

namespace RubiksCubeSolver.Hardware;

public sealed class SerialDevice
{
    public required string PortName { get; init; }
    public required string Caption { get; init; }
    public bool IsMaestroCommandPort =>
        Caption.Contains("Pololu", StringComparison.OrdinalIgnoreCase)
        && Caption.Contains("Command", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"{PortName} — {Caption}";
}

public static partial class DeviceEnumerator
{
    public static IReadOnlyList<SerialDevice> ListSerialPorts()
    {
        var byPort = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "";
                var start = name.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                {
                    continue;
                }

                var end = name.IndexOf(')', start);
                if (end < 0)
                {
                    continue;
                }

                var port = name[(start + 1)..end];
                byPort[port] = name;
            }
        }
        catch
        {
            // WMI is optional; fall back to port names only.
        }

        var ports = SerialPort.GetPortNames().Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToList();
        var list = new List<SerialDevice>();
        foreach (var port in ports)
        {
            byPort.TryGetValue(port, out var caption);
            list.Add(new SerialDevice
            {
                PortName = port,
                Caption = string.IsNullOrWhiteSpace(caption) ? port : caption
            });
        }

        return list.OrderByDescending(p => p.IsMaestroCommandPort).ThenBy(p => p.PortName).ToList();
    }

}
