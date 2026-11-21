using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;
using Xunit;

namespace OpenWrtStudio.Tests;

public class SentinelFixAllTests
{
    private const string HealthyProbe =
        "===WAN_STATUS===\n{ \"up\": true, \"proto\": \"dhcp\", \"ipv4-address\": [ { \"address\": \"10.0.0.2\", \"mask\": 24 } ] }\n" +
        "===CARRIER===\n1\n===PING===\nping:ok\n===DNS_LOCAL===\nName: ya.ru\nAddress: 77.88.55.242\n" +
        "===DNS_PUBLIC===\nName: ya.ru\nAddress: 77.88.55.242\n===SERVICES===\ndnsmasq:ok\npppd:down\nmihomo:down\nsingbox:down\n===TUN===\ntun:none\n";

    private const string UnhealthyProbe =
        "===WAN_STATUS===\n{ \"up\": false, \"proto\": \"dhcp\" }\n" +
        "===CARRIER===\n1\n===PING===\nping:fail\n===DNS_LOCAL===\nfail\n===DNS_PUBLIC===\nfail\n" +
        "===SERVICES===\ndnsmasq:ok\npppd:down\nmihomo:down\nsingbox:down\n===TUN===\ntun:none\n";

    private sealed class Context
    {
        public bool Healthy;
        public readonly Dictionary<string, int> ExitCodes = new();
        public readonly List<string> MethodCommands = new();
        public readonly ScriptedSsh Ssh = new();
        public Action<string>? OnMethod;

        public Context()
        {
            Ssh.OnCommand = cmd =>
            {
                if (cmd.Contains("===WAN_STATUS===")) return (0, Healthy ? HealthyProbe : UnhealthyProbe, "");
                MethodCommands.Add(cmd);
                OnMethod?.Invoke(cmd);
                return (ExitCodes.TryGetValue(cmd, out var code) ? code : 0, "", "");
            };
        }
    }

    private static SentinelIssue IssueWith(params string[] commands) => new()
    {
        Title = "Тестовый сбой",
        RecoveryMethods = commands.Select((c, i) => new RecoveryMethod
        {
            Id = "m" + (i + 1),
            Name = "Метод " + (i + 1),
            Command = c
        }).ToList()
    };

    [Fact]
    public async Task FixAll_StopsAsSoonAsHealthIsRestored()
    {
        var ctx = new Context();
        ctx.OnMethod = cmd => { if (cmd == "m1") ctx.Healthy = true; };
        var sentinel = new SentinelService(ctx.Ssh);
        var report = await sentinel.FixAllAsync(IssueWith("m1", "m2", "m3"));
        Assert.True(report.Resolved);
        Assert.Single(report.Steps);
        Assert.True(report.Steps[0].Success);
        Assert.Equal(new[] { "m1" }, ctx.MethodCommands);
        Assert.Contains("устранена", report.Summary);
    }

    [Fact]
    public async Task FixAll_ContinuesAfterFailedMethod_AndRecordsEveryStep()
    {
        var ctx = new Context();
        ctx.ExitCodes["m1"] = 1;
        ctx.OnMethod = cmd => { if (cmd == "m3") ctx.Healthy = true; };
        var sentinel = new SentinelService(ctx.Ssh);
        var report = await sentinel.FixAllAsync(IssueWith("m1", "m2", "m3"));
        Assert.True(report.Resolved);
        Assert.Equal(3, report.Steps.Count);
        Assert.False(report.Steps[0].Success);
        Assert.True(report.Steps[1].Success);
        Assert.True(report.Steps[2].Success);
        Assert.Equal(new[] { "m1", "m2", "m3" }, ctx.MethodCommands);
    }

    [Fact]
    public async Task FixAll_WhenNothingHelps_ReportsFailureHonestly()
    {
        var ctx = new Context();
        ctx.ExitCodes["m1"] = 1;
        ctx.ExitCodes["m2"] = 1;
        var sentinel = new SentinelService(ctx.Ssh);
        var report = await sentinel.FixAllAsync(IssueWith("m1", "m2"));
        Assert.False(report.Resolved);
        Assert.False(report.RebootIssued);
        Assert.All(report.Steps, s => Assert.False(s.Success));
        Assert.Contains("не восстановлена", report.Summary);
    }

