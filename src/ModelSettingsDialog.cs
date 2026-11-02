using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DDLDesk;

internal sealed class ModelSettingsDialog : DeskDialog
{
    private readonly RadioButton deepSeekMode = new() { Content = "DeepSeek 预设", GroupName = "ApiMode", Foreground = Brushes.White, Margin = new Thickness(0, 0, 18, 0) };
    private readonly RadioButton compatibleMode = new() { Content = "通用兼容", GroupName = "ApiMode", Foreground = Brushes.White };
    private readonly TextBox endpoint;
    private readonly TextBox model;
    private readonly PasswordBox key;
    private readonly CheckBox forceJson = new() { Content = "强制 JSON 输出（平台须支持）", Margin = new Thickness(0, 12, 0, 6) };
    private readonly TextBlock preview = Ui.Text("", 11, Ui.Accent);
    private readonly TextBlock status = Ui.Text("", 12, Ui.Muted);
    private readonly TextBlock notice = Ui.Text("", 12, Ui.Muted);
    private readonly Button test;
    private readonly Button save;
    private readonly Button cancel;
    private readonly Dictionary<ApiMode, ModelSettings> drafts;
    private ApiMode mode;
    private bool loading;
    private CancellationTokenSource? request;
    private bool closed;

    public ModelSettingsDialog(Window owner, ModelSettingsStore store, IReadingAnalysisService service) : base(owner, "模型设置", 590)
    {
        drafts = new() { [ApiMode.DeepSeek] = store.Draft(ApiMode.DeepSeek), [ApiMode.Compatible] = store.Draft(ApiMode.Compatible) };
        deepSeekMode.Style = compatibleMode.Style = (Style)Application.Current.FindResource("ApiModeSelector");
        Ui.Label(Body, "接入模式");
        var modes = new WrapPanel(); modes.Children.Add(deepSeekMode); modes.Children.Add(compatibleMode); Body.Children.Add(modes);
        Ui.Label(Body, "API 地址 · 通用模式可填 Base URL 或完整 Chat Completions 地址");
        endpoint = ReadingUi.Field(); endpoint.MaxLength = 2048; Body.Children.Add(endpoint);
        preview.Margin = new Thickness(0, 7, 0, 0); preview.MaxHeight = 52; preview.TextTrimming = TextTrimming.CharacterEllipsis; Body.Children.Add(preview);
        Ui.Label(Body, "模型 ID · 使用平台提供的准确名称"); model = ReadingUi.Field(); model.MaxLength = 200; Body.Children.Add(model);
        Ui.Label(Body, "API Key · 两个模式分别加密保存，重启自动读取");
        key = new PasswordBox { Padding = new Thickness(10, 9, 10, 9), FontSize = 14, Background = Ui.Brush("#111922"),
            Foreground = Brushes.White, BorderBrush = Ui.Brush("#3A4655"), MaxLength = 8192 }; Body.Children.Add(key);
        Body.Children.Add(forceJson);
        notice.Margin = new Thickness(0, 5, 0, 0); Body.Children.Add(notice);
        if (store.Notice != null) Body.Children.Add(Ui.Text(store.Notice, 12, Ui.Red));
        status.Margin = new Thickness(0, 12, 0, 8); Body.Children.Add(status);
        var actions = new WrapPanel();
        test = Ui.Button("测试连接", "仅测试当前所填配置，会调用该平台账户", async () =>
        {
            if (request != null) return;
            Capture(); var candidate = drafts[mode].Copy();
            using var operation = new CancellationTokenSource(); request = operation; Busy(true); SetStatus("正在测试当前配置，不会自动重试…");
            try
            {
                await service.TestConnectionAsync(candidate, operation.Token);
                if (!closed) SetStatus("连接与 JSON 返回验证成功，可保存并使用当前模式。", success: true);
            }
            catch (OperationCanceledException) { if (!closed) SetStatus("已取消测试。"); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
            { if (!closed) SetStatus(ex.Message, error: true); }
            catch (Exception) { if (!closed) SetStatus("连接测试失败，请核对最终地址、模型和网络。", error: true); }
            finally { request = null; if (!closed) Busy(false); }
        });
        save = Ui.Button("保存并使用", "保存当前模式配置，另一模式已保存的配置继续保留", () =>
        {
            try { Capture(); store.Save(drafts[mode]); DialogResult = true; }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or CryptographicException)
            { SetStatus(ex is InvalidDataException ? ex.Message : "配置或密钥加密保存失败，请检查当前 Windows 用户和数据目录权限。", error: true); }
        });
        cancel = Ui.Button("取消测试", "取消当前测试请求", () => request?.Cancel()); cancel.Visibility = Visibility.Collapsed;
        foreach (var button in new[] { test, save, cancel }) { button.Margin = new Thickness(0, 0, 10, 0); actions.Children.Add(button); }
        Body.Children.Add(actions);

        endpoint.TextChanged += (_, _) =>
        {
            if (loading) return;
            var draft = drafts[mode]; string? before = draft.EndpointScope(); draft.BaseUrl = endpoint.Text.Trim();
            if (!string.Equals(before, draft.EndpointScope(), StringComparison.Ordinal) && key.Password.Length > 0)
            { key.Clear(); SetStatus("目标地址已更改，请为此地址重新输入 API Key。", error: true); }
            RefreshPreview();
        };
        model.TextChanged += (_, _) => { if (!loading) drafts[mode].Model = model.Text.Trim(); };
        key.PasswordChanged += (_, _) => { if (!loading && !closed) drafts[mode].ApiKey = key.Password.Trim(); };
        forceJson.Checked += (_, _) => { if (!loading) drafts[mode].ForceJsonOutput = true; };
        forceJson.Unchecked += (_, _) => { if (!loading) drafts[mode].ForceJsonOutput = false; };
        deepSeekMode.Checked += (_, _) => Switch(ApiMode.DeepSeek);
        compatibleMode.Checked += (_, _) => Switch(ApiMode.Compatible);
        Load(store.Current.Mode);
        Closed += (_, _) => { closed = true; request?.Cancel(); key.Clear(); foreach (var draft in drafts.Values) draft.ApiKey = ""; };
    }

