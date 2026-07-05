using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ARKServerCreationTool.Services.Rcon
{
    /// <summary>Source-RCON client over TCP for an ASA dedicated server. One command/response at a time.</summary>
    public class RconClient : IRconClient
    {
        private const int RequestId = 0x0BADC0DE;

        private readonly string _host;
        private readonly int _port;
        private TcpClient? _tcp;
        private NetworkStream? _stream;
        private int _nextId = 1;

        public RconClient(string host, int port)
        {
            _host = host;
            _port = port;
        }

        public async Task<bool> ConnectAndAuthenticateAsync(string password, CancellationToken ct = default)
        {
            _tcp = new TcpClient { NoDelay = true };
            await _tcp.ConnectAsync(_host, _port, ct);
            _stream = _tcp.GetStream();

            var auth = new RconPacket { Id = RequestId, Type = RconPacket.TypeAuth, Body = password };
            await _stream.WriteAsync(auth.Encode(), ct);

            // The full Source spec may send a leading empty RESPONSE_VALUE before the AUTH_RESPONSE; skip it.
            for (int i = 0; i < 3; i++)
            {
                var reply = await ReadPacketAsync(ct);
                if (reply.Type == RconPacket.TypeAuthResponse)
                    return reply.Id != -1 && reply.Id == RequestId; // -1 = bad password
            }
            return false;
        }

        public async Task<string> ExecuteAsync(string command, CancellationToken ct = default)
        {
            if (_stream == null) throw new InvalidOperationException("RCON is not connected.");
            var pkt = new RconPacket { Id = _nextId++, Type = RconPacket.TypeExecCommand, Body = command };
            await _stream.WriteAsync(pkt.Encode(), ct);
            var reply = await ReadPacketAsync(ct); // ARK returns a single framed response packet per command
            return reply.Body;
        }

        private async Task<RconPacket> ReadPacketAsync(CancellationToken ct)
        {
            byte[] sizeBuf = new byte[4];
            await _stream!.ReadExactlyAsync(sizeBuf, ct);
            int size = BitConverter.ToInt32(sizeBuf, 0);
            byte[] payload = new byte[size];
            await _stream.ReadExactlyAsync(payload, ct);
            return RconPacket.Parse(payload);
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _tcp?.Dispose();
        }
    }
}
