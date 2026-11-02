using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DDLDesk;

[JsonConverter(typeof(JsonStringEnumConverter<ApiMode>))]
public enum ApiMode { DeepSeek, Compatible }

public sealed class ModelSettings
{
    public ApiMode Mode { get; set; } = ApiMode.DeepSeek;
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string Model { get; set; } = "deepseek-flash";
    public bool ForceJsonOutput { get; set; }
    [JsonIgnore] public string ApiKey { get; set; } = "";
    [JsonIgnore] public bool UsesJsonOutput => Mode == ApiMode.DeepSeek || ForceJsonOutput;
    [JsonIgnore] public string DisplayName => Mode == ApiMode.DeepSeek ? "DeepSeek" : "通用兼容接口";

    public static ModelSettings EmptyCompatible() => new() { Mode = ApiMode.Compatible, BaseUrl = "", Model = "" };
    public ModelSettings Copy() => new() { Mode = Mode, BaseUrl = BaseUrl, Model = Model, ApiKey = ApiKey, ForceJsonOutput = ForceJsonOutput };

    public Uri Endpoint()
    {
        string address = BaseUrl?.Trim() ?? "";
        if (!Enum.IsDefined(Mode) || address.Any(char.IsWhiteSpace)
            || !Uri.TryCreate(address, UriKind.Absolute, out var root) || root.Scheme != "https"
            || string.IsNullOrWhiteSpace(root.Host) || !string.IsNullOrEmpty(root.UserInfo)
            || !string.IsNullOrEmpty(root.Query) || !string.IsNullOrEmpty(root.Fragment))
            throw new InvalidDataException("请填写有效的 HTTPS Base URL，不包含用户名、密码、查询参数或片段。");
        string path = root.AbsolutePath.TrimEnd('/');
        if (Mode == ApiMode.DeepSeek && (!string.Equals(root.Host, "api.deepseek.com", StringComparison.OrdinalIgnoreCase)
            || !root.IsDefaultPort || (path != "" && path != "/v1")))
            throw new InvalidDataException("DeepSeek 预设仅使用官方地址；其他平台请选择“通用兼容”模式。");
        string normalized = root.AbsoluteUri.TrimEnd('/');
        return new Uri(path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) ? normalized : normalized + "/chat/completions");
    }

    public string? EndpointScope()
    {
        try { return Endpoint().AbsoluteUri; }
        catch (InvalidDataException) { return null; }
    }

    public void ValidateForSave()
    {
        Endpoint();
        if (string.IsNullOrWhiteSpace(Model) || Model.Length > 200 || Model.Any(char.IsWhiteSpace))
            throw new InvalidDataException("请填写平台提供的有效模型 ID，不能包含空白字符。");
        if (ApiKey != null && (ApiKey.Length > 8192 || ApiKey.Contains('\r') || ApiKey.Contains('\n')))
            throw new InvalidDataException("API Key 格式无效，请重新粘贴。");
    }

    public void ValidateForRequest()
    {
        ValidateForSave();
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidDataException("当前模式尚未设置 API Key，请打开“模型设置”填写并保存。密钥由当前 Windows 用户加密保护。");
    }
}

public sealed class ModelSettingsStore
{
    private sealed class StoredProfile
    {
        public string BaseUrl { get; set; } = "https://api.deepseek.com";
        public string Model { get; set; } = "deepseek-flash";
        public bool ForceJsonOutput { get; set; }
        public string? ProtectedApiKey { get; set; }
        public string? KeyEndpoint { get; set; }
    }
    private sealed class StoredConnections
    {
        public int Version { get; set; } = 2;
        public ApiMode ActiveMode { get; set; }
        public StoredProfile DeepSeek { get; set; } = new();
        public StoredProfile Compatible { get; set; } = new() { BaseUrl = "", Model = "" };
    }

    private readonly string path;
    private StoredConnections stored = new();
    private ModelSettings deepSeek = new();
    private ModelSettings compatible = ModelSettings.EmptyCompatible();
    public ModelSettings Current => stored.ActiveMode == ApiMode.DeepSeek ? deepSeek : compatible;
    public string? Notice { get; private set; }

