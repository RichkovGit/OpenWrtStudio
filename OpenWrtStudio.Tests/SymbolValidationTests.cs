using System;
using Wpf.Ui.Controls;
using Xunit;

namespace OpenWrtStudio.Tests;

public class SymbolValidationTests
{
    [Fact]
    public void VerifyAllTargetSymbolsAreValid()
    {
        var targetSymbols = new[]
        {
            // Windows & Main
            "Router24",
            "Warning24",
            "Home24",
            "Stethoscope24",
            "Apps24",
            "ShieldKeyhole24",
            "Settings24",
            "Link24",
            "LinkDismiss24",

            // Dashboard
            "ArrowClockwise24",
            "DeveloperBoard24", // CPU
            "Storage24",        // RAM
            "HardDrive24",      // Overlay
            "Power24",
            "ArrowSync24",
            "Globe24",
            "Broom24",

            // Packages & Online
            "Search24",
            "Add24",
            "Delete24",
            "Checkmark24",
            "DarkTheme24",

            // Diagnostics
            "Wrench24",
            "CheckmarkCircle24",
            "DismissCircle24",
            "Info24",

            // Mihomo Builder
            "ArrowDownload24",
            "ArrowUpload24",
            "CloudArrowDown24",
            "Send24",
            "Flash24",
            "ChevronUp24",
            "ChevronDown24",
            "Edit24"
        };

        foreach (var s in targetSymbols)
        {
            var success = Enum.TryParse<SymbolRegular>(s, out var val);
            Assert.True(success, $"Symbol '{s}' must be a valid SymbolRegular enum value!");
        }
    }

    [Fact]
    public void ValidateAllXamlSymbolsAgainstEnum()
    {
        var xamlDir = @"C:\Users\danny\.gemini\antigravity\scratch\OpenWrtStudio";
        var xamlFiles = System.IO.Directory.GetFiles(xamlDir, "*.xaml", System.IO.SearchOption.AllDirectories);
        var invalid = new System.Collections.Generic.List<string>();
        var pattern = new System.Text.RegularExpressions.Regex(@"(?:Symbol=""|\{ui:SymbolIcon\s+)([A-Za-z0-9_]+)");

        foreach (var file in xamlFiles)
        {
            if (file.Contains("\\bin\\") || file.Contains("\\obj\\")) continue;
            var text = System.IO.File.ReadAllText(file);
            var matches = pattern.Matches(text);
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                var sym = m.Groups[1].Value;
                if (sym.Contains("Binding") || sym.Contains("StaticResource")) continue;
                if (!Enum.TryParse<SymbolRegular>(sym, out _))
                {
                    invalid.Add($"{System.IO.Path.GetFileName(file)}: {sym}");
                }
            }
        }

        Assert.Empty(invalid);
    }

    [Fact]
    public void TestPasswordBoxWithTwoWayBinding()
    {
        var thread = new System.Threading.Thread(() =>
        {
            var pb = new Wpf.Ui.Controls.PasswordBox();
            var profile = new OpenWrtStudio.Models.ConnectionProfile();
            
            var binding = new System.Windows.Data.Binding("Password")
            {
                Source = profile,
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            };
            System.Windows.Data.BindingOperations.SetBinding(pb, Wpf.Ui.Controls.PasswordBox.PasswordProperty, binding);

            pb.Password = "TestPassword123!";

            Assert.Equal("TestPassword123!", profile.Password);
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public async Task TestLiveRouterInfoFetch()
    {
        var profile = new OpenWrtStudio.Models.ConnectionProfile
        {
            Host = "192.168.10.1",
            Port = 22,
            Username = "root",
            Password = "danik05092005"
        };

        var ssh = new OpenWrtStudio.Services.SshService();
        var (connected, msg) = await ssh.ConnectAsync(profile);
        Assert.True(connected, "SSH should connect: " + msg);
        Assert.True(ssh.IsConnected, "IsConnected should be true");

        var routerService = new OpenWrtStudio.Services.RouterService(ssh);
        var info = await routerService.GetRouterInfoAsync();

        await ssh.DisconnectAsync();

        Assert.Equal("Cudy WR3000S v1 (OpenWrt U-Boot layout)", info.Model);
        Assert.Equal("OpenWrt", info.Hostname);
        Assert.Contains("25.12.5", info.OpenWrtRelease);
        Assert.Equal("6.12.94", info.KernelRelease);
        Assert.Equal("aarch64_cortex-a53", info.Architecture);
        Assert.True(info.UptimeSeconds > 0);
        Assert.True(info.RamTotalKB > 0);
        Assert.True(info.RamUsagePercent > 0);
        Assert.True(info.OverlayTotalKB > 0);
        Assert.True(info.Interfaces.Count > 0);
    }
}
