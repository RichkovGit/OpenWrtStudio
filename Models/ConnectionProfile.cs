using System;

namespace OpenWrtStudio.Models;

public class ConnectionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Мой OpenWrt роутер";
    public string Host { get; set; } = "192.168.1.1";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "root";
    public string Password { get; set; } = "";
    public string? PrivateKeyPath { get; set; }
    public string? PrivateKeyPassphrase { get; set; }
    public bool UseKeyAuth { get; set; } = false;
    public bool AutoConnectOnStartup { get; set; } = false;
    public DateTime LastConnected { get; set; }

    public override string ToString() => $"{Name} ({Host})";
}
