using Application = System.Windows.Application;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using FreeAppLock;
using FreeAppLock.Views;
class Program
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public IntPtr Extra; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct MouseInput { public int X, Y; public uint Data, Flags, Time; public IntPtr Extra; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
    struct InputUnion { [System.Runtime.InteropServices.FieldOffset(0)] public KeyboardInput Keyboard; [System.Runtime.InteropServices.FieldOffset(0)] public MouseInput Mouse; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct Input { public uint Type; public InputUnion Data; }
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)]
    static extern uint SendInput(uint count, Input[] inputs, int size);
    static void TypeText(string text)
    {
        var inputs=text.SelectMany(c=>new[] {
            new Input { Type=1,Data=new InputUnion { Keyboard=new KeyboardInput { Scan=c,Flags=4 } } },
            new Input { Type=1,Data=new InputUnion { Keyboard=new KeyboardInput { Scan=c,Flags=6 } } }
        }).ToArray();
        Assert(SendInput((uint)inputs.Length,inputs,System.Runtime.InteropServices.Marshal.SizeOf<Input>())==inputs.Length,"Windows rejected test keystrokes");
        Pump(100);
    }
    static BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static int passed;
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static void Pump(int milliseconds) {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        var frame = new DispatcherFrame(); timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    static object? Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Flags)!.Invoke(target, args);
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Flags)!.GetValue(target)!;
    static void Check(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
    [STAThread] static int Main(string[] args)
    {
        Process? child = null; Process? background = null; MainWindow? main = null;
        try {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var xml = System.Xml.Linq.XDocument.Load(args[1]); var content = xml.Root!.Elements().Single().Elements(); app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" + string.Concat(content.Select(x => x.ToString())) + "</ResourceDictionary>");

            Check("Admin and app setup/change accept one character and retain confirmation", () => {
                foreach(var title in new[] { "Create admin password", "Create app password", "Change admin password", "Change app password" })
                {
                    var dialog=new PasswordWindow(title,"Any nonempty password is allowed.",confirmation:true);
                    var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(100) };
                    timer.Tick+=(_,_)=> {
                        timer.Stop();
                        var secret=(System.Windows.Controls.PasswordBox)dialog.FindName("Secret");
                        var confirm=(System.Windows.Controls.PasswordBox)dialog.FindName("ConfirmSecret");
                        var feedback=(System.Windows.Controls.TextBlock)dialog.FindName("Feedback");
                        Call(dialog,"Submit_Click",dialog,new RoutedEventArgs());
                        Assert(dialog.IsVisible && feedback.Text=="Enter a password.","Empty password accepted");
                        secret.Password="a"; confirm.Password="b";
                        Call(dialog,"Submit_Click",dialog,new RoutedEventArgs());
                        Assert(dialog.IsVisible && feedback.Text=="Passwords do not match.","Confirmation mismatch accepted");
                        confirm.Password="a"; Call(dialog,"Submit_Click",dialog,new RoutedEventArgs());
                    };
                    timer.Start(); Assert(dialog.ShowDialog()==true && dialog.Value=="a","One-character password rejected"); timer.Stop();
                }
            });
            Check("Revealed password retains focus after wrong submission", () => {
                var dialog = new PasswordWindow("Test", "Disposable input", _ => false, _ => { });
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                timer.Tick += (_, _) => {
                    timer.Stop();
                    Call(dialog, "Show_Click", dialog, new RoutedEventArgs());
                    var visible = (System.Windows.Controls.TextBox)dialog.FindName("VisibleSecret"); visible.Text = "wrong";
                    Call(dialog, "Submit_Click", dialog, new RoutedEventArgs());
                    Assert(visible.IsKeyboardFocused, "Revealed input lost focus"); dialog.DialogResult = false;
                };
                timer.Start(); dialog.ShowDialog();
            });
            child = Process.Start(new ProcessStartInfo(args[0]) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardInput = true })!;
            string line = child.StandardOutput.ReadLine()!; var handles = line.Split(',').Select(long.Parse).Select(x => new IntPtr(x)).ToArray();
            background = Process.Start(new ProcessStartInfo(args[0], "--background") { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardInput=true })!;
            Assert(background.StandardOutput.ReadLine()=="READY", "Background fixture failed to start");
            main = new MainWindow();
            var config = new LockSettings(); SettingsStore.SetPassword(config, "test-admin-password"); SettingsStore.SetAppPassword(config, "test-app-password");
            // Replace in-memory settings before Loaded; no test writes use the real settings path.
            typeof(MainWindow).GetProperty("Config")!.SetValue(main, config);
            typeof(FreeAppLock.App).GetProperty("StartInTray", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);
            main.Left = -2000; main.Top = -2400; main.Show(); Pump(300);
            Check("Configured login startup hides to tray", () => Assert(!main.IsVisible, "Login startup remained visible"));
            Check("Shortcut activation restores the main window", () => { Call(main, "ShowFromTray"); Pump(100); Assert(main.IsVisible && main.WindowState == WindowState.Normal, "Main window did not restore"); });
            typeof(FreeAppLock.App).GetProperty("StartInTray", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, false);
            Check("Public version is shown beside the app name", () => {
                Assert(FreeAppLock.App.DisplayVersion=="1.0.1","Wrong public version");
                Assert(main.Title.Contains("FreeAppLock v1.0.1"),"Title bar has no version");
                Assert(((System.Windows.Controls.TextBlock)main.FindName("AppHeading")).Text=="FreeAppLock v1.0.1","App header has no version");
                Assert(Field<System.Windows.Forms.NotifyIcon>(main,"tray").Text=="FreeAppLock v1.0.1","Tray tooltip has no version");
            });
            Check("App, password and settings windows share the brand icon", () => {
                Assert(main.Icon?.ToString().Contains("Assets/AppLock.ico") == true, "Main window icon missing");
                var password = new PasswordWindow("Icon test", "Disposable dialog");
                Assert(password.Icon?.ToString().Contains("Assets/AppLock.ico") == true, "Password icon missing"); password.Close();
                var settings = new SettingsWindow(main);
                Assert(settings.Icon?.ToString().Contains("Assets/AppLock.ico") == true, "Settings icon missing"); settings.Close();
                Assert(Field<System.Windows.Forms.NotifyIcon>(main, "tray").Icon != null, "Tray icon missing");
            });
            Check("Brand icon contains crisp frames for Windows sizes", () => {
                using var resource = Application.GetResourceStream(new Uri("pack://application:,,,/FreeAppLock;component/Assets/AppLock.ico")).Stream;
                var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(resource, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                Assert(decoder.Frames.Count == 9 && decoder.Frames.Any(f => f.PixelWidth == 16) && decoder.Frames.Any(f => f.PixelWidth == 256), "Icon sizes missing");
            });
            Check("Multiple matching processes display one application row", () => {
                Call(main, "RefreshApps", false);
                var list = (System.Windows.Controls.ListView)main.FindName("Apps");
                var rows = ((List<AppEntry>)list.ItemsSource).Where(e => e.Name.Equals(child.ProcessName,StringComparison.OrdinalIgnoreCase)).ToArray();
                Assert(rows.Length==1 && rows[0].ProcessIds.Contains(child.Id) && rows[0].ProcessIds.Contains(background.Id), "Duplicate or incomplete grouped row");
                Assert(rows[0].Pid==child.Id && rows[0].WindowCount==3, "Helper process chosen instead of actual window owner");
                list.SelectedItem=rows[0];
                background.Kill(); background.WaitForExit();
                Call(main,"RefreshApps",false);
                Assert(list.SelectedItem is AppEntry selected && selected.Name.Equals(child.ProcessName,StringComparison.OrdinalIgnoreCase), "Selection lost when process IDs changed");
            });
            Check("Owned windows are discovered", () => {
                var found = (Dictionary<IntPtr,int>)typeof(MainWindow).GetMethod("EnumerateTopLevelWindows", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
                Assert(found.ContainsKey(handles[0]) && found.ContainsKey(handles[1]), "Owned window excluded");
            });
            Check("Browser dialogs share their parent window approval, not other windows", () => {
                var boundary=typeof(MainWindow).GetMethod("AuthorizationWindow",BindingFlags.Static|BindingFlags.NonPublic)!;
                Assert((IntPtr)boundary.Invoke(null,new object[]{handles[1],"brave"})! == handles[0], "Owned browser dialog uses a separate boundary (result="+boundary.Invoke(null,new object[]{handles[1],"brave"})+", expected="+handles[0]+", dialog="+handles[1]+")");
                Assert((IntPtr)boundary.Invoke(null,new object[]{handles[2],"brave"})! == handles[2], "Independent browser window merged into another window");
                Field<HashSet<IntPtr>>(main,"approved").Add(handles[0]);
                Assert((bool)Call(main,"IsWindowApproved",handles[1],"brave")!,"Browser child did not share approval");
                Assert(!(bool)Call(main,"IsWindowApproved",handles[2],"brave")!,"Separate browser window inherited approval");
                Field<HashSet<IntPtr>>(main,"approved").Remove(handles[0]);
            });
            Check("Window destruction invalidates approval", () => {
                Field<Dictionary<IntPtr,int>>(main, "windows")[handles[1]] = child.Id;
                Field<HashSet<IntPtr>>(main, "approved").Add(handles[1]);
                child.StandardInput.WriteLine("closeOwned"); child.StandardInput.Flush(); Pump(500);
                Call(main, "DrainDestroyedWindows");
                Assert(!Field<HashSet<IntPtr>>(main, "approved").Contains(handles[1]), "Destroyed HWND remained approved");
            });
            config.LockedNames.Add(child.ProcessName); main.Activate();
            Call(main, "Scan"); Pump(300);
            Check("Grouped locked status ignores background-only Rule set rows", () => {
                background = Process.Start(new ProcessStartInfo(args[0], "--background") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardInput=true })!;
                Assert(background.StandardOutput.ReadLine()=="READY", "Background fixture failed");
                Call(main,"RefreshApps",false);
                var list=(System.Windows.Controls.ListView)main.FindName("Apps");
                var row=((List<AppEntry>)list.ItemsSource).Single(e=>e.Name.Equals(child.ProcessName,StringComparison.OrdinalIgnoreCase));
                Assert(row.State=="Locked" && row.ProcessIds.Length==2, "Browser helper diluted grouped lock state");
                list.SelectedItem=row;
                Call(main,"Unlock_Click",main,new RoutedEventArgs());
                Assert(((System.Windows.Controls.TextBlock)main.FindName("Notice")).Text.StartsWith("Multiple windows"), "Grouped Unlock selected an arbitrary window");
            });
            Check("Hidden-owner prompt accepts typing immediately and approves only its window", () => {
                main.Hide();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                timer.Tick += (_, _) => {
                    var dialog = app.Windows.OfType<PasswordWindow>().FirstOrDefault();
                    if (dialog == null) return;
                    timer.Stop();
                    var secret=(System.Windows.Controls.PasswordBox)dialog.FindName("Secret");
                    Assert(dialog.Owner==null && dialog.IsActive && secret.IsKeyboardFocused, "Hidden-owner prompt lacks initial keyboard focus");
                    Assert(GetForegroundWindow()==new WindowInteropHelper(dialog).Handle,"Dialog is not the native foreground window (actual="+GetForegroundWindow()+", expected="+new WindowInteropHelper(dialog).Handle+", session="+Process.GetCurrentProcess().SessionId+")");
                    TypeText("test-app-password");
                    Assert(secret.Password=="test-app-password", "Typing did not reach password box (received " + secret.Password.Length + " characters)");
                    Call(dialog, "Submit_Click", dialog, new RoutedEventArgs());
                };
                timer.Start(); Call(main, "Prompt", handles[0], child.Id, child.ProcessName); timer.Stop();
                Assert(Field<HashSet<IntPtr>>(main, "approved").Contains(handles[0]), "Requested window not approved");
                Assert(!Field<HashSet<IntPtr>>(main, "approved").Contains(handles[2]), "Other window inherited approval");
                Call(main,"ShowFromTray");
            });
            Check("Changing tab-like window titles retains the same window approval", () => {
                child.StandardInput.WriteLine("title:Second tab - Browser fixture"); child.StandardInput.Flush(); Pump(150); Call(main,"Scan");
                Assert(Field<HashSet<IntPtr>>(main,"approved").Contains(handles[0]),"Changing the title revoked window approval");
                Assert(!child.HasExited,"Target process was closed");
            });
            Check("Minimized-owner prompt accepts typing without a click", () => {
                main.WindowState=WindowState.Minimized;
                var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(300) };
                timer.Tick+=(_,_)=> {
                    var dialog=app.Windows.OfType<PasswordWindow>().FirstOrDefault(); if(dialog==null)return; timer.Stop();
                    var secret=(System.Windows.Controls.PasswordBox)dialog.FindName("Secret");
                    Assert(dialog.Owner==null && dialog.IsActive && secret.IsKeyboardFocused,"Minimized-owner prompt lacks keyboard focus");
                    Assert(GetForegroundWindow()==new WindowInteropHelper(dialog).Handle,"Dialog is not the native foreground window (actual="+GetForegroundWindow()+", expected="+new WindowInteropHelper(dialog).Handle+", session="+Process.GetCurrentProcess().SessionId+")");
                    TypeText("test-app-password");
                    Assert(secret.Password=="test-app-password","Keystrokes missed minimized-owner prompt");
                    Call(dialog,"Submit_Click",dialog,new RoutedEventArgs());
                };
                timer.Start(); Call(main,"Prompt",handles[2],child.Id,child.ProcessName); timer.Stop();
                Assert(Field<HashSet<IntPtr>>(main,"approved").Contains(handles[2]),"Second target did not unlock");
                Call(main,"ShowFromTray");
            });
            Check("Asynchronous restore retains approval until target responds", () => {
                Call(main, "Minimize", handles[0]); Pump(300);
                Field<HashSet<IntPtr>>(main, "approved").Add(handles[0]);
                Field<Dictionary<IntPtr,DateTime>>(main, "grace")[handles[0]] = DateTime.UtcNow.AddSeconds(2);
                Call(main, "Restore", handles[0]); Call(main, "Scan"); Pump(300); Call(main, "Scan");
                Assert(Field<HashSet<IntPtr>>(main, "approved").Contains(handles[0]), "Approval lost during restore");
            });
            Check("Hung target cannot block restore or dispatcher", () => {
                child.StandardInput.WriteLine("hang"); child.StandardInput.Flush(); Pump(300);
                var watch = Stopwatch.StartNew(); Call(main, "Restore", handles[0]);
                Assert(watch.ElapsedMilliseconds < 1000, "Restore blocked on target");
                Call(main, "Scan"); Pump(100); Assert(watch.ElapsedMilliseconds < 1500, "Dispatcher stalled");
            });
            Console.WriteLine($"{passed} Windows integration groups passed. No real settings were changed.");
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally {
            if (background != null) { if (!background.HasExited) background.Kill(); background.Dispose(); }
            if (child != null) { if (!child.HasExited) child.Kill(); child.Dispose(); }
            if (main != null) main.QuitSecurely();
        }
    }
}
