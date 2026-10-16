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
    Task<(bool Success, byte[]? Data, string Error)> DownloadBinaryFileAsync(string remotePath);
    Task<(bool Success, string Message)> UploadBinaryFileAsync(byte[] data, string remotePath);
    Task<(bool Success, string Message)> TestConnectionAsync(ConnectionProfile profile);
}

public class SshService : ISshService, IDisposable
{
    private SshClient? _sshClient;
    private SftpClient? _sftpClient;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public bool IsConnected => _sshClient?.IsConnected == true;
    public ConnectionProfile? CurrentProfile { get; private set; }
    public event EventHandler<bool>? ConnectionChanged;

    public async Task<(bool Success, string Message)> ConnectAsync(ConnectionProfile profile)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lock.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await DisconnectInternalAsync();
            var connectionInfo = CreateConnectionInfo(profile);
            _sshClient = new SshClient(connectionInfo);

            await Task.Run(() =>
            {
                _sshClient.Connect();
                // SFTP is optional. Keep its connection timeout off the UI thread too.
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
            });

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
        var sftpClient = _sftpClient;
        var sshClient = _sshClient;
        _sftpClient = null;
        _sshClient = null;
        CurrentProfile = null;
        try
        {
            sftpClient?.Dispose();
        }
        catch { }
        try
        {
            sshClient?.Dispose();
        }
        catch { }
        return Task.CompletedTask;
    }

    public async Task<(int ExitCode, string Output, string Error)> ExecuteCommandAsync(string command, int timeoutSeconds = 30)
    {
        if (timeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        await _lock.WaitAsync();
        try
        {
        if (!IsConnected || _sshClient == null)
        {
            return (-1, string.Empty, "Нет подключения к роутеру");
        }

        // OpenWrt Dropbear / BusyBox ash requires pure Unix \n newlines.
        // Windows \r\n causes 'syntax error: unexpected word (expecting "do")' in shell loops.
        var unixCmd = command.Replace("\r\n", "\n").Replace("\r", "\n");

        return await Task.Run(() =>
        {
            try
            {
                using var cmd = _sshClient.CreateCommand(unixCmd);
                cmd.CommandTimeout = TimeSpan.FromSeconds(timeoutSeconds);
                cmd.Execute();
                return (cmd.ExitStatus ?? -1, cmd.Result, cmd.Error);
            }
            catch (Exception ex)
            {
                return (-1, string.Empty, ex.Message);
            }
        });
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RunStreamingCommandAsync(string command, Action<string> onLineReceived, CancellationToken cancellationToken)
    {
        if (!IsConnected || _sshClient == null)
        {
            onLineReceived("Ошибка: Роутер не подключен.");
            return;
        }

        var unixCmd = command.Replace("\r\n", "\n").Replace("\r", "\n");

        await Task.Run(() =>
        {
            SshCommand? cmd = null;
            CancellationTokenRegistration? reg = null;
            try
            {
                cmd = _sshClient.CreateCommand(unixCmd);
                reg = cancellationToken.Register(() =>
                {
                    try
                    {
                        cmd?.CancelAsync();
                    }
                    catch { }
                });

                var asyncResult = cmd.BeginExecute();
                using var reader = new StreamReader(cmd.OutputStream, Encoding.UTF8);

                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = reader.ReadLine();
                    if (line != null)
                    {
                        onLineReceived(line);
                    }
                    else
                    {
                        if (asyncResult.IsCompleted) break;
                        Thread.Sleep(40);
                    }

                    if (asyncResult.IsCompleted && reader.EndOfStream) break;
                }
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    onLineReceived($"[Ошибка потока]: {ex.Message}");
                }
            }
            finally
            {
                reg?.Dispose();
                try
                {
                    cmd?.CancelAsync();
                }
                catch { }
                cmd?.Dispose();
            }
        }, cancellationToken);
    }

    public Task<(bool Success, string Message)> UploadFileContentAsync(string content, string remotePath) =>
        UploadBinaryFileAsync(Encoding.UTF8.GetBytes(content), remotePath);

    public async Task<(bool Success, string Content)> DownloadFileContentAsync(string remotePath)
    {
        var result = await DownloadBinaryFileAsync(remotePath);
        return result.Success ? (true, Encoding.UTF8.GetString(result.Data!)) : (false, result.Error);
    }

    public async Task<(bool Success, byte[]? Data, string Error)> DownloadBinaryFileAsync(string remotePath)
    {
        await _lock.WaitAsync();
        try
        {
        if (!IsConnected || _sshClient == null)
        {
            return (false, null, "Роутер не подключен");
        }

        return await Task.Run<(bool Success, byte[]? Data, string Error)>(() =>
        {
            try
            {
                if (_sftpClient?.IsConnected == true && _sftpClient.Exists(remotePath))
                {
                    using var ms = new MemoryStream();
                    _sftpClient.DownloadFile(remotePath, ms);
                    return (true, ms.ToArray(), string.Empty);
                }

                // Fallback: Read via SSH base64
                var escRemote = ShellHelper.EscapeArg(remotePath);
                using var res = _sshClient.RunCommand($"[ -f {escRemote} ] && base64 < {escRemote}");
                var outText = res.Result.Trim();
                if (res.ExitStatus != 0)
                {
                    return (false, null, $"Не удалось прочитать файл: {res.Error}");
                }
                var cleanB64 = outText.Replace("\n", "").Replace("\r", "").Trim();
                var bytes = Convert.FromBase64String(cleanB64);
                return (true, bytes, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, null, $"Ошибка чтения двоичного файла: {ex.Message}");
            }
        });
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<(bool Success, string Message)> UploadBinaryFileAsync(byte[] data, string remotePath)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrWhiteSpace(remotePath) || remotePath.Contains('\0'))
            return (false, "Укажите путь к файлу на роутере");
        await _lock.WaitAsync();
        try
        {
        if (!IsConnected || _sshClient == null)
        {
            return (false, "Роутер не подключен");
        }

        return await Task.Run(() =>
        {
            try
            {
                if (!remotePath.StartsWith('/')) remotePath = "./" + remotePath;
                // Rename the target of an existing symlink, preserving links such as
                // /etc/resolv.conf rather than replacing the link with a regular file.
                using (var resolve = _sshClient.RunCommand($"if [ -L {ShellHelper.EscapeArg(remotePath)} ]; then readlink -f {ShellHelper.EscapeArg(remotePath)}; fi"))
                {
                    if (resolve.ExitStatus != 0) throw new IOException($"Не удалось разрешить путь файла: {resolve.Error}");
                    if (!string.IsNullOrWhiteSpace(resolve.Result)) remotePath = resolve.Result.TrimEnd('\r', '\n');
                }
                // Remote paths are POSIX paths regardless of the host OS.
                var slash = remotePath.LastIndexOf('/');
                var dir = slash >= 0 ? remotePath[..(slash + 1)] : "./";
                var escRemote = ShellHelper.EscapeArg(remotePath);
                var temporary = dir + $".openwrtstudio-{Guid.NewGuid():N}";
                var tempFile = ShellHelper.EscapeArg(temporary + ".part");
                void RunChecked(string command)
                {
                    using var commandResult = _sshClient.CreateCommand(command);
                    commandResult.CommandTimeout = TimeSpan.FromSeconds(30);
                    commandResult.Execute();
                    if (commandResult.ExitStatus != 0)
                        throw new IOException($"{commandResult.Error} (код {commandResult.ExitStatus})");
                }
                if (_sftpClient?.IsConnected == true)
                {
                    if (!_sftpClient.Exists(dir)) CreateRemoteDirectoriesRecursively(_sftpClient, dir);
                    var uploadPath = temporary + ".upload";
                    var tempUpload = ShellHelper.EscapeArg(uploadPath);
                    try
                    {
                        // Create the transfer file privately before SFTP opens it.
                        // Interrupted uploads must never truncate the destination.
                        RunChecked($"(umask 077; : > {tempUpload})");
                        using (var ms = new MemoryStream(data))
                            _sftpClient.UploadFile(ms, uploadPath, true);
                        if (_sftpClient.GetAttributes(uploadPath).Size != data.LongLength)
                            throw new IOException("Файл передан не полностью.");

                        // cp -p retains an existing file's ownership and executable
                        // bits; replacement is a rename on the same filesystem.
                        RunChecked($"(umask 077; if [ -e {escRemote} ]; then cp -p {escRemote} {tempFile} || exit; fi; cat {tempUpload} > {tempFile}) && mv -f {tempFile} {escRemote}");
                        return (true, $"Файл успешно загружен в {remotePath}");
                    }
                    finally
                    {
                        try { RunChecked($"rm -f {tempUpload} {tempFile}"); }
                        catch
                        {
                            // SSH may have failed while SFTP is still usable.
                            foreach (var path in new[] { uploadPath, temporary + ".part" })
                            {
                                try { if (_sftpClient.Exists(path)) _sftpClient.DeleteFile(path); }
                                catch { /* A lost connection may prevent cleanup. */ }
                            }
                        }
                    }
                }

                // Stage beside the destination, then rename only after all chunks decode.
                // This preserves the existing file on failure and isolates concurrent app instances.
                var b64 = Convert.ToBase64String(data);
                var tempB64 = ShellHelper.EscapeArg(temporary + ".b64");
                try
                {
                    RunChecked($"mkdir -p {ShellHelper.EscapeArg(dir)} && (umask 077; : > {tempB64})");
                    const int chunkSize = 30000;
                    for (int i = 0; i < b64.Length; i += chunkSize)
                    {
                        var chunk = b64.Substring(i, Math.Min(chunkSize, b64.Length - i));
                        RunChecked($"printf '%s' '{chunk}' >> {tempB64}");
                    }
                    // Preserve executable bits/ownership for existing scripts and configs.
                    RunChecked($"(umask 077; if [ -e {escRemote} ]; then cp -p {escRemote} {tempFile} || exit; fi; base64 -d < {tempB64} > {tempFile}) && mv -f {tempFile} {escRemote}");
                    return (true, $"Файл успешно загружен в {remotePath}");
                }
                finally
                {
                    try { RunChecked($"rm -f {tempB64} {tempFile}"); }
                    catch { /* Cleanup must not hide the transfer result. */ }
                }
            }
            catch (Exception ex)
            {
                return (false, $"Ошибка передачи файла: {ex.Message}");
            }
        });
        }
        finally
        {
            _lock.Release();
        }
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

        if (profile.UseKeyAuth)
        {
            if (string.IsNullOrWhiteSpace(profile.PrivateKeyPath) || !File.Exists(profile.PrivateKeyPath))
                throw new FileNotFoundException("Файл закрытого SSH ключа не найден. Проверьте путь в профиле.");
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
        var current = path.StartsWith('/') ? "" : ".";
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
        if (_disposed) return;
        _disposed = true;
        DisconnectInternalAsync().GetAwaiter().GetResult();
        // Operations already in flight still release this semaphore while shutting
        // down. Disposing it here would turn a disconnect into an unhandled error.
    }
}