    private void Switch(ApiMode next)
    {
        if (loading || mode == next || request != null) return;
        Capture(); Load(next); SetStatus("切换仅改变本窗口；点击“保存并使用”后应用当前模式。");
    }

    private void Capture()
    {
        var draft = drafts[mode]; draft.BaseUrl = endpoint.Text.Trim(); draft.Model = model.Text.Trim(); draft.ApiKey = key.Password.Trim();
        draft.ForceJsonOutput = mode == ApiMode.DeepSeek || forceJson.IsChecked == true;
    }

    private void Load(ApiMode next)
    {
        loading = true;
        try
        {
            mode = next; var draft = drafts[mode];
            deepSeekMode.IsChecked = mode == ApiMode.DeepSeek; compatibleMode.IsChecked = mode == ApiMode.Compatible;
            endpoint.Text = draft.BaseUrl; model.Text = draft.Model; key.Password = draft.ApiKey;
            endpoint.IsReadOnly = mode == ApiMode.DeepSeek;
            forceJson.IsChecked = draft.UsesJsonOutput; forceJson.IsEnabled = mode == ApiMode.Compatible;
            notice.Text = mode == ApiMode.DeepSeek
                ? "DeepSeek 预设启用 JSON 输出。密钥由当前 Windows 用户加密保存。\n点击生成会发送文献至 DeepSeek，测试连接也可能消耗额度。"
                : "通用模式使用 HTTPS、Bearer 认证和 Chat Completions 文本协议。\n点击测试或生成会向上方最终地址发送请求并可能计费；留空保存可清除密钥。";
            RefreshPreview();
        }
        finally { loading = false; }
    }

    private void RefreshPreview()
    {
        try { preview.Text = "最终请求地址：" + drafts[mode].Endpoint().AbsoluteUri; preview.Foreground = Ui.Accent; }
        catch (InvalidDataException ex) { preview.Text = ex.Message; preview.Foreground = Ui.Muted; }
        preview.ToolTip = preview.Text;
    }

    private void SetStatus(string text, bool error = false, bool success = false)
    { status.Text = text; status.Foreground = error ? Ui.Red : success ? Ui.Accent : Ui.Muted; }

    private void Busy(bool busy)
    {
        deepSeekMode.IsEnabled = compatibleMode.IsEnabled = endpoint.IsEnabled = model.IsEnabled = key.IsEnabled = test.IsEnabled = save.IsEnabled = !busy;
        forceJson.IsEnabled = !busy && mode == ApiMode.Compatible;
        cancel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }
}
