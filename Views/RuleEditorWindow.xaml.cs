using System.Globalization;
using System.Windows;
namespace FreeAppLock.Views;
public partial class RuleEditorWindow : Window
{
    readonly bool profile;
    public LockRule Result { get; private set; }=new();
    public string ProfileLabel => ProfileName.Text.Trim();
    public bool ProfileOnlyMode => profile && ProfileOnly.IsChecked==true;
    public RuleEditorWindow(string name,LockRule rule,bool browserProfile=false)
    {
        InitializeComponent();Title=App.DisplayName+" — Protection rule";profile=browserProfile;
        Heading.Text=browserProfile?"Browser profile rule":"Application rule";
        Scope.Text=browserProfile?"Applies to each regular window in this registered profile. Tabs stay together.":name+" — applies to each application window.";
        ProfileName.Text=name;ProfileName.Visibility=NameLabel.Visibility=browserProfile?Visibility.Visible:Visibility.Collapsed;
        ProfileOnly.Visibility=ProfileOnlyHelp.Visibility=browserProfile?Visibility.Visible:Visibility.Collapsed;ProfileOnly.IsChecked=true;
        AwayEnabled.IsChecked=rule.AwaySeconds!=null;AwayMinutes.Text=((rule.AwaySeconds??0)/60.0).ToString("0.###",CultureInfo.InvariantCulture);
        MaximumEnabled.IsChecked=rule.MaximumUnlockedSeconds!=null;MaximumMinutes.Text=((rule.MaximumUnlockedSeconds??1800)/60.0).ToString("0.###",CultureInfo.InvariantCulture);
        MinimizeLocks.IsChecked=rule.LockOnMinimize;
    }
    static bool Minutes(string text,bool allowZero,out int seconds)
    {
        seconds=0;if(!decimal.TryParse(text,NumberStyles.Number,CultureInfo.InvariantCulture,out var minutes) || minutes<0 || minutes>1440)return false;
        seconds=(int)Math.Round(minutes*60,MidpointRounding.AwayFromZero);return allowZero || seconds>0;
    }
    void Cancel_Click(object sender,RoutedEventArgs e)=>DialogResult=false;
    void Save_Click(object sender,RoutedEventArgs e)
    {
        if(profile&&(string.IsNullOrWhiteSpace(ProfileLabel)||ProfileLabel.Any(char.IsControl))){Feedback.Text="Enter a profile name.";return;}
        int away=0,maximum=0;
        if(AwayEnabled.IsChecked==true&&!Minutes(AwayMinutes.Text,true,out away)){Feedback.Text="Away delay: enter 0–1440 minutes.";return;}
        if(MaximumEnabled.IsChecked==true&&!Minutes(MaximumMinutes.Text,false,out maximum)){Feedback.Text="Session limit: enter a positive duration up to 1440 minutes.";return;}
        Result=new LockRule { AwaySeconds=AwayEnabled.IsChecked==true?away:null,MaximumUnlockedSeconds=MaximumEnabled.IsChecked==true?maximum:null,LockOnMinimize=MinimizeLocks.IsChecked==true };
        DialogResult=true;
    }
}
