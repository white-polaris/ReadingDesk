using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DDLDesk;

public sealed class ReadingFinding
{
    [JsonPropertyName("conclusion")] public string Conclusion { get; set; } = "";
    [JsonPropertyName("evidence")] public string Evidence { get; set; } = "";
}

public sealed class ReadingAnalysis
{
    [JsonPropertyName("research_question")] public string ResearchQuestion { get; set; } = "";
    [JsonPropertyName("method")] public string Method { get; set; } = "";
    [JsonPropertyName("findings")] public List<ReadingFinding> Findings { get; set; } = new();
    [JsonPropertyName("keywords")] public List<string> Keywords { get; set; } = new();
    [JsonPropertyName("limitations")] public string Limitations { get; set; } = "";
}

public sealed class ReadingNote
{
    public string SourceText { get; set; } = "";
    public ReadingAnalysis Analysis { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string Model { get; set; } = "deepseek-flash";
}

public sealed class CompletedReading
{
    public Deadline Item { get; set; } = new();
    public DateTime CompletedAt { get; set; } = DateTime.Now;
}

public sealed record ReadingEvidenceIssue(int FindingNumber, string Evidence);

public static class ReadingValidation
{
    public const int MaxSourceLength = 12000;
    public const string EvidenceMatchingRule = "依据须是左侧片段中的连续原文；仅忽略空格、换行等空白，保留连字符、标点和大小写差异。此校验不判断结论是否正确。";
    public static void ValidateSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) throw new InvalidDataException("请先粘贴文献片段。");
        if (source.Length > MaxSourceLength) throw new InvalidDataException($"原文超过 {MaxSourceLength:N0} 字符，请缩短后再分析。");
    }

    public static void ValidateAnalysis(ReadingAnalysis value, string source, bool validateEvidence = true)
    {
        ValidateSource(source);
        if (value == null || string.IsNullOrWhiteSpace(value.ResearchQuestion) || string.IsNullOrWhiteSpace(value.Method)
            || string.IsNullOrWhiteSpace(value.Limitations) || value.Findings == null || value.Findings.Count is < 1 or > 8
            || value.Keywords == null || value.Keywords.Count is < 1 or > 12
            || value.Keywords.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 100)
            || value.Findings.Any(x => x == null || string.IsNullOrWhiteSpace(x.Conclusion) || x.Evidence == null))
            throw new InvalidDataException("笔记字段不完整，请检查研究问题、方法、结论、关键词与局限。");
        if (value.ResearchQuestion.Length > 6000 || value.Method.Length > 6000 || value.Limitations.Length > 6000
            || value.Findings.Any(x => x.Conclusion.Length > 6000 || x.Evidence.Length > 2000))
            throw new InvalidDataException("分析结果过长，请精简内容后保存。");
        if (validateEvidence)
        {
            var issues = FindEvidenceIssues(value, source);
            if (issues.Count > 0) throw new InvalidDataException(DescribeEvidenceIssues(issues));
        }
    }

    public static IReadOnlyList<ReadingEvidenceIssue> FindEvidenceIssues(ReadingAnalysis value, string source)
    {
        string compactSource = Compact(source);
        var issues = new List<ReadingEvidenceIssue>();
        for (int i = 0; i < value.Findings.Count; i++)
        {
            string evidence = value.Findings[i].Evidence;
            if (!string.IsNullOrWhiteSpace(evidence) && !compactSource.Contains(Compact(evidence), StringComparison.Ordinal))
                issues.Add(new ReadingEvidenceIssue(i + 1, evidence));
        }
        return issues;
    }

    public static string DescribeEvidenceIssues(IReadOnlyList<ReadingEvidenceIssue> issues)
    {
        if (issues.Count == 0) return "";
        var lines = issues.Select(issue =>
        {
            string preview = Regex.Replace(issue.Evidence.Trim(), @"\s+", " ");
            if (preview.Length > 200) preview = preview[..200] + "…（其余见标红输入框）";
            return $"主要结论 {issue.FindingNumber} → 对应原文依据：“{preview}”";
        });
        return "以下原文依据无法在输入片段中定位：\n" + string.Join("\n", lines)
            + "\n" + EvidenceMatchingRule + " PDF 复制产生的断词连字符也会影响匹配。请核对并修正或清空所列依据。";
    }

    public static void ValidateNote(ReadingNote note)
    {
        if (note == null || string.IsNullOrWhiteSpace(note.Model) || note.CreatedAt == DateTime.MinValue)
            throw new InvalidDataException("阅读笔记元数据无效。");
        ValidateAnalysis(note.Analysis, note.SourceText);
    }

    private static string Compact(string value) => Regex.Replace(value, @"\s+", "");
}

public sealed record TaskRemoval(Deadline Item, int Index, bool Archived);

public static class TaskHistory
{
    public static TaskRemoval? Remove(DeskState state, Deadline item, bool complete)
    {
        int index = state.Items.IndexOf(item);
        if (index < 0) return null;
        bool archived = complete && item.ReadingNote != null;
        if (archived && state.CompletedReadings.Any(x => x.Item.Id == item.Id))
            throw new InvalidDataException("阅读任务已归档，请重新打开列表。");
        state.Items.RemoveAt(index);
        if (archived) state.CompletedReadings.Add(new CompletedReading { Item = item });
        return new TaskRemoval(item, index, archived);
    }

    public static bool Undo(DeskState state, TaskRemoval removal)
    {
        if (state.Items.Any(x => x.Id == removal.Item.Id)) return false;
        if (removal.Archived && !state.CompletedReadings.Any(x => x.Item.Id == removal.Item.Id)) return false;
        if (removal.Archived) state.CompletedReadings.RemoveAll(x => x.Item.Id == removal.Item.Id);
        state.Items.Insert(Math.Clamp(removal.Index, 0, state.Items.Count), removal.Item);
        return true;
    }

    public static bool DeleteCompleted(DeskState state, Guid itemId, Func<bool> persist)
    {
        int index = state.CompletedReadings.FindIndex(x => x.Item.Id == itemId);
        if (index < 0) return false;
        var entry = state.CompletedReadings[index];
        bool saved = false;
        state.CompletedReadings.RemoveAt(index);
        try { saved = persist(); return saved; }
        finally { if (!saved) state.CompletedReadings.Insert(index, entry); }
    }
}
