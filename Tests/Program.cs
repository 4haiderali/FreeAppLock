using FreeAppLock;
int passed = 0;
void Check(string name, Action action) { action(); Console.WriteLine("PASS " + name); passed++; }
void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Throws(Action action) { bool threw = false; try { action(); } catch { threw = true; } Assert(threw); }
string root = Path.Combine(Path.GetTempPath(), "FreeAppLockTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var current = new LockSettings();
    SettingsStore.SetPassword(current, "admin-test-password");
    SettingsStore.SetAppPassword(current, "app-test-password");
    current.LockedNames.Add("Notepad");
    Check("Password round trip and separation", () => {
        Assert(SettingsStore.Verify(current, "admin-test-password"));
        Assert(!SettingsStore.Verify(current, "wrong"));
        Assert(SettingsStore.VerifyApp(current, "app-test-password"));
        Assert(!SettingsStore.VerifyApp(current, "admin-test-password"));
    });
    Check("Malformed password data rejected safely", () => {
        Assert(!SettingsStore.Verify(new LockSettings { Salt = "invalid", Hash = "invalid" }, "x"));
        Assert(!SettingsStore.Verify(new LockSettings { Salt = Convert.ToBase64String(new byte[16]), Hash = "" }, "x"));
    });
    Check("Save failure preserves password, rules and monitoring", () => {
        string hash = current.Hash;
        Throws(() => SettingsStore.Update(current, c => { c.Monitoring = false; c.LockedNames.Clear(); SettingsStore.SetPassword(c, "replacement"); }, _ => throw new IOException("Injected full disk")));
        Assert(current.Monitoring && current.LockedNames.Contains("Notepad") && current.Hash == hash);
    });
    Check("Successful transaction preserves case-insensitive rules", () => {
        var next = SettingsStore.Update(current, c => c.AutoLockDelaySeconds = 120, _ => { });
        Assert(next.AutoLockDelaySeconds == 120 && current.AutoLockDelaySeconds == 0 && next.LockedNames.Contains("NOTEPAD"));
    });
    string path = Path.Combine(root, "settings.json");
    Check("Atomic save, reload and previous-version backup", () => {
        SettingsStore.SaveTo(current, path);
        var next = SettingsStore.Update(current, c => c.AutoLockDelaySeconds = 300, c => SettingsStore.SaveTo(c, path));
        Assert(SettingsStore.LoadFrom(path).AutoLockDelaySeconds == 300);
        Assert(SettingsStore.LoadFrom(path + ".bak").AutoLockDelaySeconds == 0);
        next.AutoLockDelaySeconds = 600; SettingsStore.SaveTo(next, path);
        Assert(SettingsStore.LoadFrom(path + ".bak").AutoLockDelaySeconds == 300);
        Assert(Directory.GetFiles(root, "*.tmp").Length == 0);
    });
    Check("Sharing violation preserves saved data and cleans temporary file", () => {
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Throws(() => SettingsStore.SaveTo(current, path));
        Assert(SettingsStore.LoadFrom(path).AutoLockDelaySeconds == 600);
        Assert(Directory.GetFiles(root, "*.tmp").Length == 0);
    });
    Check("Corrupt settings do not silently reset", () => {
        string bad = Path.Combine(root, "bad.json");
        foreach (var text in new[] { "null", "{", "{\"LockedNames\":null}", "{\"AutoLockDelaySeconds\":-1}", "{\"Salt\":\"bad\",\"Hash\":\"bad\"}" }) {
            File.WriteAllText(bad, text); Throws(() => SettingsStore.LoadFrom(bad)); Assert(File.ReadAllText(bad) == text);
        }
    });
    Check("Missing settings use safe defaults", () => {
        var fresh = SettingsStore.LoadFrom(Path.Combine(root, "missing.json"));
        Assert(fresh.Monitoring && !fresh.TerminateOnAppLockout && fresh.LockedNames.Count == 0);
    });
    Check("Admin cooldown escalation, persistence and reset", () => {
        var state = new LockSettings(); var now = DateTimeOffset.UtcNow;
        foreach (int seconds in new[] { 60, 120, 240, 480, 900, 900 }) {
            for (int i = 0; i < 4; i++) Assert(AdminRetryPolicy.RecordFailure(state, now) == TimeSpan.Zero);
            Assert(AdminRetryPolicy.RecordFailure(state, now) == TimeSpan.FromSeconds(seconds));
            SettingsStore.SaveTo(state, path); state = SettingsStore.LoadFrom(path);
            Assert(AdminRetryPolicy.Remaining(state, now) == TimeSpan.FromSeconds(seconds));
            now = now.AddSeconds(seconds + 1); Assert(AdminRetryPolicy.Remaining(state, now) == TimeSpan.Zero);
        }
        AdminRetryPolicy.Reset(state); Assert(state.AdminFailedAttempts == 0 && state.AdminCooldownLevel == 0 && state.AdminBlockedUntilUtc == null);
    });
    Check("Startup command quotes paths and requests tray mode", () => {
        Assert(StartupRegistration.Command(@"C:\Program Files\FreeAppLock\FreeAppLock.exe") == "\"C:\\Program Files\\FreeAppLock\\FreeAppLock.exe\" --tray");
        Assert(StartupRegistration.StartsInTray(new[] { "--TRAY" }));
        Assert(!StartupRegistration.StartsInTray(Array.Empty<string>()));
    });
    Check("Per-user startup choice overrides machine defaults", () => {
        Assert(StartupRegistration.ResolveEnabled(null, false, true));
        Assert(!StartupRegistration.ResolveEnabled(0, false, true));
        Assert(StartupRegistration.ResolveEnabled(1, false, true));
        Assert(StartupRegistration.ResolveEnabled(null, true, false));
        Assert(!StartupRegistration.ResolveEnabled(null, false, false));
        Assert(!StartupRegistration.ResolveEnabled(1, false, false));
    });
    ApplicationProcessSnapshot Row(int pid, string state, int windows=0, string title="", bool supported=true, string name="brave")
        => new(name, pid, title, state, "", windows, supported, supported ? "" : "Higher integrity");
    Check("Browser helpers collapse into one row without diluting lock state", () => {
        var rows = ApplicationGrouping.Build(new[] { Row(10,"Rule set"), Row(11,"Locked",2,"Brave window"), Row(12,"Rule set",name:"BRAVE") }).ToArray();
        Assert(rows.Length == 1 && rows[0].Pid == 11 && rows[0].ProcessIds.Length == 3);
        Assert(rows[0].State == "Locked" && rows[0].WindowCount == 2 && rows[0].Title.Contains("Brave window"));
    });
    Check("Grouped status retains mixed authorization", () => {
        var group = ApplicationGrouping.Build(new[] { Row(10,"Locked",1), Row(11,"Unlocked",1), Row(12,"Rule set") }).Single();
        Assert(group.State == "Mixed");
    });
    Check("Visible or unsupported windows cannot yield a green grouped badge", () => {
        Assert(ApplicationGrouping.Build(new[] { Row(10,"Locked",1), Row(11,"Not hidden",1) }).Single().State == "Not hidden");
        Assert(ApplicationGrouping.Build(new[] { Row(10,"Locked",1), Row(11,"Can't enforce",1,supported:false) }).Single().State == "Can't enforce");
        Assert(ApplicationGrouping.Build(new[] { Row(10,"Locked",1), Row(11,"Checking",1) }).Single().State == "Checking");
    });
    Check("Background-only groups keep truthful rule and support states", () => {
        Assert(ApplicationGrouping.Build(new[] { Row(10,"Rule set"), Row(11,"Rule set") }).Single().State == "Rule set");
        Assert(ApplicationGrouping.Build(new[] { Row(10,"Can't enforce",supported:false) }).Single().State == "Can't enforce");
        Assert(ApplicationGrouping.Build(new[] { Row(10,"Not locked"), Row(11,"Not locked") }).Single().State == "Not locked");
    });
    Check("Grouped monitoring-off and unlocked states stay explicit", () => {
        Assert(ApplicationGrouping.Build(new[] { Row(10,"Monitoring off",1), Row(11,"Rule set") }).Single().State == "Monitoring off");
        Assert(ApplicationGrouping.Build(new[] { Row(10,"Unlocked",1), Row(11,"Rule set") }).Single().State == "Unlocked");
    });
    Check("Browsers always keep running even when termination is enabled", () => {
        foreach (var browser in new[] { "brave", "BRAVE", "chrome", "msedge", "firefox", "opera", "vivaldi", "chromium" })
            Assert(!BrowserPolicy.ShouldTerminate(browser,true));
        Assert(BrowserPolicy.ShouldTerminate("DisposableEditor",true));
        Assert(!BrowserPolicy.ShouldTerminate("DisposableEditor",false));
    });
    Console.WriteLine($"{passed} test groups passed.");
}
finally { Directory.Delete(root, recursive: true); }
