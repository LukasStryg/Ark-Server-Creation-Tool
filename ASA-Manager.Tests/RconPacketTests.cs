using System;
using System.IO;
using ARKServerCreationTool.Services.Rcon;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class RconPacketTests
    {
        [Fact]
        public void Encode_produces_size_equal_to_body_plus_ten()
        {
            var p = new RconPacket { Id = 1, Type = RconPacket.TypeExecCommand, Body = "SaveWorld" };
            byte[] bytes = p.Encode();
            // total on wire = size field (4) + size value; size value = body(9) + 10 = 19 => total 23
            Assert.Equal(23, bytes.Length);
            int size = BitConverter.ToInt32(bytes, 0);
            Assert.Equal(19, size);
        }

        [Fact]
        public void Encode_writes_id_and_type_little_endian_and_two_trailing_nulls()
        {
            var p = new RconPacket { Id = 0x0BADC0DE, Type = RconPacket.TypeAuth, Body = "pw" };
            byte[] bytes = p.Encode();
            Assert.Equal(0x0BADC0DE, BitConverter.ToInt32(bytes, 4)); // Id at offset 4
            Assert.Equal(RconPacket.TypeAuth, BitConverter.ToInt32(bytes, 8)); // Type at offset 8
            Assert.Equal(0, bytes[bytes.Length - 1]);
            Assert.Equal(0, bytes[bytes.Length - 2]);
        }

        [Fact]
        public void Read_round_trips_an_encoded_packet()
        {
            var original = new RconPacket { Id = 42, Type = RconPacket.TypeResponseValue, Body = "World Saved" };
            using var ms = new MemoryStream(original.Encode());
            var read = RconPacket.Read(ms);
            Assert.Equal(42, read.Id);
            Assert.Equal(RconPacket.TypeResponseValue, read.Type);
            Assert.Equal("World Saved", read.Body);
        }

        [Fact]
        public void Read_parses_auth_failure_id_minus_one()
        {
            var fail = new RconPacket { Id = -1, Type = RconPacket.TypeAuthResponse, Body = "" };
            using var ms = new MemoryStream(fail.Encode());
            var read = RconPacket.Read(ms);
            Assert.Equal(-1, read.Id);
            Assert.Equal(RconPacket.TypeAuthResponse, read.Type);
        }
    }
}
