using System.Diagnostics;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;
using Xunit;

namespace OpenWrtStudio.Core.Tests;

public class RouterPackageInstallerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public async Task EmbeddedInstallerUsesOneAvailableProviderAndPreservesFeedFailure(int updateCode)
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var state = Path.Combine(root, "state");
            var trace = Path.Combine(root, "trace");
            await File.WriteAllTextAsync(state, "");
            var stub = Path.Combine(root, "apk");
            await File.WriteAllTextAsync(stub, """
#!/bin/sh
case "$1" in
update) exit "$UPDATE_CODE";;
info) grep -qx "$3" "$STATE";;
search) [ "$3" = openvpn-mbedtls ] && echo openvpn-mbedtls-1.0;;
add) shift; printf '%s\n' "$*" > "$TRACE"; printf '%s\n' "$@" >> "$STATE";;
esac
""");
            File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var start = new ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(RouterPackageInstaller.BuildCommand(VpnProtocolType.OpenVpn));
            start.Environment["PATH"] = root + ":/usr/bin:/bin";
            start.Environment["STATE"] = state;
            start.Environment["TRACE"] = trace;
            start.Environment["UPDATE_CODE"] = updateCode.ToString();
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == updateCode, await output + await error);
            if (updateCode == 0) Assert.Equal("openvpn-mbedtls", (await File.ReadAllTextAsync(trace)).Trim());
            else Assert.False(File.Exists(trace));
        }
        finally { Directory.Delete(root, true); }
    }
}
