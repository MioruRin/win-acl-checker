<img width="1584" height="672" alt="1780195253079" src="https://github.com/user-attachments/assets/be85b1fa-52ca-4629-9e72-35b9c3651b3b" /># Win ACL Checker
**Windows ACL 权限排查修复工具** —— 扫描目录权限链路，检测并修复沙盒/受限用户无法访问的权限问题。

![.NET 8](https://img.shields.io/badge/.NET-8.0-blueviolet) ![Windows](https://img.shields.io/badge/Windows-10%2F11-blue) ![License](https://img.shields.io/badge/license-MIT-green) ![Release](https://img.shields.io/github/v/release/MioruRin/win-acl-checker)

## 下载

[⬇ 下载最新版本](https://github.com/MioruRin/win-acl-checker/releases/latest)

提供两个版本：

- **AclChecker-WinUI3.zip** (34 MB) — 需安装 [.NET 8 运行时](https://dotnet.microsoft.com/download/dotnet/8.0)
- **AclChecker-WinUI3-SelfContained.zip** (65 MB) — 自包含版，无需安装运行时，解压即用

> 运行前请右键 `AclChecker.exe` → **以管理员身份运行**。

---

## 功能简介

在 Windows 系统上，某些软件（尤其是沙盒、受限用户环境下运行的程序）因目录 ACL 权限配置不当而无法正常运行。常见场景包括：

- C 盘程序正常、D 盘程序无法访问
- 沙盒环境中非系统目录缺少 `Users` 或 `Everyone` 读取权限
- 手动迁移目录后权限继承链断裂

本工具通过扫描从盘符根目录到目标文件的**完整权限链路**，逐级检查 ACL 权限状态，快速定位问题目录，并提供一键修复能力。

## 主要功能

- **全链路扫描** — 从根目录到目标，逐级检查每个目录的 ACL 权限
- **权限状态可视化** — 以图标方式展示继承状态、Users/Everyone/SYSTEM/Administrators 权限
- **安全风险检测** — 检测 Everyone 写入权限、Guests 访问权限、系统目录继承禁用等风险
- **一键修复** — 重置为系统默认继承权限，或选择性授予 Users/Everyone 读取+执行权限
- **审计日志** — 记录所有修改操作的前后状态，可追溯
- **拖放支持** — 直接拖入文件/目录即可开始扫描（WinUI 3 版）
- **权限快照** — 保存扫描结果，便于对比分析

## 两个版本

本仓库包含两个独立实现：

| 版本 | 技术栈 | 文件 |
|------|--------|------|
| Python 版 | Python 3 + Tkinter | `acl_checker.py` |
| WinUI 3 版 | C# / .NET 8 / WinUI 3 | `AclChecker/` 目录 |

WinUI 3 版功能更完善，界面采用 Fluent Design，支持 Mica 背景、拖放操作、审计日志等。

## 环境要求

### WinUI 3 版（推荐）

- Windows 10 版本 2004（Build 19041）或更高
- .NET 8 SDK（构建时）
- Visual Studio 2022 或 JetBrains Rider（可选）

### Python 版

- Windows 系统
- Python 3.8+
- Tkinter（Python 自带）

## 构建与运行

### WinUI 3 版

```bash
# 克隆仓库
git clone https://github.com/MioruRin/win-acl-checker.git
cd win-acl-checker/AclChecker

# 还原依赖并构建
dotnet restore
dotnet build -c Release

# 运行
dotnet run -c Release
```

或使用 Visual Studio 打开 `AclChecker/AclChecker.csproj`，按 F5 运行。

### Python 版

```bash
# 直接运行
python acl_checker.py

# 或使用 PyInstaller 打包为单文件 exe
pip install pyinstaller
pyinstaller "ACL权限排查修复工具.spec"
```

> **提示：** 修改 ACL 权限需要管理员权限。工具会提示以管理员身份运行。

## 使用说明

1. **选择目标** — 点击「选择文件」或「选择目录」，选定要排查的程序路径
2. **开始扫描** — 工具自动分析从根目录到目标的完整权限链
3. **查看结果** — 表格中绿色行表示权限正常，红色行表示存在问题
4. **修复权限** — 选中问题目录，点击「修改」按钮选择要授予的权限，或使用「重置为默认」恢复系统默认继承权限
5. **查看日志** — 在「操作日志」页面查看所有历史操作记录

### 权限标准参考

| 主体 | 推荐权限 | 说明 |
|------|----------|------|
| 继承 | 启用 | 确保子目录自动继承父目录权限 |
| Users | 读取 + 执行 (RX) | 标准用户可运行程序 |
| SYSTEM | 完全控制 (F) | 系统服务访问 |
| Administrators | 完全控制 (F) | 管理员维护权限 |

## 项目结构

```
win-acl-checker/
├── acl_checker.py              # Python 版（Tkinter GUI）
├── ACL权限排查修复工具.spec      # PyInstaller 打包配置
├── AclChecker/                 # WinUI 3 版
│   ├── AclChecker.csproj       # 项目文件（.NET 8 / WinUI 3）
│   ├── App.xaml / App.xaml.cs  # 应用入口
│   ├── MainWindow.xaml(.cs)    # 主窗口（导航 + Mica 背景）
│   ├── ScanPage.xaml(.cs)      # 权限扫描页
│   ├── LogPage.xaml(.cs)       # 操作日志页
│   ├── AboutPage.xaml(.cs)     # 关于页
│   ├── Models.cs               # 数据模型（AclResultItem 等）
│   └── DataService.cs          # 数据持久化服务
└── README.md
```

## 技术细节

- 使用 Windows 原生 `icacls` 命令读取和修改 ACL 权限
- WinUI 3 版使用 `System.Security.AccessControl` API 直接解析目录安全描述符
- 安全风险检测包括：Everyone 写入权限、Guests 访问权限、系统目录继承被禁用
- 数据持久化使用 JSON 文件，存储在 `%LOCALAPPDATA%\AclChecker\` 目录
- 审计日志保留最近 1000 条，权限快照保留最近 50 个

## 许可证

MIT License
