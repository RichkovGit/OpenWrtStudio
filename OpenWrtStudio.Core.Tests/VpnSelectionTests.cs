using OpenWrtStudio.Models;
using OpenWrtStudio.Services;
using Xunit;
namespace OpenWrtStudio.Core.Tests;
public class VpnSelectionTests
{
    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(false, 0, false)]
    [InlineData(true, 1, false)]
    public async Task SwitchingRequiresSuccessAndConfirmedRuntimeState(bool applied, int exitCode, bool expected)
    {
        var ssh = new RecordingSsh(applied, exitCode); var service = new ForkopService(ssh);
        var nodes = await service.GetServersAsync();
        var result = await service.SelectActiveServerAsync(nodes.Single(n => n.GroupTag == "node-a"));
        Assert.Equal(expected, result.Success);
        var command = Assert.Single(ssh.Commands.Where(c => c.Contains("set_group_proxy")));
        Assert.Contains("'main-out' 'node-a'", command);
        Assert.DoesNotContain("|| true", command); Assert.DoesNotContain("uci set", command);
    }
    [Fact]
    public async Task RemovedOrUnselectableTargetDoesNotSendSwitchCommand()
    {
        var ssh = new RecordingSsh(true,0); var service = new ForkopService(ssh);
        Assert.False((await service.SelectActiveServerAsync(new() { GroupTag = "direct-out", Name = "Direct" })).Success);
        Assert.False((await service.SelectActiveServerAsync(new() { GroupTag = "missing" })).Success);
        Assert.DoesNotContain(ssh.Commands, c => c.Contains("set_group_proxy"));
    }
    private sealed class RecordingSsh(bool applied,int exitCode) : ISshService
    {
        public List<string> Commands { get; } = new();
        private bool switched;
        public bool IsConnected => true;
        public ConnectionProfile? CurrentProfile => null;
        public event EventHandler<bool>? ConnectionChanged { add { } remove { } }
        public Task<(int,string,string)> ExecuteCommandAsync(string command,int timeoutSeconds=30)
        {
            Commands.Add(command);
            if (command.Contains("set_group_proxy")) { switched = applied && exitCode==0; return Task.FromResult((exitCode,"","denied")); }
            var snapshot=VpnTopologyTests.Snapshot();
            if(switched) snapshot=snapshot.Replace("\"now\": \"priority-route\"", "\"now\": \"node-a\"");
            return Task.FromResult((0,snapshot,""));
        }
        public Task<(bool,string)> ConnectAsync(ConnectionProfile p)=>throw new NotSupportedException();
        public Task DisconnectAsync()=>throw new NotSupportedException();
        public Task RunStreamingCommandAsync(string c,Action<string> a,CancellationToken t)=>throw new NotSupportedException();
        public Task<(bool,string)> UploadFileContentAsync(string c,string p)=>throw new NotSupportedException();
        public Task<(bool,string)> DownloadFileContentAsync(string p)=>throw new NotSupportedException();
        public Task<(bool,byte[]?,string)> DownloadBinaryFileAsync(string p)=>throw new NotSupportedException();
        public Task<(bool,string)> UploadBinaryFileAsync(byte[] d,string p)=>throw new NotSupportedException();
        public Task<(bool,string)> TestConnectionAsync(ConnectionProfile p)=>throw new NotSupportedException();
    }
}
