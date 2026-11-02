using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DDLDesk;

internal sealed class CardView
{
    public Deadline Item { get; }
    public Border Root { get; }
    public TextBlock Title { get; }
    public TextBlock Remaining { get; }
    public Line Strike { get; }
    public Border InsertMark { get; }
    public CardView(Deadline item, Action edit, Action complete, Action delete, Action<string, bool> open)
    {
        Item = item;
        var content = new StackPanel { Margin = new Thickness(14, 12, 14, 10) };
        if (item.ReadingNote != null) { var badge = Ui.Text("READING / 阅读", 10, Ui.Accent); badge.Margin = new Thickness(0, 0, 0, 6); content.Children.Add(badge); }
        var titleGrid = new Grid();
        Title = Ui.Text(item.Title, 16); Title.FontWeight = FontWeights.SemiBold; Title.LineHeight = 23;
        titleGrid.Children.Add(Title);
        Strike = new Line { Stroke = Ui.Accent, StrokeThickness = 2, X1 = 0, X2 = 0, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
        titleGrid.Children.Add(Strike); content.Children.Add(titleGrid);
        var due = Ui.Text(item.Due?.ToString("yyyy.MM.dd  HH:mm") ?? "截止时间未定", 12, Ui.Brush("#E0E8F2")); due.Margin = new Thickness(0, 8, 0, 0); content.Children.Add(due);
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var done = Ui.Button("\uE73E", "完成：" + item.Title, complete, true); done.Foreground = Ui.Accent; done.Background = Brushes.Transparent; done.BorderThickness = new Thickness(0);
        var trash = Ui.Button("\uE74D", "删除：" + item.Title, delete, true); trash.Foreground = Ui.Muted; trash.Background = Brushes.Transparent; trash.BorderThickness = new Thickness(0);
        actions.Children.Add(done); actions.Children.Add(trash); DockPanel.SetDock(actions, Dock.Right); bottom.Children.Add(actions);
        var detail = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Remaining = Ui.Text("", 12); detail.Children.Add(Remaining);
        if (item.Manual) { var manual = Ui.Text("手动排序", 10, Ui.Brush("#E0E8F2")); manual.Margin = new Thickness(0, 4, 0, 0); detail.Children.Add(manual); }
        bottom.Children.Add(detail);
        if (!string.IsNullOrEmpty(item.Url) || !string.IsNullOrEmpty(item.Folder) || item.ReadingNote != null)
        {
            var links = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            if (item.ReadingNote != null)
            {
                var noteButton = Ui.Button("查看笔记", "查看与核对阅读笔记", edit); noteButton.FontSize = 11;
                noteButton.Margin = new Thickness(0, 0, 6, 4); links.Children.Add(noteButton);
            }
            if (!string.IsNullOrEmpty(item.Url))
            {
                var link = Ui.Button(item.ReadingNote != null ? "↗ 原文链接" : "↗ 提交网址", item.Url, () => open(item.Url, false)); link.FontSize = 11; link.Padding = new Thickness(8, 4, 8, 4); link.Margin = new Thickness(0, 0, 6, 4); links.Children.Add(link);
            }
            if (!string.IsNullOrEmpty(item.Folder))
            {
                string label = System.IO.Path.GetFileName(item.Folder.TrimEnd('\\', '/'));
                if (string.IsNullOrEmpty(label)) label = item.Folder;
                if (label.Length > 20) label = label[..19] + "…";
                var folder = Ui.Button("▱ " + label, item.Folder, () => open(item.Folder, true)); folder.FontSize = 11; folder.Padding = new Thickness(8, 4, 8, 4); folder.Margin = new Thickness(0, 0, 6, 4); links.Children.Add(folder);
            }
            content.Children.Add(links);
        }
        content.Children.Add(bottom);
        var grid = new Grid(); grid.Children.Add(content);
        InsertMark = new Border { Height = 3, Background = Ui.Accent, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Hidden, CornerRadius = new CornerRadius(1), IsHitTestVisible = false };
        grid.Children.Add(InsertMark);
        CardColors.EnsureColor(item);
        var color = (Color)ColorConverter.ConvertFromString(item.Color);
        var outline = new SolidColorBrush(Color.FromRgb((byte)Math.Min(255, color.R + 30), (byte)Math.Min(255, color.G + 30), (byte)Math.Min(255, color.B + 30))); outline.Freeze();
        Root = new Border { Child = grid, Background = Ui.Brush(item.Color), BorderBrush = outline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 0, 10), Cursor = Cursors.Hand, ToolTip = "双击编辑 · 按住拖动排序 · 拖出安全区松手删除" };
        System.Windows.Automation.AutomationProperties.SetName(Root, "DDL：" + item.Title);
        UpdateRemaining();
    }
    public void UpdateRemaining()
    {
        if (Item.Due is not DateTime due)
        {
            Remaining.Text = "待安排"; Remaining.Foreground = Ui.Brush("#E0E8F2"); return;
        }
        var delta = due - DateTime.Now;
        Remaining.Foreground = delta.TotalSeconds < 0 ? Ui.Brush("#FFD1D8") : delta.TotalHours < 24 ? Ui.Brush("#FFE5B3") : Ui.Brush("#C5F7E9");
        if (delta.TotalSeconds < 0) Remaining.Text = delta.TotalDays <= -1 ? $"已逾期 {(int)-delta.TotalDays} 天" : delta.TotalHours <= -1 ? $"已逾期 {(int)-delta.TotalHours} 小时" : "已逾期不足 1 小时";
        else Remaining.Text = delta.TotalDays >= 1 ? $"剩余 {(int)delta.TotalDays} 天 {delta.Hours} 小时" : delta.TotalHours >= 1 ? $"剩余 {(int)delta.TotalHours} 小时 {delta.Minutes} 分钟" : $"剩余 {Math.Max(1, (int)Math.Ceiling(delta.TotalMinutes))} 分钟";
    }
    public FrameworkElement CreateDragVisual()
    {
        // A separate, locally arranged card has no ScrollViewer clipping or
        // StackPanel offset. Build it only once, when dragging starts.
        var copy = new CardView(Item, () => { }, () => { }, () => { }, (_, _) => { }).Root;
        copy.Margin = new Thickness(0);
        copy.Width = Root.ActualWidth;
        copy.Measure(new Size(Root.ActualWidth, double.PositiveInfinity));
        copy.Arrange(new Rect(0, 0, Root.ActualWidth, Root.ActualHeight));
        copy.UpdateLayout();
        return copy;
    }
}

