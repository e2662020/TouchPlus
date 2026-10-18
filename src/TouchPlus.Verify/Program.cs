using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using TouchPlus.Core;

namespace TouchPlus.Verify
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                Log.Instance.Error(e.Exception, "界面线程异常");
                MessageBox.Show("程序发生异常：\r\n" + e.Exception.Message + "\r\n\r\n详情已写入日志。",
                    "Touch+ 验证工具", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };

            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                MessageBox.Show("程序发生严重异常：\r\n" + (ex == null ? "未知" : ex.Message),
                    "Touch+ 验证工具", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            SettingsStore.EnsureDirectory();
            Log.Instance.Info("验证工具启动：{0}，管理员={1}", SystemInfo.VersionText, SystemInfo.IsElevated);
            Log.Instance.Info("{0}", SystemInfo.DescribeDigitizer());
            Log.Instance.Info("{0}", DoubleClickParams.DescribeCurrent());

            using (VerifyForm form = new VerifyForm(args))
            {
                Application.Run(form);
            }
            return Environment.ExitCode;
        }
    }
}
