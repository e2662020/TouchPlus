using System.Windows.Forms;

namespace TouchPlus.UI.Controls
{
    /// <summary>
    /// 自绘控件基类：统一开启双缓冲与自绘（SetStyle 是 protected，只能由派生类调用）。
    /// </summary>
    public class ThemedControl : Control
    {
        public ThemedControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            UpdateStyles();
        }
    }
}