    public ModelSettingsStore(string directory)
    {
        path = Path.Combine(directory, "model-settings.json");
        if (!File.Exists(path)) return;
        try
        {
            string json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
            StoredConnections loaded;
            if (!document.RootElement.TryGetProperty("Version", out _))
            {
                var legacy = JsonSerializer.Deserialize<StoredProfile>(json) ?? throw new InvalidDataException();
                legacy.ForceJsonOutput = true;
                legacy.KeyEndpoint = new ModelSettings { BaseUrl = legacy.BaseUrl, Model = legacy.Model }.Endpoint().AbsoluteUri;
                loaded = new StoredConnections { DeepSeek = legacy };
            }
            else loaded = JsonSerializer.Deserialize<StoredConnections>(json) ?? throw new InvalidDataException();
            if (loaded.Version != 2 || !Enum.IsDefined(loaded.ActiveMode) || loaded.DeepSeek == null || loaded.Compatible == null)
                throw new InvalidDataException();
            var loadedDeepSeek = Restore(loaded.DeepSeek, ApiMode.DeepSeek, false);
            var loadedCompatible = Restore(loaded.Compatible, ApiMode.Compatible, loaded.ActiveMode != ApiMode.Compatible);
            stored = loaded; deepSeek = loadedDeepSeek; compatible = loadedCompatible;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { Notice = "模型配置未能读取，已使用默认值。原配置保留；已有笔记仍可离线查看。"; }
    }

    private ModelSettings Restore(StoredProfile profile, ApiMode mode, bool allowEmpty)
    {
        var result = new ModelSettings { Mode = mode, BaseUrl = profile.BaseUrl, Model = profile.Model, ForceJsonOutput = profile.ForceJsonOutput };
        if (allowEmpty && string.IsNullOrWhiteSpace(profile.BaseUrl) && string.IsNullOrWhiteSpace(profile.Model) && string.IsNullOrEmpty(profile.ProtectedApiKey)) return result;
        result.ValidateForSave();
        if (string.IsNullOrEmpty(profile.ProtectedApiKey)) return result;
        if (!string.Equals(profile.KeyEndpoint, result.Endpoint().AbsoluteUri, StringComparison.Ordinal))
        { AddNotice(result.DisplayName + "地址与保存密钥的目标不一致，请为此地址重新输入密钥。"); return result; }
        try { result.ApiKey = UserKeyProtection.Unprotect(profile.ProtectedApiKey); }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        { AddNotice(result.DisplayName + "已保存密钥无法解密，请在当前 Windows 用户下重新输入并保存。原配置保留。"); }
        return result;
    }

    private void AddNotice(string message) => Notice = string.IsNullOrEmpty(Notice) ? message : Notice + "\n" + message;
    public ModelSettings Draft(ApiMode mode) => (mode == ApiMode.DeepSeek ? deepSeek : compatible).Copy();

    public void Save(ModelSettings settings)
    {
        var selected = settings.Copy(); selected.ValidateForSave();
        selected.BaseUrl = selected.BaseUrl.Trim(); selected.Model = selected.Model.Trim(); selected.ApiKey = selected.ApiKey?.Trim() ?? "";
        var profile = new StoredProfile { BaseUrl = selected.BaseUrl, Model = selected.Model, ForceJsonOutput = selected.UsesJsonOutput,
            ProtectedApiKey = UserKeyProtection.Protect(selected.ApiKey), KeyEndpoint = string.IsNullOrEmpty(selected.ApiKey) ? null : selected.Endpoint().AbsoluteUri };
        var next = new StoredConnections { ActiveMode = selected.Mode,
            DeepSeek = selected.Mode == ApiMode.DeepSeek ? profile : stored.DeepSeek,
            Compatible = selected.Mode == ApiMode.Compatible ? profile : stored.Compatible };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true);
        else File.Move(temp, path);
        stored = next;
        if (selected.Mode == ApiMode.DeepSeek) deepSeek = selected; else compatible = selected;
        Notice = null;
    }
}
