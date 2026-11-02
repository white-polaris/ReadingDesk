using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace DDLDesk;

internal static class Ui
{
    public static readonly Brush Accent = Brush("#83E2CF"), Muted = Brush("#98A3B3"), Red = Brush("#FF8E96");
    public static SolidColorBrush Brush(string color) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!; b.Freeze(); return b; }
    public static TextBlock Text(string text, double size = 13, Brush? brush = null) => new()
    { Text = text, FontSize = size, Foreground = brush ?? Brush("#F0F3F7"), TextWrapping = TextWrapping.Wrap };
    public static Button Button(string text, string help, Action action, bool icon = false)
    {
        var b = new Button { Content = text, ToolTip = help, Padding = new Thickness(9, 6, 9, 6), MinWidth = 30, MinHeight = 30, Focusable = true };
        if (icon) b.Content = new TextBlock { Text = text, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13 };
        AutomationProperties.SetName(b, help);
        b.Click += (_, e) => { e.Handled = true; action(); };
        return b;
    }
    public static void Label(Panel panel, string value)
    { var t = Text(value, 12, Muted); t.Margin = new Thickness(0, 14, 0, 7); panel.Children.Add(t); }
    public static Border Shell(UIElement content, Thickness? padding = null) => new()
    { Background = Brush("#F219212C"), BorderBrush = Brush("#3B4859"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = padding ?? new Thickness(22), Child = content };
    public static void EnsureCalendarHeader(DatePicker date)
    {
        if (date.Template.FindName("PART_Popup", date) is not Popup popup || popup.Child == null) return;
        var item = FindChild<CalendarItem>(popup.Child);
        if (item == null) return;
        item.ApplyTemplate();
        if (item.Template.FindName("PART_HeaderButton", item) is not Button header) return;
        // The native CalendarItem header template otherwise creates an implicit
        // TextBlock that picks up the app's light text on its pale background.
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding());
        text.SetValue(TextBlock.ForegroundProperty, Brush("#182432"));
        text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        text.SetValue(TextBlock.FontSizeProperty, 13d);
        text.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        header.ContentTemplate = new DataTemplate { VisualTree = text };
        header.Foreground = Brush("#182432");
    }
    private static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindChild<T>(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }
}

internal class DeskDialog : Window
{
    public StackPanel Body { get; } = new();
    public DeskDialog(Window owner, string title, double width = 440)
    {
        Owner = owner; Title = title; Width = width; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        MaxHeight = Math.Max(360, Native.WorkArea(owner).Height - 32);
        var root = new DockPanel();
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var close = Ui.Button("\uE711", "关闭", () => DialogResult = false, true);
        close.Background = Brushes.Transparent; close.BorderThickness = new Thickness(0);
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var heading = Ui.Text(title, 20); heading.FontWeight = FontWeights.SemiBold; heading.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(heading);
        header.MouseLeftButtonDown += (_, e) => { if (e.Source == header || e.Source == heading) DragMove(); };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var scroll = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var smooth = new SmoothScroll(scroll); Closed += (_, _) => smooth.Dispose(); root.Children.Add(scroll);
        Content = WindowAppearance.Shell(this, root);
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { DialogResult = false; e.Handled = true; } };
    }
}

