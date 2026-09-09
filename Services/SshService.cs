using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenWrtStudio.Models;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace OpenWrtStudio.Services;

public interface ISshService
{
    bool IsConnected { get; }
    ConnectionProfile? CurrentProfile { get; }
    event EventHandler<bool>? ConnectionChanged;

    Task<(bool Success, string Message)> ConnectAsync(ConnectionProfile profile);
    Task DisconnectAsync();
    Task<(int ExitCode, string Output, string Error)> ExecuteCommandAsync(string command, int timeoutSeconds = 30);
    Task RunStreamingCommandAsync(string command, Action<string> onLineReceived, CancellationToken cancellationToken);
    Task<(bool Success, string Message)> UploadFileContentAsync(string content, string remotePath);
    Task<(bool Success, string Content)> DownloadFileContentAsync(string remotePath);
    Task<(bool Success, string Message)> TestConnectionAsync(ConnectionProfile profile);
}

public class SshService : ISshService, IDisposable
{
    private SshClient? _sshClient;
    private SftpClient? _sftpClient;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsConnected => _sshClient?.IsConnected == true;
    public ConnectionProfile? CurrentProfile { get; private set; }
    public event EventHandler<bool>? ConnectionChanged;

    public async Task<(bool Success, string Message)> ConnectAsync(ConnectionProfile profile)
    {
        await _lock.WaitAsync();
        try
        {
            await DisconnectInternalAsync();
            var connectionInfo = CreateConnectionInfo(profile);
            _sshClient = new SshClient(connectionInfo);

            await Task.Run(() =>
            {
                _sshClient.Connect();
            });

            // SFTP is optional (most OpenWrt routers only have Dropbear SSH without SFTP subsystem)
            try
            {
                _sftpClient = new SftpClient(connectionInfo);
                _sftpClient.Connect();
            }
            catch
            {
                _sftpClient?.Dispose();
                _sftpClient = null;
            }

            CurrentProfile = profile;
            ConnectionChanged?.Invoke(this, true);
            return (true, "Подключение к роутеру успешно установлено!");
        }
        catch (Exception ex)
        {
            await DisconnectInternalAsync();
            ConnectionChanged?.Invoke(this, false);
            return (false, $"Ошибка подключения: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await DisconnectInternalAsync();
            ConnectionChanged?.Invoke(this, false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private Task DisconnectInternalAsync()
    {
        try
        {
            if (_sftpClient?.IsConnected == true) _sftpClient.Disconnect();
            _sftpClient?.Dispose();
            _sftpClient = null;

            if (_sshClient?.IsConnected == true) _sshClient.Disconnect();
            _sshClient?.Dispose();
            _sshClient = null;
        }
        catch
        {
            // Ignore on cleanup
        }
        CurrentProfile = null;
        return Task.CompletedTask;
    }

    public async Task<(int ExitCode, string Output, string Error)> ExecuteCommandAsync(string command, int timeoutSeconds = 30)
    {
        if (!IsConnected || _sshClient == null)
        {
            return (-1, string.Empty, "Нет подключения к роутеру");
        }

        return await Task.Run(() =>
        {
            try
            {
                using var cmd = _sshClient.CreateCommand(command);
                cmd.CommandTimeout = TimeSpan.FromSeconds(timeoutSeconds);
                var asyncResult = cmd.BeginExecute();
                asyncResult.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(timeoutSeconds));
                cmd.EndExecute(asyncResult);

                return (cmd.ExitStatus ?? 0, cmd.Result, cmd.Error);
            }
            catch (Exception ex)
            {
                return (-1, string.Empty, ex.Message);
            }
        });
    }

    public async Task RunStreamingCommandAsync(string command, Action<string> onLineReceived, CancellationToken cancellationToken)
    {
        if (!IsConnected || _sshClient == null)
        {
            onLineReceived("Ошибка: Роутер не подключен.");
            return;
        }

        await Task.Run(() =>
        {
            try
            {
                using var cmd = _sshClient.CreateCommand(command);
                var asyncResult = cmd.BeginExecute();

                using var reader = new StreamReader(cmd.OutputStream, Encoding.UTF8);
                while (!asyncResult.IsCompleted && !cancellationToken.IsCancellationRequested)
                {
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        if (line != null)
                        {
                            onLineReceived(line);
                        }
                    }
                    Thread.Sleep(50);
                }

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (line != null)
                    {
                        onLineReceived(line);
                    }
                }
            }
            catch (Exception ex)
            {
                onLineReceived($"[Ошибка потока]: {ex.Message}");
            }
        }, cancellationToken);
    }

    public async Task<(bool Success, string Message)> UploadFileContentAsync(string content, string remotePath)
    {
        if (!IsConnected || _sshClient == null)
        {
            return (false, "Роутер не подключен");
        }

        return await Task.Run(() =>
        {
            try
            {
                var dir = Path.GetDirectoryName(remotePath)?.Replace("\\", "/");

                // Try SFTP first if available
                if (_sftpClient?.IsConnected == true)
                {
                    if (!string.IsNullOrEmpty(dir) && !_sftpClient.Exists(dir))
                    {
                        CreateRemoteDirectoriesRecursively(_sftpClient, dir);
                    }

                    using var ms = new MemoryStream(Encoding.UTF8.GetBytes(content));
                    _sftpClient.UploadFile(ms, remotePath, true);
                    return (true, $"Файл успешно загружен в {remotePath}");
                }

                // Fallback: Robust base64 upload via SSH (compatible with 100% of OpenWrt/BusyBox routers)
                if (!string.IsNullOrEmpty(dir))
                {
                    _sshClient.RunCommand($"mkdir -p '{dir}'");
                }

                var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
                var uploadCmd = $"echo '{b64}' | base64 -d > '{remotePath}'";
                var res = _sshClient.RunCommand(uploadCmd);
                if (res.ExitStatus == 0)
                {
                    return (true, $"Файл успешно загружен в {remotePath}");
                }

                return (false, $"Ошибка передачи файла: {res.Error}");
            }
            catch (Exception ex)
            {
                return (false, $"Ошибка передачи файла: {ex.Message}");
            }
        });
    }

    public async Task<(bool Success, string Content)> DownloadFileContentAsync(string remotePath)
    {
        if (!IsConnected || _sshClient == null)
        {
            return (false, "Роутер не подключен");
        }

        return await Task.Run(() =>
        {
            try
            {
                if (_sftpClient?.IsConnected == true && _sftpClient.Exists(remotePath))
                {
                    using var ms = new MemoryStream();
                    _sftpClient.DownloadFile(remotePath, ms);
                    var content = Encoding.UTF8.GetString(ms.ToArray());
                    return (true, content);
                }

                // Fallback: Read via SSH base64
                var res = _sshClient.RunCommand($"[ -f '{remotePath}' ] && base64 '{remotePath}' || echo '__FILE_NOT_FOUND__'");
                var outText = res.Result.Trim();
                if (outText.Contains("__FILE_NOT_FOUND__"))
                {
                    return (false, "Указанный файл не найден на роутере");
                }
                var bytes = Convert.FromBase64String(outText.Replace("\n", "").Replace("\r", ""));
                return (true, Encoding.UTF8.GetString(bytes));
            }
            catch (Exception ex)
            {
                return (false, $"Ошибка чтения: {ex.Message}");
            }
        });
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(ConnectionProfile profile)
    {
        return await Task.Run(() =>
        {
            try
            {
                var connectionInfo = CreateConnectionInfo(profile);
                using var client = new SshClient(connectionInfo);
                client.Connect();
                var cmd = client.RunCommand("echo OK");
                client.Disconnect();

                if (cmd.ExitStatus == 0)
                {
                    return (true, "Связь с роутером успешно проверена!");
                }
                return (false, $"Ответ роутера: {cmd.Error}");
            }
            catch (Exception ex)
            {
                return (false, $"Не удалось подключиться: {ex.Message}");
            }
        });
    }

    private static ConnectionInfo CreateConnectionInfo(ConnectionProfile profile)
    {
        var authMethods = new System.Collections.Generic.List<AuthenticationMethod>();

        if (profile.UseKeyAuth && !string.IsNullOrWhiteSpace(profile.PrivateKeyPath) && File.Exists(profile.PrivateKeyPath))
        {
            var keyFile = string.IsNullOrEmpty(profile.PrivateKeyPassphrase)
                ? new PrivateKeyFile(profile.PrivateKeyPath)
                : new PrivateKeyFile(profile.PrivateKeyPath, profile.PrivateKeyPassphrase);

            authMethods.Add(new PrivateKeyAuthenticationMethod(profile.Username, keyFile));
        }
        else
        {
            var password = profile.Password ?? string.Empty;
            authMethods.Add(new PasswordAuthenticationMethod(profile.Username, password));

            var keyboardInteractive = new KeyboardInteractiveAuthenticationMethod(profile.Username);
            keyboardInteractive.AuthenticationPrompt += (sender, e) =>
            {
                foreach (var prompt in e.Prompts)
                {
                    prompt.Response = password;
                }
            };
            authMethods.Add(keyboardInteractive);
        }

        return new ConnectionInfo(
            profile.Host,
            profile.Port,
            profile.Username,
            authMethods.ToArray()
        )
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    private static void CreateRemoteDirectoriesRecursively(SftpClient client, string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = "";
        foreach (var part in parts)
        {
            current += "/" + part;
            if (!client.Exists(current))
            {
                client.CreateDirectory(current);
            }
        }
    }

    public void Dispose()
    {
        DisconnectInternalAsync().GetAwaiter().GetResult();
        _lock.Dispose();
    }
}
