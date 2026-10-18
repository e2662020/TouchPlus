using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using TouchPlus.Core;
using TouchPlus.Core.Plugins;
using TouchPlus.Plugins;
using TouchPlus.UI;

namespace TouchPlus.Wpf
{
    /// <summary>
    /// Touch+ 的 WPF 外壳：整个界面走 WPF（DirectX 渲染），WinForms 只剩托盘图标。
    /// 一个窗口两个视图：插件中心（主界面）与某个插件的设置。
    /// 所有尺寸用 DIP，DPI 交给 WPF 处理；颜色跟随 Theme（浅色/深色/跟随系统）。
    /// </summary>
    public sealed class WpfShell : Window
    {
        private readonly PluginHost _host;
        private readonly PluginContext _context;
        private IPatchPlugin _current;
        private int _nav = 0;

        private readonly Grid _root = new Grid();
        private readonly ContentControl _body = new ContentControl();
        private readonly TextBlock _crumb = Text("插件中心", 13, false, Theme.TextFaint);
        private readonly TextBlock _statusDot = Text("●", 11, false, Theme.Ok);
        private readonly TextBlock _statusText = Text("", 12, false, Theme.TextDim);
        private readonly TextBlock _specText = Text("", 12, true, Theme.TextFaint);
        private DispatcherTimer _tick;

        public WpfShell(PluginHost host, PluginContext context)
        {
            _host = host;
            _context = context;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Width = 960; Height = 700; MinWidth = 880; MinHeight = 620;
            Background = ToBrush(Theme.Bg);
            UseLayoutRounding = true;
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            Title = "Touch+";

            Content = _root;
            _host.Changed += delegate { Dispatcher.BeginInvoke(new Action(Rebuild)); };
            Theme.Changed += delegate { Dispatcher.BeginInvoke(new Action(Rebuild)); };

            Rebuild();

            _tick = new DispatcherTimer();
            _tick.Interval = TimeSpan.FromSeconds(1);
            _tick.Tick += delegate { RefreshStatus(); };
            _tick.Start();
        }

        /* ---------------- 基础绘制助手 ---------------- */

