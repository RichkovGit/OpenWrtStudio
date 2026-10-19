using OpenWrtStudio.Models;
using OpenWrtStudio.Services;
using Xunit;
namespace OpenWrtStudio.Tests;
public class ForkopTests
{
    [Fact]
    public void ForkopViewModel_ApplyServerFilter_FiltersByProviderAndQuery()
    {
        var vm = new OpenWrtStudio.ViewModels.ForkopViewModel(new ForkopService(new SshService()), new SshService());
        vm.Servers.Add(new ForkopServerNode { Name = "Priority Group", Protocol = "Priority", IsAutoGroup = true });
        vm.Servers.Add(new ForkopServerNode { Name = "Blanc 🇳🇱 Amsterdam", Protocol = "VLESS", Provider = "Blanc", Subtitle = "143.20.254.145:443" });
        vm.Servers.Add(new ForkopServerNode { Name = "Stealth 🚀 Fast", Protocol = "Hysteria2", Provider = "Stealth", Subtitle = "93.114.47.73:443" });
        vm.Servers.Add(new ForkopServerNode { Name = "Abuz ⚡ Poland", Protocol = "VLESS", Provider = "Abuz", Subtitle = "93.114.47.95:443" });

        // Initial filter
        vm.ApplyServerFilter();
        Assert.Equal(3, vm.FilteredServers.Count);

        // Filter by Provider "Blanc"
        vm.SelectedProviderFilter = "Blanc";
        Assert.Single(vm.FilteredServers);
        Assert.Equal("Blanc 🇳🇱 Amsterdam", vm.FilteredServers[0].Name);

        // Search query "Poland"
        vm.SelectedProviderFilter = "Все";
        vm.ServerSearchQuery = "Poland";
        Assert.Single(vm.FilteredServers);
        Assert.Equal("Abuz ⚡ Poland", vm.FilteredServers[0].Name);
    }
}