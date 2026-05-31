using System.Text.Json;

namespace AclChecker;

/// <summary>
/// 数据持久化服务 - 管理模板、快照、审计日志
/// </summary>
public static class DataService
{
    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AclChecker");

    private static readonly string TemplatesPath = Path.Combine(AppDataPath, "templates.json");
    private static readonly string SnapshotsPath = Path.Combine(AppDataPath, "snapshots.json");
    private static readonly string AuditLogPath = Path.Combine(AppDataPath, "audit.json");
    private static readonly string LastScanPath = Path.Combine(AppDataPath, "lastscan.json");

    static DataService()
    {
        Directory.CreateDirectory(AppDataPath);
    }

    // ========== 权限模板 ==========

    public static List<PermissionTemplate> LoadTemplates()
    {
        if (!File.Exists(TemplatesPath)) return new List<PermissionTemplate>();
        var json = File.ReadAllText(TemplatesPath);
        return JsonSerializer.Deserialize<List<PermissionTemplate>>(json) ?? new List<PermissionTemplate>();
    }

    public static void SaveTemplates(List<PermissionTemplate> templates)
    {
        var json = JsonSerializer.Serialize(templates, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(TemplatesPath, json);
    }

    // ========== 权限快照 ==========

    public static List<PermissionSnapshot> LoadSnapshots()
    {
        if (!File.Exists(SnapshotsPath)) return new List<PermissionSnapshot>();
        var json = File.ReadAllText(SnapshotsPath);
        return JsonSerializer.Deserialize<List<PermissionSnapshot>>(json) ?? new List<PermissionSnapshot>();
    }

    public static void SaveSnapshot(PermissionSnapshot snapshot)
    {
        var snapshots = LoadSnapshots();
        snapshots.Add(snapshot);
        // 只保留最近 50 个快照
        if (snapshots.Count > 50)
            snapshots = snapshots.Skip(snapshots.Count - 50).ToList();
        var json = JsonSerializer.Serialize(snapshots, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SnapshotsPath, json);
    }

    // ========== 审计日志 ==========

    public static List<AuditLogEntry> LoadAuditLog()
    {
        if (!File.Exists(AuditLogPath)) return new List<AuditLogEntry>();
        var json = File.ReadAllText(AuditLogPath);
        return JsonSerializer.Deserialize<List<AuditLogEntry>>(json) ?? new List<AuditLogEntry>();
    }

    public static void AddAuditEntry(AuditLogEntry entry)
    {
        var logs = LoadAuditLog();
        logs.Add(entry);
        // 只保留最近 1000 条记录
        if (logs.Count > 1000)
            logs = logs.Skip(logs.Count - 1000).ToList();
        var json = JsonSerializer.Serialize(logs, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(AuditLogPath, json);
    }

    // ========== 上次扫描结果 ==========

    public static LastScanInfo? LoadLastScan()
    {
        if (!File.Exists(LastScanPath)) return null;
        var json = File.ReadAllText(LastScanPath);
        return JsonSerializer.Deserialize<LastScanInfo>(json);
    }

    public static void SaveLastScan(LastScanInfo info)
    {
        var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(LastScanPath, json);
    }
}

/// <summary>
/// 权限模板
/// </summary>
public class PermissionTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool EnableInheritance { get; set; } = true;
    public bool GrantUsers { get; set; } = true;
    public bool GrantEveryone { get; set; } = false;
    public bool GrantSystem { get; set; } = true;
    public bool GrantAdmins { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 权限快照
/// </summary>
public class PermissionSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TargetPath { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<DirectoryAclInfo> Directories { get; set; } = new();
}

public class DirectoryAclInfo
{
    public string DirPath { get; set; } = "";
    public bool HasInheritance { get; set; }
    public bool UsersHasReadExecute { get; set; }
    public bool EveryoneHasReadExecute { get; set; }
    public bool SystemHasFullControl { get; set; }
    public bool AdminsHasFullControl { get; set; }
    public string RawAclDetail { get; set; } = "";
}

/// <summary>
/// 上次扫描结果
/// </summary>
public class LastScanInfo
{
    public string TargetPath { get; set; } = "";
    public DateTime ScanTime { get; set; } = DateTime.Now;
    public List<AclResultItem> Results { get; set; } = new();
}

/// <summary>
/// 审计日志条目
/// </summary>
public class AuditLogEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Action { get; set; } = ""; // Modify, Reset, ApplyTemplate
    public string TargetPath { get; set; } = "";
    public string BeforeState { get; set; } = "";
    public string AfterState { get; set; } = "";
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
