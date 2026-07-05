using System;
using System.IO;
using System.Text;

namespace ARKServerCreationTool.Services.Rcon
{
    /// <summary>
    /// Valve Source RCON packet. Wire layout (all ints little-endian):
    /// [Size:i32][Id:i32][Type:i32][Body:bytes][0x00][0x00], where Size = Body.Length + 10
    /// and excludes its own 4 bytes. Within the payload (bytes after Size): Id@0, Type@4, Body@8.
    /// </summary>
    public class RconPacket
    {
        public const int TypeResponseValue = 0;
        public const int TypeAuthResponse = 2;
        public const int TypeExecCommand = 2;
        public const int TypeAuth = 3;

        public int Id { get; set; }
        public int Type { get; set; }
        public string Body { get; set; } = string.Empty;

        public byte[] Encode()
        {
            byte[] body = Encoding.UTF8.GetBytes(Body);
            int size = body.Length + 10; // Id(4) + Type(4) + body + null + null
            using var ms = new MemoryStream(size + 4);
            using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
            w.Write(size);    // BinaryWriter writes little-endian
            w.Write(Id);
            w.Write(Type);
            w.Write(body);
            w.Write((byte)0); // body null terminator
            w.Write((byte)0); // empty-string null pad
            w.Flush();
            return ms.ToArray();
        }

        /// <summary>Reads exactly one framed packet from the stream (synchronous; used by tests).</summary>
        public static RconPacket Read(Stream stream)
        {
            int size = BitConverter.ToInt32(ReadExactly(stream, 4), 0);
            return Parse(ReadExactly(stream, size));
        }

        /// <summary>Parses a packet payload (the bytes after the leading Size field).</summary>
        public static RconPacket Parse(byte[] payload)
        {
            int id = BitConverter.ToInt32(payload, 0);
            int type = BitConverter.ToInt32(payload, 4);
            int bodyLen = Math.Max(0, payload.Length - 8 - 2); // minus Id/Type header and the two trailing nulls
            string body = Encoding.UTF8.GetString(payload, 8, bodyLen);
            return new RconPacket { Id = id, Type = type, Body = body };
        }

        private static byte[] ReadExactly(Stream s, int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = s.Read(buffer, offset, count - offset);
                if (read == 0) throw new EndOfStreamException("RCON stream closed mid-packet.");
                offset += read;
            }
            return buffer;
        }
    }
}
