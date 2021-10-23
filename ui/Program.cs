using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Logging;
using Avalonia.ReactiveUI;
using Splat;

namespace tb_ui
{
    class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            const string logFileName = "log.txt";
            if (File.Exists(logFileName)) File.Delete(logFileName);
            Trace.Listeners.Add(new TextWriterTraceListener(logFileName, "myListener"));
            try
            {
                Bootstrapper.Register(Locator.CurrentMutable, Locator.Current);
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                Logger.Sink.Log(LogEventLevel.Error, "track_books", ex, ex.ToString());
            }
            finally
            {
                Trace.Flush();
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace(Avalonia.Logging.LogEventLevel.Information)
                .UseReactiveUI();
    }
}
