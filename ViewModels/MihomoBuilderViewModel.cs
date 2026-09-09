using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;

namespace OpenWrtStudio.ViewModels;

public partial class MihomoBuilderViewModel : ObservableObject
{
    private readonly IMihomoConfigService _configService;
    private readonly ISshService _ssh;

    // --- ОБЩИЕ ПАРАМЕТРЫ ---
    [ObservableProperty] private int _mixedPort = 7890;
    [ObservableProperty] private int _redirPort = 7891;
    [ObservableProperty] private int _tproxyPort = 7892;
    [ObservableProperty] private bool _allowLan = true;
    [ObservableProperty] private string _bindAddress = "*";
    [ObservableProperty] private string _mode = "rule"; // rule, global, direct
    [ObservableProperty] private string _logLevel = "info"; // silent, error, warning, info, debug
    [ObservableProperty] private bool _ipv6 = false;
    [ObservableProperty] private string _externalController = "0.0.0.0:9090";
    [ObservableProperty] private string _secret = "";
    [ObservableProperty] private string _externalUi = "metacubexd"; // metacubexd, yacd, zashboard
    [ObservableProperty] private bool _tcpConcurrent = true;
    [ObservableProperty] private bool _unifiedDelay = true;
    [ObservableProperty] private string _findProcessMode = "strict";
    [ObservableProperty] private bool _geodataMode = true;
    [ObservableProperty] private bool _geoAutoUpdate = true;
    [ObservableProperty] private int _geoUpdateInterval = 24;

    // --- TUN РЕЖИМ ---
    [ObservableProperty] private bool _tunEnable = true;
    [ObservableProperty] private string _tunStack = "mixed"; // system, gvisor, mixed
    [ObservableProperty] private string _tunDevice = "mihomo0";
    [ObservableProperty] private bool _tunAutoRoute = true;
    [ObservableProperty] private bool _tunAutoDetectInterface = true;
    [ObservableProperty] private bool _tunAutoRedirect = false;
    [ObservableProperty] private bool _tunStrictRoute = true;
    [ObservableProperty] private bool _tunEndpointIndependentNat = true;
    [ObservableProperty] private int _tunMtu = 9000;
    [ObservableProperty] private string _tunDnsHijack = "any:53\ntcp://any:53";

    // --- DNS ДВИЖОК ---
    [ObservableProperty] private bool _dnsEnable = true;
    [ObservableProperty] private string _dnsListen = "0.0.0.0:1053";
    [ObservableProperty] private bool _dnsIpv6 = false;
    [ObservableProperty] private string _dnsEnhancedMode = "fake-ip"; // fake-ip, redir-host
    [ObservableProperty] private string _dnsFakeIpRange = "198.18.0.1/16";
    [ObservableProperty] private string _dnsDefaultNameservers = "77.88.8.8\n1.1.1.1\n8.8.8.8";
    [ObservableProperty] private string _dnsNameservers = "https://common.dot.dns.yandex.net/dns-query\nhttps://1.1.1.1/dns-query\nhttps://dns.google/dns-query";
    [ObservableProperty] private string _dnsFallbacks = "tls://1.1.1.1:853\ntls://8.8.8.8:853";
    [ObservableProperty] private string _dnsFakeIpFilters = "*.lan\n*.local\nrouter.asus.com\nopenwrt.lan\n+.msftconnecttest.com\n+.msftncsi.com\ntime.*.com\npool.ntp.org";

    // --- SNIFFER (СНИФФЕР) ---
    [ObservableProperty] private bool _snifferEnable = true;
    [ObservableProperty] private bool _snifferParsePureIp = true;
    [ObservableProperty] private bool _snifferOverrideDestination = false;
    [ObservableProperty] private bool _sniffHttp = true;
    [ObservableProperty] private bool _sniffTls = true;
    [ObservableProperty] private bool _sniffQuic = true;
    [ObservableProperty] private string _snifferSkipDomains = "+.apple.com\nMijia Cloud";

