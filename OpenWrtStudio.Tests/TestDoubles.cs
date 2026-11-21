using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;

namespace OpenWrtStudio.Tests;

/// <summary>Scriptable SSH double recording every command and upload for offline tests.</summary>
internal sealed class ScriptedSsh : ISshService
{
    public bool IsConnected { get; set; } = true;
    public ConnectionProfile? CurrentProfile => null;
    public event EventHandler<bool>? ConnectionChanged;
    public void SetConnected(bool connected)
    {
        IsConnected = connected;
        ConnectionChanged?.Invoke(this, connected);
    }

    public List<string> Commands { get; } = new();
    public List<(string Path, string Content)> Uploads { get; } = new();
    public Func<string, (int ExitCode, string Output, string Error)> OnCommand { get; set; } = _ => (0, "", "");
    public Func<string, Task<(int ExitCode, string Output, string Error)>>? OnCommandAsync { get; set; }
    public Func<string, string, (bool Success, string Message)> OnUpload { get; set; } = (_, _) => (true, "");

    public async Task<(int ExitCode, string Output, string Error)> ExecuteCommandAsync(string command, int timeoutSeconds = 30)
    {
        Commands.Add(command);
        if (OnCommandAsync != null) return await OnCommandAsync(command).ConfigureAwait(false);
        return OnCommand(command);
    }

    public Task<(bool Success, string Message)> UploadFileContentAsync(string content, string remotePath)
    {
        Uploads.Add((remotePath, content));
        return Task.FromResult(OnUpload(remotePath, content));
    }

    public Task<(bool Success, string Message)> ConnectAsync(ConnectionProfile profile) => Task.FromResult((true, ""));
    public Task DisconnectAsync() { IsConnected = false; return Task.CompletedTask; }
    public Task RunStreamingCommandAsync(string command, Action<string> onLineReceived, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<(bool Success, string Content)> DownloadFileContentAsync(string remotePath) => Task.FromResult((false, ""));
    public Task<(bool Success, byte[]? Data, string Error)> DownloadBinaryFileAsync(string remotePath) => Task.FromResult<(bool, byte[]?, string)>((false, null, ""));
    public Task<(bool Success, string Message)> UploadBinaryFileAsync(byte[] data, string remotePath) => Task.FromResult((true, ""));
    public Task<(bool Success, string Message)> TestConnectionAsync(ConnectionProfile profile) => Task.FromResult((true, ""));
}
