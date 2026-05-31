using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT;

namespace AclChecker;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        App.Log("MainWindow() START");
        this.InitializeComponent();
        App.Log("MainWindow InitializeComponent DONE");
        this.Title = "ACL 权限排查修复工具";
        this.AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 720));

        // ─── 启用 Mica 背景并延伸到标题栏 ───────────────────
        TrySetMicaBackdrop();
        SetupTitleBar();

        App.Log("MainWindow() DONE");

        // 默认显示扫描页
        ContentFrame.Navigate(typeof(ScanPage));
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private void TrySetMicaBackdrop()
    {
        try
        {
            this.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
            App.Log("Mica backdrop set OK");
        }
        catch (Exception ex)
        {
            App.Log($"Mica backdrop failed: {ex.Message}");
        }
    }

    private void SetupTitleBar()
    {
        var appWindow = this.AppWindow;

        // 延伸内容到标题栏
        appWindow.TitleBar.ExtendsContentIntoTitleBar = true;

        // 将自定义标题栏设为拖拽区域
        appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        this.SetTitleBar(AppTitleBar);

        // 设置标题栏按钮背景为透明，让 Mica 透出来
        appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
        appWindow.TitleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF);
        appWindow.TitleBar.ButtonForegroundColor = Colors.White;

        // 设置窗口圆角 + 标题栏 Mica
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(uint));

            // 让系统标题栏也使用 Mica 背景
            var backdropType = DWM_SYSTEMBACKDROP_TYPE.DWMSBT_MAINWINDOW;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(uint));
        }
        catch (Exception ex)
        {
            App.Log($"DWM setup failed: {ex.Message}");
        }

        App.Log("TitleBar setup OK");
    }

    // ─── Win32 DWM API ──────────────────────────────────────

    private const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const uint DWMWA_SYSTEMBACKDROP_TYPE = 38;

    private enum DWM_WINDOW_CORNER_PREFERENCE
    {
        DWMWCP_DEFAULT = 0,
        DWMWCP_DONOTROUND = 1,
        DWMWCP_ROUND = 2,
        DWMWCP_ROUNDSMALL = 3
    }

    private enum DWM_SYSTEMBACKDROP_TYPE
    {
        DWMSBT_DISABLE = 0,
        DWMSBT_MAINWINDOW = 2,  // Mica
        DWMSBT_TABBEDWINDOW = 4, // Mica Alt
        DWMSBT_TRANSIENTWINDOW = 3 // Acrylic
    }

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void DwmSetWindowAttribute(
        IntPtr hwnd, uint attribute, ref DWM_WINDOW_CORNER_PREFERENCE value, uint size);

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void DwmSetWindowAttribute(
        IntPtr hwnd, uint attribute, ref DWM_SYSTEMBACKDROP_TYPE value, uint size);

    // ─── 导航 ──────────────────────────────────────────────

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            // 设置按钮点击 - 显示关于页面
            ContentFrame.Navigate(typeof(AboutPage));
            return;
        }

        if (args.SelectedItemContainer is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString();
            switch (tag)
            {
                case "scan":
                    ContentFrame.Navigate(typeof(ScanPage));
                    break;
                case "log":
                    ContentFrame.Navigate(typeof(LogPage));
                    break;
            }
        }
    }

    public static void Log(string message)
    {
        App.Log(message);
    }
}