    // --- ГРУППЫ ПРОКСИ (PROXY GROUPS) ---
    [ObservableProperty] private ObservableCollection<MihomoProxyGroup> _proxyGroups = new();
    [ObservableProperty] private MihomoProxyGroup? _selectedGroup;
    [ObservableProperty] private string _newGroupName = "";
    [ObservableProperty] private string _newGroupType = "select"; // select, url-test, fallback, load-balance
    [ObservableProperty] private string _newGroupProxies = "DIRECT, REJECT";

    // --- ПРОВАЙДЕРЫ ПРАВИЛ (RULE PROVIDERS) ---
    [ObservableProperty] private ObservableCollection<KeyValuePair<string, MihomoRuleProvider>> _ruleProviders = new();
    [ObservableProperty] private string _newProviderName = "";
    [ObservableProperty] private string _newProviderUrl = "";
    [ObservableProperty] private string _newProviderBehavior = "domain"; // domain, ipcidr, classical
    [ObservableProperty] private string _newProviderFormat = "yaml"; // yaml, text, mrs

    // --- ПОДПИСКИ И ПРОКСИ ПРОВАЙДЕРЫ (PROXY PROVIDERS / SUBSCRIPTIONS) ---
    [ObservableProperty] private ObservableCollection<KeyValuePair<string, MihomoProxyProvider>> _proxyProviders = new();
    [ObservableProperty] private string _subscriptionUrl = "";
    [ObservableProperty] private string _subscriptionName = "my-sub";
    [ObservableProperty] private int _subscriptionIntervalHours = 24;
    [ObservableProperty] private bool _isUpdatingSubscription;

    // --- ТАБЛИЦА ПРАВИЛ (RULES) ---
    [ObservableProperty] private ObservableCollection<VisualRuleItem> _rules = new();
    [ObservableProperty] private VisualRuleItem? _selectedRule;
    [ObservableProperty] private string _newRuleType = "GEOSITE";
    [ObservableProperty] private string _newRulePayload = "youtube";
    [ObservableProperty] private string _newRuleTarget = "PROXY";
    [ObservableProperty] private bool _newRuleNoResolve = false;

    // --- YAML ТЕКСТ И РАЗВЕРТЫВАНИЕ ---
    [ObservableProperty] private string _yamlContent = "";
    [ObservableProperty] private string _deployRemotePath = "/etc/mihomo/config.yaml";
    [ObservableProperty] private bool _autoRestartService = true;
    [ObservableProperty] private bool _isDeploying;
    [ObservableProperty] private string _builderStatus = "Конструктор готов к созданию конфигурации.";

    public MihomoBuilderViewModel(IMihomoConfigService configService, ISshService ssh)
    {
        _configService = configService;
        _ssh = ssh;

        // Load Antizapret preset by default
        ApplyAntizapretPreset();
    }

    [RelayCommand]
    public void ApplyAntizapretPreset()
    {
        var preset = _configService.CreateAntizapretSmartPreset();
        LoadFromModel(preset);
        GenerateYaml();
        BuilderStatus = "Применен рекомендованный пресет 'Умный обход (Антизапрет + RU напрямую)'.";
    }

    [RelayCommand]
    public void ApplyFullTunnelPreset()
    {
        var preset = _configService.CreateFullTunnelPreset();
        LoadFromModel(preset);
        GenerateYaml();
        BuilderStatus = "Применен пресет 'Полный туннель всего трафика'.";
    }

    [RelayCommand]
    public void GenerateYaml()
    {
        try
        {
            var model = BuildModelFromFields();
            YamlContent = _configService.GenerateYaml(model);
            BuilderStatus = "Конфигурация YAML успешно сгенерирована!";
        }
        catch (Exception ex)
        {
            BuilderStatus = $"Ошибка генерации: {ex.Message}";
        }
    }

