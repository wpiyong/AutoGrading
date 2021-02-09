using AutoGrading.ViewModel;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using AutoGrading.Model;
using AutoGrading.View;
using System.Threading;

namespace AutoGrading
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static LogEntryViewModel LogEntry;

        public static AutoGradingSettings Settings = new AutoGradingSettings();

        MainWindowViewModel viewModel = null;

        private static Mutex _mutex = null;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "AutoDiamondGrading";

            bool createdNew;
            _mutex = new Mutex(true, appName, out createdNew);
            if (!createdNew)
            {
                //app is already running! Exiting the application
                MessageBox.Show("Application is already running!", "Warning");
                Application.Current.Shutdown();
            }
            else
            {
                base.OnStartup(e);
            }
        }


        private void Application_Startup(object sender, StartupEventArgs e)
        {

            if (!Settings.Load())
            {
                MessageBox.Show("Could not load settinsg file", "Cannot start");
                App.Current.Shutdown();
                return;
            }

            LogEntry = new LogEntryViewModel();

            var window = new MainWindow();
            viewModel = new MainWindowViewModel();
            window.DataContext = viewModel;
            window.Loaded += viewModel.OnWindowLoaded;
            window.Closing += viewModel.OnWindowClosing;

            window.Show();
        }

        private void Application_DispatcherUnhandledException(object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            System.Windows.MessageBox.Show(e.Exception.ToString(), "Unhandled exception, shutting down");
            Application.Current.Shutdown();
        }
    }
}
