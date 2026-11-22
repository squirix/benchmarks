using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

const int sessionCount = 48;
const double schedulerBudgetMs = 0.5;
const int maxWindows = 3;
var keys = new string[16_384];
for (var i = 0; i < keys.Length; i++)
    keys[i] = $"key:{i:D12}";

var value = new string('x', 254);
if (args is ["certificates", var directory])
{
    Certificates.Create(directory);
    return;
}

var system = args[0];
var repetition = int.Parse(args[1], CultureInfo.InvariantCulture);
var measurementSeconds = int.Parse(args[2], CultureInfo.InvariantCulture);
var rates = args[3].Split(',').Select(static value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
var usesSquirix = system == "Squirix";
var commands = new RespCommands(keys, value);
var endpoint = new Uri("https://localhost:6380");
var sessions = new List<ISession>();

using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(2));
try
{
    var probe = await (usesSquirix
        ? SquirixSession.ConnectAsync(endpoint, keys, startup.Token)
        : RespSession.ConnectAsync(endpoint, commands, startup.Token));
    sessions.Add(probe);
    await probe.ExecuteAsync(0, true, startup.Token);
    await probe.ExecuteAsync(0, false, startup.Token);
    Console.Error.WriteLine($"{system}: TLS SET/GET passed");

    var opening = Enumerable.Range(1, sessionCount - 1).Select(_ => usesSquirix
        ? SquirixSession.ConnectAsync(endpoint, keys, startup.Token)
        : RespSession.ConnectAsync(endpoint, commands, startup.Token));
    sessions.AddRange(await Task.WhenAll(opening));
    await Task.WhenAll(sessions.Select(async (session, worker) =>
    {
        for (var key = worker; key < keys.Length; key += sessions.Count)
            await session.ExecuteAsync(key, true, startup.Token);
    }));
    Console.Error.WriteLine($"{system}: seeded {keys.Length} keys on {sessionCount} sessions");

    var seed = 1138 + repetition;
    _ = await LoadMeasurement.MeasureAsync(sessions, rates[0], 5, seed, keys.Length);
    foreach (var rate in rates)
    {
        var earlierWindows = new List<PointResult>(maxWindows - 1);
        PointResult result;
        while (true)
        {
            _ = await LoadMeasurement.MeasureAsync(sessions, rate, 2, seed, keys.Length);
            result = await LoadMeasurement.MeasureAsync(sessions, rate, measurementSeconds, seed, keys.Length);
            if (result.SchedulerLag.P99Ms <= schedulerBudgetMs || earlierWindows.Count == maxWindows - 1)
                break;

            earlierWindows.Add(result);
            Console.Error.WriteLine($"{system}: retrying {rate}/s after scheduler p99 {result.SchedulerLag.P99Ms:F3} ms");
        }
        Console.WriteLine(JsonSerializer.Serialize(new { System = system, Repetition = repetition, Result = result, EarlierWindows = earlierWindows }));
        Console.Error.WriteLine($"{system}: {rate}/s, p99 GET {result.Read.P99Ms:F3} ms, drops {result.Dropped}");
    }
}
finally
{
    foreach (var session in sessions)
        await session.DisposeAsync();
}
