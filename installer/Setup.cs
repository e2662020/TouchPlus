using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TouchPlus.Setup
{
    /// <summary>
    /// Touch+ 安装程序：单文件、按用户安装、无需管理员权限。
    /// 负载（宿主 exe / 插件 dll / 图标 / README）以嵌入资源形式打包在本 exe 内。
    /// 用法：
    ///   TouchPlusSetup.exe                图形安装
    ///   TouchPlusSetup.exe --silent       静默安装（默认选项）
    ///   TouchPlusSetup.exe --uninstall    卸载
    /// </summary>
    internal static class Program
    {
        private const string AppName = "Touch+";
        private const string AppId = "TouchPlus";
        private const string Version = "1.1.0";
        private const string Publisher = "Touch+";

        internal static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\TouchPlus"); }
        }

        internal static string TargetExe { get { return Path.Combine(InstallDir, "TouchPlus.exe"); } }
        private static string SetupCopy { get { return Path.Combine(InstallDir, "TouchPlusSetup.exe"); } }

        [STAThread]
        private static int Main(string[] args)
        {
            bool uninstall = HasFlag(args, "--uninstall");
            bool silent = HasFlag(args, "--silent") || HasFlag(args, "--quiet");

            if (uninstall)
            {
                try { Uninstall(); return 0; }
                catch (Exception ex) { ShowError("卸载失败：" + ex.Message); return 1; }
            }

            if (silent)
            {
                try { Install(true, true, false); return 0; }
                catch (Exception ex) { ShowError("安装失败：" + ex.Message); return 1; }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new InstallerForm());
            return 0;
        }

        internal static bool HasFlag(string[] args, string flag)
        {
            foreach (string a in args)
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /* ---------------- 安装 ---------------- */

        internal static void Install(bool desktopShortcut, bool startMenuShortcut, bool autostart)
        {
            StopRunning();
            Directory.CreateDirectory(InstallDir);
            Directory.CreateDirectory(Path.Combine(InstallDir, "plugins"));
            Directory.CreateDirectory(Path.Combine(InstallDir, "assets"));

            Extract("TouchPlus.exe", TargetExe);
            Extract("TouchPlus.Plugins.DoubleClick.dll",
                Path.Combine(InstallDir, @"plugins\TouchPlus.Plugins.DoubleClick.dll"));
            Extract("TouchPlus.ico", Path.Combine(InstallDir, @"assets\TouchPlus.ico"));
            Extract("README.md", Path.Combine(InstallDir, "README.md"));

            // 安装程序自身留一份在安装目录，供"应用和功能"里的卸载调用
            string self = Application.ExecutablePath;
            if (!string.Equals(self, SetupCopy, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Copy(self, SetupCopy, true); } catch (Exception) { }
            }

            if (autostart) SetAutostart(true);

            if (desktopShortcut)
                CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    AppName + ".lnk"), TargetExe, InstallDir);
            if (startMenuShortcut)
            {
                string menuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
                Directory.CreateDirectory(menuDir);
                CreateShortcut(Path.Combine(menuDir, AppName + ".lnk"), TargetExe, InstallDir);
            }

            WriteUninstallEntry();
        }

        private static void WriteUninstallEntry()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppId))
            {
                key.SetValue("DisplayName", AppName + " 触控屏修补宿主");
                key.SetValue("DisplayVersion", Version);
                key.SetValue("Publisher", Publisher);
                key.SetValue("DisplayIcon", TargetExe + ",0");
                key.SetValue("InstallLocation", InstallDir);
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                key.SetValue("UninstallString", "\"" + SetupCopy + "\" --uninstall");
                key.SetValue("QuietUninstallString", "\"" + SetupCopy + "\" --uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                try
                {
                    key.SetValue("EstimatedSize", DirectorySize(InstallDir) / 1024, RegistryValueKind.DWord);
                }
                catch (Exception) { }
            }
        }

        /* ---------------- 卸载 ---------------- */

        internal static void Uninstall()
        {
            StopRunning();
            RestoreSystemDefaults();
            SetAutostart(false);
            RemoveShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                AppName + ".lnk"));
            try
            {
                string menuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
                if (Directory.Exists(menuDir)) Directory.Delete(menuDir, true);
            }
            catch (Exception) { }
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppId, false);
            }
            catch (Exception) { }

            // 自删除：安装目录里可能有正在运行的 setup 副本，交给 cmd 延迟删除
            try
            {
                string dir = InstallDir;
                Process.Start(new ProcessStartInfo("cmd.exe",
                    "/c ping -n 3 127.0.0.1 >nul & rd /s /q \"" + dir + "\"")
                {
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch (Exception) { }
        }

        /// <summary>卸载时把系统双击参数还原为 Windows 默认值（4×4 / 500ms）。</summary>
        private static void RestoreSystemDefaults()
        {
            try
            {
                SystemParametersInfo(0x001D, 4, IntPtr.Zero, 0x0001 | 0x0002);
                SystemParametersInfo(0x001E, 4, IntPtr.Zero, 0x0001 | 0x0002);
                SetDoubleClickTime(500);
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", true))
                {
                    if (key != null) key.SetValue("DoubleClickSpeed", "500", RegistryValueKind.String);
                }
            }
            catch (Exception) { }
        }

        private static void StopRunning()
        {
            foreach (Process p in Process.GetProcessesByName("TouchPlus"))
            {
                try
                {
                    if (!p.CloseMainWindow()) p.Kill();
                    else p.WaitForExit(3000);
                }
                catch (Exception) { }
            }
            System.Threading.Thread.Sleep(300);
        }

        private static void SetAutostart(bool on)
        {
            string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(run, true))
            {
                if (key == null) return;
                if (on) key.SetValue(AppId, "\"" + TargetExe + "\" --tray");
                else key.DeleteValue(AppId, false);
            }
        }

        /* ---------------- 快捷方式 ---------------- */

        private static void CreateShortcut(string lnkPath, string target, string workDir)
        {
            try
            {
                object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                object shortcut = shell.GetType().InvokeMember("CreateShortcut",
                    BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                Type t = shortcut.GetType();
                t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { target });
                t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workDir });
                t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { target + ",0" });
                t.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Touch+ 触控屏修补宿主" });
                t.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                Marshal.ReleaseComObject(shortcut);
                Marshal.ReleaseComObject(shell);
            }
            catch (Exception) { }
        }

        private static void RemoveShortcut(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
        }

        /* ---------------- 负载与杂项 ---------------- */

        private static void Extract(string resourceName, string targetPath)
        {
            using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (input == null) throw new InvalidOperationException("安装包缺少资源：" + resourceName);
                string dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                using (FileStream output = File.Create(targetPath)) input.CopyTo(output);
            }
        }

        private static long DirectorySize(string dir)
        {
            long total = 0;
            try
            {
                foreach (FileInfo f in new DirectoryInfo(dir).GetFiles("*.*", SearchOption.AllDirectories))
                    total += f.Length;
            }
            catch (Exception) { }
            return total;
        }

        private static void ShowError(string message)
        {
            try
            {
                MessageBox.Show(message, "Touch+ 安装程序", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception) { }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool SystemParametersInfo(int action, int param, IntPtr vparam, int winIni);

        [DllImport("user32.dll")]
        private static extern bool SetDoubleClickTime(int interval);
    }

    /// <summary>图形安装界面：暗色、单窗口、不折腾。</summary>
    internal sealed class InstallerForm : Form
    {
        private readonly CheckBox _desktop = new CheckBox();
        private readonly CheckBox _startMenu = new CheckBox();
        private readonly CheckBox _autostart = new CheckBox();
        private readonly Label _status = new Label();
        private readonly Button _install = new Button();
        private readonly Button _cancel = new Button();

        private static readonly Color Bg = Color.FromArgb(24, 27, 30);
        private static readonly Color Ink = Color.FromArgb(238, 239, 234);
        private static readonly Color Dim = Color.FromArgb(150, 156, 150);
        private static readonly Color Accent = Color.FromArgb(63, 224, 236);

        public InstallerForm()
        {
            Text = "Touch+ 安装程序";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(520, 372);
            BackColor = Bg;
            ForeColor = Ink;
            Font = new Font("Microsoft YaHei UI", 9.5f);

            Label title = new Label();
            title.Text = "Touch+";
            title.Font = new Font("Microsoft YaHei UI", 22f, FontStyle.Bold);
            title.ForeColor = Ink;
            title.SetBounds(28, 22, 300, 44);
            Controls.Add(title);

            Label sub = new Label();
            sub.Text = "触控屏修补宿主 · 按用户安装，不需要管理员权限";
            sub.ForeColor = Dim;
            sub.SetBounds(30, 66, 460, 22);
            Controls.Add(sub);

            Label pathLabel = new Label();
            pathLabel.Text = "安装位置  " + Program.InstallDir;
            pathLabel.ForeColor = Dim;
            pathLabel.SetBounds(30, 100, 470, 22);
            Controls.Add(pathLabel);

            _desktop.Text = "创建桌面快捷方式";
            _desktop.Checked = true;
            _desktop.ForeColor = Ink;
            _desktop.SetBounds(30, 140, 300, 24);

            _startMenu.Text = "添加到开始菜单";
            _startMenu.Checked = true;
            _startMenu.ForeColor = Ink;
            _startMenu.SetBounds(30, 170, 300, 24);

            _autostart.Text = "开机自动启动（写入当前用户启动项）";
            _autostart.ForeColor = Ink;
            _autostart.SetBounds(30, 200, 380, 24);
            Controls.Add(_desktop);
            Controls.Add(_startMenu);
            Controls.Add(_autostart);

            Label note = new Label();
            note.Text = "安装后可在「设置 → 应用」里卸载；卸载会把双击参数还原为 Windows 默认值。";
            note.ForeColor = Dim;
            note.SetBounds(30, 236, 460, 40);
            Controls.Add(note);

            _status.Text = "";
            _status.ForeColor = Accent;
            _status.SetBounds(30, 276, 460, 22);
            Controls.Add(_status);

            _install.Text = "安装";
            _install.SetBounds(310, 310, 88, 34);
            _install.Click += delegate { DoInstall(); };
            Controls.Add(_install);

            _cancel.Text = "取消";
            _cancel.SetBounds(406, 310, 88, 34);
            _cancel.Click += delegate { Close(); };
            Controls.Add(_cancel);
        }

        private void DoInstall()
        {
            _install.Enabled = false;
            _status.Text = "正在安装…";
            Application.DoEvents();
            try
            {
                Program.Install(_desktop.Checked, _startMenu.Checked, _autostart.Checked);
                _status.Text = "安装完成";
                if (MessageBox.Show("Touch+ 已安装完成。\n\n现在启动？", "Touch+ 安装程序",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    Process.Start(Program.TargetExe);
                }
                Close();
            }
            catch (Exception ex)
            {
                _status.Text = "安装失败";
                MessageBox.Show("安装失败：\n\n" + ex.Message, "Touch+ 安装程序",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                _install.Enabled = true;
            }
        }
    }
}