public sealed class MainWindow : Window
{
    private readonly DeskState state;
    private readonly StateStore store;
    private readonly ModelSettingsStore modelSettings;
    private readonly IReadingAnalysisService analysisService;
    private readonly StackPanel cardsPanel = new();
    private readonly ScrollViewer viewer;
    private readonly SmoothScroll smoothScroll;
    private readonly TextBlock count = Ui.Text("", 11, Ui.Muted);
    private readonly TextBlock footer = Ui.Text("双击编辑 · 拖动排序", 11, Ui.Muted);
    private readonly Button undo;
    private readonly Border shell;
    private readonly List<CardView> cards = new();
    private readonly DispatcherTimer minuteTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly DispatcherTimer edgeTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer undoTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly System.Windows.Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon trayIcon;
    private bool quitting, saveError, sizing;
    private CardView? selected;
    private Point downPoint, grabOffset;
    private bool dragging;
    private DragGhost? ghost;
    private int slot;
    private readonly List<(Point Point, double Time)> samples = new();
    private TaskRemoval? lastRemoved;
    private static double Seconds => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

    public MainWindow(DeskState state, StateStore store, IReadingAnalysisService? analysisService = null)
    {
        this.state = state; this.store = store;
        this.analysisService = analysisService ?? new ReadingAnalysisService();
        modelSettings = new ModelSettingsStore(System.IO.Path.GetDirectoryName(store.FilePath)!);
        bool colorsAssigned = CardColors.Ensure(state.Items);
        Title = "研读桌面 · 文献与阅读任务"; Width = 400; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = true; Topmost = false;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        var root = new StackPanel();
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14), Background = Brushes.Transparent };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        var organize = Ui.Button("\uE8CB", "一键整理：按截止时间排序并清除手动标记", Organize, true);
        var settings = Ui.Button("\uE713", "设置", Settings, true);
        foreach (var b in new[] { organize, settings }) { b.Margin = new Thickness(5, 0, 0, 0); buttons.Children.Add(b); }
        DockPanel.SetDock(buttons, Dock.Right); header.Children.Add(buttons);
        var title = new StackPanel();
        var brand = Ui.Text("研读 / DESK", 19); brand.FontWeight = FontWeights.Bold; title.Children.Add(brand);
        count.Margin = new Thickness(0, 5, 0, 0); title.Children.Add(count); header.Children.Add(title);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (OverButton(e.OriginalSource as DependencyObject)) return;
            try { DragMove(); SavePosition(); } catch (InvalidOperationException) { }
        };
        root.Children.Add(header);
        var readingTools = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var newReading = Ui.Button("＋ 新建阅读", "粘贴文献片段并整理阅读笔记", () => EditReading(null));
        var completed = Ui.Button("已完成阅读", "查看完成后保留的阅读笔记", () => { CancelDrag(); new CompletedReadingsDialog(this, state, modelSettings, this.analysisService).ShowDialog(); });
        var model = Ui.Button("模型设置", "配置接口与加密保存的密钥", () => { CancelDrag(); new ModelSettingsDialog(this, modelSettings, this.analysisService).ShowDialog(); });
        foreach (var button in new[] { newReading, completed, model }) { button.FontSize = 11; button.Margin = new Thickness(0, 0, 6, 4); readingTools.Children.Add(button); }
        root.Children.Add(readingTools);
        viewer = new ScrollViewer { Content = cardsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false, PanningMode = PanningMode.VerticalOnly };
        smoothScroll = new SmoothScroll(viewer);
        viewer.ScrollChanged += (_, e) => { if (dragging && e.VerticalChange != 0) UpdateSlot(); };
        root.Children.Add(viewer);
        var footerRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0), MinHeight = 30 };
        undo = Ui.Button("撤销", "撤销上一次删除或完成", Undo); undo.FontSize = 11; undo.Padding = new Thickness(8, 4, 8, 4); undo.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(undo, Dock.Right); footerRow.Children.Add(undo); footer.VerticalAlignment = VerticalAlignment.Center; footerRow.Children.Add(footer); root.Children.Add(footerRow);
        shell = WindowAppearance.Shell(this, root, new Thickness(16)); Content = shell; SetBackground();
        trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        tray = new System.Windows.Forms.NotifyIcon { Text = "研读桌面 · 文献与阅读任务", Icon = trayIcon, Visible = true };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("显示 / 隐藏", null, (_, _) => Dispatcher.Invoke(() => { if (IsVisible) Hide(); else Reveal(); }));
        menu.Items.Add("新建阅读", null, (_, _) => Dispatcher.Invoke(() => { Reveal(); EditReading(null); }));
        menu.Items.Add("设置", null, (_, _) => Dispatcher.Invoke(() => { Reveal(); Settings(); }));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Quit)); tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(Reveal);
        minuteTimer.Tick += (_, _) => { foreach (var c in cards) c.UpdateRemaining(); UpdateCount(); };
        edgeTimer.Tick += (_, _) => EdgeScroll();
        undoTimer.Tick += (_, _) => ClearUndo();
        PreviewMouseMove += PointerMove;
        PreviewMouseLeftButtonUp += PointerUp;
        PreviewMouseWheel += (_, e) => { if (dragging) { smoothScroll.WheelDelta(e.Delta); e.Handled = true; } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && selected != null) { CancelDrag(); e.Handled = true; } };
        LostMouseCapture += (_, _) => { if (selected != null && Mouse.Captured != this) CancelDrag(); };
        Deactivated += (_, _) => { if (selected != null) CancelDrag(); };
        Loaded += (_, _) =>
        {
            var area = Native.WorkArea(this);
            Left = state.Left ?? area.Right - Width - 20; Top = state.Top ?? area.Top + 20;
            RenderCards(); ClampPosition(); minuteTimer.Start();
            if (colorsAssigned || !File.Exists(store.FilePath)) Save();
        };
        SizeChanged += (_, _) => { if (IsLoaded && !sizing) ClampPosition(); };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => { ResizeViewport(); ClampPosition(); });
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        Closing += (_, e) => { if (!quitting) { e.Cancel = true; Hide(); } };
        Closed += (_, _) => { minuteTimer.Stop(); edgeTimer.Stop(); undoTimer.Stop(); CancelDrag(); smoothScroll.Dispose(); tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); trayIcon.Dispose(); SystemEvents.DisplaySettingsChanged -= DisplayChanged; };
    }

    private static bool OverButton(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is ButtonBase || source is ScrollBar) return true;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : source is FrameworkContentElement f ? f.Parent : null;
        }
        return false;
    }

    private void SetBackground() => WindowAppearance.SetPanelOpacity(this, state.BackgroundOpacity);
    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { ResizeViewport(); ClampPosition(); });
    private void ClampPosition()
    {
        if (!IsLoaded) return;
        var area = Native.WorkArea(this);
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - ActualWidth));
        Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - ActualHeight));
    }
    private void SavePosition() { ClampPosition(); state.Left = Left; state.Top = Top; Save(); }
    public void Reveal() { Show(); WindowState = WindowState.Normal; ClampPosition(); Activate(); }
    private void Quit() { CancelDrag(); SavePosition(); quitting = true; Close(); Application.Current.Shutdown(); }
    private bool Save()
    {
        try { store.Save(state); saveError = false; return true; }
        catch (Exception ex)
        {
            if (!saveError) MessageBox.Show(this, "保存失败，请检查磁盘空间或数据目录权限。当前编辑内容会保留，尚未保存。\n" + ex.Message, "研读桌面", MessageBoxButton.OK, MessageBoxImage.Error);
            saveError = true; footer.Text = "保存失败 · 请检查数据目录"; footer.Foreground = Ui.Red; return false;
        }
    }
    private void UpdateCount()
    {
        int overdue = state.Items.Count(x => x.Due.HasValue && x.Due.Value < DateTime.Now);
        count.Text = $"{state.Items.Count} 个待办" + (overdue > 0 ? $"  ·  {overdue} 个已逾期" : "  ·  慢慢来，逐个完成");
    }
    private void RenderCards()
    {
        if (selected != null) CancelDrag();
        smoothScroll.Stop();
        double offset = viewer.VerticalOffset;
        cardsPanel.Children.Clear(); cards.Clear();
        foreach (var item in state.Items)
        {
            var card = new CardView(item, () => Edit(item), () => Complete(item), () => Delete(item), OpenTarget);
            card.Root.PreviewMouseLeftButtonDown += (_, e) => PointerDown(card, e);
            cards.Add(card); cardsPanel.Children.Add(card.Root);
        }
        if (cards.Count == 0)
        {
            var empty = new StackPanel { Margin = new Thickness(18, 22, 18, 22), HorizontalAlignment = HorizontalAlignment.Center };
            var sign = Ui.Text("✓", 32, Ui.Accent); sign.HorizontalAlignment = HorizontalAlignment.Center; empty.Children.Add(sign);
            var heading = Ui.Text("把下一件事放在这里", 15); heading.Margin = new Thickness(0, 8, 0, 6); empty.Children.Add(heading);
            var hint = Ui.Text("点击“新建阅读”，开始整理文献片段", 11, Ui.Muted); hint.HorizontalAlignment = HorizontalAlignment.Center; empty.Children.Add(hint);
            cardsPanel.Children.Add(empty);
        }
        UpdateCount(); ResizeViewport(); viewer.ScrollToVerticalOffset(offset);
    }
    private void ResizeViewport()
    {
        if (!IsLoaded || sizing) return;
        sizing = true;
        try
        {
            double available = Math.Max(110, Native.WorkArea(this).Height - 205);
            // A scrollbar changes available width and therefore wrapping/height.
            // Reconcile measurement after layout instead of assuming fixed card heights.
            for (int pass = 0; pass < 3; pass++)
            {
                double width = viewer.ViewportWidth > 0 ? viewer.ViewportWidth : Width - 45;
                cardsPanel.Measure(new Size(width, double.PositiveInfinity));
                double total = cardsPanel.Children.Cast<UIElement>().Take(3).Sum(x => x.DesiredSize.Height);
                viewer.Height = Math.Clamp(total, 100, available);
                UpdateLayout();
            }
        }
        finally { sizing = false; }
        ClampPosition();
    }
    private void Organize()
    {
        CancelDrag(); Ordering.Organize(state.Items); Save(); RenderCards(); viewer.ScrollToTop();
        FooterMessage("已按截止时间整理 · 手动标记已清除");
    }
    private void Edit(Deadline? item)
    {
        if (item?.ReadingNote != null) { EditReading(item); return; }
        CancelDrag();
        var dialog = new DeadlineDialog(this, item);
        if (dialog.ShowDialog() != true || dialog.Result == null) return;
        var result = dialog.Result;
        int index;
        if (item == null) index = Ordering.Insert(state.Items, result);
        else
        {
            index = Ordering.Replace(state.Items, item, result);
            if (index < 0) return;
        }
        Save(); RenderCards();
        cards[index].Root.BringIntoView();
    }
    private void EditReading(Deadline? item)
    {
        CancelDrag();
        new ReadingDialog(this, item, modelSettings, analysisService, result =>
        {
            var oldItems = state.Items.ToList();
            int index = item == null ? Ordering.Insert(state.Items, result) : Ordering.Replace(state.Items, item, result);
            if (index < 0) return false;
            if (!Save()) { state.Items = oldItems; return false; }
            RenderCards(); cards[index].Root.BringIntoView(); FooterMessage("阅读笔记已保存"); return true;
        }).ShowDialog();
    }
    private void Settings()
    {
        CancelDrag(); var dialog = new SettingsDialog(this, state);
        if (dialog.ShowDialog() != true) return;
        try { StartupIntegration.SetEnabled(dialog.Startup); }
        catch (Exception ex) { MessageBox.Show(this, "开机启动设置失败：\n" + ex.Message, "DDLDesk"); return; }
        state.AutoStart = dialog.Startup; state.SafeDistance = dialog.SafeDistance; state.BackgroundOpacity = dialog.BackgroundOpacity;
        SetBackground(); Save();
    }
    private void OpenTarget(string target, bool isFolder)
    {
        try
        {
            if (isFolder)
            {
                if (!Directory.Exists(target)) throw new DirectoryNotFoundException("项目文件夹已移动或不存在，请双击卡片重新选择。");
                Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { target }, UseShellExecute = true });
            }
            else
            {
                if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) throw new InvalidDataException("网址必须为 http 或 https。");
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private int Remove(Deadline item, bool complete = false)
    {
        var removal = TaskHistory.Remove(state, item, complete);
        if (removal == null) return -1;
        if (!Save()) { TaskHistory.Undo(state, removal); return -1; }
        lastRemoved = removal;
        undo.Visibility = Visibility.Visible; undoTimer.Stop(); undoTimer.Start(); return removal.Index;
    }
    private void Delete(Deadline item)
    {
        CancelDrag();
        var card = cards.FirstOrDefault(x => x.Item.Id == item.Id);
        if (card == null || !state.Items.Contains(item)) return;
        if (new DeleteConfirmationDialog(this, card.Root, item).ShowDialog() != true) return;
        if (Remove(item) >= 0) { RenderCards(); FooterMessage("已删除 · 8 秒内可撤销"); }
    }
    private void Complete(Deadline item)
    {
        var card = cards.FirstOrDefault(x => x.Item.Id == item.Id);
        if (card == null || Remove(item, true) < 0) return;
        card.Root.IsHitTestVisible = false;
        // Real text decoration covers every wrapped line; the moving line introduces the effect.
        card.Strike.BeginAnimation(Line.X2Property, new DoubleAnimation(0, Math.Max(20, card.Title.ActualWidth), TimeSpan.FromMilliseconds(240)));
        var pause = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
        pause.Tick += (_, _) =>
        {
            pause.Stop(); card.Title.TextDecorations = TextDecorations.Strikethrough;
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260));
            var shrink = new DoubleAnimation(card.Root.ActualHeight, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            shrink.Completed += (_, _) => { RenderCards(); FooterMessage(item.ReadingNote != null ? "已归档阅读笔记 · 8 秒内可撤销" : "已完成，做得好 · 8 秒内可撤销"); };
            card.Root.BeginAnimation(OpacityProperty, fade); card.Root.BeginAnimation(HeightProperty, shrink);
        };
        pause.Start(); FooterMessage("已完成 · 8 秒内可撤销");
    }
    private void Undo()
    {
        if (lastRemoved is not { } previous) return;
        var oldItems = state.Items.ToList(); var oldCompleted = state.CompletedReadings.ToList();
        if (!TaskHistory.Undo(state, previous)) return;
        if (!Save()) { state.Items = oldItems; state.CompletedReadings = oldCompleted; return; }
        ClearUndo(); RenderCards(); cards.First(x => x.Item.Id == previous.Item.Id).Root.BringIntoView(); FooterMessage("已撤销");
    }
    private void ClearUndo() { undoTimer.Stop(); lastRemoved = null; undo.Visibility = Visibility.Collapsed; FooterMessage("双击编辑 · 拖动排序"); }
    private void FooterMessage(string text) { if (saveError) return; footer.Text = text; footer.Foreground = Ui.Muted; }

    private void PointerDown(CardView card, MouseButtonEventArgs e)
    {
        if (OverButton(e.OriginalSource as DependencyObject) || !state.Items.Contains(card.Item)) return;
        if (e.ClickCount == 2) { CancelDrag(); e.Handled = true; Edit(card.Item); return; }
        selected = card; downPoint = Native.Cursor();
        var origin = card.Root.PointToScreen(new Point()); grabOffset = downPoint - (Vector)origin;
        dragging = false; samples.Clear(); AddSample(downPoint); CaptureMouse(); e.Handled = true;
    }
    private void AddSample(Point point)
    {
        double now = Seconds; samples.Add((point, now));
        while (samples.Count > 2 && samples[0].Time < now - .14) samples.RemoveAt(0);
    }
    private void PointerMove(object sender, MouseEventArgs e)
    {
        if (selected == null || e.LeftButton != MouseButtonState.Pressed) return;
        var point = Native.Cursor(); AddSample(point);
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (!dragging && (point - downPoint).Length < 7 * dpi) return;
        if (!dragging)
        {
            dragging = true; smoothScroll.Stop(); ghost = new DragGhost(this, selected); selected.Root.Opacity = .32;
            edgeTimer.Start();
        }
        MoveGhost(point); UpdateSlot(); e.Handled = true;
    }
    private bool InDeleteZone(Point point)
    {
        var r = Native.Rect(this);
        return ThrowMath.Distance(point.X, point.Y, r.Left, r.Top, r.Right, r.Bottom) > state.SafeDistance * VisualTreeHelper.GetDpi(this).DpiScaleX;
    }
    private void MoveGhost(Point point)
    {
        if (ghost == null) return;
        ghost.MoveCardTo(point.X - grabOffset.X, point.Y - grabOffset.Y);
        bool delete = InDeleteZone(point); ghost.SetDanger(delete);
        footer.Text = delete ? "松手删除 · Esc 取消" : "拖动排序 · 滚轮翻页 · Esc 取消";
        footer.Foreground = delete ? Ui.Red : Ui.Accent;
    }
    private void UpdateSlot()
    {
        if (!dragging || selected == null) return;
        foreach (var card in cards) card.InsertMark.Visibility = Visibility.Hidden;
        var local = viewer.PointFromScreen(Native.Cursor());
        if (local.X < 0 || local.X > viewer.ActualWidth || local.Y < 0 || local.Y > viewer.ActualHeight) return;
        var others = cards.Where(x => x != selected && state.Items.Contains(x.Item)).ToList();
        slot = others.Count;
        for (int i = 0; i < others.Count; i++)
        {
            var top = others[i].Root.TranslatePoint(new Point(), viewer).Y;
            if (local.Y < top + others[i].Root.ActualHeight / 2) { slot = i; break; }
        }
        if (others.Count == 0) return;
        var mark = slot == others.Count ? others[^1].InsertMark : others[slot].InsertMark;
        mark.VerticalAlignment = slot == others.Count ? VerticalAlignment.Bottom : VerticalAlignment.Top; mark.Visibility = Visibility.Visible;
    }
    private void EdgeScroll()
    {
        if (!dragging || selected == null) return;
        var point = Native.Cursor(); MoveGhost(point);
        var local = viewer.PointFromScreen(point);
        if (local.X < -24 || local.X > viewer.ActualWidth + 24 || local.Y < -36 || local.Y > viewer.ActualHeight + 36) return;
        const double edge = 36;
        double amount = local.Y < edge ? -Math.Clamp((edge - local.Y) / edge, 0, 1) * 12
            : local.Y > viewer.ActualHeight - edge ? Math.Clamp((local.Y - viewer.ActualHeight + edge) / edge, 0, 1) * 12 : 0;
        if (amount != 0) smoothScroll.ScrollBy(amount);
        UpdateSlot();
    }
    private void PointerUp(object sender, MouseButtonEventArgs e)
    {
        if (selected == null) return;
        var card = selected; var point = Native.Cursor(); bool wasDragging = dragging;
        bool delete = wasDragging && InDeleteZone(point);
        var position = viewer.PointFromScreen(point);
        bool inside = position.X >= 0 && position.X <= viewer.ActualWidth && position.Y >= 0 && position.Y <= viewer.ActualHeight;
        if (wasDragging && inside) UpdateSlot();
        int target = slot;
        var flyingGhost = ghost;
        AddSample(point);
        double vx = 0, vy = 0;
        if (samples.Count >= 2)
        {
            double duration = samples[^1].Time - samples[0].Time;
            if (duration > .008 && duration < .35) { vx = (point.X - samples[0].Point.X) / duration; vy = (point.Y - samples[0].Point.Y) / duration; }
        }
        double limit = 2300 * VisualTreeHelper.GetDpi(this).DpiScaleX;
        double speed = Math.Sqrt(vx * vx + vy * vy);
        if (speed > limit) { vx *= limit / speed; vy *= limit / speed; }
        // Detach state before releasing capture, since LostMouseCapture is synchronous.
        ghost = null; selected = null; dragging = false; edgeTimer.Stop(); ReleaseMouseCapture();
        card.Root.Opacity = 1; foreach (var c in cards) c.InsertMark.Visibility = Visibility.Hidden;
        if (delete)
        {
            if (Remove(card.Item) >= 0)
            {
                flyingGhost?.Fall(vx, vy); RenderCards(); FooterMessage("已甩出删除 · 8 秒内可撤销");
            }
            else flyingGhost?.Close();
        }
        else
        {
            flyingGhost?.Close();
            if (wasDragging && inside && Ordering.Move(state.Items, card.Item, target)) { Save(); RenderCards(); FooterMessage("已保存手动顺序"); }
            else FooterMessage(lastRemoved != null ? "8 秒内可撤销上次删除" : "双击编辑 · 拖动排序");
        }
        e.Handled = true;
    }
    private void CancelDrag()
    {
        var card = selected; selected = null; dragging = false; edgeTimer.Stop();
        if (Mouse.Captured == this) ReleaseMouseCapture();
        ghost?.Close(); ghost = null;
        if (card != null) card.Root.Opacity = 1;
        foreach (var c in cards) c.InsertMark.Visibility = Visibility.Hidden;
        FooterMessage(lastRemoved != null ? "8 秒内可撤销上次删除" : "双击编辑 · 拖动排序");
    }
}
