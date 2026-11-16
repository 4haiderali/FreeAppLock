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
    Check("Existing settings inherit the legacy away/minimize behavior", () => {
        var legacy=new LockSettings {AutoLockDelaySeconds=120}; legacy.LockedNames.Add("brave");
        Assert(legacy.RuleFor("brave").AwaySeconds==120 && legacy.RuleFor("brave").LockOnMinimize && legacy.RuleFor("brave").MaximumUnlockedSeconds==null);
        legacy.AppRules["BRAVE"]=new LockRule {AwaySeconds=null,MaximumUnlockedSeconds=600,LockOnMinimize=false};
        Assert(legacy.RuleFor("brave").AwaySeconds==null && legacy.RuleFor("brave").MaximumUnlockedSeconds==600);
    });
    Check("Session limit expires even while focused", () => {
        var rule=new LockRule {AwaySeconds=null,MaximumUnlockedSeconds=10,LockOnMinimize=false};var lease=new UnlockLease(1000);
        Assert(!LockTiming.Expired(rule,lease,10999,true,false)); Assert(LockTiming.Expired(rule,lease,11000,true,false));
    });
    Check("Away timer resets on return and supports never", () => {
        var rule=new LockRule {AwaySeconds=5,LockOnMinimize=false};var lease=new UnlockLease(0);
        Assert(!LockTiming.Expired(rule,lease,1000,false,false));Assert(!LockTiming.Expired(rule,lease,5999,false,false));
        Assert(!LockTiming.Expired(rule,lease,6000,true,false));Assert(lease.AwayAt==null);
        Assert(!LockTiming.Expired(rule,lease,7000,false,false));Assert(LockTiming.Expired(rule,lease,12000,false,false));
        rule.AwaySeconds=null;Assert(!LockTiming.Expired(rule,lease,999999,false,false));
    });
    Check("Minimize option is independent from away/session timers", () => {
        var lease=new UnlockLease(0);var rule=new LockRule {AwaySeconds=null,LockOnMinimize=false};
        Assert(!LockTiming.Expired(rule,lease,1000,false,true));rule.LockOnMinimize=true;Assert(LockTiming.Expired(rule,lease,1000,false,true));
        rule.LockOnMinimize=false;rule.MaximumUnlockedSeconds=1;Assert(LockTiming.Expired(rule,lease,1000,false,true));
    });
    Check("Rule updates persist independently and failures preserve old rules", () => {
        var original=new LockSettings();original.LockedNames.Add("editor");original.AppRules["editor"]=new LockRule {AwaySeconds=null,MaximumUnlockedSeconds=30,LockOnMinimize=false};
        Throws(()=>SettingsStore.Update(original,c=>c.AppRules["editor"].MaximumUnlockedSeconds=60,_=>throw new IOException()));
        Assert(original.RuleFor("EDITOR").MaximumUnlockedSeconds==30);
        var next=SettingsStore.Update(original,c=>c.AppRules["browser"]=new LockRule {AwaySeconds=5},_=>{});
        Assert(next.RuleFor("EDITOR").MaximumUnlockedSeconds==30 && next.RuleFor("BROWSER").AwaySeconds==5);
        Throws(()=>SettingsStore.Update(original,c=>c.AppRules["editor"].MaximumUnlockedSeconds=0,_=>{}));
    });
    string profileA=Guid.NewGuid().ToString(),profileB=Guid.NewGuid().ToString(),sessionA=Guid.NewGuid().ToString(),sessionB=Guid.NewGuid().ToString();
    ProfileRequest Packet(string profile,string session,bool unlock=false,params int[] ids)=>new(){RequestId=Guid.NewGuid().ToString(),ProfileId=profile,SessionId=session,Name="Work",Browser="brave",Type=unlock?"unlock":"sync",UnlockWindow=unlock?ids.First():null,Windows=ids.Select(id=>new ProfileWindowSnapshot {Id=id,Minimized=true}).ToList()};
    Check("Unselected browser profiles remain free; discovery cannot create rules", () => {
        var config=new LockSettings();config.BrowserProfiles[profileA]=new BrowserProfileRule {Name="Work",Browser="brave"};var coordinator=new ProfileCoordinator();
        Assert(coordinator.Handle(Packet(profileA,sessionA,false,1),config,0).Protected);
        Assert(!coordinator.Handle(Packet(profileB,sessionB,false,1),config,0).Protected);
        Assert(config.BrowserProfiles.Count==1);
    });
    Check("Profile grants belong to one window and connection", () => {
        var config=new LockSettings();config.BrowserProfiles[profileA]=new BrowserProfileRule {Name="Work",Browser="brave",Timing=new LockRule {AwaySeconds=null,LockOnMinimize=false}};
        var coordinator=new ProfileCoordinator();coordinator.Handle(Packet(profileA,sessionA,true,1,2),config,0);var key=coordinator.NextUnlock(config,0)!;Assert(key.Window==1);
        coordinator.Complete(key,true,false,0);var reply=coordinator.Handle(Packet(profileA,sessionA,false,1,2),config,1);
        Assert(reply.Approved.SequenceEqual(new[]{1}) && reply.Commands.Any(c=>c.Id==2&&c.Action=="lock"));
        Assert(coordinator.Handle(Packet(profileA,sessionB,false,1),config,1).Approved.Count==0);
    });
    Check("Closed/recreated profile windows never inherit approval", () => {
        var config=new LockSettings();config.BrowserProfiles[profileA]=new BrowserProfileRule {Name="Work",Browser="brave"};var coordinator=new ProfileCoordinator();
        coordinator.Handle(Packet(profileA,sessionA,true,1),config,0);var key=coordinator.NextUnlock(config,0)!;coordinator.Complete(key,true,false,0);
        coordinator.Handle(Packet(profileA,sessionA),config,1);Assert(!coordinator.Current(key,1));
        Assert(coordinator.Handle(Packet(profileA,sessionA,false,1),config,2).Approved.Count==0);
    });
    Check("Profile cooldown and offline status are truthful", () => {
        var config=new LockSettings();config.BrowserProfiles[profileA]=new BrowserProfileRule {Name="Work",Browser="brave"};var coordinator=new ProfileCoordinator();
        coordinator.Handle(Packet(profileA,sessionA,true,1),config,0);var key=coordinator.NextUnlock(config,0)!;coordinator.Complete(key,false,true,0);
        coordinator.Handle(Packet(profileA,sessionA,true,1),config,1000);Assert(coordinator.NextUnlock(config,1000)==null);
        coordinator.Handle(Packet(profileA,sessionA,true,1),config,60001);Assert(coordinator.NextUnlock(config,60001)!=null);
        Assert(coordinator.Status(config,66002).Single().State=="Extension offline");
    });
    Check("Native frames and malformed profile messages are bounded", () => {
        using var stream=new MemoryStream();NativeWire.Write(stream,"{\"test\":1}").GetAwaiter().GetResult();stream.Position=0;
        Assert(NativeWire.Read(stream).GetAwaiter().GetResult()=="{\"test\":1}");
        Throws(()=>NativeWire.Read(new MemoryStream(new byte[]{255,255,255,127})).GetAwaiter().GetResult());
        var bad=Packet(profileA,sessionA,false,1,1);Assert(!bad.Valid());bad=Packet(profileA,sessionA,false,1);bad.Type="changeRules";Assert(!bad.Valid());
    });
    Check("Per-user profile pipe round trip uses an isolated pipe", () => {
        string pipeName="FreeAppLock.Test."+Guid.NewGuid().ToString("N");
        using var server=new ProfilePipeServer(r=>Task.FromResult(new ProfileReply {RequestId=r.RequestId,Message="test"}),pipeName);
        using var client=new System.IO.Pipes.NamedPipeClientStream(".",pipeName,System.IO.Pipes.PipeDirection.InOut,System.IO.Pipes.PipeOptions.Asynchronous|System.IO.Pipes.PipeOptions.CurrentUserOnly);
        client.Connect(3000);var request=Packet(profileA,sessionA,false,1);var options=new System.Text.Json.JsonSerializerOptions {PropertyNamingPolicy=System.Text.Json.JsonNamingPolicy.CamelCase};
        NativeWire.Write(client,System.Text.Json.JsonSerializer.Serialize(request,options)).GetAwaiter().GetResult();
        var reply=System.Text.Json.JsonSerializer.Deserialize<ProfileReply>(NativeWire.Read(client).GetAwaiter().GetResult()!,new System.Text.Json.JsonSerializerOptions{PropertyNameCaseInsensitive=true});
        Assert(reply!.RequestId==request.RequestId && reply.Message=="test");
    });
    Console.WriteLine($"{passed} test groups passed.");
}
finally { Directory.Delete(root, recursive: true); }
