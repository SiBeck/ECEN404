// App.xaml.cs - Application Entry Point
using FellowOakDicom;
using FellowOakDicom.Imaging.NativeCodec;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace _403DesktopApp
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);


            // Set shutdown mode to explicit - app won't close until we call Shutdown()
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Show login window first
            LoginWindow loginWindow = new LoginWindow();
            bool? loginResult = loginWindow.ShowDialog();

            if (loginResult == true)
            {
                // Login successful, show main window
                // Change shutdown mode so app closes when main window closes
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                MainWindow mainWindow = new MainWindow();
                mainWindow.Show();
            }
            else
            {
                // Login failed or cancelled, exit application
                Shutdown();
            }
        }
    }
}