using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using AlKatta.ViewModels;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace AlKatta;

public partial class MainWindow : Window
{
    private readonly Forms.NotifyIcon _tray;
    private bool _reallyExit;
    private bool _balloonShown;

    public MainWindow()
    {
        InitializeComponent();
        TrySetWindowIcon();

        // --- system tray ---
        _tray = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Visible = true,
            Text = "القاطع — AL KATTA"
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("إظهار", null, (_, _) => ShowFromTray());
        menu.Items.Add("خروج", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowFromTray();

        StateChanged += OnStateChanged;
        Closing += OnWindowClosing;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized) HideToTray();
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_reallyExit) { Cleanup(); return; }
        e.Cancel = true;          // X minimizes to tray instead of quitting
        HideToTray();
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        if (!_balloonShown)
        {
            _tray.ShowBalloonTip(2000, "القاطع — AL KATTA",
                "البرنامج شغّال بالخلفية. دبل-كلك على الأيقونة للرجوع.",
                Forms.ToolTipIcon.Info);
            _balloonShown = true;
        }
    }

    private void ShowFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        _reallyExit = true;
        Close();
    }

    private void Cleanup()
    {
        try { (DataContext as MainViewModel)?.OnClosing(); } catch { }
        try { _tray.Visible = false; _tray.Dispose(); } catch { }
    }

    private void TrySetWindowIcon()
    {
        try
        {
            var p = Path.Combine(AppContext.BaseDirectory, "logo.ico");
            if (File.Exists(p))
                Icon = BitmapFrame.Create(new Uri(p), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
        catch { /* fall back to default window icon */ }
    }

    private static Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var p = Path.Combine(AppContext.BaseDirectory, "logo.ico");
            if (File.Exists(p)) return new Drawing.Icon(p);
        }
        catch { }
        return Drawing.SystemIcons.Application;
    }
}
