using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DDLDesk;

internal static class ReadingUi
{
    public static TextBox Field(string text = "", bool multiline = false, int height = 80)
        => new() { Text = text, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
            MinHeight = multiline ? height : 36 };

    public static void Configure(Window window, Window owner, string title, double width, double height)
    {
        var area = Native.WorkArea(owner);
        window.Owner = owner; window.Title = title;
        window.Width = Math.Min(width, Math.Max(360, area.Width - 40));
        window.Height = Math.Min(height, Math.Max(300, area.Height - 40));
        window.MinWidth = Math.Min(700, window.Width); window.MinHeight = Math.Min(520, window.Height);
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window.WindowStyle = WindowStyle.None; window.ResizeMode = ResizeMode.CanResizeWithGrip;
        window.AllowsTransparency = true; window.Background = Brushes.Transparent; window.Foreground = Brushes.White;
        WindowAppearance.FollowOwner(window);
        window.FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        window.UseLayoutRounding = true;
    }

    public static DockPanel Header(Window window, string title)
    {
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14), Background = Brushes.Transparent };
        var close = Ui.Button("×", "关闭", window.Close); close.Padding = new Thickness(10, 3, 10, 3);
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var label = Ui.Text(title, 20); label.FontWeight = FontWeights.Bold; label.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(label);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject origin)
            {
                DependencyObject? source = origin;
                while (source != null)
                {
                    if (source is System.Windows.Controls.Primitives.ButtonBase) return;
                    source = source is Visual ? VisualTreeHelper.GetParent(source) : null;
                }
            }
            if (e.LeftButton == MouseButtonState.Pressed) window.DragMove();
        };
        return header;
    }

    public static void OpenSource(Window owner, string url)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
                throw new InvalidDataException("原文链接必须以 http:// 或 https:// 开头。");
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception) { MessageBox.Show(owner, "原文链接无法打开，请检查网址和默认浏览器设置。", "打开原文"); }
    }
}

internal sealed class ReadingResultEditor : StackPanel
{
    private readonly TextBox question;
    private readonly TextBox method;
    private readonly TextBox keywords;
    private readonly TextBox limitations;
    private readonly List<(TextBox Conclusion, TextBox Evidence, TextBlock EvidenceLabel)> findings = new();
    public event Action? EvidenceChanged;
    public ReadingResultEditor(ReadingAnalysis analysis, bool readOnly)
    {
        question = AddField("研究问题", analysis.ResearchQuestion, true);
        method = AddField("研究方法", analysis.Method, true);
        for (int i = 0; i < analysis.Findings.Count; i++)
        {
            var conclusion = AddField($"主要结论 {i + 1}", analysis.Findings[i].Conclusion, true);
            var evidence = AddField("对应原文依据 · 无法定位时留空", analysis.Findings[i].Evidence, true, out var evidenceLabel);
            evidence.FontSize = 12; evidence.Foreground = Ui.Brush("#BFE8DB");
            evidence.ToolTip = ReadingValidation.EvidenceMatchingRule;
            System.Windows.Automation.AutomationProperties.SetName(evidence, $"主要结论 {i + 1} 的对应原文依据");
            evidence.TextChanged += (_, _) => EvidenceChanged?.Invoke();
            findings.Add((conclusion, evidence, evidenceLabel));
        }
        keywords = AddField("关键词 · 用逗号分隔", string.Join("，", analysis.Keywords), false);
        limitations = AddField("局限 / 资料未说明", analysis.Limitations, true);
        foreach (var field in Children.OfType<TextBox>()) field.IsReadOnly = readOnly;
    }

    private TextBox AddField(string label, string text, bool multiline) => AddField(label, text, multiline, out _);

    private TextBox AddField(string label, string text, bool multiline, out TextBlock caption)
    {
        caption = Ui.Text(label, 12, Ui.Muted); caption.Margin = new Thickness(0, 14, 0, 7); Children.Add(caption);
        var box = ReadingUi.Field(text, multiline, 66); box.MaxLength = multiline ? 6000 : 1400;
        Children.Add(box); return box;
    }