    [RelayCommand]
    public void SyncFromYaml()
    {
        if (string.IsNullOrWhiteSpace(YamlContent)) return;

        try
        {
            var model = _configService.ParseYaml(YamlContent);
            LoadFromModel(model);
            BuilderStatus = "Визуальные поля обновлены из текста YAML.";
        }
        catch (Exception ex)
        {
            BuilderStatus = $"Ошибка парсинга YAML: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task DeployToRouterAsync()
    {
        if (!_ssh.IsConnected)
        {
            BuilderStatus = "Ошибка: Нет подключения к роутеру по SSH!";
            return;
        }

        // Always update YAML from current controls first
        GenerateYaml();

        IsDeploying = true;
        BuilderStatus = $"Отправка конфигурации в {DeployRemotePath}...";

        var (success, msg) = await _configService.DeployToRouterAsync(YamlContent, DeployRemotePath, AutoRestartService);
        IsDeploying = false;
        BuilderStatus = msg;
    }

    [RelayCommand]
    public async Task FetchFromRouterAsync()
    {
        if (!_ssh.IsConnected)
        {
            BuilderStatus = "Ошибка: Нет подключения к роутеру по SSH!";
            return;
        }

        BuilderStatus = "Загрузка действующего файла конфигурации с роутера...";
        var (success, content) = await _configService.FetchConfigFromRouterAsync(DeployRemotePath);
        if (success)
        {
            YamlContent = content;
            SyncFromYaml();
            BuilderStatus = "Конфигурация успешно загружена с роутера и разобрана!";
        }
        else
        {
            BuilderStatus = content;
        }
    }

    [RelayCommand]
    public void ExportToFile()
    {
        GenerateYaml();
        var dlg = new SaveFileDialog
        {
            Filter = "YAML files (*.yaml;*.yml)|*.yaml;*.yml|All files (*.*)|*.*",
            FileName = "config.yaml",
            DefaultExt = ".yaml"
        };
        if (dlg.ShowDialog() == true)
        {
            File.WriteAllText(dlg.FileName, YamlContent);
            BuilderStatus = $"Файл сохранен: {dlg.FileName}";
        }
    }

    [RelayCommand]
    public void ImportFromFile()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "YAML files (*.yaml;*.yml)|*.yaml;*.yml|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            YamlContent = File.ReadAllText(dlg.FileName);
            SyncFromYaml();
            BuilderStatus = $"Конфигурация импортирована из {dlg.FileName}";
        }
    }

    // --- Group Operations ---
    [RelayCommand]
    public void AddProxyGroup()
    {
        if (string.IsNullOrWhiteSpace(NewGroupName)) return;

        var proxies = NewGroupProxies.Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .ToList();

        var grp = new MihomoProxyGroup
        {
            Name = NewGroupName.Trim(),
            Type = NewGroupType,
            Proxies = proxies
        };
        ProxyGroups.Add(grp);
        NewGroupName = "";
        GenerateYaml();
    }

    [RelayCommand]
    public void RemoveProxyGroup(MihomoProxyGroup? grp)
    {
        if (grp == null) return;
        ProxyGroups.Remove(grp);
        GenerateYaml();
    }

    // --- Provider Operations ---
    [RelayCommand]
    public void AddRuleProvider()
    {
        if (string.IsNullOrWhiteSpace(NewProviderName) || string.IsNullOrWhiteSpace(NewProviderUrl)) return;

        var prov = new MihomoRuleProvider
        {
            Type = "http",
            Behavior = NewProviderBehavior,
            Url = NewProviderUrl.Trim(),
            Path = $"./ruleset/{NewProviderName.Trim()}.{NewProviderFormat}",
            Format = NewProviderFormat,
            Interval = 86400
        };

        var dict = RuleProviders.ToDictionary(k => k.Key, v => v.Value);
        dict[NewProviderName.Trim()] = prov;
        RuleProviders = new ObservableCollection<KeyValuePair<string, MihomoRuleProvider>>(dict);

        NewProviderName = "";
        NewProviderUrl = "";
        GenerateYaml();
    }

    [RelayCommand]
    public void RemoveRuleProvider(KeyValuePair<string, MihomoRuleProvider>? prov)
    {
        if (prov == null) return;
        var dict = RuleProviders.ToDictionary(k => k.Key, v => v.Value);
        dict.Remove(prov.Value.Key);
        RuleProviders = new ObservableCollection<KeyValuePair<string, MihomoRuleProvider>>(dict);
        GenerateYaml();
    }

