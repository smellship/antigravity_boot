using System;
using System.Threading;
using System.Windows.Threading;

namespace AntigravityBootOverlay
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            Native.InitDpiAwareness();

            bool isSelfTest = Array.IndexOf(args, "--self-test") >= 0;
            bool isTitleTest = Array.IndexOf(args, "--target-title") >= 0;
            if (!isSelfTest && !isTitleTest)
            {
                GpuPref.Ensure();
            }

            Mutex mutex = null;
            if (!isSelfTest && !isTitleTest)
            {
                bool created;
                mutex = new Mutex(true, @"Local\io.antigravity.boot-studio.overlay", out created);
                if (!created)
                {
                    return 0;
                }
            }

            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Log.W("未捕获异常：" + e.ExceptionObject);
            };

            var app = new TrayApp(args);
            app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
            {
                Log.W("UI 调度异常：" + e.Exception);
                e.Handled = true;
            };

            int code = app.Run();
            GC.KeepAlive(mutex);
            return code;
        }
    }
}
