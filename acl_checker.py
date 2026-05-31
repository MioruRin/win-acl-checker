#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
ACL 权限排查修复工具 (ACL Checker & Fixer)
用于排查 Windows 下软件因 ACL 目录权限问题无法运行的情况
尤其适用于：沙盒环境下 C 盘正常、D 盘无法访问的权限问题

用法：
    普通运行：python acl_checker.py
    建议以管理员身份运行（右键"以管理员身份运行"）
"""

import os
import sys
import subprocess
import re
import ctypes
import threading
from pathlib import Path
from tkinter import (
    Tk, Frame, LabelFrame, Label, Entry, Button, StringVar,
    Scrollbar, ttk, scrolledtext, messagebox, Toplevel, END,
    filedialog,
)


# ============================================================
# 工具函数
# ============================================================

def is_admin():
    """检查是否以管理员身份运行"""
    try:
        return ctypes.windll.shell32.IsUserAnAdmin()
    except Exception:
        return False


def run_as_admin():
    """以管理员身份重新运行当前脚本"""
    ctypes.windll.shell32.ShellExecuteW(
        None, "runas", sys.executable, '"' + __file__ + '"', None, 1
    )
    sys.exit(0)


PERM_DESC = {
    "F": "完全控制",
    "M": "修改",
    "RX": "读取+执行",
    "R": "只读",
    "W": "写入",
    "D": "删除",
    "X": "执行",
    "DC": "删除子项",
    "WD": "写入属性",
    "AD": "追加数据",
    "RC": "读取控制",
    "S": "同步",
}


def parse_icacls_output(output):
    """
    解析 icacls 输出，返回 [{"user": str, "perms": str, "inherited": bool}, ...]
    icacls 输出格式示例：
        D:/Games/Steam
        BUILTIN/Users:(I)(RX)
        NT AUTHORITY/SYSTEM:(I)(F)
        BUILTIN/Administrators:(I)(F)
    """
    entries = []
    lines = output.strip().splitlines()
    for line in lines[1:]:  # 第一行是路径，跳过
        line = line.strip()
        if not line or line.startswith("Successfully") or line.startswith("失败"):
            continue
        if ":" not in line:
            continue

        # 格式：用户名:(I)(RX) 或 用户名:(NP)(RX)(GR,GE)
        user_part, rest = line.split(":", 1)
        user = user_part.strip()

        # 提取所有括号中的内容
        groups = re.findall(r"\(([^)]+)\)", rest)
        if not groups:
            continue

        # 最后一组通常是权限，前面的是标志（I=继承, NP=不传播等）
        flags = [g for g in groups if g in ("I", "NP", "IO")]
        perms_group = groups[-1]  # 最后一组是权限

        inherited = "I" in flags
        entries.append({
            "user": user,
            "perms": perms_group,
            "inherited": inherited,
        })

    return entries


def get_acl_info(path):
    """
    获取指定路径的 ACL 信息
    返回：{
        "path": 路径,
        "raw": icacls 原始输出,
        "entries": [{"user", "perms", "inherited"}],
        "has_rx_for_sandbox": 沙盒受限用户是否能访问,
        "issue": 问题描述或 None,
    }
    """
    result = {
        "path": path,
        "raw": "",
        "entries": [],
        "has_rx_for_sandbox": False,
        "issue": None,
    }

    try:
        cmd = "icacls " + '"' + path + '"'
        proc = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            encoding="gbk",
            errors="replace",
            shell=True,
            timeout=10,
        )
        output = proc.stdout
        result["raw"] = output
        entries = parse_icacls_output(output)
        result["entries"] = entries

        if not entries:
            result["issue"] = "无法读取 ACL 信息（路径可能不存在或权限不足）"
            return result

        # 判断沙盒受限用户是否能访问
        # 沙盒用户通常属于 Everyone 或 Users 组
        # 检查是否有 Everyone 或 Users 组的 RX / M / F 权限
        sandbox_ok = False
        for e in entries:
            user = e["user"]
            perms = e["perms"]
            if user == "Everyone" or "Users" in user:
                if any(p in perms for p in ["F", "M", "RX"]):
                    sandbox_ok = True
                    break

        result["has_rx_for_sandbox"] = sandbox_ok

        if not sandbox_ok:
            has_everyone = any(e["user"] == "Everyone" for e in entries)
            has_users = any("Users" in e["user"] for e in entries)
            if not has_everyone and not has_users:
                result["issue"] = (
                    "未授予 Everyone 或 Users 组权限，"
                    "沙盒等受限用户可能无法访问此目录"
                )
            else:
                result["issue"] = (
                    "Everyone / Users 组缺少读取+执行(RX)权限，"
                    "沙盒进程可能无法访问此目录"
                )
        else:
            # 即使是 RX，也检查一下是否是继承的
            inherited_ok = any(
                e["inherited"] and e["perms"] in ("RX", "M", "F")
                for e in entries
                if e["user"] in ("Everyone", "BUILTIN\\Users") or "Users" in e["user"]
            )
            if not inherited_ok:
                result["issue"] = (
                    "有权限但非继承，某些沙盒场景仍可能受限，"
                    "建议重置为系统默认继承权限"
                )

    except subprocess.TimeoutExpired:
        result["issue"] = "icacls 命令超时"
    except Exception as e:
        result["issue"] = f"获取 ACL 时出错：{e}"

    return result


def get_full_path_chain(target_path):
    """
    获取从盘符到目标文件/目录的完整路径链
    例如：D:/Games/Steam/steam.exe
    -> [D:/, D:/Games, D:/Games/Steam, D:/Games/Steam/steam.exe]
    """
    path = Path(target_path)
    chain = []

    # 先加入目标自身
    try:
        chain.append(str(path.resolve()))
    except Exception:
        chain.append(str(path))

    # 向上遍历所有父目录
    current = path.parent
    seen = {chain[-1]}
    while True:
        try:
            p = str(current.resolve())
        except Exception:
            p = str(current)
        if p in seen:
            break
        chain.append(p)
        seen.add(p)
        try:
            parent = current.parent
            if parent == current:
                break
            current = parent
        except Exception:
            break

    # 反转：从根目录到目标
    chain.reverse()
    return chain


def fix_add_user(path, user, perms="(RX)"):
    """
    为指定路径添加用户权限
    user: "Everyone" 或 "Users"
    perms: "(RX)"=读取执行, "(F)"=完全控制, "(M)"=修改
    返回：(success: bool, output: str)
    """
    mapping = {
        "Everyone": "Everyone",
        "Users": "Users",
    }
    actual_user = mapping.get(user, user)
    cmd = "icacls " + '"' + path + '"' + " /grant " + actual_user + ":" + perms + " /T"
    try:
        proc = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            encoding="gbk",
            errors="replace",
            shell=True,
            timeout=120,
        )
        return proc.returncode == 0, proc.stdout + proc.stderr
    except Exception as e:
        return False, str(e)


def reset_acl(path):
    """
    重置 ACL 为系统默认继承权限（icacls /reset）
    返回：(success: bool, output: str)
    """
    cmd = "icacls " + '"' + path + '"' + " /reset /T"
    try:
        proc = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            encoding="gbk",
            errors="replace",
            shell=True,
            timeout=120,
        )
        return proc.returncode == 0, proc.stdout + proc.stderr
    except Exception as e:
        return False, str(e)


# ============================================================
# GUI 主窗口
# ============================================================

class ACLCheckerApp:
    def __init__(self, root):
        self.root = root
        self.root.title("ACL 权限排查修复工具  v1.0")
        self.root.geometry("1050x720")
        self.root.minsize(900, 600)

        self.font_label = ("Microsoft YaHei", 10)
        self.font_mono = ("Consolas", 9)
        self.font_title = ("Microsoft YaHei", 11, "bold")

        self.target_path = StringVar()
        self.scan_results = []
        self.selected_path = StringVar()

        self.setup_ui()

        if not is_admin():
            self.show_admin_warning()

    # --------------------------------------------------------
    # UI 搭建
    # --------------------------------------------------------
    def setup_ui(self):
        # ===== 顶部：选择文件/目录 =====
        top_f = LabelFrame(self.root, text="  选择目标  ", font=self.font_title)
        top_f.pack(fill="x", padx=10, pady=(10, 5))

        Label(top_f, text="目标路径：", font=self.font_label).grid(
            row=0, column=0, sticky="w", padx=5, pady=6
        )
        Entry(top_f, textvariable=self.target_path, width=65, font=self.font_label).grid(
            row=0, column=1, padx=5, pady=6
        )
        Button(top_f, text="选择文件...", command=self.browse_file,
               font=self.font_label, padx=10).grid(row=0, column=2, padx=5, pady=6)
        Button(top_f, text="选择目录...", command=self.browse_dir,
               font=self.font_label, padx=10).grid(row=0, column=3, padx=5, pady=6)

        btn_f = Frame(top_f)
        btn_f.grid(row=1, column=0, columnspan=4, pady=(0, 6))
        Button(btn_f, text="\u2740  \u67e5\u627e  \u5f00\u59cb\u626b\u63cf", command=self.start_scan,
               bg="#0078D4", fg="white", font=self.font_label, padx=22, pady=4).pack(side="left", padx=6)
        Button(btn_f, text="\u6502  \u626b\u63cf\u5f53\u524d\u76ee\u5f55", command=self.scan_current_dir,
               font=self.font_label, padx=16, pady=4).pack(side="left", padx=6)

        # ===== 中间：扫描结果表格 =====
        mid_f = LabelFrame(self.root, text="  扫描结果  ", font=self.font_title)
        mid_f.pack(fill="both", expand=True, padx=10, pady=5)

        columns = ("#", "目录路径", "Everyone", "Users", "SYSTEM", "状态", "问题描述")
        self.tree = ttk.Treeview(mid_f, columns=columns, show="headings", height=14)

        col_conf = {
            "#": (40, "center"),
            "目录路径": (300, "w"),
            "Everyone": (75, "center"),
            "Users": (75, "center"),
            "SYSTEM": (75, "center"),
            "状态": (80, "center"),
            "问题描述": (300, "w"),
        }
        for col, (width, anchor) in col_conf.items():
            self.tree.heading(col, text=col)
            self.tree.column(col, width=width, anchor=anchor)

        ys = ttk.Scrollbar(mid_f, orient="vertical", command=self.tree.yview)
        xs = ttk.Scrollbar(mid_f, orient="horizontal", command=self.tree.xview)
        self.tree.configure(yscrollcommand=ys.set, xscrollcommand=xs.set)

        self.tree.pack(side="left", fill="both", expand=True)
        ys.pack(side="right", fill="y")

        self.tree.bind("<<TreeviewSelect>>", self.on_item_select)

        # ===== 底部：修复操作 =====
        fix_f = LabelFrame(self.root, text="  修复操作  ", font=self.font_title)
        fix_f.pack(fill="x", padx=10, pady=5)

        Label(fix_f, text="选中路径：", font=self.font_label).grid(
            row=0, column=0, sticky="w", padx=5, pady=5
        )
        Entry(fix_f, textvariable=self.selected_path, width=55,
              state="readonly", font=self.font_mono).grid(
            row=0, column=1, columnspan=4, padx=5, pady=5, sticky="we"
        )
        fix_f.columnconfigure(1, weight=1)

        op_f = Frame(fix_f)
        op_f.grid(row=1, column=0, columnspan=5, pady=(0, 6))

        Button(op_f, text="\u2795  \u6dfb\u52a0 Everyone (RX)",
               command=lambda: self.fix_selected("Everyone", "(RX)"),
               bg="#E8F5E9", font=self.font_label, padx=12, pady=4).pack(side="left", padx=4)
        Button(op_f, text="\u2795  \u6dfb\u52a0 Users (RX)",
               command=lambda: self.fix_selected("Users", "(RX)"),
               bg="#E3F2FD", font=self.font_label, padx=12, pady=4).pack(side="left", padx=4)
        Button(op_f, text="\u2744\ufe0f  \u91cd\u7f6e\u4e3a\u7cfb\u7edf\u9ed8\u8ba4",
               command=self.reset_selected,
               bg="#FFF8E1", font=self.font_label, padx=12, pady=4).pack(side="left", padx=4)
        Button(op_f, text="\u2699\ufe0f  \u4e00\u952e\u4fee\u590d\u6240\u6709\u95ee\u9898",
               command=self.fix_all_issues,
               bg="#C8E6C9", font=self.font_label, padx=14, pady=4).pack(side="left", padx=14)

        # ===== 详细权限信息 =====
        det_f = LabelFrame(self.root, text="  详细权限信息 (icacls)  ", font=self.font_title)
        det_f.pack(fill="both", expand=True, padx=10, pady=(5, 8))

        self.detail_text = scrolledtext.ScrolledText(
            det_f, height=10, font=self.font_mono, wrap="word"
        )
        self.detail_text.pack(fill="both", expand=True, padx=5, pady=5)

        # 状态栏
        self.status_var = StringVar(value="\u5c31\u7eea")
        Label(self.root, textvariable=self.status_var,
              relief="sunken", anchor="w", font=("Microsoft YaHei", 9)).pack(
            fill="x", side="bottom"
        )

    # --------------------------------------------------------
    # 管理员权限提示
    # --------------------------------------------------------
    def show_admin_warning(self):
        win = Toplevel(self.root)
        win.title("\u9700\u8981\u7ba1\u7406\u5458\u6743\u9650")
        win.geometry("520x210")
        win.transient(self.root)
        win.grab_set()

        Label(win, text="\u26a0\ufe0f  \u9700\u8981\u7ba1\u7406\u5458\u6743\u9650", font=("Microsoft YaHei", 13, "bold"),
              fg="#D32F2F").pack(pady=(14, 6))
        Label(win, text=(
            "\u4fee\u6539 ACL \u6743\u9650\u9700\u8981\u7ba1\u7406\u5458\u6743\u9650\u3002\n\n"
            "\u70b9\u51fb\u300c\u4ee5\u7ba1\u7406\u5458\u8eab\u4efd\u8fd0\u884c\u300d\u5c06\u4ee5\u7ba1\u7406\u5458\u6743\u9650\n"
            "\u91cd\u65b0\u542f\u52a8\u672c\u5de5\u5177\u3002\n\n"
            "\u67e5\u770b\u6743\u9650\u4fe1\u606f\u4e0d\u9700\u8981\u7ba1\u7406\u5458\u6743\u9650\u3002"
        ), font=self.font_label, justify="left").pack(pady=4)

        bf = Frame(win)
        bf.pack(pady=(10, 8))
        Button(bf, text="\u4ee5\u7ba1\u7406\u5458\u8eab\u4efd\u8fd0\u884c",
               command=lambda: (run_as_admin(), win.destroy()),
               bg="#0078D4", fg="white", font=self.font_label, padx=18, pady=4).pack(side="left", padx=10)
        Button(bf, text="\u7ee7\u7eed\uff08\u4ec5\u67e5\u770b\uff09",
               command=win.destroy,
               font=self.font_label, padx=18, pady=4).pack(side="left", padx=10)

    # --------------------------------------------------------
    # 浏览文件/目录
    # --------------------------------------------------------
    def browse_file(self):
        p = filedialog.askopenfilename(
            title="\u9009\u62e9\u76ee\u6807\u6587\u4ef6\uff08\u5982 steam.exe\uff09",
            filetypes=[("\u53ef\u6267\u884c\u6587\u4ef6", "*.exe"), ("\u6240\u6709\u6587\u4ef6", "*.*")]
        )
        if p:
            self.target_path.set(p)

    def browse_dir(self):
        p = filedialog.askdirectory(title="\u9009\u62e9\u8981\u626b\u63cf\u7684\u76ee\u5f55")
        if p:
            self.target_path.set(p)

    def scan_current_dir(self):
        p = self.target_path.get().strip()
        if not p:
            messagebox.showwarning("\u63d0\u793a", "\u8bf7\u5148\u9009\u62e9\u6587\u4ef6\u6216\u76ee\u5f55\uff01")
            return
        if not os.path.exists(p):
            messagebox.showerror("\u9519\u8bef", "\u8def\u5f84\u4e0d\u5b58\u5728\uff1a\n" + p)
            return
        self.start_scan()

    # --------------------------------------------------------
    # 扫描
    # --------------------------------------------------------
    def start_scan(self):
        target = self.target_path.get().strip()
        if not target:
            messagebox.showwarning("\u63d0\u793a", "\u8bf7\u5148\u9009\u62e9\u76ee\u6807\u6587\u4ef6\u6216\u76ee\u5f55\uff01")
            return
        if not os.path.exists(target):
            messagebox.showerror("\u9519\u8bef", "\u8def\u5f84\u4e0d\u5b58\u5728\uff1a\n" + target)
            return

        self.set_status("\u6b63\u5728\u626b\u63cf\uff0c\u8bf7\u7a0d\u5019...")
        self.tree.delete(*self.tree.get_children())
        self.detail_text.delete(1.0, END)
        self.scan_results = []

        def do_scan():
            chain = get_full_path_chain(target)
            results = []
            for i, d in enumerate(chain):
                self.set_status("\u6b63\u5728\u626b\u63cf (" + str(i+1) + "/" + str(len(chain)) + ")：" + d)
                results.append(get_acl_info(d))
            self.root.after(0, lambda: self.show_scan_results(results))

        threading.Thread(target=do_scan, daemon=True).start()

    def show_scan_results(self, results):
        self.scan_results = results
        self.tree.delete(*self.tree.get_children())

        issue_count = 0
        for i, res in enumerate(results):
            path = res["path"]
            issue = res["issue"]
            if issue:
                issue_count += 1

            everyone_str = self.format_entry(res, "Everyone")
            users_str = self.format_entry(res, "Users")
            system_str = self.format_entry(res, "SYSTEM")

            status = "\u274c \u6709\u95ee\u9898" if issue else "\u2705 \u6b63\u5e38"
            tag = "problem" if issue else "ok"

            self.tree.insert("", "end", values=(
                i + 1,
                path,
                everyone_str,
                users_str,
                system_str,
                status,
                issue if issue else "",
            ), tags=(tag,))

        self.tree.tag_configure("problem", background="#FFEBEE")
        self.tree.tag_configure("ok", background="#E8F5E9")

        self.set_status("\u626b\u63cf\u5b8c\u6210\uff01\u5171 " + str(len(results)) + " \u4e2a\u76ee\u5f55\uff0c\u53d1\u73b0 " + str(issue_count) + " \u4e2a\u95ee\u9898\u3002")
        if issue_count:
            messagebox.showinfo("\u626b\u63cf\u5b8c\u6210", "\u53d1\u73b0 " + str(issue_count) + " \u4e2a\u6743\u9650\u95ee\u9898\uff01\n\n\u8bf7\u67e5\u770b\u8868\u683c\u4e2d\u7ea2\u8272\u6807\u8bb0\u884c\uff0c\u9009\u62e9\u540e\u70b9\u51fb\u4fee\u590d\u6309\u94ae\u3002")
        else:
            messagebox.showinfo("\u626b\u63cf\u5b8c\u6210", "\u672a\u53d1\u73b0\u6743\u9650\u95ee\u9898\uff0c\u6240\u6709\u76ee\u5f55\u6743\u9650\u6b63\u5e38\u3002")

    # --------------------------------------------------------
    # 表格辅助
    # --------------------------------------------------------
    def format_entry(self, res, user_keyword):
        """格式化指定用户/组的权限显示"""
        for e in res["entries"]:
            user = e["user"]
            if user_keyword == "Everyone" and user == "Everyone":
                return self.perm_to_str(e["perms"])
            if user_keyword == "Users" and "Users" in user:
                return self.perm_to_str(e["perms"])
            if user_keyword == "SYSTEM" and "SYSTEM" in user:
                return self.perm_to_str(e["perms"])
        return "\u274c \u65e0"

    @staticmethod
    def perm_to_str(perms_str):
        desc = PERM_DESC.get(perms_str, perms_str)
        return "\u2705 " + desc if perms_str in ("F", "M", "RX") else "\u26a0\ufe0f " + desc

    # --------------------------------------------------------
    # 选中事件
    # --------------------------------------------------------
    def on_item_select(self, _event):
        sel = self.tree.selection()
        if not sel:
            return
        values = self.tree.item(sel[0], "values")
        path = values[1]
        self.selected_path.set(path)
        for res in self.scan_results:
            if res["path"] == path:
                self.show_detail(res)
                break

    def show_detail(self, res):
        self.detail_text.delete(1.0, END)
        lines = [
            "\u8def\u5f84\uff1a" + res["path"],
            "=" * 58,
            "",
            "ACL \u6743\u9650\u5217\u8868\uff1a",
            "-" * 58,
        ]
        if not res["entries"]:
            lines.append("\uff08\u65e0\u6cd5\u8bfb\u53d6 ACL \u4fe1\u606f\uff0c\u8bf7\u786e\u8ba4\u8def\u5f84\u5b58\u5728\u5e76\u4ee5\u7ba1\u7406\u5458\u8eab\u4efd\u8fd0\u884c\uff09")
        else:
            for e in res["entries"]:
                inh = "\uff08\u7ee7\u627f\uff09" if e["inherited"] else "\uff08\u663e\u5f0f\uff09"
                lines.append("  " + e["user"] + "  " + self.perm_to_str(e["perms"]) + "  " + inh)

        lines += [
            "",
            "-" * 58,
            "\u95ee\u9898\u8bca\u65ad\uff1a" + (res["issue"] if res["issue"] else "\u2705 \u672a\u53d1\u73b0\u5f02\u5e38"),
            "",
            "\u539f\u59cb icacls \u8f93\u51fa\uff1a",
            "-" * 58,
            res["raw"] if res["raw"] else "\uff08\u65e0\u8f93\u51fa\uff09",
        ]
        self.detail_text.insert(1.0, "\n".join(lines))

    # --------------------------------------------------------
    # 修复操作
    # --------------------------------------------------------
    def fix_selected(self, user, perm_str):
        path = self.selected_path.get()
        if not path:
            messagebox.showwarning("\u63d0\u793a", "\u8bf7\u5148\u5728\u8868\u683c\u4e2d\u9009\u62e9\u8981\u4fee\u590d\u7684\u76ee\u5f55\uff01")
            return
        desc = {"(RX)": "\u8bfb\u53d6+\u6267\u884c", "(F)": "\u5b8c\u5168\u63a7\u5236", "(M)": "\u4fee\u6539"}[perm_str]
        if not messagebox.askyesno("\u786e\u8ba4\u4fee\u590d", (
            "\u5c06\u4e3a\u4ee5\u4e0b\u8def\u5f84\u6dfb\u52a0\u6743\u9650\uff1a\n\n"
            "\u8def\u5f84\uff1a" + path + "\n"
            "\u7528\u6237\uff1a" + user + "\n"
            "\u6743\u9650\uff1a" + desc + " " + perm_str + "\n\n"
            "\u662f\u5426\u7ee7\u7eed\uff1f\n\n"
            "\uff08\u5c06\u4ee5\u7ba1\u7406\u5458\u8eab\u4efd\u6267\u884c icacls \u547d\u4ee4\uff0c/T \u53c2\u6570\u5c06\u9012\u5f52\u5904\u7406\u5b50\u76ee\u5f55\uff09"
        )):
            return
        self.set_status("\u6b63\u5728\u4fee\u590d\uff1a" + path + " ...")

        def do_fix():
            ok, out = fix_add_user(path, user, perm_str)
            self.root.after(0, lambda: self.on_fix_complete(ok, out, path, user))

        threading.Thread(target=do_fix, daemon=True).start()

    def on_fix_complete(self, ok, output, path, user):
        if ok:
            messagebox.showinfo("\u4fee\u590d\u6210\u529f", (
                "\u5df2\u6210\u529f\u4fee\u590d\uff1a\n" + path + "\n\n"
                "\u7528\u6237\uff1a" + user + "\n\n"
                "\u5efa\u8bae\u91cd\u65b0\u626b\u63cf\u786e\u8ba4\u4fee\u590d\u6548\u679c\u3002"
            ))
            self.set_status("\u4fee\u590d\u6210\u529f\uff01")
            self.ask_rescan()
        else:
            messagebox.showerror("\u4fee\u590d\u5931\u8d25", (
                "\u4fee\u590d\u5931\u8d25\uff1a\n\n" + output + "\n\n"
                "\u8bf7\u786e\u4fdd\u4ee5\u7ba1\u7406\u5458\u8eab\u4efd\u8fd0\u884c\u672c\u5de5\u5177\u3002"
            ))
            self.set_status("\u4fee\u590d\u5931\u8d25\uff01")

    def reset_selected(self):
        path = self.selected_path.get()
        if not path:
            messagebox.showwarning("\u63d0\u793a", "\u8bf7\u5148\u5728\u8868\u683c\u4e2d\u9009\u62e9\u8981\u91cd\u7f6e\u7684\u76ee\u5f55\uff01")
            return
        if not messagebox.askyesno("\u786e\u8ba4\u91cd\u7f6e", (
            "\u5c06\u91cd\u7f6e\u4ee5\u4e0b\u8def\u5f84\u7684 ACL \u4e3a\u7cfb\u7edf\u9ed8\u8ba4\u7ee7\u627f\u6743\u9650\uff1a\n\n"
            + path + "\n\n"
            "\u26a0\ufe0f \u6ce8\u610f\uff1a\u8fd9\u5c06\u79fb\u9664\u624b\u52a8\u6dfb\u52a0\u7684\u6743\u9650\uff0c\n"
            "\u6062\u590d\u4e3a\u4ece\u7236\u76ee\u5f55\u7ee7\u627f\u7684\u9ed8\u8ba4\u6743\u9650\uff08/reset /T\uff09\u3002\n\n"
            "\u662f\u5426\u7ee7\u7eed\uff1f"
        )):
            return
        self.set_status("\u6b63\u5728\u91cd\u7f6e\uff1a" + path + " ...")

        def do_reset():
            ok, out = reset_acl(path)
            self.root.after(0, lambda: self.on_reset_complete(ok, out, path))

        threading.Thread(target=do_reset, daemon=True).start()

    def on_reset_complete(self, ok, output, path):
        if ok:
            messagebox.showinfo("\u91cd\u7f6e\u6210\u529f", (
                "\u5df2\u6210\u529f\u91cd\u7f6e\uff1a\n" + path + "\n\n"
                "\u5efa\u8bae\u91cd\u65b0\u626b\u63cf\u786e\u8ba4\u6548\u679c\u3002"
            ))
            self.set_status("\u91cd\u7f6e\u6210\u529f\uff01")
            self.ask_rescan()
        else:
            messagebox.showerror("\u91cd\u7f6e\u5931\u8d25", (
                "\u91cd\u7f6e\u5931\u8d25\uff1a\n\n" + output + "\n\n"
                "\u8bf7\u786e\u4fdd\u4ee5\u7ba1\u7406\u5458\u8eab\u4efd\u8fd0\u884c\u672c\u5de5\u5177\u3002"
            ))
            self.set_status("\u91cd\u7f6e\u5931\u8d25\uff01")

    def ask_rescan(self):
        if messagebox.askyesno("\u91cd\u65b0\u626b\u63cf", "\u662f\u5426\u7acb\u5373\u91cd\u65b0\u626b\u63cf\u4ee5\u786e\u8ba4\u4fee\u590d\u6548\u679c\uff1f"):
            self.start_scan()

    def fix_all_issues(self):
        if not self.scan_results:
            messagebox.showwarning("\u63d0\u793a", "\u8bf7\u5148\u626b\u63cf\uff01")
            return
        problems = [r for r in self.scan_results if r["issue"]]
        if not problems:
            messagebox.showinfo("\u63d0\u793a", "\u672a\u53d1\u73b0\u9700\u8981\u4fee\u590d\u7684\u95ee\u9898\uff01")
            return
        if not messagebox.askyesno("\u4e00\u952e\u4fee\u590d", (
            "\u53d1\u73b0 " + str(len(problems)) + " \u4e2a\u76ee\u5f55\u6709\u6743\u9650\u95ee\u9898\u3002\n\n"
            "\u5c06\u6267\u884c\u4ee5\u4e0b\u64cd\u4f5c\uff1a\n"
            "  1. \u91cd\u7f6e\u4e3a\u7cfb\u7edf\u9ed8\u8ba4\u7ee7\u627f\u6743\u9650\uff08/reset /T\uff09\n"
            "  2. \u6dfb\u52a0 Everyone\uff08\u8bfb\u53d6+\u6267\u884c\uff09\u6743\u9650\uff08/grant Everyone:(RX) /T\uff09\n\n"
            "\u26a0\ufe0f \u662f\u5426\u7ee7\u7eed\uff1f"
        )):
            return
        self.set_status("\u6b63\u5728\u4e00\u952e\u4fee\u590d\u6240\u6709\u95ee\u9898...")

        def do_fix_all():
            ok_cnt = 0
            fail_cnt = 0
            msgs = []
            for res in problems:
                p = res["path"]
                s1, o1 = reset_acl(p)
                s2, o2 = fix_add_user(p, "Everyone", "(RX)")
                if s1 and s2:
                    ok_cnt += 1
                else:
                    fail_cnt += 1
                    msgs.append(p + "\n  reset: " + o1 + "\n  grant: " + o2)
            self.root.after(0, lambda: self.on_fix_all_complete(ok_cnt, fail_cnt, msgs))

        threading.Thread(target=do_fix_all, daemon=True).start()

    def on_fix_all_complete(self, ok_cnt, fail_cnt, msgs):
        msg = (
            "\u4fee\u590d\u5b8c\u6210\uff01\n\n"
            "\u6210\u529f\uff1a" + str(ok_cnt) + " \u4e2a\n"
            "\u5931\u8d25\uff1a" + str(fail_cnt) + " \u4e2a\n\n"
            "\u5efa\u8bae\u91cd\u65b0\u626b\u63cf\u786e\u8ba4\u4fee\u590d\u6548\u679c\u3002"
        )
        if msgs:
            msg += "\n\n失败详情（前3条）：\n" + "\n".join(msgs[:3])
        if fail_cnt == 0:
            messagebox.showinfo("\u4fee\u590d\u5b8c\u6210", msg)
        else:
            messagebox.showwarning("\u4fee\u590d\u5b8c\u6210\uff08\u90e8\u5206\u5931\u8d25\uff09", msg)
        self.set_status("\u4e00\u952e\u4fee\u590d\u5b8c\u6210\uff1a\u6210\u529f " + str(ok_cnt) + "，失败 " + str(fail_cnt))
        self.ask_rescan()

    # --------------------------------------------------------
    # 工具
    # --------------------------------------------------------
    def set_status(self, text):
        self.status_var.set(text)
        self.root.update_idletasks()


# ============================================================
# 入口
# ============================================================

def main():
    root = Tk()
    app = ACLCheckerApp(root)
    root.mainloop()


if __name__ == "__main__":
    main()