    [Fact]
    public async Task FixAll_RebootAlwaysRunsLast_AndOnlyAfterSoftMethods()
    {
        var ctx = new Context();
        ctx.ExitCodes["soft"] = 1;
        var sentinel = new SentinelService(ctx.Ssh);
        // Reboot is deliberately listed first in the issue; the orchestrator must defer it.
        var report = await sentinel.FixAllAsync(IssueWith("reboot", "soft"));
        Assert.True(report.RebootIssued);
        Assert.False(report.Resolved);
        Assert.Equal(new[] { "soft", "reboot" }, ctx.MethodCommands);
        Assert.Contains("перезагрузки", report.Summary);
    }

    [Fact]
    public async Task FixAll_PreCancelledToken_DoesNothing()
    {
        var ctx = new Context();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sentinel = new SentinelService(ctx.Ssh);
        var report = await sentinel.FixAllAsync(IssueWith("m1", "m2"), null, cts.Token);
        Assert.True(report.Cancelled);
        Assert.Empty(report.Steps);
        Assert.Empty(ctx.Ssh.Commands);
    }

    [Fact]
    public async Task FixAll_CancellationBetweenSteps_StopsTheRun()
    {
        var ctx = new Context();
        using var cts = new CancellationTokenSource();
        ctx.OnMethod = cmd => { if (cmd == "m1") cts.Cancel(); };
        var sentinel = new SentinelService(ctx.Ssh);
        var report = await sentinel.FixAllAsync(IssueWith("m1", "m2"), null, cts.Token);
        Assert.True(report.Cancelled);
        Assert.Single(report.Steps);
        Assert.Equal(new[] { "m1" }, ctx.MethodCommands);
    }

    [Fact]
    public async Task FixAll_WithoutConnection_FailsFast()
    {
        var ctx = new Context();
        ctx.Ssh.IsConnected = false;
        var sentinel = new SentinelService(ctx.Ssh);
        var report = await sentinel.FixAllAsync(IssueWith("m1"));
        Assert.False(report.Resolved);
        Assert.Contains("Нет подключения", report.Summary);
        Assert.Empty(ctx.Ssh.Commands);
    }

    [Fact]
    public async Task FixAll_ReportsProgressForEachStep()
    {
        var ctx = new Context();
        ctx.ExitCodes["m1"] = 1;
        ctx.ExitCodes["m2"] = 1;
        var progress = new List<SentinelFixProgress>();
        var sentinel = new SentinelService(ctx.Ssh);
        await sentinel.FixAllAsync(IssueWith("m1", "m2"), new ProgressCollector(progress));
        Assert.Equal(4, progress.Count);
        Assert.All(progress, p => Assert.Equal(2, p.TotalSteps));
        Assert.Equal(1, progress[0].StepIndex);
        Assert.Equal("Выполняется...", progress[0].Status);
        Assert.Equal("Ошибка", progress[1].Status);
        Assert.Equal(2, progress[2].StepIndex);
    }

    [Fact]
    public async Task FixAll_SecondConcurrentRun_IsRejected()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ctx = new Context();
        ctx.Ssh.OnCommandAsync = async cmd =>
        {
            if (cmd.Contains("===WAN_STATUS===")) return (0, UnhealthyProbe, "");
            if (cmd == "m1") await gate.Task;
            return (1, "", "");
        };
        var sentinel = new SentinelService(ctx.Ssh);
        var first = sentinel.FixAllAsync(IssueWith("m1", "m2"));
        await WaitUntilAsync(() => ctx.Ssh.Commands.Contains("m1"));
        var second = await sentinel.FixAllAsync(IssueWith("m1", "m2")).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Contains("уже выполняется", second.Summary);
        gate.SetResult(true);
        var firstReport = await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(firstReport.Resolved);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Condition was not reached in time.");
            await Task.Delay(20);
        }
    }

    private sealed class ProgressCollector : IProgress<SentinelFixProgress>
    {
        private readonly List<SentinelFixProgress> _list;
        public ProgressCollector(List<SentinelFixProgress> list) => _list = list;
        public void Report(SentinelFixProgress value) { lock (_list) _list.Add(value); }
    }
}
