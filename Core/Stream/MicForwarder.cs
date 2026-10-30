using System.Net.Sockets;
using System.Runtime.Versioning;
using NAudio.Wave;

namespace AntCloud.Core.Stream;

/// Captures the default microphone and sends raw 44.1 kHz / 16-bit / mono PCM over UDP to the VM, the same
/// NAudio WaveInEvent → UdpClient pipeline AntCloud's own client uses (and the format the VM expects).
[SupportedOSPlatform("windows")]
sealed class MicForwarder : IDisposable
{
    private readonly WaveInEvent wave;
    private readonly UdpClient udp;

    private MicForwarder(string host, int port)
    {
        udp = new UdpClient();
        udp.Connect(host, port);
        wave = new WaveInEvent { WaveFormat = new WaveFormat(44_100, 16, 1), BufferMilliseconds = 50 };
        wave.DataAvailable += (_, e) =>
        {
            try { udp.Send(e.Buffer, e.BytesRecorded); }
            catch (SocketException) { }
        };
    }

    public static MicForwarder Start(string host, int port = Config.MicPort)
    {
        var mic = new MicForwarder(host, port);
        try
        {
            mic.wave.StartRecording();
        }
        catch (Exception e)
        {
            mic.Dispose();
            Log.Info($"[mic] couldn't start: {e.Message}");
            throw new Exception("Couldn't use the microphone. Check Settings → Privacy & security → Microphone, and that one is plugged in.");
        }
        Log.Info($"[mic] forwarding to {host}:{port}");
        return mic;
    }

    public void Dispose()
    {
        try { wave.StopRecording(); } catch (Exception) { }
        wave.Dispose();
        udp.Dispose();
    }
}
