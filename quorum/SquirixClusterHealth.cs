using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

internal sealed class SquirixClusterHealth : IDisposable
{
    private readonly HttpClient _client = new()
    {
        DefaultRequestVersion = HttpVersion.Version20,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        Timeout = TimeSpan.FromSeconds(4),
    };

    internal SquirixClusterHealth(string token) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    internal async Task<ClusterNode[]> WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                return await ReadAsync(cancellationToken);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
                error is HttpRequestException or TaskCanceledException or InvalidDataException)
            {
                Console.Error.WriteLine($"Waiting for the Squirix cluster: {error.Message}");
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }

    internal async Task<ClusterNode[]> ReadAsync(CancellationToken cancellationToken)
    {
        var nodes = await Task.WhenAll(ReadNodeAsync("A", cancellationToken), ReadNodeAsync("B", cancellationToken),
            ReadNodeAsync("C", cancellationToken));
        var leaderCopies = nodes[0].Copies.OrderBy(static copy => copy.NodeId).ToArray();
        if (leaderCopies is not [{ NodeId: "B", State: "ready" }, { NodeId: "C", State: "ready" }] ||
            nodes[1].Copies.Length != 0 || nodes[2].Copies.Length != 0)
            throw new InvalidDataException("Group A must be led by A with ready copies on B and C.");

        return nodes;
    }

    public void Dispose() => _client.Dispose();

    private async Task<ClusterNode> ReadNodeAsync(string node, CancellationToken cancellationToken)
    {
        var endpoint = $"https://node-{node.ToLowerInvariant()}:5000";
        using var ready = await _client.GetAsync(endpoint + "/health/ready", cancellationToken);
        _ = ready.EnsureSuccessStatusCode();
        using var details = JsonDocument.Parse(await _client.GetStringAsync(endpoint + "/health/ready/details", cancellationToken));
        foreach (var group in details.RootElement.GetProperty("replicaGroups").EnumerateArray())
        {
            if (group.GetProperty("groupId").GetString() != "A")
                continue;
            var copies = group.GetProperty("copies").Deserialize<ReplicaCopy[]>(JsonSerializerOptions.Web)
                ?? throw new InvalidDataException($"Node {node} returned no copy state.");
            return new ClusterNode(node, copies);
        }

        throw new InvalidDataException($"Node {node} does not report replica group A.");
    }
}

internal sealed record ClusterNode(string NodeId, ReplicaCopy[] Copies);
internal sealed record ReplicaCopy(string NodeId, string State, ulong LagEntries);
