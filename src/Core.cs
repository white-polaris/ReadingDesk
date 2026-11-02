using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DDLDesk;

public sealed class Deadline
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public DateTime? Due { get; set; } = DateTime.Today.AddDays(1).AddHours(18);
    public string Url { get; set; } = "";
    public string Folder { get; set; } = "";
    public bool Manual { get; set; }
    public string Color { get; set; } = "";
    public ReadingNote? ReadingNote { get; set; }
}

public static class CardColors
{
    private static readonly string[] Palette = { "#3D5B72", "#4A587F", "#63537C", "#715165", "#77554D", "#6F5E3E", "#416451", "#3A626F" };
    // Preserve any previously assigned RGB color, even if the palette changes
    // in a future release. Only missing or malformed metadata is regenerated.
    public static bool IsValid(string? color) => color is { Length: 7 } && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit);
    public static string Pick() => Palette[Random.Shared.Next(Palette.Length)];
    public static bool EnsureColor(Deadline item)
    {
        if (IsValid(item.Color)) return false;
        item.Color = Pick(); return true;
    }
    public static bool Ensure(IEnumerable<Deadline> items)
    {
        bool changed = false;
        foreach (var item in items) changed |= EnsureColor(item);
        return changed;
    }
}

public sealed class DeskState
{
    public int Version { get; set; } = 1;
    public List<Deadline> Items { get; set; } = new();
    public List<CompletedReading> CompletedReadings { get; set; } = new();
    public bool AutoStart { get; set; } = true;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public int SafeDistance { get; set; } = 120;
    public double BackgroundOpacity { get; set; } = 0.88;
}

public static class Ordering
{
    public static int Insert(List<Deadline> items, Deadline item)
    {
        CardColors.EnsureColor(item);
        // Undated tasks append. A dated task must come before the undated
        // section, even when all earlier dated cards have manual markers.
        int index = item.Due.HasValue ? items.FindIndex(x => !x.Due.HasValue || (!x.Manual && x.Due > item.Due)) : -1;
        if (index < 0) index = items.Count;
        items.Insert(index, item);
        return index;
    }

    public static int Replace(List<Deadline> items, Deadline original, Deadline replacement)
    {
        int index = items.IndexOf(original);
        if (index < 0) return -1;
        bool changedKind = original.Due.HasValue != replacement.Due.HasValue;
        if (changedKind || (!original.Manual && original.Due != replacement.Due))
        {
            items.RemoveAt(index);
            if (changedKind) replacement.Manual = false;
            return Insert(items, replacement);
        }
        items[index] = replacement;
        return index;
    }

    // target is an insertion slot in the list with the dragged item excluded.
    public static bool Move(List<Deadline> items, Deadline item, int target)
    {
        int before = items.IndexOf(item);
        if (before < 0) return false;
        target = Math.Clamp(target, 0, items.Count - 1);
        if (before == target) return false;
        items.RemoveAt(before);
        items.Insert(target, item);
        item.Manual = true;
        return true;
    }

    public static void Organize(List<Deadline> items)
    {
        var sorted = items.OrderBy(x => x.Due.HasValue ? 0 : 1).ThenBy(x => x.Due).ToList();
        items.Clear();
        foreach (var item in sorted) { item.Manual = false; items.Add(item); }
    }
}

public static class ThrowMath
{
    public static double Distance(double x, double y, double left, double top, double right, double bottom)
    {
        double dx = Math.Max(Math.Max(left - x, x - right), 0);
        double dy = Math.Max(Math.Max(top - y, y - bottom), 0);
        return Math.Sqrt(dx * dx + dy * dy);
    }
    public static (double X, double Y) Position(double x, double y, double vx, double vy, double gravity, double t)
        => (x + vx * t, y + vy * t + gravity * t * t / 2);
}

public sealed class StateStore
{
    private readonly string directory;
    public string FilePath => Path.Combine(directory, "deadlines.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public string? RecoveryNotice { get; private set; }
    public StateStore(string directory) { this.directory = directory; }

    private static DeskState Read(string path)
    {
        var value = JsonSerializer.Deserialize<DeskState>(File.ReadAllText(path)) ?? throw new InvalidDataException("记录为空");
        if (value.Version != 1) throw new InvalidDataException("记录版本不支持，请使用匹配版本的程序");
        Validate(value);
        value.SafeDistance = Math.Clamp(value.SafeDistance, 60, 400);
        value.BackgroundOpacity = double.IsFinite(value.BackgroundOpacity) ? Math.Clamp(value.BackgroundOpacity, .55, 1) : .88;
        if (value.Left.HasValue && !double.IsFinite(value.Left.Value)) value.Left = null;
        if (value.Top.HasValue && !double.IsFinite(value.Top.Value)) value.Top = null;
        return value;
    }

    public DeskState Load()
    {
        Directory.CreateDirectory(directory);
        if (!File.Exists(FilePath) && !File.Exists(FilePath + ".bak")) return new DeskState();
        DeskState state;
        try { state = Read(FilePath); }
        catch (Exception primary) when (primary is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            try
            {
                state = Read(FilePath + ".bak");
                if (File.Exists(FilePath)) File.Move(FilePath, FilePath + ".damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
                RecoveryNotice = "记录文件损坏，已恢复上一份备份。损坏文件已保留在数据目录中。";
            }
            catch (Exception backup) when (backup is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
            { throw new InvalidDataException("无法读取任务记录和备份。为保护原文件，程序不会覆盖它们。\n数据目录：" + directory, primary); }
        }
        // Older files have no Color field. Assign once and persist atomically
        // before returning, so opening the app again never picks a new color.
        // Keep migration writes outside the read/recovery catch block.
        if (CardColors.Ensure(state.Items.Concat(state.CompletedReadings.Select(x => x.Item)))) Save(state);
        return state;
    }

    public void Save(DeskState state)
    {
        Validate(state);
        CardColors.Ensure(state.Items.Concat(state.CompletedReadings.Select(x => x.Item)));
        Directory.CreateDirectory(directory);
        string temp = FilePath + ".tmp";
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(file, state, Json);
            file.Flush(true);
        }
        if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak", true);
        else File.Move(temp, FilePath);
    }

    private static void Validate(DeskState value)
    {
        if (value.Version != 1 || value.Items == null || value.CompletedReadings == null
            || value.CompletedReadings.Any(x => x == null || x.Item == null || x.Item.ReadingNote == null || x.CompletedAt == DateTime.MinValue))
            throw new InvalidDataException("记录内容无效");
        var all = value.Items.Concat(value.CompletedReadings.Select(x => x.Item)).ToList();
        if (all.Any(x => x == null || x.Id == Guid.Empty || string.IsNullOrWhiteSpace(x.Title) || x.Due == DateTime.MinValue)
            || all.Select(x => x.Id).Distinct().Count() != all.Count)
            throw new InvalidDataException("记录内容无效或任务身份重复");
        foreach (var item in all) if (item.ReadingNote != null) ReadingValidation.ValidateNote(item.ReadingNote);
    }
}
