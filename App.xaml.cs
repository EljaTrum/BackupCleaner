using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BackupCleaner.Services;

namespace BackupCleaner
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            LogService.Info("Applicatie gestart");
            LogService.CleanOldLogs();

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogService.Error("Onverwachte UI-thread fout", e.Exception);
            MessageBox.Show(
                $"Er is een onverwachte fout opgetreden:\n\n{e.Exception.Message}\n\nDetails zijn opgeslagen in het logbestand.",
                "Fout",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogService.Error("Onverwachte achtergrond-fout (fataal)", ex);
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogService.Error("Onafgevangen Task-fout", e.Exception);
            e.SetObserved();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            LogService.Info("Applicatie afgesloten");
            base.OnExit(e);
        }
    }
}
