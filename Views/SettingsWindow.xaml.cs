using System.Windows;
using System.Windows.Input;
namespace FreeAppLock.Views;
public partial class SettingsWindow : Window
{
    readonly MainWindow main;
    bool loading = true;
    public SettingsWindow(MainWindow owner)
    { InitializeComponent(); Title = App.DisplayName + " — Settings"; Owner = owner; main = owner; Monitoring.IsChecked = main.Config.Monitoring; AutoStart.IsChecked = main.IsAutoStart(); Delay.Text = (main.Config.AutoLockDelaySeconds / 60).ToString(); TerminateOnFailures.IsChecked = main.Config.TerminateOnAppLockout; loading = false; }
    void Monitoring_Click(object sender, RoutedEventArgs e) { if (loading) return; if (!main.VerifyAdmin("Change monitoring settings")) { Monitoring.IsChecked = main.Config.Monitoring; return; } bool requested = Monitoring.IsChecked == true; if (!main.TryUpdateConfig(c => c.Monitoring = requested)) { Monitoring.IsChecked = main.Config.Monitoring; Status.Text = "Monitoring unchanged."; return; } main.MonitorChanged(); Status.Text = "Monitoring updated."; }
    void AutoStart_Click(object sender, RoutedEventArgs e) { if (loading) return; if (!main.VerifyAdmin("Change Windows startup settings")) { AutoStart.IsChecked = main.IsAutoStart(); return; } try { main.SetAutoStart(AutoStart.IsChecked == true); Status.Text = "Startup preference updated."; } catch (Exception ex) { AutoStart.IsChecked = main.IsAutoStart(); Status.Text = ex.Message; } }
    void Delay_PreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = !e.Text.All(char.IsDigit);
    void Delay_Click(object sender, RoutedEventArgs e) { if (!int.TryParse(Delay.Text, out int value) || value is < 0 or > 1440) { Status.Text = "Enter 0–1440 minutes."; return; } if (value * 60 == main.Config.AutoLockDelaySeconds) return; if (!main.VerifyAdmin("Change relock delay")) return; if (!main.TryUpdateConfig(c => c.AutoLockDelaySeconds = value * 60)) { Delay.Text = (main.Config.AutoLockDelaySeconds / 60).ToString(); Status.Text = "Relock delay unchanged."; return; } Status.Text = "Default away delay saved."; }
    void TerminateOnFailures_Click(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        bool requested = TerminateOnFailures.IsChecked == true;
        if (!main.VerifyAdmin("change the behavior after five incorrect app unlock attempts"))
        {
            TerminateOnFailures.IsChecked = main.Config.TerminateOnAppLockout;
            return;
        }
        if (!main.TryUpdateConfig(c => c.TerminateOnAppLockout = requested))
        {
            TerminateOnFailures.IsChecked = main.Config.TerminateOnAppLockout;
            Status.Text = "Failed-attempt behavior unchanged.";
            return;
        }
        Status.Text = requested
            ? "Process termination enabled for other apps. Browsers always keep running; other apps may lose unsaved work."
            : "Safe behavior enabled. Five wrong attempts keep the app locked for 60 seconds.";
    }
    void Admin_Click(object sender, RoutedEventArgs e) => main.ChangeAdmin();
    void App_Click(object sender, RoutedEventArgs e) => main.ChangeAppPassword();
    void History_Click(object sender, RoutedEventArgs e) => main.ShowHistory();
    void Quit_Click(object sender, RoutedEventArgs e) { if (main.VerifyAdmin("Quit FreeAppLock and stop monitoring")) main.QuitSecurely(); }
}