internal sealed class DeadlineDialog : DeskDialog
{
    public Deadline? Result { get; private set; }
    private readonly TextBox title = new() { TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 66, MaxHeight = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = 300 };
    private readonly DatePicker date = new() { FontSize = 14, MinHeight = 36 };
    private readonly TextBox time = new() { Width = 90, MaxLength = 5 };
    private readonly CheckBox undated = new() { Content = "截止时间未定", Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBox url = new() { MaxLength = 2048 };
    private readonly TextBox folder = new() { MaxLength = 2048 };
    private readonly TextBlock error = Ui.Text("", 12, Ui.Red);
    public DeadlineDialog(Window owner, Deadline? item = null) : base(owner, item == null ? "添加 DDL" : "编辑 DDL")
    {
        date.Style = (Style)Application.Current.FindResource("ReadableDatePicker");
        date.CalendarOpened += (_, _) => Dispatcher.BeginInvoke(() => Ui.EnsureCalendarHeader(date), System.Windows.Threading.DispatcherPriority.Loaded);
        Ui.Label(Body, "标题 · 必填"); Body.Children.Add(title);
        Ui.Label(Body, "截止日期与时间 · 本地时间 / 24 小时制");
        Body.Children.Add(undated);
        undated.Checked += (_, _) => UpdateDateEnabled();
        undated.Unchecked += (_, _) => UpdateDateEnabled();
        var dateRow = new DockPanel();
        DockPanel.SetDock(time, Dock.Right); time.Margin = new Thickness(10, 0, 0, 0); dateRow.Children.Add(time); dateRow.Children.Add(date); Body.Children.Add(dateRow);
        Ui.Label(Body, "提交网址 · 可选，http / https"); Body.Children.Add(url);
        Ui.Label(Body, "项目文件夹 · 可选");
        var folderRow = new DockPanel();
        var browse = Ui.Button("\uE8B7", "选择文件夹", () =>
        {
            var picker = new Microsoft.Win32.OpenFolderDialog { Title = "选择项目文件夹", Multiselect = false };
            if (picker.ShowDialog(this) == true) folder.Text = picker.FolderName;
        }, true);
        DockPanel.SetDock(browse, Dock.Right); browse.Margin = new Thickness(8, 0, 0, 0); folderRow.Children.Add(browse); folderRow.Children.Add(folder); Body.Children.Add(folderRow);
        error.Margin = new Thickness(0, 12, 0, 8); Body.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Ui.Button("取消", "取消编辑", () => DialogResult = false); cancel.Margin = new Thickness(0, 0, 8, 0); actions.Children.Add(cancel);
        var save = Ui.Button(item == null ? "添加" : "保存", "保存 DDL", () => Save(item)); save.Background = Ui.Brush("#235548"); save.Foreground = Ui.Accent; actions.Children.Add(save); Body.Children.Add(actions);
        title.Text = item?.Title ?? ""; date.SelectedDate = item?.Due?.Date ?? DateTime.Today.AddDays(1); time.Text = item?.Due?.ToString("HH:mm") ?? "18:00";
        undated.IsChecked = item != null && !item.Due.HasValue; UpdateDateEnabled();
        url.Text = item?.Url ?? ""; folder.Text = item?.Folder ?? "";
        System.Windows.Automation.AutomationProperties.SetName(title, "DDL 标题");
        System.Windows.Automation.AutomationProperties.SetName(time, "截止时间 HH:mm");
        System.Windows.Automation.AutomationProperties.SetName(url, "提交网址");
        System.Windows.Automation.AutomationProperties.SetName(folder, "项目文件夹");
        Loaded += (_, _) => title.Focus();
    }
    private void UpdateDateEnabled() { date.IsEnabled = time.IsEnabled = undated.IsChecked != true; }
    private void Save(Deadline? original)
    {
        if (string.IsNullOrWhiteSpace(title.Text)) { error.Text = "请填写标题。"; title.Focus(); return; }
        DateTime? due = null;
        if (undated.IsChecked != true)
        {
            if (date.SelectedDate is not DateTime day || !TimeSpan.TryParseExact(time.Text.Trim(), @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture, out var clock) || clock.TotalHours >= 24)
            { error.Text = "请选择日期，时间格式例如 09:30 或 23:59。"; return; }
            due = day.Date.Add(clock);
        }
        string address = url.Text.Trim();
        if (address.Length > 0 && (!Uri.TryCreate(address, UriKind.Absolute, out var parsed) || (parsed.Scheme != "http" && parsed.Scheme != "https")))
        { error.Text = "提交网址必须以 http:// 或 https:// 开头。"; return; }
        string path = folder.Text.Trim().Trim('"');
        if (path.Length > 0 && !System.IO.Directory.Exists(path)) { error.Text = "项目文件夹不存在，请重新选择。"; return; }
        Result = new Deadline { Id = original?.Id ?? Guid.NewGuid(), Title = title.Text.Trim(), Due = due, Url = address, Folder = path, Manual = original?.Manual ?? false, Color = original != null && CardColors.IsValid(original.Color) ? original.Color : CardColors.Pick(), ReadingNote = original?.ReadingNote };
        DialogResult = true;
    }
}

internal sealed class DeleteConfirmationDialog : DeskDialog
{
    private readonly Button cancel;
    private readonly Button confirm;
    public DeleteConfirmationDialog(Window owner, FrameworkElement anchor, Deadline item) : base(owner, "删除 DDL", Math.Min(340, owner.ActualWidth - 24))
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = owner.Left + (owner.ActualWidth - Width) / 2; Top = owner.Top + 12;
        var title = Ui.Text(item.Title, 14); title.FontWeight = FontWeights.SemiBold;
        title.LineHeight = 21; title.MaxHeight = 42; title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.ToolTip = item.Title; title.Margin = new Thickness(0, 8, 0, 10); Body.Children.Add(title);
        Body.Children.Add(Ui.Text("确认删除？删除后仍可在 8 秒内撤销。", 12, Ui.Muted));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        cancel = Ui.Button("取消", "取消删除", () => DialogResult = false); cancel.IsDefault = true;
        cancel.Margin = new Thickness(0, 0, 8, 0); actions.Children.Add(cancel);
        confirm = Ui.Button("删除", "确认删除", () => DialogResult = true);
        confirm.Background = Ui.Brush("#63323D"); confirm.Foreground = Ui.Brush("#FFE1E5"); actions.Children.Add(confirm); Body.Children.Add(actions);
        Loaded += (_, _) =>
        {
            // Align the confirmation's bottom with the clicked card, then clamp
            // it inside the owner and monitor work area instead of screen center.
            var work = Native.WorkArea(owner);
            double left = Math.Max(work.Left + 4, owner.Left + 8);
            double right = Math.Min(work.Right - 4, owner.Left + owner.ActualWidth - 8);
            double top = Math.Max(work.Top + 4, owner.Top + 8);
            double bottom = Math.Min(work.Bottom - 4, owner.Top + owner.ActualHeight - 8);
            double desiredTop = owner.Top + anchor.TranslatePoint(new Point(0, anchor.ActualHeight), owner).Y - ActualHeight + 12;
            Left = Math.Clamp(owner.Left + (owner.ActualWidth - ActualWidth) / 2, left, Math.Max(left, right - ActualWidth));
            Top = Math.Clamp(desiredTop, top, Math.Max(top, bottom - ActualHeight));
            cancel.Focus();
        };
    }
}

