using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Net.Client;
using Squirix.Benchmarks.Etcd;

internal sealed class EtcdSession : ISession
{
    private readonly GrpcChannel _channel;
    private readonly KV.KVClient _client;
    private readonly ByteString[] _keys;
    private readonly ByteString _value;

    internal EtcdSession(Uri endpoint, ByteString[] keys, ByteString value)
    {
        _channel = GrpcChannel.ForAddress(endpoint);
        _client = new KV.KVClient(_channel);
        _keys = keys;
        _value = value;
    }

    public async ValueTask ExecuteAsync(int key, bool write, CancellationToken cancellationToken)
    {
        var bytes = _keys[key];
        if (write)
        {
            using var call = _client.PutAsync(new PutRequest { Key = bytes, Value = _value }, cancellationToken: cancellationToken);
            var response = await call.ResponseAsync;
            if (response.Header?.Revision is not > 0)
                throw new InvalidDataException("etcd PUT did not return a commit revision.");
            return;
        }
        using var read = _client.RangeAsync(new RangeRequest { Key = bytes, Serializable = false }, cancellationToken: cancellationToken);
        var result = await read.ResponseAsync;
        if (result.Kvs.Count != 1 || !result.Kvs[0].Key.Equals(bytes) || !result.Kvs[0].Value.Equals(_value))
            throw new InvalidDataException("Unexpected value from etcd Range.");
    }

    public ValueTask DisposeAsync()
    {
        _channel.Dispose();
        return ValueTask.CompletedTask;
    }
}
