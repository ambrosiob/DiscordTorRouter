using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DiscordTorRouter.Routing;

namespace DiscordTorRouter.Network;

public static class Socks5Client
{
    public static async Task<TcpClient> ConnectAsync(string proxyHost, int proxyPort, RedirectedConnection destination, CancellationToken cancellationToken)
    {
        var client = new TcpClient(AddressFamily.InterNetwork);
        try
        {
            await client.ConnectAsync(proxyHost, proxyPort, cancellationToken);
            var stream = client.GetStream();
            await stream.WriteAsync(new byte[] { 5, 1, 0 }, cancellationToken);
            var greeting = new byte[2];
            await ReadExactlyAsync(stream, greeting, cancellationToken);
            if (greeting[0] != 5 || greeting[1] != 0) throw new IOException("O proxy Tor recusou autenticação SOCKS5 sem credenciais.");

            var request = BuildConnectRequest(destination);
            await stream.WriteAsync(request, cancellationToken);
            var header = new byte[4];
            await ReadExactlyAsync(stream, header, cancellationToken);
            if (header[0] != 5 || header[1] != 0) throw new IOException($"SOCKS5 CONNECT falhou com código 0x{header[1]:X2}.");
            var addressLength = header[3] switch
            {
                1 => 4,
                4 => 16,
                3 => await ReadLengthAsync(stream, cancellationToken),
                _ => throw new IOException("Resposta SOCKS5 contém tipo de endereço inválido.")
            };
            await ReadExactlyAsync(stream, new byte[addressLength + 2], cancellationToken);
            return client;
        }
        catch { client.Dispose(); throw; }
    }

    private static byte[] BuildConnectRequest(RedirectedConnection destination)
    {
        byte type;
        byte[] address;
        if (!IPAddress.TryParse(destination.Host, out var literal))
        {
            type = 3;
            var host = Encoding.ASCII.GetBytes(destination.Host);
            if (host.Length > 255) throw new IOException("Nome de host excede o limite do SOCKS5.");
            address = [(byte)host.Length, .. host];
        }
        else
        {
            literal = GatewayResolver.Normalize(literal);
            type = literal.AddressFamily == AddressFamily.InterNetwork ? (byte)1 : (byte)4;
            address = literal.GetAddressBytes();
        }
        var request = new byte[4 + address.Length + 2];
        request[0] = 5; request[1] = 1; request[2] = 0; request[3] = type;
        address.CopyTo(request, 4);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(request.Length - 2), destination.DestinationPort);
        return request;
    }

    private static async Task<int> ReadLengthAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var value = new byte[1];
        await ReadExactlyAsync(stream, value, cancellationToken);
        return value[0];
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[read..], cancellationToken);
            if (count == 0) throw new EndOfStreamException("Conexão SOCKS5 encerrada durante o handshake.");
            read += count;
        }
    }
}
