using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using FreeAppLock.Views;
using Forms = System.Windows.Forms;

namespace FreeAppLock;
public sealed class AppEntry
{
    public string Name { get; init; } = "";
    public string Title { get; init; } = "";
    public int Pid { get; init; }
    public int[] ProcessIds { get; init; } = [];
    public int WindowCount { get; init; }
    public bool CanEnforce { get; init; }
    public string EnforcementReason { get; init; } = "";
    public string ProcessLabel => ProcessIds.Length > 1 ? $"{ProcessIds.Length} processes" : Pid.ToString();
    public string ProcessDetail => "Process IDs: " + string.Join(", ", ProcessIds);
    public ImageSource? Icon { get; init; }
    public string State { get; init; } = "";
    public string StatusDetail { get; init; } = "";
    static readonly System.Windows.Media.Brush GreenBackground = MakeBrush("#193E30");
    static readonly System.Windows.Media.Brush GreenText = MakeBrush("#78DEA8");
    static readonly System.Windows.Media.Brush BlueBackground = MakeBrush("#203949");
    static readonly System.Windows.Media.Brush BlueText = MakeBrush("#72CBE4");
    static readonly System.Windows.Media.Brush AmberBackground = MakeBrush("#49391E");
    static readonly System.Windows.Media.Brush AmberText = MakeBrush("#F1C778");
    static readonly System.Windows.Media.Brush RedBackground = MakeBrush("#493036");
    static readonly System.Windows.Media.Brush RedText = MakeBrush("#F0A0A6");
    static readonly System.Windows.Media.Brush NeutralBackground = MakeBrush("#2A313A");
    static readonly System.Windows.Media.Brush NeutralText = MakeBrush("#9CA8B5");
    public System.Windows.Media.Brush BadgeBackground => State switch
    {
        "Locked" => GreenBackground,
        "Unlocked" => BlueBackground,
        "Rule set" or "Mixed" or "Checking" => AmberBackground,
        "Can't enforce" or "Not hidden" => RedBackground,
        _ => NeutralBackground
    };
    public System.Windows.Media.Brush BadgeForeground => State switch
    {
        "Locked" => GreenText,
        "Unlocked" => BlueText,
        "Rule set" or "Mixed" or "Checking" => AmberText,
        "Can't enforce" or "Not hidden" => RedText,
        _ => NeutralText
    };
    static System.Windows.Media.Brush MakeBrush(string hex)
    {
        var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
public partial class MainWindow : Window
{
    public LockSettings Config { get; private set; }
    readonly DispatcherTimer scan = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly HashSet<IntPtr> approved = new();
    readonly Dictionary<IntPtr,UnlockLease> leases = new();
    readonly HashSet<IntPtr> pendingRestores = new();
    // Only restore windows minimized by FreeAppLock, never windows the user minimized.
    readonly HashSet<IntPtr> minimizedByUs = new();
    readonly Dictionary<IntPtr,int> attempts = new();
    // A lockout applies to the specific HWND, not every window from the process.
    readonly Dictionary<IntPtr,DateTime> appBlockedUntil = new();
    readonly Dictionary<IntPtr,DateTime> awayAt = new();
    readonly Dictionary<IntPtr,DateTime> grace = new();
    readonly Dictionary<IntPtr,int> windows = new();
    readonly Dictionary<IntPtr,string> knownNames = new();
    readonly Dictionary<string,ImageSource?> cachedIcons = new(StringComparer.OrdinalIgnoreCase);
    readonly Forms.NotifyIcon tray;
    readonly System.Drawing.Icon appIcon;
    readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, byte> destroyedWindows = new();
    NativeMethods.WinEventCallback? windowEventCallback;
    IntPtr windowEventHook;
    bool scanning, prompting, quitting, sessionEndingPending;
    // Once an admin-state disk write fails, don't silently allow more guesses.
    bool adminStateWriteFailed;
    const int WmQueryEndSession = 0x0011;
    const int WmEndSession = 0x0016;
    static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) { "freeapplock", "explorer", "winlogon", "csrss", "lsass", "services", "smss", "svchost", "dwm", "system", "registry", "fontdrvhost", "sihost", "taskmgr", "userinit", "searchhost", "startmenuexperiencehost", "securityhealthservice", "shellexperiencehost", "lockapp", "logonui", "runtimebroker", "applicationframehost" };
    static string LocalDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreeAppLock");
    static string AttemptLog => Path.Combine(LocalDir,"attempts.log");
    public MainWindow()
    {
        try { Config = SettingsStore.Load(); } catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "Settings error"); throw; }
        InitializeComponent();
        using (var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/FreeAppLock;component/Assets/AppLock.ico")).Stream)
        using (var loadedIcon = new System.Drawing.Icon(iconStream, 32, 32))
            appIcon = (System.Drawing.Icon)loadedIcon.Clone();
        tray = new Forms.NotifyIcon { Icon = appIcon, Text = App.DisplayName, Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open FreeAppLock", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add("Quit securely", null, (_, _) => Dispatcher.Invoke(() => { if (VerifyAdmin("Quit monitoring")) QuitSecurely(); }));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
        scan.Tick += (_, _) => Scan(); refresh.Tick += (_, _) => RefreshApps();
        Loaded += (_, _) =>
        {
            if (!EnsurePasswords())
            {
                // There may be no admin password yet: never hide to tray on cancelled setup.
                quitting = true;
                Close();
                return;
            }
            windowEventCallback = (_, _, hwnd, objectId, childId, _, _) =>
            {
                if (hwnd != IntPtr.Zero && objectId == 0 && childId == 0 && windows.ContainsKey(hwnd)) destroyedWindows[hwnd] = 0;
            };
            windowEventHook = NativeMethods.SetWinEventHook(0x8001, 0x8001, IntPtr.Zero, windowEventCallback, 0, 0, 0);
            if (windowEventHook == IntPtr.Zero)
            {
                System.Windows.MessageBox.Show("Window lifetime monitoring could not start. FreeAppLock will close rather than retain unsafe window approvals.", "Monitoring unavailable");
                quitting = true; Close(); return;
            }
            StartProfileBridge();
            scan.Start(); refresh.Start(); MonitorChanged(); RefreshApps();
            if (App.StartInTray) Hide();
        };
        Closing += OnClosing;
        // WM_ENDSESSION(wParam=0), not a dispatcher idle callback, identifies
        // a cancelled Windows logoff/shutdown request.
        SourceInitialized += (_, _) =>
        {
            if (HwndSource.FromHwnd(new WindowInteropHelper(this).Handle) is { } source)
                source.AddHook(OnSessionMessage);
        };
        System.Windows.Application.Current.SessionEnding += (_, _) => sessionEndingPending = true;
    }
    IntPtr OnSessionMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmQueryEndSession) sessionEndingPending = true;
        else if (msg == WmEndSession)
        {
            if (wParam == IntPtr.Zero)
            {
                // Shutdown was cancelled; continue monitoring and normal tray behavior.
                sessionEndingPending = false;
            }
            else
            {
                sessionEndingPending = true;
                quitting = true;
            }
        }
        // Do not consume Windows messages; let WPF/default processing proceed.
        return IntPtr.Zero;
    }

    bool EnsurePasswords()
    {
        if (string.IsNullOrWhiteSpace(Config.Hash))
        {
            if (!NewPassword("Create admin password", "Create your admin password. Any nonempty password is allowed.", out var password)) return false;
            if (!TryUpdateConfig(c => SettingsStore.SetPassword(c, password))) return false;
        }
        if (string.IsNullOrWhiteSpace(Config.AppHash))
        {
            if (!NewPassword("Create app password", "Separate from admin password. Any nonempty password is allowed.", out var password)) return false;
            if (!TryUpdateConfig(c => SettingsStore.SetAppPassword(c, password))) return false;
        }
        return true;
    }
    bool NewPassword(string title, string description, out string value)
    {
        var d = new PasswordWindow(title, description, confirmation:true) { Owner = this };
        bool success = d.ShowDialog() == true; value = success ? d.Value : ""; return success;
    }
    public bool VerifyAdmin(string reason)
    {
        if (adminStateWriteFailed)
        {
            System.Windows.MessageBox.Show("Admin retry state could not be saved. Check write permissions in %LOCALAPPDATA%\\FreeAppLock and restart after repairing the problem.",
                "Admin verification unavailable");
            return false;
        }
        var remaining = AdminRetryPolicy.Remaining(Config, DateTimeOffset.UtcNow);
        if (remaining > TimeSpan.Zero)
        {
            ShowAdminCooldown(remaining);
            return false;
        }

        // Failed attempts persist across dialog closures and FreeAppLock restarts.
        var d = new PasswordWindow("Admin verification", "Enter your admin password to: " + reason + ".",
            s => VerifyAndRecordAdmin(s, reason),
            n => Notice.Text = "Incorrect admin password. Failed attempts are logged.",
            Config.AdminFailedAttempts) { Owner = this };
        bool success = d.ShowDialog() == true;
        remaining = AdminRetryPolicy.Remaining(Config, DateTimeOffset.UtcNow);
        if (!success && remaining > TimeSpan.Zero) ShowAdminCooldown(remaining);
        return success;
    }

    bool VerifyAndRecordAdmin(string candidate, string reason)
    {
        // Recheck on every submission; never verify a password during a cooldown.
        if (adminStateWriteFailed || AdminRetryPolicy.Remaining(Config, DateTimeOffset.UtcNow) > TimeSpan.Zero)
            return false;
        if (SettingsStore.Verify(Config, candidate))
        {
            AdminRetryPolicy.Reset(Config);
            PersistAdminRetryState();
            return !adminStateWriteFailed;
        }
        var cooldown = AdminRetryPolicy.RecordFailure(Config, DateTimeOffset.UtcNow);
        PersistAdminRetryState();
        LogAdminAttempt(reason, cooldown);
        return false;
    }

    void PersistAdminRetryState()
    {
        try { SettingsStore.Save(Config); }
        catch (Exception ex)
        {
            adminStateWriteFailed = true;
            System.Windows.MessageBox.Show("Could not save admin retry state: " + ex.Message +
                "\nFurther admin verification is disabled until the application is restarted after fixing storage.",
                "Settings write failed");
        }
    }

    void ShowAdminCooldown(TimeSpan remaining)
    {
        int seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        System.Windows.MessageBox.Show($"Too many incorrect admin passwords. Try again in {seconds} seconds.\n" +
            "Failed attempts are recorded locally.", "Admin verification temporarily blocked");
    }

    void LogAdminAttempt(string reason, TimeSpan cooldown)
    {
        // Never record passwords. Reason is application-generated; sanitize for one-line logs.
        string safeReason = reason.Replace('\r', ' ').Replace('\n', ' ').Replace('|', '/');
        string wait = cooldown > TimeSpan.Zero ? $" | Cooldown {cooldown.TotalSeconds:0} seconds" : "";
        try
        {
            Directory.CreateDirectory(LocalDir);
            File.AppendAllText(AttemptLog, $"{DateTimeOffset.Now:O} | ADMIN | {safeReason} | Failed password{wait}{Environment.NewLine}");
        }
        catch { /* Logs are best-effort and stored in user-controlled app data. */ }
    }
    public void ChangeAdmin() { if (!VerifyAdmin("change your admin password")) return; if (NewPassword("Change admin password", "Any nonempty password is allowed: letters, numbers or symbols.", out var next)) { TryUpdateConfig(c => SettingsStore.SetPassword(c, next)); } }
    public void ChangeAppPassword() { if (!VerifyAdmin("change the application unlock password")) return; if (NewPassword("Change app password", "This password unlocks protected apps. Any nonempty password is allowed.", out var next)) { if (TryUpdateConfig(c => SettingsStore.SetAppPassword(c, next))) { approved.Clear(); leases.Clear(); pendingRestores.Clear(); awayAt.Clear(); grace.Clear(); Scan(); } } }
    public void ShowHistory()
    {
        if (!VerifyAdmin("view failed attempts")) return;
        string data; try { data = File.Exists(AttemptLog) ? File.ReadAllText(AttemptLog) : "No failed attempts recorded."; } catch (Exception ex) { data = "Could not read the history: " + ex.Message; }
        var dialog = new Window { Owner=this, Title="Failed attempts", Width=750, Height=470, WindowStartupLocation=WindowStartupLocation.CenterOwner, Content = new System.Windows.Controls.TextBox { Text=data, IsReadOnly=true, TextWrapping=TextWrapping.NoWrap, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Auto, Margin=new Thickness(20), FontFamily=new System.Windows.Media.FontFamily("Consolas") } }; dialog.ShowDialog();
    }
    public bool TryUpdateConfig(Action<LockSettings> change)
    {
        try { Config = SettingsStore.Update(Config, change); RefreshRuleViews(); return true; }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("The change was not applied because settings could not be saved: " + ex.Message, "Settings unchanged");
            return false;
        }
    }
    public void MonitorChanged() { MonitorStatus.Text = Config.Monitoring ? "●  Monitoring on" : "●  Monitoring off"; if (!Config.Monitoring) { approved.Clear(); leases.Clear(); pendingRestores.Clear(); awayAt.Clear(); RestoreAll(); } }
    public bool IsAutoStart() { try { return StartupRegistration.IsEnabled(); } catch { return false; } }
    public void SetAutoStart(bool enabled) => StartupRegistration.SetEnabled(enabled);
    // Presentation-only: use spare ListView width for long window titles.
    // Does not modify monitoring, authentication, or instance state.
    void Apps_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Apps.View is GridView view && view.Columns.Count >= 4)
        {
            double available = Apps.ActualWidth - view.Columns[0].Width
                - view.Columns[2].Width - view.Columns[3].Width - 56;
            if (available > 200)
                view.Columns[1].Width = available;
        }
    }

    void Settings_Click(object sender,RoutedEventArgs e) => new SettingsWindow(this).ShowDialog();
    void Search_TextChanged(object sender,TextChangedEventArgs e) { SearchHint.Visibility = string.IsNullOrEmpty(Search.Text) ? Visibility.Visible : Visibility.Collapsed; if (IsLoaded) RefreshApps(resetScroll:true); }
    void ShowSystem_Changed(object sender,RoutedEventArgs e) { if (IsLoaded) RefreshApps(resetScroll:true); }
    void Lock_Click(object sender,RoutedEventArgs e) => ToggleLock();
    void Apps_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateLockButton();
        if (Apps.SelectedItem is AppEntry item && !Config.LockedNames.Contains(item.Name))
        {
            var support = CheckApplicationSupport(item);
            if (!support.CanEnforce)
                Notice.Text = "Cannot protect " + item.Name + ": " + support.Reason;
        }
    }
    void UpdateLockButton()
    {
        if (LockButton is null) return;
        var item = Apps.SelectedItem as AppEntry;
        bool ruleExists = item != null && Config.LockedNames.Contains(item.Name);
        var support = item != null && !ruleExists
            ? CheckApplicationSupport(item) : ProcessSupport.Supported;
        LockButton.IsEnabled = item != null && !Excluded.Contains(item.Name) && (ruleExists || support.CanEnforce);
        LockButton.ToolTip = item != null && !ruleExists && !support.CanEnforce ? support.Reason : null;
        LockButton.Content = ruleExists ? "Remove lock" : "Lock selected";
    }
    ProcessSupport CheckApplicationSupport(AppEntry item)
    {
        var discovered = EnumerateTopLevelWindows();
        var visiblePids = discovered.Values.ToHashSet();
        var reasons = new List<string>();
        var windowPids = item.ProcessIds.Where(visiblePids.Contains).ToArray();
        foreach (int pid in windowPids.Length > 0 ? windowPids : item.ProcessIds)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (!process.ProcessName.Equals(item.Name, StringComparison.OrdinalIgnoreCase)) continue;
                var support = ProcessCompatibility.Check(pid, visiblePids.Contains(pid));
                if (support.CanEnforce) return support;
                reasons.Add(support.Reason);
            }
            catch (ArgumentException) { }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
        }
        return ProcessSupport.Unsupported(reasons.FirstOrDefault() ?? "The application has exited. Refresh the list and try again.");
    }
    void ToggleLock()
    {
        if (Apps.SelectedItem is not AppEntry item) return;
        if (Excluded.Contains(item.Name)) { System.Windows.MessageBox.Show("This system or monitoring process cannot be locked to avoid a Windows lockout."); return; }
        if (!Config.LockedNames.Contains(item.Name))
        {
            var support = CheckApplicationSupport(item);
            if (!support.CanEnforce)
            {
                Notice.Text = "Cannot protect " + item.Name + ": " + support.Reason;
                System.Windows.MessageBox.Show(support.Reason + "\n\nFreeAppLock will not claim to lock this app.", "Protection unavailable");
                return;
            }
        }
        if (!VerifyAdmin("change the protection rule for " + item.Name)) return;
        bool removing = Config.LockedNames.Contains(item.Name);
        if (!TryUpdateConfig(c => { if (removing) { c.LockedNames.Remove(item.Name); c.AppRules.Remove(item.Name); } else { c.LockedNames.Add(item.Name); c.AppRules[item.Name]=new LockRule {AwaySeconds=c.AutoLockDelaySeconds}; } })) return;
        if (removing)
        {
            foreach (var kv in knownNames.Where(x => x.Value.Equals(item.Name, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                RestoreIfMinimizedByUs(kv.Key);
                CleanupWindow(kv.Key);
            }
        }
        Scan(); RefreshApps();
    }
    // Authorization belongs to a specific top-level window, never to a process name or PID.
    // One process can host several independent windows, including modern Notepad.
    static Dictionary<IntPtr,int> EnumerateTopLevelWindows()
    {
        var found = new Dictionary<IntPtr,int>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd) && !NativeMethods.IsIconic(hwnd)) return true;
            // Include owned dialogs/popups and report any surfaces that remain visible.
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != 0) found[hwnd] = unchecked((int)pid);
            return true;
        }, IntPtr.Zero);
        return found;
    }
    static bool HasDirectWindow(int pid) => EnumerateTopLevelWindows().ContainsValue(pid);

    static IntPtr AuthorizationWindow(IntPtr hwnd, string processName)
    {
        if (!BrowserPolicy.PreservesTabs(processName)) return hwnd;
        var root = hwnd;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        // Follow explicit GW_OWNER links; some overlapped dialogs are owned without
        // being WS_POPUP, so GetAncestor alone does not cover every owner chain.
        for (int depth=0; depth<16; depth++)
        {
            var owner = NativeMethods.GetWindow(root,4);
            if (owner==IntPtr.Zero || owner==root || owner==hwnd || !NativeMethods.IsWindow(owner) ||
                !NativeMethods.IsWindowVisible(owner) && !NativeMethods.IsIconic(owner)) break;
            NativeMethods.GetWindowThreadProcessId(owner,out var ownerPid);
            if (ownerPid!=pid) break;
            root=owner;
        }
        return root;
    }
    bool IsWindowApproved(IntPtr hwnd, string name) => approved.Contains(AuthorizationWindow(hwnd, name));

    void Unlock_Click(object sender,RoutedEventArgs e)
    {
        if (Apps.SelectedItem is not AppEntry item || !Config.LockedNames.Contains(item.Name)) return;
        var support = CheckApplicationSupport(item);
        if (!support.CanEnforce) { Notice.Text = "Can't enforce: " + support.Reason; return; }
        // Unlock a window only. Never approve the whole PID.
        var matches = windows.Where(x => NativeMethods.IsWindow(x.Key) &&
            knownNames.TryGetValue(x.Key, out var name) && name.Equals(item.Name, StringComparison.OrdinalIgnoreCase))
            .Select(x => new KeyValuePair<IntPtr,int>(AuthorizationWindow(x.Key,item.Name),x.Value))
            .DistinctBy(x => x.Key).ToArray();
        if (matches.Length != 1)
        {
            Notice.Text = matches.Length == 0
                ? "No protected window found for this process."
                : "Multiple windows: activate the specific window to unlock it.";
            return;
        }
        var hwnd = matches[0].Key;
        Prompt(hwnd,matches[0].Value,item.Name);
    }
    void Scan()
    {
        DrainDestroyedWindows();
        TryPromptProfiles();
        if (scanning || !Config.Monitoring || Config.LockedNames.Count == 0) return;
        scanning=true;
        try
        {
            var active=NativeMethods.GetForegroundWindow();
            var discovered=EnumerateTopLevelWindows();
            var supportByPid = new Dictionary<int, ProcessSupport>();
            // Remove credentials for destroyed windows, even if their PID still exists.
            foreach(var hwnd in windows.Keys.Where(h => !discovered.ContainsKey(h) || discovered[h] != windows[h]).ToArray())
            {
                approved.Remove(hwnd); leases.Remove(hwnd); pendingRestores.Remove(hwnd); attempts.Remove(hwnd); appBlockedUntil.Remove(hwnd); awayAt.Remove(hwnd); grace.Remove(hwnd);
                minimizedByUs.Remove(hwnd); knownNames.Remove(hwnd); windows.Remove(hwnd);
            }
            foreach(var kv in discovered)
            {
                var hwnd=kv.Key; var pid=kv.Value;
                try
                {
                    using var proc=Process.GetProcessById(pid);
                    string name=proc.ProcessName;
                    if(Excluded.Contains(name) || !Config.LockedNames.Contains(name)) continue;
                    if (!supportByPid.TryGetValue(pid, out var support))
                        supportByPid[pid] = support = ProcessCompatibility.Check(pid, hasDirectWindow: true);
                    if (!support.CanEnforce)
                    {
                        // Existing rules are retained for future compatible processes,
                        // but never claim or attempt enforcement against unsupported ones.
                        approved.Remove(hwnd); leases.Remove(hwnd); awayAt.Remove(hwnd); grace.Remove(hwnd);
                        knownNames.Remove(hwnd); windows.Remove(hwnd);
                        continue;
                    }
                    if(knownNames.TryGetValue(hwnd,out var previous) && !previous.Equals(name,StringComparison.OrdinalIgnoreCase))
                    { approved.Remove(hwnd); leases.Remove(hwnd); attempts.Remove(hwnd); awayAt.Remove(hwnd); grace.Remove(hwnd); }
                    knownNames[hwnd]=name; windows[hwnd]=pid;
                    var ownerWindow = AuthorizationWindow(hwnd,name);
                    if (ownerWindow != hwnd)
                    {
                        // Owned browser dialogs share their browser window approval.
                        if (!approved.Contains(ownerWindow))
                        {
                            if (!NativeMethods.IsIconic(hwnd) && NativeMethods.IsWindowVisible(hwnd)) Minimize(hwnd);
                            if (active == hwnd && !prompting)
                            {
                                Dispatcher.BeginInvoke(new Action(() => Prompt(ownerWindow,pid,name)));
                                break;
                            }
                        }
                        continue;
                    }
                    if(approved.Contains(hwnd))
                    {
                        bool minimized=NativeMethods.IsIconic(hwnd);
                        bool restoring=pendingRestores.Contains(hwnd)&&grace.TryGetValue(hwnd,out var restoreDeadline)&&DateTime.UtcNow<restoreDeadline;
                        if(!minimized && pendingRestores.Remove(hwnd) && !prompting && active==new WindowInteropHelper(this).Handle)NativeMethods.SetForegroundWindow(hwnd);
                        bool focused=active==hwnd||AuthorizationWindow(active,name)==hwnd||NativeMethods.GetAncestor(active,3)==hwnd;
                        bool inGrace=grace.TryGetValue(hwnd,out var until)&&DateTime.UtcNow<until;
                        if(!leases.TryGetValue(hwnd,out var lease))leases[hwnd]=lease=new UnlockLease(Environment.TickCount64);
                        if(LockTiming.Expired(Config.RuleFor(name),lease,Environment.TickCount64,focused||inGrace,minimized&&!restoring))
                        {
                            approved.Remove(hwnd);leases.Remove(hwnd);awayAt.Remove(hwnd);grace.Remove(hwnd);pendingRestores.Remove(hwnd);
                            if(!minimized)Minimize(hwnd);
                            Notice.Text=name+" relocked by its protection rule";
                        }
                        continue;
                    }
                    // Cover every unapproved protected window (including secondary monitors),
                    // not only the foreground window. Existing user-minimized windows stay as-is.
                    if (!NativeMethods.IsIconic(hwnd) && NativeMethods.IsWindowVisible(hwnd))
                        Minimize(hwnd);
                    // Modal WPF dialogs still pump dispatcher messages; keep hiding other
                    // locked windows while prompting, but never open overlapping dialogs.
                    if (active == hwnd && !prompting)
                    {
                        Dispatcher.BeginInvoke(new Action(() => Prompt(hwnd, pid, name)));
                        break;
                    }
                }
                catch { approved.Remove(hwnd); leases.Remove(hwnd); }
            }
        }
        catch { }
        finally { scanning=false; }
    }
    void DrainDestroyedWindows()
    {
        foreach (var hwnd in destroyedWindows.Keys)
            if (destroyedWindows.TryRemove(hwnd, out _)) CleanupWindow(hwnd);
    }
    void Prompt(IntPtr hwnd,int pid,string name)
    {
        DrainDestroyedWindows();
        hwnd = AuthorizationWindow(hwnd,name);
        if(prompting || System.Windows.Application.Current.Windows.OfType<PasswordWindow>().Any(w=>w.IsVisible) || !Config.Monitoring || !Config.LockedNames.Contains(name) || !NativeMethods.IsWindow(hwnd)) return;
        if (appBlockedUntil.TryGetValue(hwnd, out var until))
        {
            if (DateTime.UtcNow < until)
            {
                int seconds = Math.Max(1, (int)Math.Ceiling((until - DateTime.UtcNow).TotalSeconds));
                Notice.Text = $"{name} remains locked. Retry in {seconds} seconds.";
                return;
            }
            appBlockedUntil.Remove(hwnd);
        }
        if (!ProcessCompatibility.Check(pid, hasDirectWindow: true).CanEnforce) return;
        // Check that window still belongs to the originally selected process.
        NativeMethods.GetWindowThreadProcessId(hwnd,out var currentPid);
        if(currentPid != unchecked((uint)pid)) return;
        prompting=true;
        try
        {
            var d=new PasswordWindow("Unlock " + name, "Enter the app password to access this " + name + " window.",
                s=>SettingsStore.VerifyApp(Config,s), n=> { attempts[hwnd]=n; LogAttempt(name,pid,n); Minimize(hwnd); Notice.Text=$"Wrong password: {n}/5"; },
                attempts.GetValueOrDefault(hwnd));
            if (IsVisible && WindowState != WindowState.Minimized) d.Owner=this;
            else { d.ShowInTaskbar=true; d.WindowStartupLocation=WindowStartupLocation.CenterScreen; }
            // A modal password prompt must not outlive its target window.
            // Otherwise closing window A leaves its dialog blocking monitoring of window B.
            var targetWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            targetWatch.Tick += (_, _) =>
            {
                if (destroyedWindows.ContainsKey(hwnd) || !NativeMethods.IsWindow(hwnd)) { targetWatch.Stop(); d.DialogResult = false; return; }
                NativeMethods.GetWindowThreadProcessId(hwnd, out var ownerPid);
                if (ownerPid != unchecked((uint)pid)) { targetWatch.Stop(); d.DialogResult = false; }
            };
            bool success;
            targetWatch.Start();
            try { success=d.ShowDialog()==true; }
            finally { targetWatch.Stop(); }
            // Closing another window cannot grant access to this one.
            if(destroyedWindows.TryRemove(hwnd, out _) || !NativeMethods.IsWindow(hwnd)) { CleanupWindow(hwnd); return; }
            NativeMethods.GetWindowThreadProcessId(hwnd,out currentPid);
            if(currentPid != unchecked((uint)pid)) { CleanupWindow(hwnd); return; }
            if(success)
            {
                attempts.Remove(hwnd); appBlockedUntil.Remove(hwnd); approved.Add(hwnd); leases[hwnd]=new UnlockLease(Environment.TickCount64); awayAt.Remove(hwnd);
                grace[hwnd]=DateTime.UtcNow.AddSeconds(2);
                Restore(hwnd);
                if (BrowserPolicy.PreservesTabs(name))
                    foreach (var owned in minimizedByUs.ToArray().Where(h => h != hwnd && AuthorizationWindow(h,name)==hwnd))
                        RestoreIfMinimizedByUs(owned);
                Notice.Text=name+" window unlocked";
            }
            else if(d.LockedOut)
            {
                Minimize(hwnd);
                if (BrowserPolicy.ShouldTerminate(name, Config.TerminateOnAppLockout))
                {
                    // Destructive behavior is opt-in and never used for recognized browsers.
                    try { using var p=Process.GetProcessById(pid); p.Kill(); Notice.Text=name+" process termination requested after five incorrect attempts"; }
                    catch(Exception ex) { Notice.Text="Could not terminate: "+ex.Message; }
                    CleanupWindow(hwnd);
                }
                else
                {
                    // Keep the process and its unsaved work intact. A short per-window
                    // cooldown prevents immediately restarting unlimited guessing dialogs.
                    attempts.Remove(hwnd);
                    appBlockedUntil[hwnd] = DateTime.UtcNow.AddSeconds(60);
                    Notice.Text = name + " remains locked. Try again in 60 seconds; the app was not terminated.";
                }
            }
            else { Minimize(hwnd); Notice.Text=name+" remains locked"; }
        }
        finally { prompting=false; RefreshApps(); }
    }
    void CleanupWindow(IntPtr hwnd)
    {
        approved.Remove(hwnd); leases.Remove(hwnd); pendingRestores.Remove(hwnd); attempts.Remove(hwnd); appBlockedUntil.Remove(hwnd); awayAt.Remove(hwnd); grace.Remove(hwnd);
        minimizedByUs.Remove(hwnd); windows.Remove(hwnd); knownNames.Remove(hwnd);
    }
    void Minimize(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd) || NativeMethods.IsIconic(hwnd)) return;
        // Post the minimize request, avoiding a hung app blocking the monitoring loop.
        if (NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_MINIMIZE))
            minimizedByUs.Add(hwnd);
    }
    void Restore(IntPtr hwnd)
    {
        if (NativeMethods.IsWindow(hwnd))
        {
            // Never wait on another application UI thread; monitoring must keep running.
            if (NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_RESTORE))
            {
                pendingRestores.Add(hwnd);
                NativeMethods.SetForegroundWindow(hwnd);
            }
            else approved.Remove(hwnd); leases.Remove(hwnd);
        }
        minimizedByUs.Remove(hwnd);
    }
    void RestoreIfMinimizedByUs(IntPtr hwnd)
    {
        if (!minimizedByUs.Remove(hwnd)) return;
        if (NativeMethods.IsWindow(hwnd) && NativeMethods.IsIconic(hwnd))
            NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_RESTORE);
    }
    void RestoreAll()
    {
        foreach (var hwnd in minimizedByUs.ToArray()) RestoreIfMinimizedByUs(hwnd);
    }
    void LogAttempt(string name,int pid,int attempt)
    { try { Directory.CreateDirectory(LocalDir); File.AppendAllText(AttemptLog,$"{DateTimeOffset.Now:O} | {name} | PID {pid} | Failed {attempt}/5{Environment.NewLine}"); } catch { } }
    ImageSource? GetApplicationIcon(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return GetIcon(process); }
        catch (ArgumentException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
        catch (InvalidOperationException) { return null; }
    }
    ImageSource? GetIcon(Process p)
    {
        var key=p.ProcessName;
        if (cachedIcons.TryGetValue(key,out var existing) && existing != null) return existing;
        ImageSource? source=null;
        try { using var icon=System.Drawing.Icon.ExtractAssociatedIcon(p.MainModule?.FileName ?? ""); if (icon!=null) { using var memory=new MemoryStream(); using var drawing = icon.ToBitmap(); drawing.Save(memory,System.Drawing.Imaging.ImageFormat.Png); memory.Position=0; var bitmap=new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption=BitmapCacheOption.OnLoad; bitmap.StreamSource=memory; bitmap.EndInit(); bitmap.Freeze(); source=bitmap; } } catch { }
        cachedIcons[key]=source; return source;
    }
    static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            var nested = FindVisualChild<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }
    void RefreshApps(bool resetScroll = false)
    {
        if (!IsLoaded || prompting) { if(IsLoaded)RefreshRuleViews(); return; }
        var selected=(Apps.SelectedItem as AppEntry)?.Name;
        var oldOffset = resetScroll ? 0 : FindVisualChild<ScrollViewer>(Apps)?.VerticalOffset ?? 0;
        var entries=new List<AppEntry>();
        var topLevelWindows = EnumerateTopLevelWindows();
        var directlyOwnedPids = topLevelWindows.Values.ToHashSet();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var name=p.ProcessName; var title=p.MainWindowTitle;
                if (Excluded.Contains(name) && !Config.LockedNames.Contains(name) && ShowSystem.IsChecked != true) continue;
                // A rule alone does not prove we can enforce it. Verify process class,
                // integrity and actual window state before displaying "Locked".
                bool ruleExists = Config.LockedNames.Contains(name);
                var support = ruleExists || directlyOwnedPids.Contains(p.Id)
                    ? ProcessCompatibility.Check(p.Id, directlyOwnedPids.Contains(p.Id)) : ProcessSupport.Supported;
                var actualWindows = topLevelWindows.Where(w => w.Value == p.Id).Select(w => w.Key).ToArray();
                var tracked = actualWindows.Where(hwnd => windows.TryGetValue(hwnd, out int trackedPid) && trackedPid == p.Id).ToArray();
                int authorized = tracked.Count(hwnd => IsWindowApproved(hwnd,name));
                bool untrackedWindow = actualWindows.Any(hwnd => !windows.TryGetValue(hwnd, out int trackedPid) || trackedPid != p.Id);
                bool unapprovedVisible = actualWindows.Any(hwnd => !IsWindowApproved(hwnd,name) &&
                    NativeMethods.IsWindowVisible(hwnd) && !NativeMethods.IsIconic(hwnd));
                string state;
                string detail;
                if (!ruleExists) { state = "Not locked"; detail = "No protection rule has been set."; }
                else if (Excluded.Contains(name) || !support.CanEnforce)
                {
                    state = "Can't enforce";
                    detail = Excluded.Contains(name) ? "This Windows/system process is excluded from monitoring." : support.Reason;
                }
                else if (!Config.Monitoring) { state = "Monitoring off"; detail = "Monitoring is switched off; the rule is not being enforced."; }
                else if (actualWindows.Length == 0) { state = "Rule set"; detail = "No directly owned top-level window was found for this process."; }
                else if (unapprovedVisible) { state = "Not hidden"; detail = "At least one unauthorized window is still visible. Do not rely on protection."; }
                else if (untrackedWindow) { state = "Checking"; detail = "A top-level window has not yet been inspected by the monitor."; }
                else if (authorized == tracked.Length) { state = "Unlocked"; detail = "All tracked windows have been authorized."; }
                else if (authorized > 0) { state = "Mixed"; detail = "Some tracked windows are authorized; others are minimized."; }
                else { state = "Locked"; detail = "All tracked windows are currently minimized; protection is reactive and not tamper-proof."; }
                entries.Add(new AppEntry { Name=name,Title=title,Pid=p.Id,State=state,StatusDetail=detail,Icon=null,ProcessIds=[p.Id],WindowCount=actualWindows.Length,CanEnforce=support.CanEnforce,EnforcementReason=support.Reason });
            }
            catch { } finally { p.Dispose(); }
        }
        var processEntries = entries;
        entries = ApplicationGrouping.Build(processEntries.Select(e => new ApplicationProcessSnapshot(
                e.Name, e.Pid, e.Title, e.State, e.StatusDetail, e.WindowCount, e.CanEnforce, e.EnforcementReason)))
            .Select(g => new AppEntry { Name=g.Name, Pid=g.Pid, ProcessIds=g.ProcessIds, Title=g.Title,
                State=g.State, StatusDetail=g.Detail, WindowCount=g.WindowCount, CanEnforce=g.CanEnforce,
                EnforcementReason=g.Reason, Icon=GetApplicationIcon(g.Pid) })
            .Where(e => ShowSystem.IsChecked == true || Config.LockedNames.Contains(e.Name) || e.WindowCount > 0)
            .OrderBy(e => e.Name,StringComparer.OrdinalIgnoreCase).ToList();
        lastApplicationRows=entries;
        RefreshRuleViews();
        entries=entries.Where(e => e.Name.Contains(Search.Text,StringComparison.OrdinalIgnoreCase) || e.Title.Contains(Search.Text,StringComparison.OrdinalIgnoreCase)
                || e.StatusDetail.Contains(Search.Text,StringComparison.OrdinalIgnoreCase)).ToList();
        // Recreating ListView items every two seconds resets the scrollbar and selection.
        // Retain the existing items whenever nothing displayed has changed.
        if (Apps.ItemsSource is List<AppEntry> current && current.Count == entries.Count &&
            current.Zip(entries).All(pair =>
                pair.First.Pid == pair.Second.Pid &&
                pair.First.ProcessIds.SequenceEqual(pair.Second.ProcessIds) &&
                pair.First.CanEnforce == pair.Second.CanEnforce &&
                pair.First.Name == pair.Second.Name &&
                pair.First.Title == pair.Second.Title &&
                pair.First.State == pair.Second.State &&
                pair.First.StatusDetail == pair.Second.StatusDetail))
        {
            UpdateLockButton();
            return;
        }
        Apps.ItemsSource = entries;
        Apps.SelectedItem = entries.FirstOrDefault(x => x.Name.Equals(selected,StringComparison.OrdinalIgnoreCase));
        Count.Text = entries.Count + (entries.Count == 1 ? " app" : " apps");
        UpdateLockButton();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            FindVisualChild<ScrollViewer>(Apps)?.ScrollToVerticalOffset(oldOffset);
        }), DispatcherPriority.Loaded);
    }
    internal void ShowFromTray() { Show(); WindowState=WindowState.Normal; Activate(); }
    void OnClosing(object? sender,CancelEventArgs e)
    {
        if (quitting || sessionEndingPending)
        {
            scan.Stop(); refresh.Stop(); profileServer?.Dispose();
            if (windowEventHook != IntPtr.Zero) { NativeMethods.UnhookWinEvent(windowEventHook); windowEventHook = IntPtr.Zero; }
            tray.Visible = false; tray.Dispose(); appIcon.Dispose();
            return;
        }
        e.Cancel = true;
        Hide();
    }
    public void QuitSecurely() { quitting=true; scan.Stop(); refresh.Stop(); RestoreAll(); Close(); }
}
