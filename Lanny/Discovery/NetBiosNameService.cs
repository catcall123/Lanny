using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Lanny.Discovery;

public sealed class NetBiosNameService : INetBiosNameService
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromMilliseconds(750);

    public NetBiosNameService(ILogger<NetBiosNameService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
    }

    public async Task<string?> ResolveAsync(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ipAddress);

        if (ipAddress.AddressFamily != AddressFamily.InterNetwork)
            return null;

        using var udpClient = new UdpClient(AddressFamily.InterNetwork);
        udpClient.Client.ReceiveTimeout = (int)QueryTimeout.TotalMilliseconds;
        udpClient.Client.SendTimeout = (int)QueryTimeout.TotalMilliseconds;

        var transactionId = (ushort)RandomNumberGenerator.GetInt32(0, ushort.MaxValue + 1);
        var endpoint = new IPEndPoint(ipAddress, 137);
        var request = BuildNodeStatusRequest(transactionId);

        try
        {
            await udpClient.SendAsync(request, request.Length, endpoint);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(QueryTimeout);

            var response = await udpClient.ReceiveAsync(timeoutCts.Token);
            return TryParseNodeStatusResponse(response.Buffer, transactionId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    public static string? TryParseNodeStatusResponse(ReadOnlySpan<byte> response, ushort expectedTransactionId)
    {
        if (response.Length < 25)
            return null;

        if (BinaryPrimitives.ReadUInt16BigEndian(response[..2]) != expectedTransactionId)
            return null;

        var flags = BinaryPrimitives.ReadUInt16BigEndian(response.Slice(2, 2));
        if ((flags & 0x8000) == 0)
            return null;

        var questionCount = BinaryPrimitives.ReadUInt16BigEndian(response.Slice(4, 2));
        var answerCount = BinaryPrimitives.ReadUInt16BigEndian(response.Slice(6, 2));
        if (answerCount == 0)
            return null;

        var offset = 12;
        for (var i = 0; i < questionCount; i++)
        {
            if (!TrySkipDnsName(response, offset, out offset) || response.Length < offset + 4)
                return null;

            offset += 4;
        }

        if (!TrySkipDnsName(response, offset, out offset) || response.Length < offset + 10)
            return null;

        var queryType = BinaryPrimitives.ReadUInt16BigEndian(response.Slice(offset, 2));
        var queryClass = BinaryPrimitives.ReadUInt16BigEndian(response.Slice(offset + 2, 2));
        var recordLength = BinaryPrimitives.ReadUInt16BigEndian(response.Slice(offset + 8, 2));
        if (queryType != 0x0021 || queryClass != 0x0001)
            return null;

        offset += 10;
        if (response.Length < offset + recordLength || recordLength < 1)
            return null;

        var nameCount = response[offset++];
        if (response.Length < offset + (nameCount * 18))
            return null;

        string? workstationName = null;
        string? serverName = null;
        string? uniqueName = null;

        for (var i = 0; i < nameCount; i++)
        {
            var name = Encoding.ASCII.GetString(response.Slice(offset, 15)).TrimEnd(' ', '\0');
            var suffix = response[offset + 15];
            var nameFlags = BinaryPrimitives.ReadUInt16BigEndian(response.Slice(offset + 16, 2));
            var isGroup = (nameFlags & 0x8000) != 0;
            offset += 18;

            if (isGroup || string.IsNullOrWhiteSpace(name) || name == "*")
                continue;

            uniqueName ??= name;
            if (suffix == 0x00)
                workstationName ??= name;
            else if (suffix == 0x20)
                serverName ??= name;
        }

        return workstationName ?? serverName ?? uniqueName;
    }

    private static byte[] BuildNodeStatusRequest(ushort transactionId)
    {
        var encodedName = EncodeWildcardName();
        var request = new byte[12 + encodedName.Length + 4];

        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0, 2), transactionId);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2, 2), 0x0000);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4, 2), 0x0001);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(6, 2), 0x0000);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(8, 2), 0x0000);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(10, 2), 0x0000);

        encodedName.CopyTo(request, 12);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(12 + encodedName.Length, 2), 0x0021);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(14 + encodedName.Length, 2), 0x0001);

        return request;
    }

    private static byte[] EncodeWildcardName()
    {
        var rawName = new byte[16];
        rawName[0] = (byte)'*';

        var encodedName = new byte[34];
        encodedName[0] = 0x20;

        for (var i = 0; i < rawName.Length; i++)
        {
            encodedName[1 + (i * 2)] = (byte)('A' + ((rawName[i] >> 4) & 0x0F));
            encodedName[2 + (i * 2)] = (byte)('A' + (rawName[i] & 0x0F));
        }

        encodedName[^1] = 0x00;
        return encodedName;
    }

    private static bool TrySkipDnsName(ReadOnlySpan<byte> buffer, int offset, out int nextOffset)
    {
        nextOffset = offset;

        while (nextOffset < buffer.Length)
        {
            var labelLength = buffer[nextOffset];
            if (labelLength == 0)
            {
                nextOffset++;
                return true;
            }

            if ((labelLength & 0xC0) == 0xC0)
            {
                if (nextOffset + 1 >= buffer.Length)
                    return false;

                nextOffset += 2;
                return true;
            }

            nextOffset++;
            if (nextOffset + labelLength > buffer.Length)
                return false;

            nextOffset += labelLength;
        }

        return false;
    }
}
