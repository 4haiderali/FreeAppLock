using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Interop;
namespace FreeAppLock;
public partial class App : System.Windows.Application
{
    public static string DisplayVersion => typeof(App).Assembly.GetName().Version!.ToString(3);
    public static string DisplayName => "FreeAppLock v" + DisplayVersion;
    public static string WindowTitle => DisplayName + " — Privacy Monitor";
    internal static bool StartInTray { get; private set; }
    private System.Threading.Mutex? mutex;
    private System.Threading.EventWaitHandle? activation;
    private System.Threading.RegisteredWaitHandle? activationWait;
    private const string ActivationName = "FreeAppLock.Activate.CurrentUser";
    protected override void OnStartup(StartupEventArgs e)
    {
        StartInTray = StartupRegistration.StartsInTray(e.Args);
        try { StartupRegistration.ReconcileInstalledStartup(); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException) { /* Existing registration is retained if Windows denies cleanup. */ }
        if (StartInTray && !StartupRegistration.IsEnabled()) { Shutdown(); return; }
        mutex = new System.Threading.Mutex(true, "FreeAppLock.SingleInstance.CurrentUser", out bool first);
        if (!first)
        {
            if (!StartInTray)
            {
                try { using var existing = System.Threading.EventWaitHandle.OpenExisting(ActivationName); existing.Set(); }
                catch (System.Threading.WaitHandleCannotBeOpenedException) { System.Windows.MessageBox.Show("FreeAppLock is already running. Open it from the tray icon."); }
                catch (UnauthorizedAccessException) { System.Windows.MessageBox.Show("FreeAppLock is already running in another security context. Open it from its tray icon."); }
            }
            Shutdown(); return;
        }
        activation = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, ActivationName);
        activationWait = System.Threading.ThreadPool.RegisterWaitForSingleObject(activation, (_, _) =>
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (MainWindow is MainWindow window) window.ShowFromTray();
            })), null, System.Threading.Timeout.Infinite, false);
        base.OnStartup(e);
        // Preserve the native Windows caption/buttons while requesting immersive dark chrome.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => { if (sender is Window w) TryDarkCaption(w); }));
    }
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    private static void TryDarkCaption(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int enabled = 1;
            if (DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref enabled, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
    protected override void OnExit(ExitEventArgs e) { activationWait?.Unregister(null); activation?.Dispose(); if (mutex != null) { try { mutex.ReleaseMutex(); } catch { } mutex.Dispose(); } base.OnExit(e); }
}
