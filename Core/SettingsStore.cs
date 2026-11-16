using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace FreeAppLock;

public sealed class LockSettings
{
    public string Salt { get; set; } = "";
    public string Hash { get; set; } = "";
    public HashSet<string> LockedNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string,LockRule> AppRules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string,BrowserProfileRule> BrowserProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public LockRule RuleFor(string name) => AppRules.TryGetValue(name,out var rule) ? rule : new LockRule { AwaySeconds=AutoLockDelaySeconds };
    public bool Monitoring { get; set; } = true;
    public string AppSalt { get; set; } = "";
    public string AppHash { get; set; } = "";
    public int AutoLockDelaySeconds { get; set; } = 0;
    // Safe default, including when upgrading from v8.7 settings.json.
    public bool TerminateOnAppLockout { get; set; } = false;
    // Best-effort admin retry protection: user-editable, not an OS security boundary.
    public int AdminFailedAttempts { get; set; } = 0;
    public int AdminCooldownLevel { get; set; } = 0;
    public DateTimeOffset? AdminBlockedUntilUtc { get; set; }
}

public static class SettingsStore
{
    private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreeAppLock");
    private static readonly string FileName = Path.Combine(Dir, "settings.json");
    public static LockSettings Load() => LoadFrom(FileName);
    internal static LockSettings LoadFrom(string path)
    {
        if (!File.Exists(path)) return new LockSettings();
        try
        {
            var item = JsonSerializer.Deserialize<LockSettings>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Settings must contain an object.");
            Validate(item);
            Normalize(item);
            return item;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Cannot read settings. Restore settings.json.bak or repair the settings file; no settings were changed.", ex);
        }
    }
    static void Normalize(LockSettings item)
    {
        item.LockedNames = new HashSet<string>(item.LockedNames,StringComparer.OrdinalIgnoreCase);
        item.AppRules = new Dictionary<string,LockRule>(item.AppRules,StringComparer.OrdinalIgnoreCase);
        item.BrowserProfiles = new Dictionary<string,BrowserProfileRule>(item.BrowserProfiles,StringComparer.OrdinalIgnoreCase);
    }
    internal static void Validate(LockSettings item)
    {
        if (item.LockedNames == null || item.LockedNames.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Invalid application rules.");
        if (item.AutoLockDelaySeconds is < 0 or > 86400 || item.AdminFailedAttempts is < 0 or > 4 || item.AdminCooldownLevel is < 0 or > 5)
            throw new InvalidDataException("Invalid settings range.");
        if (item.AppRules==null || item.BrowserProfiles==null || item.BrowserProfiles.Count>100) throw new InvalidDataException("Invalid protection rules.");
        foreach(var pair in item.AppRules)
        {
            if(string.IsNullOrWhiteSpace(pair.Key) || pair.Value==null) throw new InvalidDataException("Invalid application rule.");
            pair.Value.Validate();
        }
        foreach(var pair in item.BrowserProfiles)
        {
            if(!Guid.TryParse(pair.Key,out _) || pair.Value==null || string.IsNullOrWhiteSpace(pair.Value.Name) || pair.Value.Name.Length>80 || pair.Value.Timing==null || !ProfileRequest.IsBrowser(pair.Value.Browser))
                throw new InvalidDataException("Invalid browser profile rule.");
            pair.Value.Timing.Validate();
        }
        ValidateHash(item.Salt, item.Hash);
        ValidateHash(item.AppSalt, item.AppHash);
    }
    static void ValidateHash(string salt, string hash)
    {
        if (salt == "" && hash == "") return;
        if (salt == null || hash == null || Convert.FromBase64String(salt).Length != 16 || Convert.FromBase64String(hash).Length != 32)
            throw new InvalidDataException("Invalid password data.");
    }
    internal static LockSettings Update(LockSettings current, Action<LockSettings> change, Action<LockSettings>? persist = null)
    {
        var candidate = JsonSerializer.Deserialize<LockSettings>(JsonSerializer.Serialize(current))!;
        Normalize(candidate);
        change(candidate);
        Validate(candidate);
        (persist ?? Save)(candidate);
        return candidate;
    }
    public static void Save(LockSettings item) => SaveTo(item, FileName);
    internal static void SaveTo(LockSettings item, string path)
    {
        Validate(item);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, item, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void SetPassword(LockSettings item, string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 310_000, HashAlgorithmName.SHA256, 32);
        item.Salt = Convert.ToBase64String(salt);
        item.Hash = Convert.ToBase64String(hash);
    }
    public static void SetAppPassword(LockSettings item, string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 310_000, HashAlgorithmName.SHA256, 32);
        item.AppSalt = Convert.ToBase64String(salt);
        item.AppHash = Convert.ToBase64String(hash);
    }
    public static bool VerifyApp(LockSettings item, string password) => VerifyHash(item.AppSalt, item.AppHash, password);
    public static bool Verify(LockSettings item, string password) => VerifyHash(item.Salt, item.Hash, password);
    private static bool VerifyHash(string saltString, string hashString, string password)
    {
        try
        {
            if (saltString == null || hashString == null) return false;
            byte[] salt = Convert.FromBase64String(saltString);
            byte[] expected = Convert.FromBase64String(hashString);
            if (salt.Length != 16 || expected.Length != 32) return false;
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 310_000, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException) { return false; }
    }
}