internal sealed class SettingsDialog : DeskDialog
{
    public bool Startup { get; private set; }
    public int SafeDistance { get; private set; }
    public double BackgroundOpacity { get; private set; }
    public SettingsDialog(Window owner, DeskState state) : base(owner, "设置")
    {
        var startup = new CheckBox { Content = "开机自动启动", IsChecked = state.AutoStart };
        if (App.SuppressStartup) { startup.IsEnabled = false; startup.ToolTip = "当前测试会话不会修改开机启动项"; }
        Body.Children.Add(startup);
        Ui.Label(Body, "甩出安全距离 · 超出此范围松手才删除");
        var safe = new Slider { Minimum = 60, Maximum = 400, TickFrequency = 10, IsSnapToTickEnabled = true, Value = state.SafeDistance };
        var safeLabel = Ui.Text($"{state.SafeDistance} 逻辑像素", 12, Ui.Accent);
        safe.ValueChanged += (_, _) => safeLabel.Text = $"{safe.Value:0} 逻辑像素"; Body.Children.Add(safe); Body.Children.Add(safeLabel);
        Ui.Label(Body, "窗口背景不透明度");
        var opacity = new Slider { Minimum = .55, Maximum = 1, TickFrequency = .01, IsSnapToTickEnabled = true, Value = state.BackgroundOpacity };
        var opacityLabel = Ui.Text($"{state.BackgroundOpacity:P0}", 12, Ui.Accent);
        opacity.ValueChanged += (_, _) => opacityLabel.Text = $"{opacity.Value:P0}"; Body.Children.Add(opacity); Body.Children.Add(opacityLabel);
        Ui.Label(Body, "操作提示");
        Body.Children.Add(Ui.Text("双击卡片编辑 · 拖动卡片排序\n拖动时可滚轮翻页，靠近边缘自动滚动\n拖到安全区外松手：按当前速度抛出并删除\nEsc 取消拖动 · 删除后可短时撤销\n一键整理会清除手动标记并按截止时间排序", 12, Ui.Muted));
        var save = Ui.Button("保存设置", "保存设置", () =>
        {
            Startup = startup.IsChecked == true; SafeDistance = (int)safe.Value; BackgroundOpacity = opacity.Value; DialogResult = true;
        }); save.Margin = new Thickness(0, 20, 0, 0); Body.Children.Add(save);
    }
}