    public ReadingAnalysis Value() => new()
    {
        ResearchQuestion = question.Text.Trim(), Method = method.Text.Trim(), Limitations = limitations.Text.Trim(),
        Keywords = keywords.Text.Split(new[] { ',', '，', ';', '；', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        Findings = findings.Select(x => new ReadingFinding { Conclusion = x.Conclusion.Text.Trim(), Evidence = x.Evidence.Text.Trim() }).ToList()
    };

    public void MarkEvidenceIssues(IReadOnlyList<ReadingEvidenceIssue> issues, bool focusFirst = false)
    {
        var numbers = issues.Select(x => x.FindingNumber).ToHashSet();
        TextBox? first = null;
        for (int i = 0; i < findings.Count; i++)
        {
            var evidence = findings[i].Evidence;
            if (numbers.Contains(i + 1))
            {
                evidence.BorderBrush = Ui.Red;
                findings[i].EvidenceLabel.Text = $"主要结论 {i + 1} 的原文依据 · 未匹配";
                findings[i].EvidenceLabel.Foreground = Ui.Red;
                evidence.ToolTip = $"主要结论 {i + 1} 的依据未匹配。\n{ReadingValidation.EvidenceMatchingRule}\n当前依据：{evidence.Text}";
                first ??= evidence;
            }
            else
            {
                evidence.ClearValue(Control.BorderBrushProperty);
                findings[i].EvidenceLabel.Text = "对应原文依据 · 无法定位时留空";
                findings[i].EvidenceLabel.Foreground = Ui.Muted;
                evidence.ToolTip = ReadingValidation.EvidenceMatchingRule;
            }
        }
        if (focusFirst && first != null)
        {
            var target = first;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (target.IsVisible && target.IsEnabled) { target.Focus(); target.BringIntoView(); }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}

internal sealed class ReadingDialog : Window
{
    private readonly ModelSettingsStore settings;
    private readonly IReadingAnalysisService service;
    private readonly Deadline? original;
    private readonly Func<Deadline, bool>? commit;
    private readonly TextBox title = ReadingUi.Field();
    private readonly TextBox url = ReadingUi.Field();
    private readonly TextBox source = ReadingUi.Field("", true);
    private readonly DatePicker date = new() { FontSize = 13, MinHeight = 32, Width = 160 };
    private readonly TextBox time = ReadingUi.Field("18:00");
    private readonly CheckBox undated = new() { Content = "截止时间未定", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
    private readonly TextBlock count = Ui.Text("", 11, Ui.Muted);
    private readonly TextBlock status = Ui.Text("", 12, Ui.Muted);
    private readonly ContentControl output = new();
    private readonly Button analyze;
    private readonly Button save;
    private readonly Button cancel;
    private readonly Button configure;
    private ReadingResultEditor? editor;
    private string analyzedSource = "";
    private string analyzedModel = "";
    private DateTime createdAt;
    private CancellationTokenSource? request;
    private bool closed;
    private bool evidenceCheckShown;
    private readonly bool readOnly;

    public ReadingDialog(Window owner, Deadline? original, ModelSettingsStore settings, IReadingAnalysisService service,
        Func<Deadline, bool>? commit, bool readOnly = false)
    {
        this.settings = settings; this.service = service; this.original = original; this.commit = commit; this.readOnly = readOnly;
        ReadingUi.Configure(this, owner, readOnly ? "已完成阅读 · 笔记" : original == null ? "新建阅读任务" : "查看与修改阅读笔记", 1040, 820);
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(ReadingUi.Header(this, Title));
        var form = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        Ui.Label(form, "阅读标题"); title.MaxLength = 300; form.Children.Add(title);
        Ui.Label(form, "原文链接 · 可选，仅保存网址，不抓取网页内容"); url.MaxLength = 2048; form.Children.Add(url);
        Grid.SetRow(form, 1); layout.Children.Add(form);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition());
        var sourceArea = new Grid { Margin = new Thickness(0, 0, 12, 0) };
        sourceArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); sourceArea.RowDefinitions.Add(new RowDefinition());
        sourceArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var sourceLabel = Ui.Text("文献片段 / 摘要", 14); sourceLabel.Margin = new Thickness(0, 0, 0, 8); sourceArea.Children.Add(sourceLabel);
        source.MaxLength = 0; source.MinHeight = 0; source.AcceptsTab = true; Grid.SetRow(source, 1); sourceArea.Children.Add(source);
        count.Margin = new Thickness(0, 6, 0, 0); Grid.SetRow(count, 2); sourceArea.Children.Add(count); columns.Children.Add(sourceArea);
        var resultArea = new Grid { Margin = new Thickness(12, 0, 0, 0) };
        resultArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); resultArea.RowDefinitions.Add(new RowDefinition());
        var resultLabel = Ui.Text("结构化笔记 · 请对照原文核对", 14); resultLabel.Margin = new Thickness(0, 0, 0, 8); resultArea.Children.Add(resultLabel);
        var outputScroll = new ScrollViewer { Content = output, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(outputScroll, 1); resultArea.Children.Add(outputScroll); Grid.SetColumn(resultArea, 1); columns.Children.Add(resultArea);
        Grid.SetRow(columns, 2); layout.Children.Add(columns);
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var schedule = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var dueLabel = Ui.Text("阅读截止", 12); dueLabel.VerticalAlignment = VerticalAlignment.Center; schedule.Children.Add(dueLabel); schedule.Children.Add(undated);
        date.Style = (Style)Application.Current.FindResource("ReadableDatePicker");
        date.CalendarOpened += (_, _) => Dispatcher.BeginInvoke(() => Ui.EnsureCalendarHeader(date), System.Windows.Threading.DispatcherPriority.Loaded);
        time.Width = 75; time.MaxLength = 5; time.Margin = new Thickness(8, 0, 0, 0);
        schedule.Children.Add(date); schedule.Children.Add(time); footer.Children.Add(schedule);
        status.TextWrapping = TextWrapping.Wrap;
        var statusScroll = new ScrollViewer { Content = status, MaxHeight = 112, Margin = new Thickness(0, 10, 0, 8),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        footer.Children.Add(statusScroll);
        var actions = new WrapPanel();
        configure = Ui.Button("模型设置", "配置所选接口和加密保存的密钥", () =>
        {
            if (new ModelSettingsDialog(this, settings, service).ShowDialog() == true) SetStatus(CurrentServiceNotice());
        });
        analyze = Ui.Button("生成阅读笔记", "将当前原文发送至所选模型服务分析", async () => await AnalyzeAsync());
        analyze.Background = Ui.Brush("#235548"); analyze.Foreground = Ui.Accent;
        cancel = Ui.Button("取消请求", "取消正在进行的模型请求", () => request?.Cancel()); cancel.Visibility = Visibility.Collapsed;
        save = Ui.Button("保存阅读任务", "保存原文和核对后的笔记", SaveReading);
        foreach (var button in new[] { configure, analyze, cancel, save }) actions.Children.Add(button);
        if (!string.IsNullOrWhiteSpace(original?.Url))
        { var link = Ui.Button("打开原文", "使用默认浏览器打开原文链接", () => ReadingUi.OpenSource(this, url.Text)); actions.Children.Add(link); }
        foreach (var button in actions.Children.OfType<Button>())
        { button.Margin = new Thickness(0, 0, 10, 4); button.VerticalAlignment = VerticalAlignment.Top; }
        footer.Children.Add(actions); Grid.SetRow(footer, 3); layout.Children.Add(footer);
        Content = WindowAppearance.Shell(this, layout, new Thickness(22));
        title.Text = original?.Title ?? ""; url.Text = original?.Url ?? "";
        date.SelectedDate = original?.Due?.Date ?? DateTime.Today.AddDays(1); time.Text = original?.Due?.ToString("HH:mm") ?? "18:00";
        undated.IsChecked = original?.Due == null;
        undated.Checked += (_, _) => UpdateSchedule(); undated.Unchecked += (_, _) => UpdateSchedule(); UpdateSchedule();
        if (original?.ReadingNote is { } note)
        {
            source.Text = note.SourceText; analyzedSource = source.Text; analyzedModel = note.Model; createdAt = note.CreatedAt;
            ShowEditor(note.Analysis, readOnly);
            string savedInfo = $"已保存笔记 · {createdAt:yyyy-MM-dd HH:mm} · {analyzedModel} · 可离线查看";
            SetStatus(readOnly ? savedInfo : savedInfo + "\n" + CurrentServiceNotice());
        }
        else
        {
            output.Content = Ui.Text("粘贴一段文献摘要，生成研究问题、方法、结论、关键词与局限。\n\n未说明的信息会明确标记。", 13, Ui.Muted);
            SetStatus(CurrentServiceNotice());
        }
        source.TextChanged += (_, _) =>
        {
            UpdateActions();
            if (editor != null)
            {
                if (source.Text != analyzedSource)
                { editor.MarkEvidenceIssues(Array.Empty<ReadingEvidenceIssue>()); SetStatus("原文已修改，请重新生成笔记，再核对保存。", true); }
                else if (evidenceCheckShown) UpdateEvidenceFeedback(false, "当前原文与笔记对应。请核对、修改结果后保存。");
                else SetStatus("当前原文与笔记对应。请核对、修改结果后保存。");
            }
        };
        title.IsReadOnly = url.IsReadOnly = source.IsReadOnly = readOnly;
        if (readOnly) { analyze.Visibility = save.Visibility = configure.Visibility = Visibility.Collapsed; undated.IsEnabled = date.IsEnabled = time.IsEnabled = false; }
        System.Windows.Automation.AutomationProperties.SetName(source, "文献原文");
        System.Windows.Automation.AutomationProperties.SetName(title, "阅读标题");
        Closed += (_, _) => { closed = true; request?.Cancel(); };
        UpdateActions();
    }

    private void UpdateSchedule() { date.IsEnabled = time.IsEnabled = !readOnly && undated.IsChecked != true; }
    private string CurrentServiceNotice()
    {
        var selected = settings.Current; string? endpoint = selected.EndpointScope();
        string target = endpoint == null ? "地址尚未配置" : new Uri(endpoint).GetLeftPart(UriPartial.Authority);
        return $"当前生成服务：{selected.DisplayName}（{target}）。点击生成会发送原文并使用该账户额度。";
    }
    private void SetStatus(string text, bool error = false) { status.Text = text; status.Foreground = error ? Ui.Red : Ui.Muted; }
    private void ShowEditor(ReadingAnalysis analysis, bool isReadOnly)
    {
        editor = new ReadingResultEditor(analysis, isReadOnly);
        editor.EvidenceChanged += () =>
        {
            if (evidenceCheckShown && !readOnly && request == null && source.Text == analyzedSource)
                UpdateEvidenceFeedback(false, "原文依据均可定位。请继续核对结论后保存。");
        };
        output.Content = editor;
    }
    private void UpdateEvidenceFeedback(bool focusFirst, string success)
    {
        if (editor == null) return;
        evidenceCheckShown = true;
        var issues = ReadingValidation.FindEvidenceIssues(editor.Value(), source.Text);
        editor.MarkEvidenceIssues(issues, focusFirst);
        SetStatus(issues.Count == 0 ? success : ReadingValidation.DescribeEvidenceIssues(issues), issues.Count > 0);
    }
    private void UpdateActions()
    {
        count.Text = $"{source.Text.Length:N0} / {ReadingValidation.MaxSourceLength:N0} 字符 · 内容请由你确认可发送";
        count.Foreground = source.Text.Length > ReadingValidation.MaxSourceLength ? Ui.Red : Ui.Muted;
        analyze.IsEnabled = !readOnly && request == null && !string.IsNullOrWhiteSpace(source.Text) && source.Text.Length <= ReadingValidation.MaxSourceLength;
        save.IsEnabled = !readOnly && request == null && editor != null && source.Text == analyzedSource;
        configure.IsEnabled = request == null;
        source.IsReadOnly = readOnly || request != null;
        if (editor != null) editor.IsEnabled = request == null;
        cancel.Visibility = request == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task AnalyzeAsync()
    {
        if (request != null) return;
        var selected = settings.Current.Copy();
        try { ReadingValidation.ValidateSource(source.Text); selected.ValidateForRequest(); }
        catch (InvalidDataException ex) { SetStatus(ex.Message, true); return; }
        string snapshot = source.Text; string model = selected.Model;
        using var operation = new CancellationTokenSource(); request = operation; UpdateActions();
        SetStatus($"正在请求{selected.DisplayName}，最多等待 60 秒；可取消，输入会保留。");
        try
        {
            var result = await service.AnalyzeAsync(snapshot, selected, operation.Token);
            if (closed || operation.IsCancellationRequested) return;
            ShowEditor(result, false);
            analyzedSource = snapshot; analyzedModel = model; createdAt = DateTime.Now;
            UpdateEvidenceFeedback(false, "分析完成，请对照原文核对修改。保存时校验引文；无法定位的依据可修正或清空。");
        }
        catch (OperationCanceledException) { if (!closed) SetStatus("请求已取消，输入与原有笔记均已保留。"); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
        { if (!closed) SetStatus(ex.Message, true); }
        catch (Exception) { if (!closed) SetStatus("分析未完成，请检查配置后重试；输入已保留。", true); }
        finally { request = null; if (!closed) UpdateActions(); }
    }

    private void SaveReading()
    {
        if (request != null || editor == null || source.Text != analyzedSource || readOnly) return;
        try
        {
            if (string.IsNullOrWhiteSpace(title.Text)) throw new InvalidDataException("请填写阅读标题。");
            string address = url.Text.Trim();
            if (address.Length > 0 && (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")))
                throw new InvalidDataException("原文链接必须以 http:// 或 https:// 开头。");
            DateTime? due = null;
            if (undated.IsChecked != true)
            {
                if (date.SelectedDate is not DateTime day || !TimeSpan.TryParseExact(time.Text.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var clock) || clock.TotalHours >= 24)
                    throw new InvalidDataException("请选择截止日期，时间格式例如 18:00。");
                due = day.Date.Add(clock);
            }
            var note = new ReadingNote { SourceText = source.Text, Analysis = editor.Value(), CreatedAt = createdAt, Model = analyzedModel };
            UpdateEvidenceFeedback(true, "原文依据均可定位。请继续核对结论后保存。");
            ReadingValidation.ValidateNote(note);
            var item = new Deadline { Id = original?.Id ?? Guid.NewGuid(), Title = title.Text.Trim(), Due = due,
                Url = address, Folder = original?.Folder ?? "", Manual = original?.Manual ?? false,
                Color = original?.Color ?? CardColors.Pick(), ReadingNote = note };
            if (commit?.Invoke(item) == true) { DialogResult = true; }
            else SetStatus("未能保存，编辑内容已保留。请检查数据目录后再次保存。", true);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        { SetStatus(ex.Message, true); }
    }
}

internal sealed class CompletedReadingsDialog : Window
{
    private readonly DeskState state;
    private readonly ModelSettingsStore settings;
    private readonly IReadingAnalysisService service;
    private readonly Func<CompletedReading, bool> delete;
    private readonly TextBox search = ReadingUi.Field();
    private readonly Button clear;
    private readonly TextBlock summary = Ui.Text("", 12, Ui.Muted);
    private readonly TextBlock status = Ui.Text("", 12, Ui.Muted);
    private readonly StackPanel rows = new();

    public CompletedReadingsDialog(Window owner, DeskState state, ModelSettingsStore settings, IReadingAnalysisService service,
        Func<CompletedReading, bool> delete)
    {
        this.state = state; this.settings = settings; this.service = service; this.delete = delete;
        ReadingUi.Configure(this, owner, "已完成阅读", 760, 620);
        var layout = new DockPanel(); var header = ReadingUi.Header(this, "已完成阅读"); DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        var filter = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var label = Ui.Text("查找记录 · 按标题即时筛选", 12, Ui.Muted); label.Margin = new Thickness(0, 0, 0, 7); filter.Children.Add(label);
        var searchRow = new Grid(); searchRow.ColumnDefinitions.Add(new ColumnDefinition()); searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        search.MaxLength = 300; search.ToolTip = "按阅读标题部分匹配，不区分英文字母大小写";
        System.Windows.Automation.AutomationProperties.SetName(search, "查找已完成阅读标题"); searchRow.Children.Add(search);
        clear = Ui.Button("清空", "清空查找并显示全部记录", () => { search.Clear(); search.Focus(); }); clear.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(clear, 1); searchRow.Children.Add(clear); filter.Children.Add(searchRow);
        summary.Margin = new Thickness(0, 8, 0, 0); filter.Children.Add(summary);
        DockPanel.SetDock(filter, Dock.Top); layout.Children.Add(filter);
        status.Margin = new Thickness(0, 8, 0, 0); status.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(status, Dock.Bottom); layout.Children.Add(status);
        layout.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = WindowAppearance.Shell(this, layout, new Thickness(22));
        search.TextChanged += (_, _) => { status.Visibility = Visibility.Collapsed; RefreshRows(); };
        Loaded += (_, _) => search.Focus();
        RefreshRows();
    }

    private void RefreshRows()
    {
        rows.Children.Clear();
        string query = search.Text.Trim();
        var matches = state.CompletedReadings.Where(x => query.Length == 0 || x.Item.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.CompletedAt).ToList();
        clear.IsEnabled = search.Text.Length > 0;
        summary.Text = query.Length == 0 ? $"共 {state.CompletedReadings.Count} 条记录" : $"找到 {matches.Count} 条 / 共 {state.CompletedReadings.Count} 条";
        if (matches.Count == 0) rows.Children.Add(Ui.Text(state.CompletedReadings.Count == 0
            ? "尚无已完成阅读。阅读卡片点击勾号后，笔记会保留在这里。" : "未找到匹配记录，请更换关键词或清空查找。", 14, Ui.Muted));
        foreach (var entry in matches)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            row.Children.Add(Ui.Text(entry.Item.Title, 16)); row.Children.Add(Ui.Text($"完成于 {entry.CompletedAt:yyyy-MM-dd HH:mm}", 12, Ui.Muted));
            var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            var button = Ui.Button("查看笔记", "离线查看原文与已保存笔记", () => new ReadingDialog(this, entry.Item, settings, service, null, true).ShowDialog());
            button.Margin = new Thickness(0, 0, 8, 4); actions.Children.Add(button);
            var remove = Ui.Button("删除记录", "删除已完成阅读及笔记：" + entry.Item.Title, () => DeleteEntry(entry));
            remove.Foreground = Ui.Red; remove.Margin = new Thickness(0, 0, 0, 4); actions.Children.Add(remove); row.Children.Add(actions);
            var shell = Ui.Shell(row, new Thickness(14)); shell.Tag = entry.Item.Id; rows.Children.Add(shell);
        }
    }

    private void DeleteEntry(CompletedReading entry)
    {
        if (new CompletedReadingDeleteDialog(this, entry.Item.Title).ShowDialog() != true) return;
        bool saved = delete(entry);
        RefreshRows();
        status.Text = saved ? "已删除记录：" + entry.Item.Title : "未能保存删除，记录已保留。请检查数据目录后重试。";
        status.Foreground = saved ? Ui.Muted : Ui.Red; status.Visibility = Visibility.Visible;
    }
}

internal sealed class CompletedReadingDeleteDialog : DeskDialog
{
    private readonly Button cancel;
    private readonly Button confirm;
    public CompletedReadingDeleteDialog(Window owner, string title) : base(owner, "删除阅读记录", 420)
    {
        var name = Ui.Text(title, 16); name.FontWeight = FontWeights.SemiBold; name.MaxHeight = 70;
        name.TextTrimming = TextTrimming.CharacterEllipsis; name.ToolTip = title; name.Margin = new Thickness(0, 0, 0, 12); Body.Children.Add(name);
        Body.Children.Add(Ui.Text("该记录及保存的笔记将从已完成阅读中删除，无法在界面中撤销。", 13, Ui.Muted));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        cancel = Ui.Button("取消", "保留阅读记录", () => DialogResult = false); cancel.IsDefault = true; cancel.IsCancel = true;
        cancel.Margin = new Thickness(0, 0, 8, 0); actions.Children.Add(cancel);
        confirm = Ui.Button("删除记录", "确认删除该完成记录及保存的笔记", () => DialogResult = true);
        confirm.Background = Ui.Brush("#63323D"); confirm.Foreground = Ui.Brush("#FFE1E5"); actions.Children.Add(confirm); Body.Children.Add(actions);
        Loaded += (_, _) => cancel.Focus();
    }
}

