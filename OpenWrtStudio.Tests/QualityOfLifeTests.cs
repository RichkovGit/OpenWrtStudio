using System.Collections.Generic;
using System.Threading.Tasks;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;
using OpenWrtStudio.ViewModels;
using Xunit;

namespace OpenWrtStudio.Tests;

public class QualityOfLifeTests
{
    [Fact]
    public void FixAllReportText_ListsEveryStepWithOutcome()
    {
        var report = new SentinelFixAllReport
        {
            Summary = "Проблема устранена! Успешных шагов: 1.",
            Resolved = true,
            Steps = new List<SentinelFixStepResult>
            {
                new() { MethodName = "Перезапуск DNS", Success = true, Message = "Метод успешно применен" },
                new() { MethodName = "Сброс ARP", Success = false, Message = "Ошибка выполнения" }
            }
        };
        var text = DiagnosticsViewModel.BuildFixAllReportText(report);
        Assert.Contains("Проблема устранена", text);
        Assert.Contains("[OK] Перезапуск DNS", text);
        Assert.Contains("[ОШИБКА] Сброс ARP", text);
    }

    [Fact]
    public void AuditReportText_ContainsAllChecks()
    {
        var items = new List<HealthCheckItem>
        {
            new() { Title = "Доступ к интернету (WAN)", Category = "Сеть", Status = HealthStatus.Good, StatusText = "Подключен" },
            new() { Title = "Память Overlay", Category = "Хранилище", Status = HealthStatus.Warning, StatusText = "Заполнено на 87%", Recommendation = "Следите за местом" }
        };
        var text = DiagnosticsViewModel.BuildAuditReportText(items, "Аудит завершен: предупреждений 1.");
        Assert.Contains("Доступ к интернету (WAN)", text);
        Assert.Contains("Память Overlay", text);
        Assert.Contains("Следите за местом", text);
        Assert.Contains("Аудит завершен", text);
    }

    [Fact]
    public void AddStaticLease_InvalidMacOrIp_IsRejectedWithMessage()
    {
        var vm = new RouterSettingsViewModel(new UnusedRouterConfigService(), new ScriptedSsh());
        vm.NewLeaseName = "PC";
        vm.NewLeaseMac = "not-a-mac";
        vm.NewLeaseIp = "192.168.1.50";
        vm.AddStaticLease();
        Assert.Empty(vm.StaticLeases);
        Assert.Contains("MAC", vm.StatusMessage);

        vm.NewLeaseMac = "AA:BB:CC:DD:EE:FF";
        vm.NewLeaseIp = "999.1.1.1";
        vm.AddStaticLease();
        Assert.Empty(vm.StaticLeases);
        Assert.Contains("IPv4", vm.StatusMessage);

        vm.NewLeaseIp = "192.168.1.50";
        vm.AddStaticLease();
        Assert.Single(vm.StaticLeases);
    }

    private sealed class UnusedRouterConfigService : IRouterConfigService
    {
        public Task<List<WifiRadioConfig>> GetWifiConfigsAsync() => Task.FromResult(new List<WifiRadioConfig>());
        public Task<(bool, string)> ApplyWifiConfigAsync(List<WifiRadioConfig> radios) => Task.FromResult((true, ""));
        public Task<LanDhcpConfig> GetLanDhcpConfigAsync() => Task.FromResult(new LanDhcpConfig());
        public Task<(bool, string)> ApplyLanDhcpConfigAsync(LanDhcpConfig config) => Task.FromResult((true, ""));
        public Task<WanConfig> GetWanConfigAsync() => Task.FromResult(new WanConfig());
        public Task<(bool, string)> ApplyWanConfigAsync(WanConfig config) => Task.FromResult((true, ""));
        public Task<List<PortForwardRule>> GetPortForwardRulesAsync() => Task.FromResult(new List<PortForwardRule>());
        public Task<(bool, string)> SavePortForwardRuleAsync(PortForwardRule rule) => Task.FromResult((true, ""));
        public Task<(bool, string)> DeletePortForwardRuleAsync(string sectionId) => Task.FromResult((true, ""));
        public Task<SystemAdminConfig> GetSystemAdminConfigAsync() => Task.FromResult(new SystemAdminConfig());
        public Task<(bool, string)> ApplySystemAdminConfigAsync(SystemAdminConfig config) => Task.FromResult((true, ""));
    }
}