    // --- Subscription Operations ---
    [RelayCommand]
    public void InjectSubscription()
    {
        if (string.IsNullOrWhiteSpace(SubscriptionUrl))
        {
            BuilderStatus = "Укажите URL ссылки на подписку!";
            return;
        }

        var model = BuildModelFromFields();
        var name = string.IsNullOrWhiteSpace(SubscriptionName) ? "my-sub" : SubscriptionName.Trim();
        _configService.InjectSubscription(model, name, SubscriptionUrl, SubscriptionIntervalHours);
        LoadFromModel(model);
        GenerateYaml();
        BuilderStatus = $"Подписка '{name}' успешно внедрена в конфигурацию Mihomo!";
    }

    [RelayCommand]
    public async Task UpdateSubscriptionOnRouterAsync()
    {
        if (string.IsNullOrWhiteSpace(SubscriptionUrl))
        {
            BuilderStatus = "Укажите URL ссылки на подписку!";
            return;
        }

        if (!_ssh.IsConnected)
        {
            BuilderStatus = "Роутер не подключен по SSH!";
            return;
        }

        IsUpdatingSubscription = true;
        var name = string.IsNullOrWhiteSpace(SubscriptionName) ? "my-sub" : SubscriptionName.Trim();
        BuilderStatus = $"Загрузка подписки '{name}' на роутер...";

        var (success, msg) = await _configService.UpdateSubscriptionOnRouterAsync(name, SubscriptionUrl);
        IsUpdatingSubscription = false;
        BuilderStatus = msg;
    }

    // --- Rule Operations ---
    [RelayCommand]
    public void AddRule()
    {
        var item = new VisualRuleItem
        {
            RuleType = NewRuleType,
            Payload = NewRulePayload.Trim(),
            Target = NewRuleTarget.Trim(),
            NoResolve = NewRuleNoResolve
        };
        Rules.Add(item);
        GenerateYaml();
    }

    [RelayCommand]
    public void RemoveRule(VisualRuleItem? rule)
    {
        if (rule == null) return;
        Rules.Remove(rule);
        GenerateYaml();
    }

    [RelayCommand]
    public void MoveRuleUp(VisualRuleItem? rule)
    {
        if (rule == null) return;
        int idx = Rules.IndexOf(rule);
        if (idx > 0)
        {
            Rules.Move(idx, idx - 1);
            GenerateYaml();
        }
    }

    [RelayCommand]
    public void MoveRuleDown(VisualRuleItem? rule)
    {
        if (rule == null) return;
        int idx = Rules.IndexOf(rule);
        if (idx >= 0 && idx < Rules.Count - 1)
        {
            Rules.Move(idx, idx + 1);
            GenerateYaml();
        }
    }

    private MihomoRootConfig BuildModelFromFields()
    {
        var config = new MihomoRootConfig
        {
            MixedPort = MixedPort,
            RedirPort = RedirPort,
            TproxyPort = TproxyPort,
            AllowLan = AllowLan,
            BindAddress = BindAddress,
            Mode = Mode,
            LogLevel = LogLevel,
            Ipv6 = Ipv6,
            ExternalController = ExternalController,
            Secret = Secret,
            ExternalUi = ExternalUi,
            TcpConcurrent = TcpConcurrent,
            UnifiedDelay = UnifiedDelay,
            FindProcessMode = FindProcessMode,
            GeodataMode = GeodataMode,
            GeoAutoUpdate = GeoAutoUpdate,
            GeoUpdateInterval = GeoUpdateInterval
        };

        config.Tun = new MihomoTunConfig
        {
            Enable = TunEnable,
            Stack = TunStack,
            Device = TunDevice,
            AutoRoute = TunAutoRoute,
            AutoDetectInterface = TunAutoDetectInterface,
            AutoRedirect = TunAutoRedirect,
            StrictRoute = TunStrictRoute,
            EndpointIndependentNat = TunEndpointIndependentNat,
            Mtu = TunMtu,
            DnsHijack = SplitLines(TunDnsHijack)
        };

        config.Dns = new MihomoDnsConfig
        {
            Enable = DnsEnable,
            Listen = DnsListen,
            Ipv6 = DnsIpv6,
            EnhancedMode = DnsEnhancedMode,
            FakeIpRange = DnsFakeIpRange,
            DefaultNameserver = SplitLines(DnsDefaultNameservers),
            Nameserver = SplitLines(DnsNameservers),
            Fallback = SplitLines(DnsFallbacks),
            FakeIpFilter = SplitLines(DnsFakeIpFilters)
        };

        config.Sniffer = new MihomoSnifferConfig
        {
            Enable = SnifferEnable,
            ParsePureIp = SnifferParsePureIp,
            OverrideDestination = SnifferOverrideDestination,
            SkipDomain = SplitLines(SnifferSkipDomains)
        };

        config.ProxyGroups = ProxyGroups.ToList();
        config.ProxyProviders = ProxyProviders.ToDictionary(k => k.Key, v => v.Value);
        config.RuleProviders = RuleProviders.ToDictionary(k => k.Key, v => v.Value);
        config.Rules = Rules.Select(r => r.ToRuleString()).ToList();

        return config;
    }

