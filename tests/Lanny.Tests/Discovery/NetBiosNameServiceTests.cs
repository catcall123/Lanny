using Lanny.Discovery;

namespace Lanny.Tests.Discovery;

public class NetBiosNameServiceTests
{
    [Fact]
    public void TryParseNodeStatusResponse_PrefersUniqueWorkstationName()
    {
        const ushort transactionId = 0x1337;
        var response = CreateNodeStatusResponse(
            transactionId,
            ("DESKTOP01", 0x00, false),
            ("WORKGROUP", 0x00, true),
            ("DESKTOP01", 0x20, false));

        var hostname = NetBiosNameService.TryParseNodeStatusResponse(response, transactionId);

        Assert.Equal("DESKTOP01", hostname);
    }

    [Fact]
    public void TryParseNodeStatusResponse_WithUnexpectedTransactionId_ReturnsNull()
    {
        var response = CreateNodeStatusResponse(0x1337, ("DESKTOP01", 0x00, false));

        var hostname = NetBiosNameService.TryParseNodeStatusResponse(response, 0x9999);

        Assert.Null(hostname);
    }

    private static byte[] CreateNodeStatusResponse(
        ushort transactionId,
        params (string Name, byte Suffix, bool IsGroup)[] entries)
    {
        var payload = new List<byte>
        {
            0x20,
        };

        payload.AddRange(System.Text.Encoding.ASCII.GetBytes("CKAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));
        payload.Add(0x00);
        payload.Add(0x00);
        payload.Add(0x21);
        payload.Add(0x00);
        payload.Add(0x01);
        payload.AddRange([0x00, 0x00, 0x00, 0x00]);

        var recordLength = (ushort)(1 + (entries.Length * 18) + 6);
        payload.Add((byte)(recordLength >> 8));
        payload.Add((byte)recordLength);
        payload.Add((byte)entries.Length);

        foreach (var entry in entries)
        {
            var paddedName = entry.Name.PadRight(15);
            payload.AddRange(System.Text.Encoding.ASCII.GetBytes(paddedName[..15]));
            payload.Add(entry.Suffix);

            var flags = entry.IsGroup ? (ushort)0x8000 : (ushort)0x0000;
            payload.Add((byte)(flags >> 8));
            payload.Add((byte)flags);
        }

        payload.AddRange([0x00, 0x11, 0x22, 0x33, 0x44, 0x55]);

        var response = new List<byte>
        {
            (byte)(transactionId >> 8),
            (byte)transactionId,
            0x85,
            0x00,
            0x00,
            0x00,
            0x00,
            0x01,
            0x00,
            0x00,
            0x00,
            0x00,
        };

        response.AddRange(payload);
        return [.. response];
    }
}
