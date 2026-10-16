using System;
using System.Text.RegularExpressions;

namespace OpenWrtStudio.Services;

/// <summary>
/// Utility class for safely escaping shell arguments and sanitizing inputs
/// to prevent command injection vulnerabilities across SSH execution.
/// </summary>
public static class ShellHelper
{
    /// <summary>
    /// Safely escapes an argument for POSIX shell (sh / ash / bash) enclosed in single quotes.
    /// In POSIX sh, nothing inside single quotes is evaluated (no variable expansion, command substitution, or backticks).
    /// Any literal single quote inside the string is safely terminated and escaped as '\''.
    /// </summary>
    public static string EscapeArg(string? input)
    {
        if (string.IsNullOrEmpty(input)) return "''";
        return "'" + input.Replace("'", @"'\''") + "'";
    }

    /// <summary>
    /// Validates and sanitizes an identifier (e.g. interface name, section name, service name).
    /// Only allows alphanumeric characters, underscores, hyphens, and dots.
    /// </summary>
    public static string SanitizeIdentifier(string? input, string fallback = "default")
    {
        if (string.IsNullOrWhiteSpace(input)) return fallback;
        var sanitized = Regex.Replace(input, @"[^a-zA-Z0-9_\-\.]", "");
        return string.IsNullOrEmpty(sanitized) ? fallback : sanitized;
    }

    /// <summary>Accepts named and anonymous UCI sections without changing their identity.</summary>
    public static bool IsValidUciSection(string? section)
    {
        return !string.IsNullOrWhiteSpace(section)
               && Regex.IsMatch(section, @"\A(?:[a-zA-Z0-9_\-]+|@[a-zA-Z0-9_\-]+\[-?[0-9]+\])\z");
    }

    /// <summary>Linux interface names used as UCI sections and shell arguments.</summary>
    public static bool IsValidNetworkInterface(string? name)
    {
        return !string.IsNullOrWhiteSpace(name)
               && Regex.IsMatch(name, @"\A[a-zA-Z0-9_][a-zA-Z0-9_\-]{0,14}\z");
    }

    /// <summary>
    /// Validates if a host or IP target is safe for ping, traceroute, and nslookup.
    /// Prevents command injection via metacharacters like ;, &, |, `, $, \n.
    /// </summary>
    public static bool IsSafeHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        return Regex.IsMatch(host.Trim(), @"^[a-zA-Z0-9\.\-:_]+$");
    }

    /// <summary>
    /// Validates standard 48-bit MAC address formats (e.g. 00:11:22:33:44:55 or 00-11-22-33-44-55).
    /// </summary>
    public static bool IsValidMac(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return false;
        return Regex.IsMatch(mac.Trim(), @"^([0-9a-fA-F]{2}[:-]){5}([0-9a-fA-F]{2})$");
    }

    /// <summary>
    /// Validates IPv4 address string.
    /// </summary>
    public static bool IsValidIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return false;
        return System.Net.IPAddress.TryParse(ip.Trim(), out var parsed) 
               && parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
    }
}
