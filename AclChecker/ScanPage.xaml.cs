using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using WinRT;

namespace AclChecker;

public sealed partial class ScanPage : Page
{
    private readonly ObservableCollection<AclResultItem> _results = new();
    private Grid? _selectedRow;

    public ScanPage()
    {
        this.InitializeComponent();
        this.NavigationCacheMode = NavigationCacheMode.Required;
    }

    // ─── 拖拽支持 ───────────────────────────────────────────

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "拖放到此处扫描";
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.Count > 0)
            {
                var item = items[0];
                PathBox.Text = item.Path;
                // 自动开始扫描
                OnScan(this, new RoutedEventArgs());
            }
        }
    }

    // ─── 选择目标 ───────────────────────────────────────────

    private async void OnSelectFile(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".exe");
            picker.FileTypeFilter.Add("*");
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
            InitPicker(picker);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
                PathBox.Text = file.Path;
        }
        catch (Exception ex)
        {
            App.Log($"选择文件失败: {ex.Message}");
        }
    }

    private async void OnSelectFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
            InitPicker(picker);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
                PathBox.Text = folder.Path;
        }
        catch (Exception ex)
        {
            App.Log($"选择目录失败: {ex.Message}");
        }
    }

    private void InitPicker(object picker)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(
            App.MainWindow ?? throw new InvalidOperationException("MainWindow not set"));
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
    }

    // ─── 扫描 ──────────────────────────────────────────────

    private async void OnScan(object sender, RoutedEventArgs e)
    {
        var targetPath = PathBox.Text?.Trim();
        if (string.IsNullOrEmpty(targetPath))
        {
            var dialog = new ContentDialog
            {
                Title = "提示",
                Content = "请先选择目标文件或目录",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
            return;
        }

        ScanBtn.IsEnabled = false;
        ScanProgress.Visibility = Visibility.Visible;
        _results.Clear();
        _selectedRow = null;
        DetailText.Text = "";
        ResultCard.Visibility = Visibility.Collapsed;
        DetailCard.Visibility = Visibility.Collapsed;

        App.Log($"开始扫描: {targetPath}");

        var rawItems = new List<AclResultItem>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            await Task.Run(() =>
            {
                var dirs = GetDirectoryChain(targetPath);
                foreach (var dir in dirs)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    try
                    {
                        var item = AnalyzeDirectory(dir);
                        rawItems.Add(item);
                    }
                    catch (Exception ex)
                    {
                        rawItems.Add(new AclResultItem
                        {
                            DirPath = dir,
                            AclDetail = $"分析失败: {ex.Message}"
                        });
                    }
                }
            }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            App.Log("扫描超时 (30秒)");
        }

        // 重新绑定数据源以刷新 UI
        _results.Clear();
        foreach (var item in rawItems)
        {
            _results.Add(item);
        }
        ResultRepeater.ItemsSource = null;
        ResultRepeater.ItemsSource = _results;
        ScanBtn.IsEnabled = true;
        ScanProgress.Visibility = Visibility.Collapsed;
        ResultCard.Visibility = Visibility.Visible;

        // 保存扫描结果
        DataService.SaveLastScan(new LastScanInfo
        {
            TargetPath = targetPath,
            ScanTime = DateTime.Now,
            Results = _results.ToList()
        });

        App.Log($"扫描完成, 共 {_results.Count} 个目录");
    }

    private static List<string> GetDirectoryChain(string targetPath)
    {
        var chain = new List<string>();
        var fullPath = System.IO.Path.GetFullPath(targetPath);

        if (File.Exists(fullPath))
            fullPath = System.IO.Path.GetDirectoryName(fullPath)!;

        var root = System.IO.Path.GetPathRoot(fullPath)!;
        var current = root.TrimEnd(System.IO.Path.DirectorySeparatorChar);

        var parts = fullPath.Substring(root.Length)
            .Split(new[] { System.IO.Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            current = current + System.IO.Path.DirectorySeparatorChar + part;
            if (!chain.Contains(current, StringComparer.OrdinalIgnoreCase))
                chain.Add(current);
        }

        return chain;
    }

    private AclResultItem AnalyzeDirectory(string dirPath)
    {
        var item = new AclResultItem { DirPath = dirPath };

        if (!Directory.Exists(dirPath))
        {
            item.AclDetail = "目录不存在";
            return item;
        }

        var task = Task.Run(() => AnalyzeDirectoryInternal(dirPath));
        if (!task.Wait(TimeSpan.FromSeconds(5)))
        {
            item.AclDetail = "[超时] 权限分析超过 5 秒";
            return item;
        }

        return task.Result;
    }

    private AclResultItem AnalyzeDirectoryInternal(string dirPath)
    {
        var item = new AclResultItem { DirPath = dirPath };
        var aclInfo = GetAclInfo(dirPath);

        item.HasInheritance = aclInfo.HasInheritance;
        item.UsersHasReadExecute = aclInfo.UsersHasReadExecute;
        item.EveryoneHasReadExecute = aclInfo.EveryoneHasReadExecute;
        item.SystemHasFullControl = aclInfo.SystemHasFullControl;
        item.AdminsHasFullControl = aclInfo.AdminsHasFullControl;
        item.AclDetail = aclInfo.RawOutput;

        // 检测安全风险
        DetectSecurityRisks(item, aclInfo, dirPath);

        return item;
    }

    private void DetectSecurityRisks(AclResultItem item, AclInfo aclInfo, string dirPath)
    {
        var risks = new List<string>();

        // 风险1: Everyone 有写入权限
        if (aclInfo.EveryoneHasWrite)
        {
            risks.Add("Everyone 有写入权限");
        }

        // 风险2: Guests 有访问权限
        if (aclInfo.GuestsHasAccess)
        {
            risks.Add("Guests 有访问权限");
        }

        // 风险3: 系统目录继承被禁用
        var systemDirs = new[] { "Program Files", "Program Files (x86)", "Windows", "System32" };
        bool isSystemDir = systemDirs.Any(sd => dirPath.Contains(sd, StringComparison.OrdinalIgnoreCase));
        if (isSystemDir && !aclInfo.HasInheritance)
        {
            risks.Add("系统目录继承被禁用");
        }

        item.HasSecurityRisk = risks.Count > 0;
        item.RiskDescription = string.Join("; ", risks);
    }

    private AclInfo GetAclInfo(string dirPath)
    {
        var info = new AclInfo();
        var lines = new List<string>();

        try
        {
            var security = new DirectoryInfo(dirPath).GetAccessControl();
            var rules = security.GetAccessRules(true, true, typeof(NTAccount));

            info.HasInheritance = (security.AreAccessRulesProtected == false);

            foreach (FileSystemAccessRule rule in rules)
            {
                var identity = rule.IdentityReference.Value;
                var rights = rule.FileSystemRights;
                var inheritanceFlags = rule.InheritanceFlags;
                var isInherited = rule.IsInherited;

                var line = $"{identity}: ";
                if ((rights & FileSystemRights.FullControl) == FileSystemRights.FullControl)
                    line += "F";
                else if ((rights & FileSystemRights.Write) == FileSystemRights.Write)
                    line += "M";
                else if ((rights & (FileSystemRights.ReadAndExecute | FileSystemRights.Read)) != 0)
                    line += "RX";
                else
                    line += rights.ToString();

                if (inheritanceFlags.HasFlag(InheritanceFlags.ObjectInherit))
                    line += " (OI)";
                if (inheritanceFlags.HasFlag(InheritanceFlags.ContainerInherit))
                    line += " (CI)";
                if (!isInherited)
                    line += " (NP)";

                lines.Add(line);

                if (identity.Contains("Users", StringComparison.OrdinalIgnoreCase) ||
                    identity.Contains("Authenticated Users", StringComparison.OrdinalIgnoreCase))
                {
                    if ((rights & (FileSystemRights.ReadAndExecute | FileSystemRights.Read)) != 0)
                        info.UsersHasReadExecute = true;
                }

                if (identity.Contains("Everyone", StringComparison.OrdinalIgnoreCase))
                {
                    if ((rights & (FileSystemRights.ReadAndExecute | FileSystemRights.Read)) != 0)
                        info.EveryoneHasReadExecute = true;
                    // 检测 Everyone 是否有写入权限（安全风险）
                    if ((rights & (FileSystemRights.Write | FileSystemRights.FullControl | FileSystemRights.Modify)) != 0)
                        info.EveryoneHasWrite = true;
                }

                // 检测 Guests 权限
                if (identity.Contains("Guest", StringComparison.OrdinalIgnoreCase) ||
                    identity.Contains("S-1-5-32-546", StringComparison.OrdinalIgnoreCase))
                {
                    if ((rights & (FileSystemRights.ReadAndExecute | FileSystemRights.Read | FileSystemRights.Write)) != 0)
                        info.GuestsHasAccess = true;
                }

                if (identity.Contains("SYSTEM", StringComparison.OrdinalIgnoreCase))
                {
                    if ((rights & FileSystemRights.FullControl) == FileSystemRights.FullControl)
                        info.SystemHasFullControl = true;
                }

                if (identity.Contains("Administrators", StringComparison.OrdinalIgnoreCase))
                {
                    if ((rights & FileSystemRights.FullControl) == FileSystemRights.FullControl)
                        info.AdminsHasFullControl = true;
                }
            }

            info.RawOutput = string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            info.RawOutput = $"获取 ACL 失败: {ex.Message}";
        }

        return info;
    }

    // ─── 修改权限 ──────────────────────────────────────────

    private async void OnModify(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string dirPath)
        {
            // 记录修改前的状态
            var beforeState = GetCurrentAclState(dirPath);

            // 创建勾选框面板
            var panel = new StackPanel { Spacing = 12 };

            var chkInherit = new CheckBox { Content = "启用权限继承", IsChecked = true };
            var chkUsers = new CheckBox { Content = "Users - 读取+执行 (RX)", IsChecked = true };
            var chkEveryone = new CheckBox { Content = "Everyone - 读取+执行 (RX)", IsChecked = false };
            var chkSystem = new CheckBox { Content = "SYSTEM - 完全控制 (F)", IsChecked = true };
            var chkAdmins = new CheckBox { Content = "Administrators - 完全控制 (F)", IsChecked = true };

            panel.Children.Add(new TextBlock { Text = "选择要授予的权限:", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(chkInherit);
            panel.Children.Add(chkUsers);
            panel.Children.Add(chkEveryone);
            panel.Children.Add(chkSystem);
            panel.Children.Add(chkAdmins);

            var dialog = new ContentDialog
            {
                Title = $"修改权限: {System.IO.Path.GetFileName(dirPath)}",
                Content = panel,
                PrimaryButtonText = "应用",
                SecondaryButtonText = "重置为默认",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            bool success = false;
            string action = "";
            string? errorMsg = null;

            if (result == ContentDialogResult.Primary)
            {
                action = "Modify";
                var (s, msg) = await ApplySelectedPermissions(dirPath,
                    chkInherit.IsChecked == true,
                    chkUsers.IsChecked == true,
                    chkEveryone.IsChecked == true,
                    chkSystem.IsChecked == true,
                    chkAdmins.IsChecked == true);
                success = s;
                if (!s) errorMsg = msg;
            }
            else if (result == ContentDialogResult.Secondary)
            {
                action = "Reset";
                var (s, msg) = await ResetToDefault(dirPath);
                success = s;
                if (!s) errorMsg = msg;
            }

            // 记录审计日志
            if (result != ContentDialogResult.None)
            {
                var afterState = success ? GetCurrentAclState(dirPath) : beforeState;
                DataService.AddAuditEntry(new AuditLogEntry
                {
                    Action = action,
                    TargetPath = dirPath,
                    BeforeState = beforeState,
                    AfterState = afterState,
                    Success = success,
                    ErrorMessage = errorMsg
                });
            }

            // 刷新扫描
            OnScan(this, new RoutedEventArgs());
        }
    }

    private string GetCurrentAclState(string dirPath)
    {
        try
        {
            var info = GetAclInfo(dirPath);
            return $"Inherit:{info.HasInheritance}, Users:{info.UsersHasReadExecute}, Everyone:{info.EveryoneHasReadExecute}, System:{info.SystemHasFullControl}, Admins:{info.AdminsHasFullControl}";
        }
        catch
        {
            return "Unknown";
        }
    }

    private Task<(bool Success, string Message)> ApplySelectedPermissions(string dirPath, bool inherit, bool users, bool everyone, bool system, bool admins)
    {
        return Task.Run(() =>
        {
            var results = new List<string>();
            try
            {
                if (inherit)
                {
                    var r = RunIcacls(dirPath, "/inheritance:e");
                    results.Add($"inheritance:e => {r.Split('\n')[0]}");
                }
                else
                {
                    var r = RunIcacls(dirPath, "/inheritance:d");
                    results.Add($"inheritance:d => {r.Split('\n')[0]}");
                }

                if (users)
                {
                    var r = RunIcacls(dirPath, "/grant:r \"*S-1-5-32-545:(OI)(CI)(RX)\"");
                    results.Add($"grant Users => {r.Split('\n')[0]}");
                }
                if (everyone)
                {
                    var r = RunIcacls(dirPath, "/grant:r \"*S-1-1-0:(OI)(CI)(RX)\"");
                    results.Add($"grant Everyone => {r.Split('\n')[0]}");
                }
                if (system)
                {
                    var r = RunIcacls(dirPath, "/grant:r \"*S-1-5-18:(OI)(CI)F\"");
                    results.Add($"grant SYSTEM => {r.Split('\n')[0]}");
                }
                if (admins)
                {
                    var r = RunIcacls(dirPath, "/grant:r \"*S-1-5-32-544:(OI)(CI)F\"");
                    results.Add($"grant Admins => {r.Split('\n')[0]}");
                }

                App.Log(string.Join("; ", results));
                return (true, string.Join("\n", results));
            }
            catch (Exception ex)
            {
                App.Log($"ApplySelectedPermissions 失败: {ex.Message}");
                return (false, ex.Message);
            }
        });
    }

    private Task<(bool Success, string Message)> ResetToDefault(string dirPath)
    {
        return Task.Run(() =>
        {
            try
            {
                var r1 = RunIcacls(dirPath, "/reset");
                var r2 = RunIcacls(dirPath, "/inheritance:e");
                App.Log($"ResetToDefault: reset => {r1.Split('\n')[0]}; inheritance:e => {r2.Split('\n')[0]}");
                return (true, $"reset => {r1.Split('\n')[0]}\ninheritance:e => {r2.Split('\n')[0]}");
            }
            catch (Exception ex)
            {
                App.Log($"ResetToDefault 失败: {ex.Message}");
                return (false, ex.Message);
            }
        });
    }

    // ─── 选中详情 ──────────────────────────────────────────

    private void OnResultTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Grid grid && grid.DataContext is AclResultItem item)
        {
            // 恢复之前选中行的背景
            if (_selectedRow != null && _selectedRow != grid)
            {
                _selectedRow.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
            }
            
            // 设置当前行为选中状态
            _selectedRow = grid;
            grid.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"];
            
            DetailText.Text = string.IsNullOrEmpty(item.AclDetail) ? "无详细信息" : item.AclDetail;
            DetailCard.Visibility = Visibility.Visible;
        }
    }

    private void OnRowPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid && grid != _selectedRow)
        {
            grid.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"];
        }
    }

    private void OnRowPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid && grid != _selectedRow)
        {
            grid.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
        }
    }

    // ─── 工具方法 ──────────────────────────────────────────

    internal static string RunIcacls(string dirPath, string extraArgs = "")
    {
        var args = string.IsNullOrEmpty(extraArgs)
            ? $"\"{dirPath}\""
            : $"\"{dirPath}\" {extraArgs}";

        var psi = new ProcessStartInfo
        {
            FileName = "icacls",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)!;

        var outputTask = proc.StandardOutput.ReadToEndAsync();
        var errorTask = proc.StandardError.ReadToEndAsync();

        var completed = Task.WhenAll(outputTask, errorTask).Wait(5000);

        if (!completed)
        {
            try { proc.Kill(); } catch { }
            return $"[超时] icacls 命令在 5 秒内未响应";
        }

        proc.WaitForExit(2000);
        var output = outputTask.Result;
        var error = errorTask.Result;

        if (proc.ExitCode != 0 && !string.IsNullOrEmpty(error))
            return $"[错误] {error.Trim()}";

        return output;
    }
}
