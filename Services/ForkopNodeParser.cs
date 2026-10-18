using System.Text;
using System.Text.Json;
using OpenWrtStudio.Models;

namespace OpenWrtStudio.Services;

/// <summary>Combines configured metadata with live Clash state without inventing nodes or selections.</summary>
public static class ForkopNodeParser
{
    private sealed class Entry(string tag)
    {
        public string Tag = tag, Name = tag, Type = "", Server = "", Now = "";
        public int Port;
        public int? Delay;
        public bool Live, Mutable;
        public List<string> Members = new();
    }

    public static List<ForkopServerNode> Parse(string output)
    {
        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        Entry Get(string tag) => entries.TryGetValue(tag, out var e) ? e : entries[tag] = new(tag);
        string final = "";
        var sections = new HashSet<string>(StringComparer.Ordinal);
        foreach (var doc in Documents(Section(output, "SINGBOX_CONFIG")))
        {
            using (doc)
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("route", out var route)) final = Text(route, "final");
                if (!root.TryGetProperty("outbounds", out var list) || list.ValueKind != JsonValueKind.Array) continue;
                foreach (var item in list.EnumerateArray())
                {
                    var tag = Text(item, "tag"); if (tag.Length == 0) continue;
                    var e = Get(tag); e.Type = Text(item, "type"); e.Server = Text(item, "server");
                    e.Port = Number(item, "server_port") ?? 0;
                    e.Members = Strings(item, "outbounds");
                    e.Mutable = e.Type.Equals("selector", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        foreach (var doc in Documents(Section(output, "SECTION_CACHE")))
        {
            using (doc)
            {
                var root = doc.RootElement;
                var section = Text(root, "section"); if (section.Length > 0) sections.Add(section);
                if (root.TryGetProperty("servers", out var servers) && servers.ValueKind == JsonValueKind.Object)
                    foreach (var p in servers.EnumerateObject())
                    {
                        var e = Get(p.Name);
                        if (p.Value.ValueKind == JsonValueKind.String) e.Server = p.Value.GetString() ?? "";
                        else Metadata(e, p.Value);
                    }
                if (root.TryGetProperty("outboundMetadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
                {
                    if (metadata.TryGetProperty("names", out var names) && names.ValueKind == JsonValueKind.Object)
                        foreach (var p in names.EnumerateObject())
                            if (p.Value.ValueKind == JsonValueKind.String) Get(p.Name).Name = p.Value.GetString() ?? p.Name;
                    if (metadata.TryGetProperty("protocols", out var protocols) && protocols.ValueKind == JsonValueKind.Object)
                        foreach (var p in protocols.EnumerateObject())
                            if (p.Value.ValueKind == JsonValueKind.String) Get(p.Name).Type = p.Value.GetString() ?? "";
                    foreach (var p in metadata.EnumerateObject())
                        if (!new[] { "names", "protocols", "countries", "transports", "securities" }.Contains(p.Name)) Metadata(Get(p.Name), p.Value);
                }
                foreach (var (key, kind) in new[] { ("urltestGroups", "URLTest"), ("priorityGroups", "Priority") })
                {
                    if (!root.TryGetProperty(key, out var groups)) continue;
                    if (groups.ValueKind == JsonValueKind.Object)
                        foreach (var p in groups.EnumerateObject())
                        {
                            var tag = Text(p.Value, "tag"); if (tag.Length == 0) tag = p.Name;
                            var e = Get(tag); Metadata(e, p.Value); e.Type = kind;
                            if (p.Value.ValueKind == JsonValueKind.String) e.Name = p.Value.GetString() ?? tag;
                            if (p.Value.ValueKind == JsonValueKind.Array) e.Members = p.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList();
                        }
                }
            }
        }
        foreach (var doc in Documents(Section(output, "CLASH_PROXIES")))
        {
            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("proxies", out var proxies) || proxies.ValueKind != JsonValueKind.Object) continue;
                foreach (var p in proxies.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.Object) continue;
                    var e = Get(p.Name); var type = Text(p.Value, "type");
                    // Cache metadata may describe a Priority policy implemented as a live Selector.
                    if (e.Type != "Priority" || !type.Equals("Selector", StringComparison.OrdinalIgnoreCase)) e.Type = type.Length > 0 ? type : e.Type;
                    e.Live = true; e.Mutable = type.Equals("Selector", StringComparison.OrdinalIgnoreCase);
                    e.Now = Text(p.Value, "now");
                    if (p.Value.TryGetProperty("all", out _)) e.Members = Strings(p.Value, "all");
                    e.Delay = Delay(p.Value);
                }
            }
        }
        bool Group(Entry e) => e.Members.Count > 0 || new[] { "selector", "urltest", "priority", "fallback", "loadbalance", "load-balance" }.Contains(e.Type.ToLowerInvariant());
        bool Internal(Entry e) => new[] { "direct", "reject", "block", "dns", "pass", "compatible" }.Contains(e.Type.ToLowerInvariant()) || new[] { "DIRECT", "REJECT", "GLOBAL", "direct-out", "bypass-out", "block-out", "dns-out" }.Contains(e.Tag);
        var groupsList = entries.Values.Where(e => Group(e) && !Internal(e)).ToList();
        var referenced = groupsList.SelectMany(e => e.Members).ToHashSet(StringComparer.Ordinal);
        var roots = groupsList.Where(e => !referenced.Contains(e.Tag)).ToList();
        var rootTags = sections.Select(section => section + "-out" + (section is "direct" or "bypass" ? "-1" : ""))
            .Where(tag => entries.TryGetValue(tag, out var e) && Group(e) && !Internal(e)).ToHashSet(StringComparer.Ordinal);
        if (rootTags.Count == 0)
        {
            if (entries.TryGetValue(final, out var e) && Group(e) && !Internal(e)) rootTags.Add(final);
            else if (entries.TryGetValue("main-out", out e) && Group(e)) rootTags.Add("main-out");
            else if (roots.Count == 1) rootTags.Add(roots[0].Tag);
        }
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in rootTags)
        {
            var current = root;
            while (entries.TryGetValue(current, out var selected) && selected.Live && active.Add(current)) current = selected.Now;
        }
        int? ResolveDelay(Entry e, HashSet<string> seen)
        {
            if (!seen.Add(e.Tag)) return null;
            if (e.Delay.HasValue) return e.Delay;
            return entries.TryGetValue(e.Now, out var next) ? ResolveDelay(next, seen) : null;
        }
        string SelectGroup(Entry target)
        {
            var parents = groupsList.Where(e => e.Live && e.Mutable && e.Tag != target.Tag && e.Members.Contains(target.Tag)).ToList();
            var directParents = parents.Where(e => rootTags.Contains(e.Tag)).ToList();
            if (directParents.Count == 1) return directParents[0].Tag;
            var activeParents = parents.Where(e => active.Contains(e.Tag)).ToList();
            return activeParents.Count == 1 ? activeParents[0].Tag : parents.Count == 1 ? parents[0].Tag : "";
        }
        return entries.Values.Where(e => !Internal(e) && (Group(e) || e.Live || e.Server.Length > 0))
            .OrderByDescending(Group).ThenBy(e => e.Name, StringComparer.Ordinal)
            .Select(e => new ForkopServerNode
            {
                Id = e.Tag, GroupTag = e.Tag, Name = e.Name, Protocol = Protocol(e.Type),
                ServerAddress = e.Server, ServerPort = e.Port, Provider = Provider(e.Name),
                Subtitle = Group(e) ? (e.Now.Length > 0 ? $"Выбран: {(entries.TryGetValue(e.Now, out var next) ? next.Name : e.Now)}" : "Текущий выбор недоступен") : e.Server.Length > 0 ? $"{e.Server}{(e.Port > 0 ? $":{e.Port}" : "")}" : "Прокси-сервер",
                CurrentNode = e.Now, Candidates = e.Members, SelectionGroupTag = SelectGroup(e),
                HasLiveState = e.Live, IsAutoGroup = Group(e), IsActive = active.Contains(e.Tag),
                LatencyMs = ResolveDelay(e, new(StringComparer.Ordinal))
            }).ToList();
    }

