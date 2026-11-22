using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

internal static class LoadMeasurement
{
    internal static async Task<PointResult> MeasureAsync(
        IReadOnlyList<ISession> sessions, int rate, int seconds, int seed, int keyCount, Workload workload = Workload.Mixed)
    {
        var total = checked(rate * seconds);
        var random = new Random(seed);
        var keyIds = new int[total];
        for (var i = 0; i < total; i++)
            keyIds[i] = random.Next(keyCount);

        var queue = Channel.CreateBounded<Work>(new BoundedChannelOptions(sessions.Count * 8)
        {
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        var latency = new long[total];
        var serviceTime = new long[total];
        var schedulerLag = new long[total];
        var completedInWindow = new bool[total];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds + 20));
        var start = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 10;
        var end = start + seconds * Stopwatch.Frequency;

        var workers = sessions.Select(async session =>
        {
            await foreach (var work in queue.Reader.ReadAllAsync(timeout.Token))
            {
                var dispatched = Stopwatch.GetTimestamp();
                await session.ExecuteAsync(keyIds[work.Index], IsWrite(workload, work.Index), timeout.Token);
                var completed = Stopwatch.GetTimestamp();
                latency[work.Index] = completed - work.Due;
                serviceTime[work.Index] = completed - dispatched;
                completedInWindow[work.Index] = completed <= end;
            }
        }).ToArray();

        var dropped = 0;
        var scheduler = new Thread(() =>
        {
            for (var index = 0; index < total; index++)
            {
                var due = start + index * Stopwatch.Frequency / rate;
                long now;
                while ((now = Stopwatch.GetTimestamp()) < due)
                    Thread.SpinWait(12);
                schedulerLag[index] = now - due;
                if (!queue.Writer.TryWrite(new Work(index, due)))
                    dropped++;
            }
            _ = queue.Writer.TryComplete();
        });
        scheduler.IsBackground = true;
        scheduler.Start();
        await Task.WhenAll(workers);
        scheduler.Join();

        var reads = new List<long>(total);
        var writes = new List<long>(total / 20);
        var readService = new List<long>(total);
        var writeService = new List<long>(total / 20);
        for (var i = 0; i < total; i++)
        {
            if (latency[i] == 0)
                continue;
            if (IsWrite(workload, i))
            {
                writes.Add(latency[i]);
                writeService.Add(serviceTime[i]);
            }
            else
            {
                reads.Add(latency[i]);
                readService.Add(serviceTime[i]);
            }
        }

        return new PointResult(rate, seconds, total, dropped, completedInWindow.Count(static done => done),
            Distribution.From(reads), Distribution.From(writes), Distribution.From(readService),
            Distribution.From(new List<long>(schedulerLag)), Distribution.From(writeService));
    }

    private static bool IsWrite(Workload workload, int index) =>
        workload == Workload.Write || (workload == Workload.Mixed && index % 20 == 0);
}

internal enum Workload { Mixed, Read, Write }

internal readonly record struct Work(int Index, long Due);

internal sealed record PointResult(int OfferedOpsPerSecond, int Seconds, int Offered, int Dropped,
    int CompletedWithinWindow, Distribution Read, Distribution Write, Distribution ReadService, Distribution SchedulerLag,
    Distribution WriteService);

internal readonly record struct Distribution(int Count, double P50Ms, double P95Ms, double P99Ms)
{
    public static Distribution From(List<long> samples)
    {
        samples.Sort();
        return new(samples.Count, At(samples, .5), At(samples, .95), At(samples, .99));
    }

    private static double At(List<long> samples, double percentile) => samples.Count == 0
        ? 0
        : samples[(int)Math.Ceiling(samples.Count * percentile) - 1] * 1000.0 / Stopwatch.Frequency;
}
