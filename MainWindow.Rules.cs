using System.IO;
using System.Windows;
using System.Windows.Controls;
using FreeAppLock.Views;
namespace FreeAppLock;
public sealed class SavedRuleEntry
{
    public string Key {get;init;}="";
    public string Name {get;init;}="";
    public string Scope {get;init;}="";
    public string State {get;init;}="";
    public string Summary {get;init;}="";
    public bool Profile {get;init;}
    public string Id {get;init;}="";
}
public sealed class BrowserProfileEntry
{
    public string Id {get;init;}="";
    public string Name {get;init;}="";
    public string Browser {get;init;}="";
    public string State {get;init;}="";
    public string Summary {get;init;}="";
}
public partial class MainWindow
{
    List<AppEntry> lastApplicationRows=[];
    readonly ProfileCoordinator profileCoordinator=new();
    ProfilePipeServer? profileServer;
    internal bool EnableProfileBridge {get;set;}=true;
    internal string? ProfileBridgePipeName {get;set;}
    void StartProfileBridge()
    {
        if(EnableProfileBridge)profileServer=new ProfilePipeServer(request=>Dispatcher.InvokeAsync(()=>
        {
            var reply=profileCoordinator.Handle(request,Config,Environment.TickCount64);
            Dispatcher.BeginInvoke(new Action(TryPromptProfiles));
            return reply;
        }).Task,ProfileBridgePipeName);
    }
    void RefreshRuleViews()
    {
        if(ProtectedApps==null||ProfileList==null)return;
        var selected=(ProtectedApps.SelectedItem as SavedRuleEntry)?.Key;
        var selectedProfile=(ProfileList.SelectedItem as BrowserProfileEntry)?.Id;
        var profileStatus=profileCoordinator.Status(Config,Environment.TickCount64).ToArray();
        var entries=Config.LockedNames.OrderBy(n=>n,StringComparer.OrdinalIgnoreCase).Select(name=>new SavedRuleEntry
        {
            Key="app:"+name,Name=name,Id=name,Scope="Application",State=lastApplicationRows.FirstOrDefault(r=>r.Name.Equals(name,StringComparison.OrdinalIgnoreCase))?.State??"Not running — protection enabled",Summary=Config.RuleFor(name).Summary
        }).Concat(profileStatus.Where(s=>Config.BrowserProfiles.ContainsKey(s.Id)).Select(s=>new SavedRuleEntry
        {Key="profile:"+s.Id,Name=Config.BrowserProfiles[s.Id].Name,Id=s.Id,Scope=s.Browser+" profile",Profile=true,State=s.State,Summary=Config.BrowserProfiles[s.Id].Timing.Summary}))
            .Where(e=>e.Name.Contains(Search.Text,StringComparison.OrdinalIgnoreCase)||e.Scope.Contains(Search.Text,StringComparison.OrdinalIgnoreCase)).ToList();
        ProtectedApps.ItemsSource=entries;ProtectedApps.SelectedItem=entries.FirstOrDefault(e=>e.Key==selected);
        var profiles=profileStatus.Select(s=>new BrowserProfileEntry {Id=s.Id,Name=s.Name,Browser=s.Browser,State=Config.LockedNames.Contains(s.Browser)?s.State+" · app-wide rule also enabled":s.State,
            Summary=Config.BrowserProfiles.TryGetValue(s.Id,out var rule)?rule.Timing.Summary:"Discovered — select Protect / edit profile to configure"}).ToList();
        ProfileList.ItemsSource=profiles;ProfileList.SelectedItem=profiles.FirstOrDefault(e=>e.Id==selectedProfile);
        SavedCount.Text=$"{Config.LockedNames.Count+Config.BrowserProfiles.Count} saved protection rule(s)";
    }
    void PageTabs_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(PageTabs==null||!ReferenceEquals(e.Source,PageTabs)||LockButton==null||UnlockButton==null)return;
        bool running=PageTabs.SelectedIndex==0;LockButton.Visibility=UnlockButton.Visibility=ShowSystem.Visibility=Count.Visibility=running?Visibility.Visible:Visibility.Collapsed;
        if(SectionHeading!=null)SectionHeading.Text=PageTabs.SelectedIndex==0?"Running applications":PageTabs.SelectedIndex==1?"All saved protections":"Registered browser profiles";
        if(IsLoaded)RefreshRuleViews();
    }
    void AddSavedApp_Click(object sender,RoutedEventArgs e)
    {
        var picker=new Microsoft.Win32.OpenFileDialog {Title="Choose an application to protect",Filter="Windows applications (*.exe)|*.exe",CheckFileExists=true};
        if(picker.ShowDialog(this)!=true)return;
        string name=Path.GetFileNameWithoutExtension(picker.FileName);
        if(Excluded.Contains(name)){System.Windows.MessageBox.Show("This Windows/system process cannot be protected.");return;}
        EditAppRule(name);
    }
    void EditAppRule(string name)
    {
        var dialog=new RuleEditorWindow(name,Config.RuleFor(name)){Owner=this};
        if(dialog.ShowDialog()!=true||!VerifyAdmin("save protection settings for "+name))return;
        if(TryUpdateConfig(c=>{c.LockedNames.Add(name);c.AppRules[name]=dialog.Result;})){Scan();RefreshApps();RefreshRuleViews();}
    }
    void EditSavedRule_Click(object sender,RoutedEventArgs e)
    {
        if(ProtectedApps.SelectedItem is not SavedRuleEntry entry)return;
        if(entry.Profile)EditProfileRule(entry.Id);else EditAppRule(entry.Id);
    }
    void RemoveSavedRule_Click(object sender,RoutedEventArgs e)
    {
        if(ProtectedApps.SelectedItem is not SavedRuleEntry entry||!VerifyAdmin("remove protection for "+entry.Name))return;
        if(!TryUpdateConfig(c=>{if(entry.Profile)c.BrowserProfiles.Remove(entry.Id);else{c.LockedNames.Remove(entry.Id);c.AppRules.Remove(entry.Id);}}))return;
        if(entry.Profile)profileCoordinator.Revoke(entry.Id);else ReleaseAppWindows(entry.Id);
        RefreshApps();RefreshRuleViews();
    }
    void LockSavedNow_Click(object sender,RoutedEventArgs e)
    {
        if(ProtectedApps.SelectedItem is not SavedRuleEntry entry)return;
        if(entry.Profile)profileCoordinator.Revoke(entry.Id);
        else foreach(var window in knownNames.Where(p=>p.Value.Equals(entry.Id,StringComparison.OrdinalIgnoreCase)).Select(p=>p.Key).ToArray())
        {approved.Remove(window);leases.Remove(window);grace.Remove(window);awayAt.Remove(window);Minimize(window);}
        RefreshApps();RefreshRuleViews();
    }
    void ReleaseAppWindows(string name)
    {
        foreach(var hwnd in knownNames.Where(p=>p.Value.Equals(name,StringComparison.OrdinalIgnoreCase)).Select(p=>p.Key).ToArray())
        {RestoreIfMinimizedByUs(hwnd);CleanupWindow(hwnd);}
    }
    void EditProfile_Click(object sender,RoutedEventArgs e){if(ProfileList.SelectedItem is BrowserProfileEntry entry)EditProfileRule(entry.Id);}
    void EditProfileRule(string id)
    {
        var current=profileCoordinator.Status(Config,Environment.TickCount64).FirstOrDefault(s=>s.Id==id);if(current==null)return;
        var timing=Config.BrowserProfiles.TryGetValue(id,out var existing)?existing.Timing:new LockRule {AwaySeconds=Config.AutoLockDelaySeconds};
        var dialog=new RuleEditorWindow(existing?.Name??current.Name,timing,true){Owner=this};
        if(!current.Connected)
        {
            var profileOnly=(System.Windows.Controls.CheckBox)dialog.FindName("ProfileOnly");profileOnly.IsChecked=false;profileOnly.IsEnabled=false;
            ((TextBlock)dialog.FindName("ProfileOnlyHelp")).Text="Reconnect the extension before switching from an app-wide browser rule to profile-only mode.";
        }
        if(dialog.ShowDialog()!=true||!VerifyAdmin("protect browser profile "+dialog.ProfileLabel))return;
        if(!TryUpdateConfig(c=>
        {
            c.BrowserProfiles[id]=new BrowserProfileRule {Name=dialog.ProfileLabel,Browser=current.Browser,Timing=dialog.Result};
            if(dialog.ProfileOnlyMode){c.LockedNames.Remove(current.Browser);c.AppRules.Remove(current.Browser);}
        }))return;
        if(dialog.ProfileOnlyMode)ReleaseAppWindows(current.Browser);
        profileCoordinator.Revoke(id);RefreshApps();RefreshRuleViews();
    }
    void RemoveProfile_Click(object sender,RoutedEventArgs e)
    {
        if(ProfileList.SelectedItem is not BrowserProfileEntry entry||!Config.BrowserProfiles.ContainsKey(entry.Id)||!VerifyAdmin("remove profile protection for "+entry.Name))return;
        if(TryUpdateConfig(c=>c.BrowserProfiles.Remove(entry.Id))){profileCoordinator.Revoke(entry.Id);RefreshRuleViews();}
    }
    void ExtensionSetup_Click(object sender,RoutedEventArgs e)
    {
        string folder=Path.Combine(AppContext.BaseDirectory,"BrowserExtension");
        System.Windows.MessageBox.Show("1. In the browser profile you want to protect, open brave://extensions, chrome://extensions or edge://extensions.\n2. Enable Developer mode, choose Load unpacked and select:\n"+folder+"\n3. Open the companion's popup, name this profile and choose the browser.\n4. Select it here and choose Protect / edit profile.\n\nOnly regular windows are covered. The extension must stay enabled and connected. No tab-content permissions are requested.","Connect a browser profile");
        if(Directory.Exists(folder))System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",folder){UseShellExecute=true});
    }
    void TryPromptProfiles()
    {
        if(prompting||quitting||!Config.Monitoring||System.Windows.Application.Current.Windows.OfType<Window>().Any(w=>w.IsVisible&&(w is PasswordWindow||w is RuleEditorWindow)))return;
        var key=profileCoordinator.NextUnlock(Config,Environment.TickCount64);if(key==null)return;
        if(!Config.BrowserProfiles.TryGetValue(key.Profile,out var rule))return;
        prompting=true;
        try
        {
            var dialog=new PasswordWindow("Unlock "+rule.Name,"App password for this "+rule.Browser+" profile window.",s=>SettingsStore.VerifyApp(Config,s),
                n=>{profileCoordinator.Wrong(key,n);LogAttempt("profile:"+rule.Name,key.Window,n);},profileCoordinator.Attempts(key));
            if(IsVisible&&WindowState!=WindowState.Minimized)dialog.Owner=this;else{dialog.ShowInTaskbar=true;dialog.WindowStartupLocation=WindowStartupLocation.CenterScreen;}
            var watch=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(250)};
            watch.Tick+=(_,_)=>{if(!profileCoordinator.Current(key,Environment.TickCount64)){watch.Stop();dialog.DialogResult=false;}};
            watch.Start();bool success;try{success=dialog.ShowDialog()==true;}finally{watch.Stop();}
            profileCoordinator.Complete(key,success,dialog.LockedOut,Environment.TickCount64);
            Notice.Text=success?rule.Name+" window unlocked":rule.Name+" remains locked";
        }
        finally{prompting=false;RefreshRuleViews();}
    }
}
