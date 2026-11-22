using System;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Squirix;
using Squirix.Client;

internal interface ISession : IAsyncDisposable
{
    ValueTask ExecuteAsync(int key, bool write, CancellationToken cancellationToken);
}

internal sealed class SquirixSession : ISession
{
    private static readonly string Value = new('x', 254);
    private readonly ISquirixClient _client;
    private readonly ICache<string> _cache;
    private readonly string[] _keys;

    private SquirixSession(ISquirixClient client, ICache<string> cache, string[] keys)
    {
        _client = client;
        _cache = cache;
        _keys = keys;
    }

    public static async Task<ISession> ConnectAsync(Uri endpoint, string[] keys, CancellationToken cancellationToken)
    {
        var client = await SquirixClient.ConnectAsync(endpoint, cancellationToken);
        try
        {
            var cache = await client.GetCacheAsync<string>("local-benchmark", cancellationToken);
            return new SquirixSession(client, cache, keys);
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    public async ValueTask ExecuteAsync(int key, bool write, CancellationToken cancellationToken)
    {
        var name = _keys[key];
        if (write)
        {
            await _cache.SetAsync(name, Value, cancellationToken: cancellationToken);
            return;
        }

        var result = await _cache.GetValueAsync(name, cancellationToken);
        if (!result.Found || !string.Equals(result.Value, Value, StringComparison.Ordinal))
            throw new InvalidDataException("Unexpected value from Squirix.");
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}

internal sealed class RespCommands
{
    public byte[][] Gets { get; }
    public byte[][] Sets { get; }
    public byte[] ExpectedValue { get; }

    public RespCommands(string[] keys, string value)
    {
        var json = JsonSerializer.Serialize(value);
        ExpectedValue = Encoding.ASCII.GetBytes($"${json.Length}\r\n{json}\r\n");
        Gets = keys.Select(static key => Encoding.ASCII.GetBytes($"*2\r\n$3\r\nGET\r\n$16\r\n{key}\r\n")).ToArray();
        Sets = keys.Select(key => Encoding.ASCII.GetBytes($"*3\r\n$3\r\nSET\r\n$16\r\n{key}\r\n${json.Length}\r\n{json}\r\n")).ToArray();
    }
}

internal sealed class RespSession : ISession
{
    private readonly TcpClient _client;
    private readonly SslStream _stream;
    private readonly RespCommands _commands;
    private readonly byte[] _response;

    private RespSession(TcpClient client, SslStream stream, RespCommands commands)
    {
        _client = client;
        _stream = stream;
        _commands = commands;
        _response = new byte[commands.ExpectedValue.Length];
    }

    public static async Task<ISession> ConnectAsync(Uri endpoint, RespCommands commands, CancellationToken cancellationToken)
    {
        var client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken);
            var stream = new SslStream(client.GetStream());
            try
            {
                await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = endpoint.Host }, cancellationToken);
                return new RespSession(client, stream, commands);
            }
            catch
            {
                await stream.DisposeAsync();
                throw;
            }
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async ValueTask ExecuteAsync(int key, bool write, CancellationToken cancellationToken)
    {
        var request = write ? _commands.Sets[key] : _commands.Gets[key];
        await _stream.WriteAsync(request, cancellationToken);
        var length = write ? 5 : _commands.ExpectedValue.Length;
        await _stream.ReadExactlyAsync(_response.AsMemory(0, length), cancellationToken);
        var expected = write ? "+OK\r\n"u8 : _commands.ExpectedValue.AsSpan();
        if (!_response.AsSpan(0, length).SequenceEqual(expected))
            throw new InvalidDataException("Unexpected RESP reply.");
    }

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync();
        _client.Dispose();
    }
}