        private static SolidColorBrush ToBrush(System.Drawing.Color c)
        {
            return new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B));
        }

        private static TextBlock Text(string text, double size, bool mono, System.Drawing.Color color)
        {
            TextBlock t = new TextBlock();
            t.Text = text ?? "";
            t.FontSize = size;
            t.Foreground = ToBrush(color);
            if (mono) t.FontFamily = new FontFamily("Consolas");
            else t.FontFamily = new FontFamily("Microsoft YaHei UI");
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            return t;
        }

        private static Border Rule(bool vertical)
        {
            Border b = new Border();
            b.Background = ToBrush(Theme.Line);
            if (vertical) { b.Width = 1; b.HorizontalAlignment = HorizontalAlignment.Left; }
            else { b.Height = 1; b.VerticalAlignment = VerticalAlignment.Top; }
            return b;
        }

        private static Button FlatButton(string text, bool primary, Action click)
        {
            Button b = new Button();
            b.Content = text;
            b.FontSize = 13;
            b.FontFamily = new FontFamily("Microsoft YaHei UI");
            b.Padding = new Thickness(10, 6, 10, 6);
            b.Cursor = Cursors.Hand;
            System.Drawing.Color fg = primary ? Theme.Card : Theme.Text;
            b.Foreground = ToBrush(fg);
            b.Background = primary ? ToBrush(Theme.Accent) : Brushes.Transparent;
            b.BorderBrush = ToBrush(Theme.Line);
            b.BorderThickness = new Thickness(primary ? 0 : 1);
            b.Template = FlatTemplate(b);
            b.Click += delegate { if (click != null) click(); };
            return b;
        }

        private static ControlTemplate FlatTemplate(Button owner)
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
            { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness")
            { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
            border.AppendChild(content);
            return new ControlTemplate(typeof(Button)) { VisualTree = border };
        }

        /// <summary>开关：WPF ToggleButton + 位移动画（GPU 合成，跟手）。</summary>
        private static ToggleButton Switch(bool value, Action<bool> changed)
        {
            ToggleButton t = new ToggleButton();
            t.IsChecked = value;
            t.Cursor = Cursors.Hand;
            t.Width = 44; t.Height = 24;
            t.ToolTip = value ? "已启用，点击停用" : "未启用，点击启用";

            Border track = new Border();
            track.CornerRadius = new CornerRadius(12);
            System.Drawing.Color trackColor = value ? Theme.Accent : Theme.Track;
            track.Background = ToBrush(trackColor);
            track.BorderBrush = ToBrush(Theme.Line);
            track.BorderThickness = new Thickness(1);

            Ellipse knob = new Ellipse();
            knob.Width = 18; knob.Height = 18;
            knob.Fill = ToBrush(Theme.Card);
            knob.HorizontalAlignment = HorizontalAlignment.Left;
            knob.Margin = new Thickness(value ? 22 : 3, 0, 0, 0);
            knob.VerticalAlignment = VerticalAlignment.Center;

            track.Child = knob;
            t.Content = track;
            t.Template = SwitchTemplate();

            t.Checked += delegate
            {
                track.Background = ToBrush(Theme.Accent);
                knob.Margin = new Thickness(22, 0, 0, 0);
                if (changed != null) changed(true);
            };
            t.Unchecked += delegate
            {
                track.Background = ToBrush(Theme.Track);
                knob.Margin = new Thickness(3, 0, 0, 0);
                if (changed != null) changed(false);
            };
            t.Loaded += delegate
            {
                // 动效：Margins 动画不够顺，用 RenderTransform 平移
                TranslateTransform tt = new TranslateTransform(value ? 0 : 0, 0);
                knob.RenderTransform = tt;
            };
            return t;
        }

        private static ControlTemplate SwitchTemplate()
        {
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            return new ControlTemplate(typeof(ToggleButton)) { VisualTree = presenter };
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        /* ---------------- 外壳 ---------------- */

        public void Rebuild()
        {
            _root.Children.Clear();
            _root.RowDefinitions.Clear();
            _root.ColumnDefinitions.Clear();

            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _root.Children.Add(BuildHeader());

            Border content = new Border();
            content.Background = ToBrush(Theme.Bg);
            content.Child = _body;
            Grid.SetRow(content, 1);
            _root.Children.Add(content);

            if (_current == null) BuildPluginList(); else BuildSettings(_current);
        }

        private UIElement BuildHeader()
        {
            Grid header = new Grid();
            header.Background = ToBrush(Theme.BgDeep);
            header.MouseLeftButtonDown += DragWindow;
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            try
            {
                BitmapSource icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    AppIcon.Get().Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                Image img = new Image();
                img.Source = icon; img.Width = 26; img.Height = 26;
                img.Margin = new Thickness(0, 0, 10, 0);
                left.Children.Add(img);
            }
            catch (Exception)
            {
            }
            left.Children.Add(Text("Touch+", 16, false, Theme.Text));
            _crumb.Text = _current == null ? "插件中心" : _current.Name;
            _crumb.FontSize = 12; _crumb.Margin = new Thickness(12, 0, 0, 0);
            _crumb.Foreground = ToBrush(Theme.TextFaint);
            left.Children.Add(_crumb);
            Grid.SetColumn(left, 0);
            header.Children.Add(left);

            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            _statusText.Margin = new Thickness(0, 0, 10, 0);
            _specText.Margin = new Thickness(0, 0, 16, 0);
            right.Children.Add(_statusDot);
            right.Children.Add(_statusText);
            right.Children.Add(_specText);
            right.Children.Add(WindowButton("—", delegate { WindowState = WindowState.Minimized; }));
            right.Children.Add(WindowButton("✕", delegate { Close(); }));
            Grid.SetColumn(right, 2);
            header.Children.Add(right);

            RefreshStatus();

            Border wrap = new Border();
            wrap.Child = header;
            wrap.BorderBrush = ToBrush(Theme.Line);
            wrap.BorderThickness = new Thickness(0, 0, 0, 1);
            return wrap;
        }

        private Button WindowButton(string glyph, Action click)
        {
            Button b = new Button();
            b.Content = glyph;
            b.Width = 34; b.Height = 30;
            b.FontSize = 13;
            b.Foreground = ToBrush(Theme.TextDim);
            b.Background = Brushes.Transparent;
            b.BorderThickness = new Thickness(0);
            b.Cursor = Cursors.Hand;
            b.Click += delegate { click(); };
            return b;
        }

        private void RefreshStatus()
        {
            bool running = _host.RunningCount > 0;
            _statusDot.Foreground = ToBrush(running ? Theme.Ok : Theme.TextFaint);
            _statusText.Text = running ? "运行中" : "全部停用";
            _statusDot.Foreground = ToBrush(running ? Theme.Ok : Theme.TextFaint);
            _specText.Text = _context.Settings.SystemDoubleClickWidth + "px / "
                + _context.Settings.SystemDoubleClickTime + "ms";
        }

        /* ---------------- 视图一：插件中心 ---------------- */

        private void BuildPluginList()
        {
            _current = null;
            _crumb.Text = "插件中心";
            _crumb.Text = "插件中心";

            Grid page = new Grid();
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            /* 工具条 */
            Grid bar = new Grid();
            bar.Margin = new Thickness(24, 0, 20, 0);
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock count = Text(_host.Count + " 个插件 · " + _host.RunningCount + " 个已启用", 13, false, Theme.TextDim);
            count.VerticalAlignment = VerticalAlignment.Center;
            bar.Children.Add(count);

            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(ThemeSelector());
            Button about = FlatButton("关于 Touch+", false, ShowHostAbout);
            about.Margin = new Thickness(10, 0, 0, 0);
            actions.Children.Add(about);
            Button add = FlatButton("添加插件", true, AddPlugin);
            add.Margin = new Thickness(10, 0, 0, 0);
            actions.Children.Add(add);
            Grid.SetColumn(actions, 1);
            bar.Children.Add(actions);
            Grid.SetRow(bar, 0);
            page.Children.Add(bar);

            /* 插件列表 */
            StackPanel list = new StackPanel { Margin = new Thickness(24, 0, 24, 0) };
            if (_host.Count == 0)
            {
                TextBlock empty = Text("还没有安装插件。点右上角「添加插件…」选择插件包（.dll）。", 13, false, Theme.TextDim);
                empty.Margin = new Thickness(0, 40, 0, 0);
                list.Children.Add(empty);
            }
            foreach (IPatchPlugin p in _host.Plugins) list.Children.Add(PluginRow(p));
            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.Content = list;
            Grid.SetRow(scroll, 1);
            page.Children.Add(scroll);

            _body.Content = page;
        }

        private UIElement ThemeSelector()
        {
            StackPanel group = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            string mode = _context.Settings.ThemeMode;
            group.Children.Add(ModeButton("跟随系统", mode == "Auto", "Auto"));
            group.Children.Add(ModeButton("浅色", mode == "Light", "Light"));
            group.Children.Add(ModeButton("深色", mode == "Dark", "Dark"));
            return group;
        }

        private Button ModeButton(string text, bool selected, string mode)
        {
            Button b = FlatButton(text, selected, delegate
            {
                _context.Settings.ThemeMode = mode;
                if (!_context.Settings.ReadOnlySession) SettingsStore.Save(_context.Settings);
                Theme.ApplyMode(mode == "Dark" ? Theme.ThemeMode.Dark
                    : (mode == "Light" ? Theme.ThemeMode.Light : Theme.ThemeMode.Auto));
            });
            b.Margin = new Thickness(2, 0, 2, 0);
            b.MinWidth = 58;
            b.Padding = new Thickness(10, 6, 10, 6);
            return b;
        }

        private UIElement PluginRow(IPatchPlugin plugin)
        {
            Border row = new Border();
            row.BorderBrush = ToBrush(Theme.Line);
            row.BorderThickness = new Thickness(0, 0, 0, 1);
            row.Padding = new Thickness(0, 16, 0, 16);

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            g.Children.Add(PluginGlyph(plugin));

            StackPanel info = new StackPanel();
            StackPanel titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(Text(plugin.Name, 15, false, Theme.Text));
            TextBlock safe = Text(plugin.Safety == PluginSafety.Safe ? "安全" : "高级", 11, false,
                plugin.Safety == PluginSafety.Safe ? Theme.Ok : Theme.Warn);
            safe.Margin = new Thickness(10, 3, 0, 0);
            titleRow.Children.Add(safe);
            info.Children.Add(titleRow);

            TextBlock summary = Text(plugin.Summary, 12, false, Theme.TextDim);
            summary.Margin = new Thickness(0, 6, 0, 0);
            summary.TextWrapping = TextWrapping.NoWrap;
            info.Children.Add(summary);

            PluginStats stats = null;
            try { stats = plugin.GetStats(); } catch (Exception) { }
            if (stats != null && !string.IsNullOrEmpty(stats.Detail))
            {
                TextBlock detail = Text(stats.Detail, 11, true, Theme.TextFaint);
                detail.Margin = new Thickness(0, 6, 0, 0);
                info.Children.Add(detail);
            }
            Grid.SetColumn(info, 1);
            g.Children.Add(info);

            Border stamp = new Border();
            stamp.BorderBrush = ToBrush(plugin.Enabled ? Theme.Ok : Theme.Line);
            stamp.BorderThickness = new Thickness(1);
            stamp.Padding = new Thickness(10, 3, 10, 3);
            stamp.Margin = new Thickness(10, 0, 0, 0);
            stamp.VerticalAlignment = VerticalAlignment.Center;
            System.Drawing.Color stampColor = plugin.Enabled ? Theme.Ok : Theme.TextFaint;
            stamp.Child = Text(plugin.Enabled ? "已启用" : "未启用", 11, false, stampColor);
            Grid.SetColumn(stamp, 2);
            g.Children.Add(stamp);

            Button settings = FlatButton("设置", false, delegate { OpenSettings(plugin); });
            settings.Margin = new Thickness(10, 0, 0, 0);
            settings.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(settings, 3);
            g.Children.Add(settings);

            ToggleButton sw = Switch(plugin.Enabled, delegate(bool on)
            {
                if (on == plugin.Enabled) return;
                string error;
                if (!_host.SetEnabled(plugin, on, out error))
                {
                    MessageBox.Show(this, "启用失败：" + error, "Touch+", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                plugin.SaveConfig();
                Dispatcher.BeginInvoke(new Action(Rebuild));
            });
            sw.Margin = new Thickness(12, 0, 4, 0);
            sw.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(sw, 4);
            g.Children.Add(sw);

            row.Child = g;
            return row;
        }

        private UIElement PluginGlyph(IPatchPlugin plugin)
        {
            Border tile = new Border();
            tile.Width = 40; tile.Height = 40;
            tile.CornerRadius = new CornerRadius(9);
            tile.VerticalAlignment = VerticalAlignment.Top;
            LinearGradientBrush brush = new LinearGradientBrush(
                System.Windows.Media.Color.FromRgb(27, 31, 36),
                System.Windows.Media.Color.FromRgb(8, 9, 11), 90);
            tile.Background = brush;
            tile.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(38, 255, 255, 255));
            tile.BorderThickness = new Thickness(1);

            Canvas c = new Canvas { Width = 40, Height = 40 };
            Rectangle zone = new Rectangle();
            zone.Width = 18; zone.Height = 18;
            zone.Stroke = Brushes.White; zone.StrokeThickness = 1;
            zone.StrokeDashArray = new DoubleCollection(new double[] { 2, 2 });
            Canvas.SetLeft(zone, 11); Canvas.SetTop(zone, 11);
            c.Children.Add(zone);
            Ellipse ok = new Ellipse();
            ok.Width = 4; ok.Height = 4; ok.Fill = Brushes.White;
            Canvas.SetLeft(ok, 18); Canvas.SetTop(ok, 23);
            c.Children.Add(ok);
            Ellipse bad = new Ellipse();
            bad.Width = 4; bad.Height = 4; bad.Fill = ToBrush(Theme.Accent);
            Canvas.SetLeft(bad, 27); Canvas.SetTop(bad, 15);
            c.Children.Add(bad);
            tile.Child = c;
            return tile;
        }

        private void OpenSettings(IPatchPlugin plugin)
        {
            _current = plugin;
            Rebuild();
        }

        private void ShowHostAbout()
        {
            MessageBox.Show(this,
                "Touch+ v" + Program.Version + "\r\n"
                + SystemInfo.VersionText + "\r\n"
                + "插件接口 " + PluginApiInfo.Version + "\r\n"
                + "已装插件 " + _host.Count + " 个 · 已启用 " + _host.RunningCount + " 个\r\n\r\n"
                + "数据目录：" + SettingsStore.DataDirectory,
                "关于 Touch+", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void AddPlugin()
        {
            using (System.Windows.Forms.OpenFileDialog dialog = new System.Windows.Forms.OpenFileDialog())
            {
                dialog.Filter = "Touch+ 插件 (*.dll)|*.dll|所有文件 (*.*)|*.*";
                dialog.Title = "选择插件包";
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

                string error;
                IPatchPlugin plugin = PluginLoader.Load(dialog.FileName, _context, out error);
                if (plugin == null)
                {
                    MessageBox.Show(this, "不能作为插件使用：\r\n" + error, "添加插件", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!PluginLoader.CheckCapabilities(plugin, _host.Capabilities, out error))
                {
                    MessageBox.Show(this, "权限超出宿主授权：\r\n" + error, "添加插件", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (_host.Find(plugin.Id) != null)
                {
                    MessageBox.Show(this, "已安装同 ID 插件：" + plugin.Id, "添加插件", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                _host.Add(plugin);
                if (!_context.Settings.PluginPaths.Contains(dialog.FileName))
                    _context.Settings.PluginPaths.Add(dialog.FileName);
                if (!_context.Settings.ReadOnlySession) SettingsStore.Save(_context.Settings);
                Rebuild();
            }
        }

        /* ---------------- 视图二：插件设置 ---------------- */

        private void BuildSettings(IPatchPlugin plugin)
        {
            _crumb.Text = plugin.Name + " · 设置";

            Grid page = new Grid();
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            page.Children.Add(BuildNav());

            Border line = new Border();
            line.Background = ToBrush(Theme.Line);
            line.Width = 1;
            Grid.SetColumn(line, 1);
            page.Children.Add(line);

            ContentControl content = new ContentControl();
            content.Content = BuildPage(plugin);
            Grid.SetColumn(content, 2);
            page.Children.Add(content);

            _body.Content = page;
        }

        private UIElement BuildNav()
        {
            StackPanel nav = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            nav.Children.Add(NavItem("概览", 0));
            nav.Children.Add(NavItem("参数", 1));
            nav.Children.Add(NavItem("日志", 2));
            nav.Children.Add(NavItem("关于", 3));

            Button back = FlatButton("← 插件列表", false, delegate { _current = null; Rebuild(); });
            back.Margin = new Thickness(16, 24, 16, 0);
            nav.Children.Add(back);
            return nav;
        }

        private UIElement NavItem(string title, int index)
        {
            Button b = new Button();
            b.Content = title;
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            b.Padding = new Thickness(20, 10, 0, 10);
            b.Margin = new Thickness(0, 2, 0, 2);
            b.FontSize = 13;
            b.Background = _nav == index ? ToBrush(Theme.Card) : Brushes.Transparent;
            b.Foreground = ToBrush(_nav == index ? Theme.Text : Theme.TextDim);
            b.BorderThickness = new Thickness(_nav == index ? 2 : 0, 0, 0, 0);
            b.BorderBrush = ToBrush(Theme.Accent);
            b.Cursor = Cursors.Hand;
            b.FontWeight = _nav == index ? FontWeights.Bold : FontWeights.Normal;
            b.Click += delegate { _nav = index; Rebuild(); };
            return b;
        }

        private UIElement BuildPage(IPatchPlugin plugin)
        {
            if (_nav == 1) return BuildParams(plugin);
            if (_nav == 2) return BuildLog();
            if (_nav == 3) return BuildAboutPlugin(plugin);
            return BuildOverview(plugin);
        }

        private UIElement BuildOverview(IPatchPlugin plugin)
        {
            StackPanel stack = new StackPanel { Margin = new Thickness(24, 16, 24, 16) };

            /* 状态区 */
            StackPanel head = new StackPanel();
            StackPanel titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleRow.Children.Add(Text(plugin.Enabled ? plugin.Name + " 运行中" : plugin.Name + " 已停用", 26, false, Theme.Text));
            ToggleButton sw = Switch(plugin.Enabled, delegate(bool on)
            {
                string error;
                _host.SetEnabled(plugin, on, out error);
                plugin.SaveConfig();
                Dispatcher.BeginInvoke(new Action(Rebuild));
            });
            sw.Margin = new Thickness(20, 4, 0, 0);
            titleRow.Children.Add(sw);
            head.Children.Add(titleRow);

            TextBlock sub = Text("两次点击落在 " + _context.Settings.SystemDoubleClickWidth + "px 内、间隔 "
                + _context.Settings.SystemDoubleClickTime + "ms 内，系统就会识别为双击。", 13, false, Theme.TextDim);
            sub.Margin = new Thickness(0, 10, 0, 0);
            head.Children.Add(sub);
            stack.Children.Add(head);

            /* 读数 */
            StackPanel readouts = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 0) };
            PluginStats stats = null;
            try { stats = plugin.GetStats(); } catch (Exception) { }
            readouts.Children.Add(Readout(stats == null ? "0" : stats.Count, stats == null ? "次数" : stats.CountLabel));
            readouts.Children.Add(Readout(stats == null ? "0" : stats.Average, stats == null ? "px" : stats.AverageLabel + " (px)"));
            stack.Children.Add(readouts);

            /* 容差场 */
            stack.Children.Add(SectionTitle("自检", "在这里点两下，验证补丁是否生效"));
            stack.Children.Add(ToleranceField(plugin));
            return stack;
        }

        private UIElement Readout(string value, string label)
        {
            StackPanel s = new StackPanel { Margin = new Thickness(0, 0, 48, 0) };
            s.Children.Add(Text(label, 11, false, Theme.TextFaint));
            TextBlock v = Text(value, 30, true, Theme.Accent);
            v.Margin = new Thickness(0, 4, 0, 0);
            s.Children.Add(v);
            return s;
        }

        private UIElement SectionTitle(string title, string subtitle)
        {
            StackPanel s = new StackPanel { Margin = new Thickness(0, 26, 0, 10) };
            Border rule = new Border();
            rule.Height = 1; rule.Background = ToBrush(Theme.Line);
            rule.Margin = new Thickness(0, 0, 0, 10);
            s.Children.Add(rule);
            s.Children.Add(Text(title, 15, false, Theme.Text));
            TextBlock sub = Text(subtitle, 12, false, Theme.TextFaint);
            sub.Margin = new Thickness(0, 3, 0, 0);
            s.Children.Add(sub);
            return s;
        }

        /// <summary>容差场：WPF 画布，落点 1:1、判定框按 DIP（50 = 系统判定尺寸）。</summary>
        private UIElement ToleranceField(IPatchPlugin plugin)
        {
            Border frame = new Border();
            frame.Background = ToBrush(Theme.Card);
            frame.BorderBrush = ToBrush(Theme.Line);
            frame.BorderThickness = new Thickness(1);
            frame.Height = 190;

            Canvas canvas = new Canvas();
            canvas.Background = Brushes.Transparent;
            canvas.ClipToBounds = true;
            frame.Child = canvas;

            // 方格纸
            GeometryDrawing gridGeom = new GeometryDrawing(null,
                new System.Windows.Media.Pen(ToBrush(System.Drawing.Color.FromArgb(90, Theme.Grid)), 1),
                new RectangleGeometry(new System.Windows.Rect(0, 0, 10, 10)));
            DrawingBrush tile = new DrawingBrush(gridGeom);
            tile.TileMode = TileMode.Tile;
            tile.Viewport = new System.Windows.Rect(0, 0, 10, 10);
            tile.ViewportUnits = BrushMappingMode.Absolute;
            Rectangle grid = new Rectangle();
            grid.Fill = tile;
            grid.Width = 2000; grid.Height = 400;
            canvas.Children.Add(grid);

            // 判定框（50 DIP）
            Rectangle zone = new Rectangle();
            zone.Width = 50; zone.Height = 50;
            zone.Stroke = ToBrush(Theme.TextDim);
            zone.StrokeThickness = 1;
            zone.StrokeDashArray = new DoubleCollection(new double[] { 3, 3 });
            zone.Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(18, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B));
            Canvas.SetLeft(zone, 300); Canvas.SetTop(zone, 60);
            canvas.Children.Add(zone);

            TextBlock readout = Text("--", 34, true, Theme.Text);
            Canvas.SetLeft(readout, 16); Canvas.SetTop(readout, 140);
            canvas.Children.Add(readout);

            TextBlock unit = Text("px", 12, false, Theme.TextFaint);
            Canvas.SetLeft(unit, 76); Canvas.SetTop(unit, 156);
            canvas.Children.Add(unit);

            TextBlock verdict = Text("在这里连续点两下，看看两次落点差多少", 12, false, Theme.TextFaint);
            Canvas.SetLeft(verdict, 110); Canvas.SetTop(verdict, 156);
            canvas.Children.Add(verdict);

            TextBlock label = Text("容差场 · 1:1 实尺 · 判定范围 " + _context.Settings.SystemDoubleClickWidth + "px", 11, false, Theme.TextFaint);
            Canvas.SetLeft(label, 16); Canvas.SetTop(label, 12);
            canvas.Children.Add(label);

            // 点按逻辑
            Point prev = new Point(0, 0);
            DateTime prevAt = DateTime.MinValue;
            bool hasPrev = false;
            int dblBase = 0;
            int nativeClicks = 0;
            List<double> markXs = new List<double>();
            List<double> markYs = new List<double>();

            canvas.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                System.Windows.Point pt = e.GetPosition(canvas);
                Point px = new Point(pt.X, pt.Y);
                DateTime now = DateTime.Now;
                if (hasPrev && (now - prevAt).TotalMilliseconds <= 2000)
                {
                    double dx = px.X - prev.X, dy = px.Y - prev.Y;
                    int dist = (int)Math.Round(Math.Sqrt(dx * dx + dy * dy));
                    bool inside = dist <= _context.Settings.SystemDoubleClickWidth;
                    bool native = nativeClicks > dblBase;

                    Line link = new Line();
                    link.X1 = prev.X; link.Y1 = prev.Y; link.X2 = px.X; link.Y2 = px.Y;
                    link.Stroke = ToBrush(inside || native ? Theme.Ok : Theme.Err);
                    link.StrokeThickness = 1.2;
                    canvas.Children.Add(link);

                    Ellipse a = new Ellipse();
                    a.Width = 8; a.Height = 8;
                    a.Stroke = ToBrush(Theme.TextDim); a.StrokeThickness = 1.2;
                    Canvas.SetLeft(a, prev.X - 4); Canvas.SetTop(a, prev.Y - 4);
                    canvas.Children.Add(a);

                    Ellipse b = new Ellipse();
                    b.Width = 10; b.Height = 10;
                    b.Fill = ToBrush(inside || native ? Theme.Ok : Theme.Err);
                    Canvas.SetLeft(b, px.X - 5); Canvas.SetTop(b, px.Y - 5);
                    canvas.Children.Add(b);

                    markXs.Add(px.X); markYs.Add(px.Y);
                    Canvas.SetLeft(zone, prev.X - 25); Canvas.SetTop(zone, prev.Y - 25);

                    readout.Text = dist.ToString(CultureInfo.InvariantCulture);
                    readout.Foreground = ToBrush(inside || native ? Theme.Ok : Theme.Err);
                    unit.Text = "px";
                    verdict.Text = native ? "系统已识别为双击"
                        : (inside ? "判定范围内，但系统没合成双击" : "未识别：偏离超出判定范围");
                    verdict.Foreground = ToBrush(native ? Theme.Ok : (inside ? Theme.Warn : Theme.Err));
                    hasPrev = false;
                }
                else
                {
                    prev = px;
                    prevAt = now;
                    dblBase = nativeClicks;
                    hasPrev = true;
                }
            };

            // 用 HwndSource 钩子统计系统合成的双击
            frame.Loaded += delegate
            {
                Window w = Window.GetWindow(frame);
                Window owner = Window.GetWindow(frame);
                if (owner == null) return;
                System.Windows.Interop.HwndSource src = (System.Windows.Interop.HwndSource)System.Windows.Interop.HwndSource.FromHwnd(
                    new System.Windows.Interop.WindowInteropHelper(owner).Handle);
                if (src == null) return;
                src.AddHook(delegate(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
                {
                    if (msg == 0x0203) nativeClicks++;
                    return IntPtr.Zero;
                });
            };

            return frame;
        }

        private UIElement BuildParams(IPatchPlugin plugin)
        {
            StackPanel stack = new StackPanel { Margin = new Thickness(24, 16, 24, 16) };
            AppSettings s = _context.Settings;

            stack.Children.Add(SectionTitle("系统层参数（推荐）", "改完立即生效，可一键还原"));
            stack.Children.Add(SliderRow("双击矩形", 4, 200, 2, s.SystemDoubleClickWidth, "px", delegate(int v)
            {
                s.SystemDoubleClickWidth = v; s.SystemDoubleClickHeight = v;
                _context.Controller.ApplySettings(true, false);
                if (!s.ReadOnlySession) SettingsStore.Save(s);
            }));
            stack.Children.Add(SliderRow("双击时间窗", 150, 1500, 50, s.SystemDoubleClickTime, "ms", delegate(int v)
            {
                s.SystemDoubleClickTime = v;
                _context.Controller.ApplySettings(true, false);
                if (!s.ReadOnlySession) SettingsStore.Save(s);
            }));
            Button restore = FlatButton("还原系统默认", false, delegate
            {
                _context.Controller.CaptureOriginals();
                _context.Controller.RestoreSystemLayer();
                s.SystemDoubleClickWidth = 4; s.SystemDoubleClickHeight = 4; s.SystemDoubleClickTime = 500;
                if (!s.ReadOnlySession) SettingsStore.Save(s);
                Rebuild();
            });
            restore.Margin = new Thickness(0, 12, 0, 0);
            restore.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(restore);

            stack.Children.Add(SectionTitle("输入层参数（高级）", "仅在启用输入层修复时生效"));
            stack.Children.Add(SliderRow("容差", 8, 200, 2, s.Tolerance, "px", delegate(int v)
            {
                s.Tolerance = v; _context.Controller.ApplySettings(false, true);
                if (!s.ReadOnlySession) SettingsStore.Save(s);
            }));
            stack.Children.Add(SliderRow("时间窗", 200, 2000, 50, s.TimeWindow, "ms", delegate(int v)
            {
                s.TimeWindow = v; _context.Controller.ApplySettings(false, true);
                if (!s.ReadOnlySession) SettingsStore.Save(s);
            }));

            TextBlock note = Text("输入层需要全局钩子并会合成点击，默认关闭；系统层已经能解决绝大多数抖动。", 12, false, Theme.TextFaint);
            note.Margin = new Thickness(0, 16, 0, 0);
            note.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(note);

            return stack;
        }

        private UIElement SliderRow(string label, int min, int max, int step, int value, string unit, Action<int> changed)
        {
            StackPanel row = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock name = Text(label, 12, false, Theme.TextDim);
            head.Children.Add(name);
            TextBlock now = Text(value + " " + unit, 12, true, Theme.Text);
            Grid.SetColumn(now, 1);
            head.Children.Add(now);
            row.Children.Add(head);

            Slider slider = new Slider();
            slider.Minimum = min; slider.Maximum = max;
            slider.SmallChange = step; slider.LargeChange = step * 5;
            slider.Value = value;
            slider.IsSnapToTickEnabled = true;
            slider.TickFrequency = step;
            slider.Margin = new Thickness(0, 6, 0, 0);
            slider.ValueChanged += delegate
            {
                int v = (int)Math.Round(slider.Value);
                now.Text = v + " " + unit;
                if (changed != null) changed(v);
            };
            row.Children.Add(slider);
            return row;
        }

        private UIElement BuildLog()
        {
            Grid page = new Grid();
            page.Margin = new Thickness(24, 16, 24, 16);
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            page.Children.Add(SectionTitle("运行日志", "记录每一次修复判定"));

            TextBox box = new TextBox();
            box.IsReadOnly = true;
            box.AcceptsReturn = true;
            box.FontFamily = new FontFamily("Consolas");
            box.FontSize = 12;
            box.Background = ToBrush(Theme.Card);
            box.Foreground = ToBrush(Theme.TextDim);
            box.BorderBrush = ToBrush(Theme.Line);
            box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            box.TextWrapping = TextWrapping.NoWrap;
            box.Padding = new Thickness(10, 8, 10, 8);
            foreach (LogEntry e in Log.Instance.Snapshot())
                box.AppendText(e.TimeText + "  " + e.Message + Environment.NewLine);
            Grid.SetRow(box, 1);
            page.Children.Add(box);

            Log.Instance.EntryAdded += delegate(object s2, LogEntry e2)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    box.AppendText(e2.TimeText + "  " + e2.Message + Environment.NewLine);
                    box.ScrollToEnd();
                }));
            };
            return page;
        }

        private UIElement BuildAboutPlugin(IPatchPlugin plugin)
        {
            StackPanel stack = new StackPanel { Margin = new Thickness(24, 16, 24, 16) };
            stack.Children.Add(SectionTitle("关于 " + plugin.Name, "插件信息与它对本机做的改动"));

            StackPanel rows = new StackPanel();
            rows.Children.Add(Kv("插件", plugin.Name));
            rows.Children.Add(Kv("标识", plugin.Id));
            rows.Children.Add(Kv("安全级别", plugin.Safety == PluginSafety.Safe
                ? "安全（只改 Windows 自己的参数，可一键还原）" : "高级（需要钩子或合成输入）"));
            rows.Children.Add(Kv("状态", plugin.Enabled ? "已启用" : "未启用"));
            rows.Children.Add(Kv("运行于", "Touch+ v" + Program.Version + "，插件接口 " + PluginApiInfo.Version));
            stack.Children.Add(rows);

            TextBlock what = Text(plugin.Summary, 13, false, Theme.TextDim);
            what.Margin = new Thickness(0, 16, 0, 0);
            what.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(what);

            TextBlock changes = Text(
                "对本机做的改动：\r\n"
                + "  · 放宽 Windows 双击矩形与时间窗（HKCU\\Control Panel\\Mouse）\r\n"
                + "  · 原值已备份，点「还原系统默认」或停用插件都会收回\r\n"
                + "  · 不写系统目录、不装服务、不驻留驱动", 12, false, Theme.TextFaint);
            changes.Margin = new Thickness(0, 16, 0, 0);
            changes.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(changes);
            return stack;
        }

        private UIElement Kv(string key, string value)
        {
            Grid row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(Text(key, 12, false, Theme.TextFaint));
            TextBlock v = Text(value, 12, true, Theme.Text);
            v.TextTrimming = TextTrimming.None;
            v.TextWrapping = TextWrapping.Wrap;
            Grid.SetColumn(v, 1);
            row.Children.Add(v);
            return row;
        }

        public void ShowFromOtherInstance()
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Show();
            Activate();
        }
    }
}
