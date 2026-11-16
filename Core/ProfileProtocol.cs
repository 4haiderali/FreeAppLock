namespace FreeAppLock;

public sealed class ProfileWindowSnapshot
{
    public int Id { get; set; }
    public bool Focused { get; set; }
    public bool Minimized { get; set; }
}
public sealed class ProfileRequest
{
    public string RequestId { get; set; } = "";
    public string Type { get; set; } = "sync";
    public string ProfileId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Name { get; set; } = "Browser profile";
    public string Browser { get; set; } = "brave";
    public List<ProfileWindowSnapshot> Windows { get; set; } = [];
    public int? UnlockWindow { get; set; }
    public static bool IsBrowser(string? name) => name is "brave" or "chrome" or "msedge";
    internal bool Valid() => RequestId is {Length: <=64} && Type is "sync" or "unlock" && Guid.TryParse(ProfileId,out _) && Guid.TryParse(SessionId,out _)
        && Name is {Length: >0 and <=80} && !Name.Any(char.IsControl) && IsBrowser(Browser) && Windows!=null && Windows.Count<=64
        && Windows.All(w=>w!=null && w.Id>0) && Windows.Select(w=>w.Id).Distinct().Count()==Windows.Count
        && (UnlockWindow==null || Windows.Any(w=>w.Id==UnlockWindow));
}
public sealed record ProfileCommand(int Id,string Action);
public sealed class ProfileReply
{
    public string RequestId { get; set; } = "";
    public bool Available { get; set; } = true;
    public bool Protected { get; set; }
    public string Message { get; set; } = "";
    public List<int> Approved { get; set; } = [];
    public List<ProfileCommand> Commands { get; set; } = [];
}
internal sealed record ProfileWindowKey(string Profile,string Session,int Window);
internal sealed class ProfileWindowRuntime
{
    internal ProfileWindowSnapshot Snapshot = new();
    internal UnlockLease? Lease;
    internal long RestoreUntil,BlockedUntil;
    internal int Attempts;
    internal bool Wanted,Prompting;
}
internal sealed class ProfileSessionRuntime
{
    internal required string Profile,Session,Name,Browser;
    internal long Seen;
    internal Dictionary<int,ProfileWindowRuntime> Windows = [];
}
internal sealed record ProfileStatus(string Id,string Name,string Browser,string State,int Windows,bool Connected);
internal sealed class ProfileCoordinator
{
    readonly Dictionary<string,ProfileSessionRuntime> sessions = [];
    internal ProfileReply Handle(ProfileRequest request,LockSettings config,long now)
    {
        var reply=new ProfileReply { RequestId=request.RequestId };
        if(!request.Valid()) {reply.Message="Invalid profile request.";return reply;}
        foreach(var stale in sessions.Where(p=>now-p.Value.Seen>10000).Select(p=>p.Key).ToArray()) sessions.Remove(stale);
        if(!sessions.TryGetValue(request.SessionId,out var session))
        {
            if(sessions.Count>=100){reply.Message="Too many connected sessions.";return reply;}
            sessions[request.SessionId]=session=new ProfileSessionRuntime { Profile=request.ProfileId,Session=request.SessionId,Name=request.Name,Browser=request.Browser };
        }
        if(session.Profile!=request.ProfileId || session.Browser!=request.Browser){reply.Message="Session identity changed; reconnect the extension.";return reply;}
        session.Seen=now;session.Name=request.Name;
        var ids=request.Windows.Select(w=>w.Id).ToHashSet();
        foreach(var gone in session.Windows.Keys.Where(id=>!ids.Contains(id)).ToArray()) session.Windows.Remove(gone);
        config.BrowserProfiles.TryGetValue(request.ProfileId,out var rule);
        reply.Protected=config.Monitoring && rule!=null && rule.Browser==request.Browser;
        reply.Message=rule==null ? "Profile discovered. Protect it in FreeAppLock → Browser profiles." : !config.Monitoring ? "Monitoring is off." : "Connected";
        foreach(var snapshot in request.Windows)
        {
            if(!session.Windows.TryGetValue(snapshot.Id,out var window))session.Windows[snapshot.Id]=window=new ProfileWindowRuntime();
            window.Snapshot=snapshot;
            if(!reply.Protected){window.Lease=null;window.Wanted=false;continue;}
            bool restoring=window.RestoreUntil>now;
            if(!snapshot.Minimized)window.RestoreUntil=0;
            if(window.Lease!=null && LockTiming.Expired(rule!.Timing,window.Lease,now,snapshot.Focused||restoring,snapshot.Minimized&&!restoring))window.Lease=null;
            if(window.Lease==null)
            {
                reply.Commands.Add(new ProfileCommand(snapshot.Id,"lock"));
                if(request.Type=="unlock" && request.UnlockWindow==snapshot.Id && now>=window.BlockedUntil)window.Wanted=true;
            }
            else
            {
                reply.Approved.Add(snapshot.Id);
                if(snapshot.Minimized && restoring)reply.Commands.Add(new ProfileCommand(snapshot.Id,"restore"));
            }
        }
        return reply;
    }
    internal ProfileWindowKey? NextUnlock(LockSettings config,long now)
    {
        foreach(var session in sessions.Values.Where(s=>now-s.Seen<=5000))
            if(config.Monitoring && config.BrowserProfiles.ContainsKey(session.Profile))
                foreach(var pair in session.Windows)
                    if(pair.Value.Wanted && !pair.Value.Prompting && pair.Value.Lease==null && now>=pair.Value.BlockedUntil)
                    {pair.Value.Wanted=false;pair.Value.Prompting=true;return new(session.Profile,session.Session,pair.Key);}
        return null;
    }
    internal bool Current(ProfileWindowKey key,long now) => sessions.TryGetValue(key.Session,out var s) && s.Profile==key.Profile && now-s.Seen<=5000 && s.Windows.ContainsKey(key.Window);
    ProfileWindowRuntime? Find(ProfileWindowKey key) => sessions.TryGetValue(key.Session,out var s)&&s.Profile==key.Profile ? s.Windows.GetValueOrDefault(key.Window) : null;
    internal int Attempts(ProfileWindowKey key) => Find(key)?.Attempts ?? 0;
    internal void Wrong(ProfileWindowKey key,int attempts) {if(Find(key) is {} window)window.Attempts=attempts;}
    internal void Complete(ProfileWindowKey key,bool success,bool lockedOut,long now)
    {
        if(!Current(key,now) || Find(key) is not {} window)return;
        window.Prompting=false;
        if(success){window.Lease=new(now);window.RestoreUntil=now+2000;window.Attempts=0;window.BlockedUntil=0;}
        else if(lockedOut){window.BlockedUntil=now+60000;window.Attempts=0;}
    }
    internal void Revoke(string profile){foreach(var s in sessions.Values.Where(s=>s.Profile==profile))foreach(var w in s.Windows.Values){w.Lease=null;w.RestoreUntil=0;}}
    internal IEnumerable<ProfileStatus> Status(LockSettings config,long now)
    {
        var ids=config.BrowserProfiles.Keys.Concat(sessions.Values.Where(s=>now-s.Seen<=5000).Select(s=>s.Profile)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach(string id in ids)
        {
            config.BrowserProfiles.TryGetValue(id,out var rule);
            var live=sessions.Values.Where(s=>s.Profile==id && now-s.Seen<=5000).ToArray();
            var windows=live.SelectMany(s=>s.Windows.Values).ToArray();
            string state=live.Length==0 ? "Extension offline" : rule==null ? "Not protected" : !config.Monitoring ? "Monitoring off" : windows.Length==0 ? "No windows" :
                windows.Any(w=>w.Lease==null&&!w.Snapshot.Minimized) ? "Not hidden" : windows.All(w=>w.Lease!=null) ? "Unlocked" : windows.Any(w=>w.Lease!=null) ? "Mixed" : "Locked";
            yield return new(id,rule?.Name??live.First().Name,rule?.Browser??live.First().Browser,state,windows.Length,live.Length>0);
        }
    }
}