    private static void Metadata(Entry e, JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return;
        foreach (var field in new[] { "displayName", "label", "name", "title" })
        { var label = Text(item, field); if (label.Length > 0) { e.Name = label; break; } }
        var type = Text(item, "type"); if (type.Length > 0) e.Type = type;
        var server = Text(item, "server"); if (server.Length > 0) e.Server = server;
        e.Port = Number(item, "server_port") ?? Number(item, "port") ?? e.Port;
        foreach (var field in new[] { "outbounds", "all", "members", "nodes" })
            if (item.TryGetProperty(field, out var list) && list.ValueKind == JsonValueKind.Array) { e.Members = Strings(item, field); break; }
    }
    private static int? Delay(JsonElement item)
    {
        if (item.TryGetProperty("history", out var history) && history.ValueKind == JsonValueKind.Array && history.GetArrayLength() > 0)
        { var n = Number(history[history.GetArrayLength() - 1], "delay"); return n > 0 ? n : null; }
        var delay = Number(item, "delay"); return delay > 0 ? delay : null;
    }
    private static string Text(JsonElement item, string key) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static int? Number(JsonElement item, string key) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
    private static List<string> Strings(JsonElement item, string key) => item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : new();
    public static string Section(string text, string tag)
    {
        var start = text.IndexOf($"==={tag}===", StringComparison.Ordinal); if (start < 0) return "";
        start += tag.Length + 6; var end = text.IndexOf("===", start, StringComparison.Ordinal);
        return (end < 0 ? text[start..] : text[start..end]).Trim();
    }
    public static IEnumerable<JsonDocument> Documents(string text)
    {
        // Each section-cache file is a separate JSON document; concatenating them is not a JSON object.
        var bytes = Encoding.UTF8.GetBytes(text); var offset = 0; var docs = new List<JsonDocument>();
        while (offset < bytes.Length)
        {
            while (offset < bytes.Length && char.IsWhiteSpace((char)bytes[offset])) offset++;
            if (offset == bytes.Length) break;
            try { var reader = new Utf8JsonReader(bytes.AsSpan(offset)); docs.Add(JsonDocument.ParseValue(ref reader)); offset += (int)reader.BytesConsumed; }
            catch (JsonException) { break; }
        }
        return docs;
    }
    private static string Protocol(string type) => type.ToLowerInvariant() switch { "urltest" => "URLTest", "priority" => "Priority", "selector" => "Selector", "fallback" => "Fallback", "hysteria2" or "hy2" => "Hysteria2", "shadowsocks" or "ss" => "Shadowsocks", "vmess" => "VMess", "trojan" => "Trojan", "wireguard" => "WireGuard", "" => "Unknown", _ => type.ToUpperInvariant() };
    private static string Provider(string name) => name.Split(new[] { ' ', '|' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Прочие";
}
