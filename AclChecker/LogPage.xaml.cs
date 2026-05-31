using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AclChecker;

public sealed partial class LogPage : Page
{
    public LogPage()
    {
        this.InitializeComponent();
        this.Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        RefreshLog();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        RefreshLog();
    }

    private void RefreshLog()
    {
        var logs = DataService.LoadAuditLog();
        
        if (logs.Count == 0)
        {
            LogBox.Text = "暂无操作记录";
            return;
        }

        var lines = new System.Collections.Generic.List<string>();
        
        // 按时间倒序显示
        foreach (var log in logs.OrderByDescending(l => l.Timestamp))
        {
            var status = log.Success ? "✓" : "✗";
            var actionText = log.Action switch
            {
                "Modify" => "修改权限",
                "Reset" => "重置默认",
                _ => log.Action
            };
            
            lines.Add($"[{log.Timestamp:HH:mm:ss}] {status} {actionText}");
            lines.Add($"    路径: {log.TargetPath}");
            lines.Add($"    修改前: {log.BeforeState}");
            lines.Add($"    修改后: {log.AfterState}");
            
            if (!string.IsNullOrEmpty(log.ErrorMessage))
                lines.Add($"    错误: {log.ErrorMessage}");
            
            lines.Add("");
        }

        LogBox.Text = string.Join("\n", lines);
    }

    public void AppendLog(string message)
    {
        if (LogBox != null)
        {
            var time = System.DateTime.Now.ToString("HH:mm:ss");
            LogBox.Text += $"[{time}] {message}{System.Environment.NewLine}";
        }
    }
}