    private void LoadFromModel(MihomoRootConfig m)
    {
        MixedPort = m.MixedPort;
        RedirPort = m.RedirPort;
        TproxyPort = m.TproxyPort;
        AllowLan = m.AllowLan;
        BindAddress = m.BindAddress ?? "*";
        Mode = m.Mode ?? "rule";
        LogLevel = m.LogLevel ?? "info";
        Ipv6 = m.Ipv6;
        ExternalController = m.ExternalController ?? "0.0.0.0:9090";
        Secret = m.Secret ?? "";
        ExternalUi = m.ExternalUi ?? "metacubexd";
        TcpConcurrent = m.TcpConcurrent;
        UnifiedDelay = m.UnifiedDelay;
        FindProcessMode = m.FindProcessMode ?? "strict";
        GeodataMode = m.GeodataMode;
        GeoAutoUpdate = m.GeoAutoUpdate;
        GeoUpdateInterval = m.GeoUpdateInterval;

        if (m.Tun != null)
        {
            TunEnable = m.Tun.Enable;
            TunStack = m.Tun.Stack ?? "mixed";
            TunDevice = m.Tun.Device ?? "mihomo0";
            TunAutoRoute = m.Tun.AutoRoute;
            TunAutoDetectInterface = m.Tun.AutoDetectInterface;
            TunAutoRedirect = m.Tun.AutoRedirect;
            TunStrictRoute = m.Tun.StrictRoute;
            TunEndpointIndependentNat = m.Tun.EndpointIndependentNat;
            TunMtu = m.Tun.Mtu;
            TunDnsHijack = string.Join("\n", m.Tun.DnsHijack ?? new());
        }

        if (m.Dns != null)
        {
            DnsEnable = m.Dns.Enable;
            DnsListen = m.Dns.Listen ?? "0.0.0.0:1053";
            DnsIpv6 = m.Dns.Ipv6;
            DnsEnhancedMode = m.Dns.EnhancedMode ?? "fake-ip";
            DnsFakeIpRange = m.Dns.FakeIpRange ?? "198.18.0.1/16";
            DnsDefaultNameservers = string.Join("\n", m.Dns.DefaultNameserver ?? new());
            DnsNameservers = string.Join("\n", m.Dns.Nameserver ?? new());
            DnsFallbacks = string.Join("\n", m.Dns.Fallback ?? new());
            DnsFakeIpFilters = string.Join("\n", m.Dns.FakeIpFilter ?? new());
        }

        if (m.Sniffer != null)
        {
            SnifferEnable = m.Sniffer.Enable;
            SnifferParsePureIp = m.Sniffer.ParsePureIp;
            SnifferOverrideDestination = m.Sniffer.OverrideDestination;
            SnifferSkipDomains = string.Join("\n", m.Sniffer.SkipDomain ?? new());
        }

        ProxyGroups = new ObservableCollection<MihomoProxyGroup>(m.ProxyGroups ?? new());
        ProxyProviders = new ObservableCollection<KeyValuePair<string, MihomoProxyProvider>>(m.ProxyProviders ?? new());
        RuleProviders = new ObservableCollection<KeyValuePair<string, MihomoRuleProvider>>(m.RuleProviders ?? new());

        var vRules = (m.Rules ?? new()).Select(VisualRuleItem.FromString);
        Rules = new ObservableCollection<VisualRuleItem>(vRules);
    }

    private static List<string> SplitLines(string text)
    {
        return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }
}
