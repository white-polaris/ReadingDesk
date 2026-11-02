using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using Microsoft.Win32;

namespace DDLDesk;

public partial class App : Application
{
    private Mutex? instance;
    private EventWaitHandle? wake;
    private RegisteredWaitHandle? wakeRegistration;
    public static bool SuppressStartup { get; private set; }
    public static string DataDirectory { get; private set; } = "";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        SuppressStartup = e.Args.Contains("--no-startup");
        int dirArg = Array.IndexOf(e.Args, "--data-dir");
        DataDirectory = dirArg >= 0 && dirArg + 1 < e.Args.Length ? Path.GetFullPath(e.Args[dirArg + 1])
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReadingDesk", "Data");
        string key = "Local\\ReadingDesk-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DataDirectory.ToUpperInvariant())))[..16];
        instance = new Mutex(true, key, out bool first);
        wake = new EventWaitHandle(false, EventResetMode.AutoReset, key + "-show");
        if (!first) { wake.Set(); Shutdown(); return; }
        try
        {
            var store = new StateStore(DataDirectory);
            var state = store.Load();
            var main = new MainWindow(state, store);
            MainWindow = main;
            wakeRegistration = ThreadPool.RegisterWaitForSingleObject(wake, (_, _) => Dispatcher.BeginInvoke(main.Reveal), null, Timeout.Infinite, false);
            main.Show();
            if (store.RecoveryNotice != null) MessageBox.Show(main, store.RecoveryNotice, "记录恢复", MessageBoxButton.OK, MessageBoxImage.Information);
            if (!SuppressStartup)
            {
                try { StartupIntegration.SetEnabled(state.AutoStart); }
                catch (Exception ex) { MessageBox.Show(main, "开机启动设置失败，可在设置中重试。\n" + ex.Message, "DDLDesk"); }
            }
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "DDLDesk 无法启动", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        wakeRegistration?.Unregister(null);
        wake?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }
}

internal static class StartupIntegration
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static void SetEnabled(bool enabled)
    {
        if (App.SuppressStartup) return;
        using var key = Registry.CurrentUser.CreateSubKey(Key);
        if (enabled)
        {
            var path = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定程序路径");
            key.SetValue("ReadingDesk", "\"" + path + "\"");
        }
        else key.DeleteValue("ReadingDesk", false);
    }
}
