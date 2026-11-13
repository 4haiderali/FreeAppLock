using System.Windows;
using System.Reflection;
namespace FreeAppLock.Views;
public partial class PasswordWindow : Window
{
    readonly Func<string, bool>? verifier;
    readonly Action<int>? wrong;
    readonly bool confirm;
    readonly bool isAdminVerification;
    int attempts;
    bool revealing;
    bool initialFocusPending = true;
    static readonly MethodInfo? PlacePasswordCaret = typeof(System.Windows.Controls.PasswordBox)
        .GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(int) }, null);
    public string Value => revealing ? VisibleSecret.Text : Secret.Password;
    public bool LockedOut { get; private set; }
    public PasswordWindow(string title, string message, Func<string,bool>? verify = null, Action<int>? onWrong = null, int previousAttempts = 0, bool confirmation = false)
    {
        InitializeComponent(); Title = title + " — " + App.DisplayName; Heading.Text = title; Description.Text = message;
        verifier = verify; wrong = onWrong; attempts = previousAttempts; confirm = confirmation;
        isAdminVerification = title == "Admin verification";
        ConfirmPanel.Visibility = confirmation ? Visibility.Visible : Visibility.Collapsed;
        InputLabel.Text = confirmation ? "New password" : "Password";
        Submit.Content = confirmation ? "Save password" : "Unlock";
        ShowActivated = true;
        Loaded += (_, _) => System.Windows.Input.FocusManager.SetFocusedElement(this, Secret);
        var settleFocus = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(125) };
        settleFocus.Tick += (_, _) =>
        {
            settleFocus.Stop();
            // Let a queued minimize/restore complete before one final startup handoff.
            // Never reset the caret after typing or selecting the confirmation field.
            if (Secret.Password.Length == 0 && VisibleSecret.Text.Length == 0 && !ConfirmSecret.IsKeyboardFocused)
                QueueInitialFocus(activate: true);
        };
        ContentRendered += (_, _) => { QueueInitialFocus(activate: true); settleFocus.Start(); };
        Closed += (_, _) => settleFocus.Stop();
        Activated += (_, _) => { if (initialFocusPending) QueueInitialFocus(activate: false); };
    }
    void QueueInitialFocus(bool activate)
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            if (!IsVisible || !initialFocusPending && !activate) return;
            if (activate)
            {
                Activate();
                NativeMethods.ActivatePasswordPrompt(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            }
            var input = revealing ? (System.Windows.IInputElement)VisibleSecret : Secret;
            System.Windows.Input.FocusManager.SetFocusedElement(this, input);
            System.Windows.Input.Keyboard.Focus(input);
            initialFocusPending = !IsActive || NativeMethods.GetForegroundWindow() != new System.Windows.Interop.WindowInteropHelper(this).Handle
                || !(revealing ? VisibleSecret.IsKeyboardFocused : Secret.IsKeyboardFocused);
        }));
    }
    // Do not mirror a masked password into a hidden plain TextBox on each keystroke.
    // A plain TextBox still contains the password while it is explicitly revealed.
    void Show_Click(object sender, RoutedEventArgs e)
    {
        revealing = !revealing;
        RevealButton.Content = revealing ? "\uED1A" : "\uE7B3";
        RevealButton.ToolTip = revealing ? "Hide password" : "Show password";
        if (revealing)
        {
            VisibleSecret.Text = Secret.Password;
            Secret.Visibility = Visibility.Collapsed;
            VisibleSecret.Visibility = Visibility.Visible;
            VisibleSecret.CaretIndex = VisibleSecret.Text.Length;
            VisibleSecret.Focus();
        }
        else
        {
            Secret.Password = VisibleSecret.Text;
            VisibleSecret.Text = "";
            VisibleSecret.Visibility = Visibility.Collapsed;
            Secret.Visibility = Visibility.Visible;
            Secret.Focus();
            // PasswordBox.Password resets its caret to the beginning. Keep typing at
            // the end after the user hides the revealed password again.
            try { PlacePasswordCaret?.Invoke(Secret, new object[] { Secret.Password.Length, 0 }); }
            catch (Exception) { Feedback.Text = "Please check the password before continuing."; }
        }
    }
    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    void Submit_Click(object sender, RoutedEventArgs e)
    {
        var value = Value;
        if (value.Length == 0) { Feedback.Text = "Enter a password."; return; }
        if (confirm && value != ConfirmSecret.Password) { Feedback.Text = "Passwords do not match."; return; }
        if (verifier != null && !verifier(value))
        {
            attempts++; wrong?.Invoke(attempts);
            if (attempts >= 5 && wrong != null) { LockedOut = true; DialogResult = false; return; }
            Feedback.Text = isAdminVerification
                ? $"Incorrect admin password · {attempts}/5 before a cooldown."
                : $"Incorrect password · {attempts}/5 attempts. Try again or Cancel.";
            Secret.Clear(); VisibleSecret.Clear(); if (revealing) VisibleSecret.Focus(); else Secret.Focus(); Activate(); return;
        }
        DialogResult = true;
    }
}
