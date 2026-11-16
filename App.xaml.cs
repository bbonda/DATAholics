using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
namespace AccurateHireIQ;
public partial class App : Application
{
 protected override async void OnStartup(StartupEventArgs e)
 {
  base.OnStartup(e);
  try {
   var root = Environment.GetEnvironmentVariable("ACCURATEHIRE_DATA") ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AccurateHireIQ");
   System.IO.Directory.CreateDirectory(root);
   Log.Logger = new LoggerConfiguration().WriteTo.File(System.IO.Path.Combine(root,"logs","app-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14).CreateLogger();
   var services = new ServiceCollection().AddSingleton(new Store(root)).AddSingleton<ImportService>().AddSingleton<AgentService>().AddSingleton<MainViewModel>().AddSingleton<MainWindow>().BuildServiceProvider();
   services.GetRequiredService<Store>().Initialize();
   await Task.Run(()=>services.GetRequiredService<ImportService>().UpgradeExisting());
   if(e.Args.Contains("--self-test")){ await SelfTest.Run(services); Shutdown(); return; }
   var window=services.GetRequiredService<MainWindow>(); MainWindow=window; window.Show();
  } catch(Exception ex){ Log.Error(ex,"Startup failed"); if(!e.Args.Contains("--self-test"))MessageBox.Show(ex.ToString(),"AccurateHire IQ startup error"); Shutdown(1); }
 }
}
