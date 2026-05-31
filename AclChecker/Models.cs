using Microsoft.UI.Xaml;

namespace AclChecker;

public class AclResultItem
{
    public string DirPath { get; set; } = "";
    public string AclDetail { get; set; } = "";

    // ACL 权限状态
    public bool HasInheritance { get; set; } = true;
    public bool UsersHasReadExecute { get; set; }
    public bool EveryoneHasReadExecute { get; set; }
    public bool SystemHasFullControl { get; set; }
    public bool AdminsHasFullControl { get; set; }

    // 图标属性（✓ 绿色 / ✗ 灰色）- 只展示有无，不判断对错
    public string HasInheritanceIcon => HasInheritance ? "\uE73E" : "\uE739";
    public string UsersIcon => UsersHasReadExecute ? "\uE73E" : "\uE739";
    public string EveryoneIcon => EveryoneHasReadExecute ? "\uE73E" : "\uE739";
    public string SystemIcon => SystemHasFullControl ? "\uE73E" : "\uE739";
    public string AdminsIcon => AdminsHasFullControl ? "\uE73E" : "\uE739";

    // 颜色属性 - 有权限=绿色，无权限=灰色
    public string HasInheritanceColorHex => HasInheritance ? "#4CAF50" : "#808080";
    public string UsersColorHex => UsersHasReadExecute ? "#4CAF50" : "#808080";
    public string EveryoneColorHex => EveryoneHasReadExecute ? "#4CAF50" : "#808080";
    public string SystemColorHex => SystemHasFullControl ? "#4CAF50" : "#808080";
    public string AdminsColorHex => AdminsHasFullControl ? "#4CAF50" : "#808080";

    // 提示属性 - 只描述状态，不判断对错
    public string HasInheritanceTip => HasInheritance ? "权限继承: 已启用" : "权限继承: 已禁用";
    public string UsersTip => UsersHasReadExecute ? "Users: 有读取+执行权限" : "Users: 无读取+执行权限";
    public string EveryoneTip => EveryoneHasReadExecute ? "Everyone: 有权限" : "Everyone: 无权限";
    public string SystemTip => SystemHasFullControl ? "SYSTEM: 有完全控制" : "SYSTEM: 无完全控制";
    public string AdminsTip => AdminsHasFullControl ? "Administrators: 有完全控制" : "Administrators: 无完全控制";

    // ========== 安全风险检测 ==========

    /// <summary>
    /// 是否有安全风险（Everyone 有写入权限）
    /// </summary>
    public bool HasSecurityRisk { get; set; }

    /// <summary>
    /// 风险描述
    /// </summary>
    public string RiskDescription { get; set; } = "";

    /// <summary>
    /// 风险图标可见性
    /// </summary>
    public Visibility RiskIconVisibility => HasSecurityRisk ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// 目录名称（用于显示）
    /// </summary>
    public string DisplayName => System.IO.Path.GetFileName(DirPath) ?? DirPath;
}

public class AclInfo
{
    public string RawOutput { get; set; } = "";
    public bool UsersHasReadExecute { get; set; }
    public bool EveryoneHasReadExecute { get; set; }
    public bool SystemHasFullControl { get; set; }
    public bool AdminsHasFullControl { get; set; }
    public bool HasInheritance { get; set; } = true;

    // 安全风险检测
    public bool EveryoneHasWrite { get; set; }
    public bool GuestsHasAccess { get; set; }
    public bool IsSystemDirectoryWithDisabledInheritance { get; set; }
}

/// <summary>
/// 将十六进制颜色字符串转换为 SolidColorBrush（在 UI 线程执行）
/// </summary>
public class HexToBrushConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string hex && hex.StartsWith('#') && hex.Length == 7)
        {
            var r = byte.Parse(hex.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
            var g = byte.Parse(hex.Substring(3, 2), System.Globalization.NumberStyles.HexNumber);
            var b = byte.Parse(hex.Substring(5, 2), System.Globalization.NumberStyles.HexNumber);
            return new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(255, r, g, b));
        }
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
