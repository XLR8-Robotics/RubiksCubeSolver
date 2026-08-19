using System.IO.Ports;

namespace RubiksCubeSolver.Hardware;

/// <summary>
/// Compact-protocol driver for the Pololu Mini Maestro in USB Dual Port mode.
/// Targets are Maestro Control Center microseconds, sent as quarter-microseconds.
/// </summary>
public sealed class MaestroController : IDisposable
{
    public const byte SetTargetCommand = 0x84;
    public const byte SetSpeedCommand = 0x87;
    public const byte SetAccelerationCommand = 0x89;
    public const byte GetMovingStateCommand = 0x93;

    readonly object _gate = new();
    SerialPort? _port;

    public bool IsConnected => _port is { IsOpen: true };
    public bool Simulate { get; set; }

    public void Connect(string portName)
    {
        Disconnect();
        if (Simulate)
        {
            return;
        }

        _port = new SerialPort(portName)
        {
            BaudRate = 9600,
            DataBits = 8,
            Parity = Parity.None,
            StopBits = StopBits.One,
            Handshake = Handshake.None,
            ReadTimeout = 500,
            WriteTimeout = 500,
            DtrEnable = false,
            RtsEnable = false
        };
        _port.Open();
        _port.DiscardInBuffer();
        _port.DiscardOutBuffer();
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            try
            {
                _port?.Close();
            }
            catch
            {
                // Ignore close errors.
            }

            _port?.Dispose();
            _port = null;
        }
    }

    public void SetSpeed(byte channel, ushort speed)
    {
        Write(SetSpeedCommand, channel, speed);
    }

    public void SetAcceleration(byte channel, ushort acceleration)
    {
        Write(SetAccelerationCommand, channel, acceleration);
    }

    public void SetTargetMicroseconds(byte channel, double microseconds)
    {
        if (microseconds < 0)
        {
            microseconds = 0;
        }

        var quarters = (ushort)Math.Clamp(Math.Round(microseconds * 4), 0, 65535);
        Write(SetTargetCommand, channel, quarters);
    }

    public void SetServoOff(byte channel) => SetTargetMicroseconds(channel, 0);

    public bool GetMovingState()
    {
        if (Simulate || _port is not { IsOpen: true })
        {
            return false;
        }

        lock (_gate)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                _port.DiscardInBuffer();
                _port.Write([GetMovingStateCommand], 0, 1);
                try
                {
                    var value = _port.ReadByte();
                    return value != 0;
                }
                catch (TimeoutException)
                {
                    // Keep treating the servos as moving so we do not start the next turn early.
                }
            }

            return true;
        }
    }

    public async Task WaitUntilIdleAsync(int timeoutMs, int settleMs, CancellationToken cancellationToken)
    {
        if (Simulate)
        {
            await Task.Delay(Math.Max(80, settleMs), cancellationToken);
            return;
        }

        var start = Environment.TickCount64;
        while (GetMovingState())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Environment.TickCount64 - start > timeoutMs)
            {
                break;
            }

            await Task.Delay(20, cancellationToken);
        }

        if (settleMs > 0)
        {
            await Task.Delay(settleMs, cancellationToken);
        }
    }

    void Write(byte command, byte channel, ushort value)
    {
        if (Simulate)
        {
            return;
        }

        if (_port is not { IsOpen: true })
        {
            throw new InvalidOperationException("The Mini Maestro is not connected. Select the Command Port and click Connect.");
        }

        var packet = new byte[]
        {
            command,
            channel,
            (byte)(value & 0x7F),
            (byte)((value >> 7) & 0x7F)
        };

        lock (_gate)
        {
            _port.Write(packet, 0, packet.Length);
        }
    }

    public void Dispose() => Disconnect();
}
