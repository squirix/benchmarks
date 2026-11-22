using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;

if (args is ["prepare", var directory])
{
    ClusterSetup.Create(directory);
    return;
}

if (args is not [var system, var endpointText, var repetitionText, var secondsText, var rateText]
    || system is not ("Squirix" or "etcd"))
    throw new ArgumentException("Usage: CacheQuorum prepare <directory> or CacheQuorum <Squirix|etcd> <endpoint> <repetition> <seconds> <rates>");

const int sessionCount = 48;
var endpoint = new Uri(endpointText);
var repetition = int.Parse(repetitionText, CultureInfo.InvariantCulture);
var seconds = int.Parse(secondsText, CultureInfo.InvariantCulture);
var rates = rateText.Split(',').Select(static value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
var keys = ClusterSetup.SelectKeys();
var encodedKeys = keys.Select(ByteString.CopyFromUtf8).ToArray();
var value = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(new string('x', 254)));
var token = system == "Squirix" ? File.ReadAllText("/credentials/token.txt") : null;
var sessions = new List<ISession>(sessionCount);

using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(2));
using var clusterHealth = token is null ? null : new SquirixClusterHealth(token);
try
{
    if (clusterHealth is not null)
    {
        var nodes = await clusterHealth.WaitUntilReadyAsync(startup.Token);
        Console.WriteLine(JsonSerializer.Serialize(new { System = system, Repetition = repetition, Phase = "before", Nodes = nodes }));
    }

    for (var worker = 0; worker < sessionCount; worker++)
    {
        sessions.Add(system == "Squirix"
            ? await SquirixSession.ConnectAsync(endpoint, keys, startup.Token, token)
            : new EtcdSession(endpoint, encodedKeys, value));
    }
    await sessions[0].ExecuteAsync(0, true, startup.Token);
    await sessions[0].ExecuteAsync(0, false, startup.Token);
    Console.Error.WriteLine($"{system}: verified PUT and quorum read over TLS");

    await Task.WhenAll(sessions.Select(async (session, worker) =>
    {
        for (var key = worker; key < keys.Length; key += sessions.Count)
            await session.ExecuteAsync(key, true, startup.Token);
    }));
    Console.Error.WriteLine($"{system}: seeded {keys.Length} keys using {sessionCount} sessions");

    var seed = 2113 + repetition;
    foreach (var workload in new[] { Workload.Write, Workload.Read })
    {
        _ = await LoadMeasurement.MeasureAsync(sessions, rates[0], 3, seed, keys.Length, workload);
        foreach (var rate in rates)
        {
            _ = await LoadMeasurement.MeasureAsync(sessions, rate, 2, seed, keys.Length, workload);
            var result = await LoadMeasurement.MeasureAsync(sessions, rate, seconds, seed, keys.Length, workload);
            var kind = workload.ToString().ToLowerInvariant();
            Console.WriteLine(JsonSerializer.Serialize(new { System = system, Repetition = repetition, Workload = kind, Result = result }));
            var p99 = workload == Workload.Write ? result.Write.P99Ms : result.Read.P99Ms;
            Console.Error.WriteLine($"{system}: {kind} {rate}/s, p99 {p99:F3} ms, drops {result.Dropped}");
        }
    }
    if (clusterHealth is not null)
    {
        using var finish = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var nodes = await clusterHealth.ReadAsync(finish.Token);
        Console.WriteLine(JsonSerializer.Serialize(new { System = system, Repetition = repetition, Phase = "after", Nodes = nodes }));
    }
}
finally
{
    foreach (var session in sessions)
        await session.DisposeAsync();
}
