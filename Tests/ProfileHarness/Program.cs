using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using FreeAppLock;
using FreeAppLock.Views;
class Program
{
    [STAThread]static void Main(string[] args)
    {
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        var xml=System.Xml.Linq.XDocument.Load(args[0]);var content=xml.Root!.Elements().Single().Elements();
        app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">"+string.Concat(content.Select(x=>x.ToString()))+"</ResourceDictionary>");
        var main=new MainWindow();var config=new LockSettings();SettingsStore.SetPassword(config,"fixture-admin");SettingsStore.SetAppPassword(config,"fixture-app");
        typeof(MainWindow).GetProperty("Config")!.SetValue(main,config);
        string normalPipe=(string)typeof(MainWindow).Assembly.GetType("FreeAppLock.NativeWire")!.GetProperty("PipeName",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        typeof(MainWindow).GetProperty("ProfileBridgePipeName",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(main,normalPipe+".IntegrationTest");main.Left=-2500;main.Top=-2500;
        bool approve=true;int prompts=0;
        var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(100)};
        timer.Tick+=(_,_)=>{
            var dialog=app.Windows.OfType<PasswordWindow>().FirstOrDefault(w=>w.IsVisible);
            if(dialog!=null&&approve){prompts++;((System.Windows.Controls.PasswordBox)dialog.FindName("Secret")).Password="fixture-app";typeof(PasswordWindow).GetMethod("Submit_Click",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(dialog,new object[]{dialog,new RoutedEventArgs()});}
        };timer.Start();
        main.Loaded+=(_,_)=>{Console.WriteLine("READY");Console.Out.Flush();};
        _=Task.Run(async()=>{
            while(await Console.In.ReadLineAsync() is {} line)
            {
                await app.Dispatcher.InvokeAsync(()=>{
                    try
                    {
                        using var data=JsonDocument.Parse(line);var root=data.RootElement;string command=root.GetProperty("command").GetString()!;
                        if(command=="protect")config.BrowserProfiles[root.GetProperty("id").GetString()!]=new BrowserProfileRule {Name="Work fixture",Browser="brave",Timing=new LockRule {AwaySeconds=null,MaximumUnlockedSeconds=10,LockOnMinimize=true}};
                        if(command=="auto")approve=root.GetProperty("value").GetBoolean();
                        if(command=="cancel")foreach(var dialog in app.Windows.OfType<PasswordWindow>().Where(w=>w.IsVisible).ToArray())dialog.DialogResult=false;
                        Console.WriteLine(JsonSerializer.Serialize(new{ok=true,prompts,approve,profiles=config.BrowserProfiles.Keys.ToArray()}));Console.Out.Flush();
                    }
                    catch(Exception ex){Console.WriteLine(JsonSerializer.Serialize(new{ok=false,error=ex.GetType().Name}));Console.Out.Flush();}
                });
            }
        });
        main.Show();app.Run();
    }
}
