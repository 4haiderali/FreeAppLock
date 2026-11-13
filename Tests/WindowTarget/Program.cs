using System.Windows.Forms;
if (args.Contains("--background")) { Console.WriteLine("READY"); Console.Out.Flush(); Console.ReadLine(); return; }
ApplicationConfiguration.Initialize();
var main = new Form { Text = "AppLock disposable integration target", Width = 300, Height = 180, StartPosition = FormStartPosition.Manual, Left = -2000, Top = -2000 };
var owned = new Form { Text = "AppLock owned dialog", Width = 200, Height = 120, StartPosition = FormStartPosition.Manual, Left = -1700, Top = -2000 };
var second = new Form { Text = "Independent protected window", Width = 250, Height = 150, StartPosition = FormStartPosition.Manual, Left = -2200, Top = -2000 };
main.Shown += (_, _) => { owned.Show(main); second.Show(); Console.WriteLine($"{main.Handle.ToInt64()},{owned.Handle.ToInt64()},{second.Handle.ToInt64()}"); Console.Out.Flush(); };
new Thread(() => { while (Console.ReadLine() is { } line) { if (line.StartsWith("title:")) main.BeginInvoke(new Action(() => main.Text=line.Substring(6))); if (line == "hang") main.BeginInvoke(new Action(() => Thread.Sleep(Timeout.Infinite))); if (line == "closeOwned") main.BeginInvoke(new Action(() => owned.Close())); } }) { IsBackground = true }.Start();
Application.Run(main);
