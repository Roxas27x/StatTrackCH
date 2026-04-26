#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CloneHeroSectionTracker.V1Stock
{
internal enum ExportTemplateCategory
{
    Metric,
    Section,
    Run
}

internal enum ExportTemplatePreviewContextKind
{
    Metric,
    Section,
    Run
}

internal enum ExportTemplateDirectiveKind
{
    None,
    If,
    IfNot
}

internal sealed class ExportTemplateDefinition
{
    public ExportTemplateDefinition(
        string templateId,
        string label,
        ExportTemplateCategory category,
        ExportTemplatePreviewContextKind previewContextKind,
        string defaultTemplate,
        IReadOnlyList<string> allowedTokens)
    {
        TemplateId = templateId;
        Label = label;
        Category = category;
        PreviewContextKind = previewContextKind;
        DefaultTemplate = defaultTemplate ?? string.Empty;
        AllowedTokens = allowedTokens?.Distinct(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
        AllowedTokenSet = new HashSet<string>(AllowedTokens, StringComparer.Ordinal);
    }

    public string TemplateId { get; }
    public string Label { get; }
    public ExportTemplateCategory Category { get; }
    public ExportTemplatePreviewContextKind PreviewContextKind { get; }
    public string DefaultTemplate { get; }
    public IReadOnlyList<string> AllowedTokens { get; }
    public HashSet<string> AllowedTokenSet { get; }
}

internal sealed class CompiledExportTemplate
{
    public string TemplateId { get; set; } = string.Empty;
    public string SourceText { get; set; } = string.Empty;
    public IReadOnlyList<CompiledExportTemplateLine> Lines { get; set; } = Array.Empty<CompiledExportTemplateLine>();
}

internal sealed class CompiledExportTemplateLine
{
    public ExportTemplateDirectiveKind DirectiveKind { get; set; }
    public string? ConditionToken { get; set; }
    public IReadOnlyList<CompiledExportTemplateSegment> Segments { get; set; } = Array.Empty<CompiledExportTemplateSegment>();
}

internal sealed class CompiledExportTemplateSegment
{
    public bool IsToken { get; set; }
    public string Value { get; set; } = string.Empty;
}

internal sealed class ExportTemplateRenderContext
{
    private readonly Dictionary<string, string> _stringValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _boolValues = new(StringComparer.Ordinal);

    public void Set(string tokenName, string? value)
    {
        _stringValues[tokenName] = value ?? string.Empty;
    }

    public void Set(string tokenName, int value)
    {
        _stringValues[tokenName] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public void SetBool(string tokenName, bool value)
    {
        _boolValues[tokenName] = value;
        _stringValues[tokenName] = value ? "True" : "False";
    }

    public string GetValue(string tokenName)
    {
        return _stringValues.TryGetValue(tokenName, out string? value) ? value : string.Empty;
    }

    public bool IsTruthy(string tokenName)
    {
        if (_boolValues.TryGetValue(tokenName, out bool boolValue))
        {
            return boolValue;
        }

        return _stringValues.TryGetValue(tokenName, out string? value) &&
            !string.IsNullOrEmpty(value);
    }
}

internal static class ExportTemplateCatalog
{
    private static readonly string[] MetricTokens =
    {
        "label",
        "value",
        "song_key",
        "title",
        "artist",
        "charter",
        "difficulty_name",
        "song_speed_label"
    };

    private static readonly string[] SectionTokens =
    {
        "label",
        "value",
        "song_key",
        "title",
        "artist",
        "charter",
        "difficulty_name",
        "song_speed_label",
        "section_name",
        "attempts",
        "fcs_past",
        "killed_the_run",
        "best_miss_count",
        "tracked",
        "has_best_miss_count"
    };

    private static readonly string[] RunTokens =
    {
        "label",
        "value",
        "song_key",
        "title",
        "artist",
        "charter",
        "difficulty_name",
        "song_speed_label",
        "run_index",
        "completed_at_utc",
        "percent",
        "score",
        "best_streak",
        "first_miss_streak",
        "ghosted_notes",
        "overstrums",
        "missed_notes",
        "fc_achieved",
        "fc_yes_no",
        "final_section",
        "has_final_section"
    };

    public static readonly ExportTemplateDefinition[] All =
    {
        CreateMetric("metric.current_section", "Current Section", JoinLines("Current Section: {{value}}")),
        CreateMetric("metric.streak", "Current Streak", JoinLines("Current Streak: {{value}}")),
        CreateMetric("metric.best_streak", "Best FC Streak", JoinLines("Best FC Streak: {{value}}")),
        CreateMetric("metric.attempts", "Total Attempts", JoinLines("Total Attempts: {{value}}")),
        CreateMetric("metric.current_ghosted_notes", "Current Ghosted Notes", JoinLines("Current Ghosted Notes: {{value}}")),
        CreateMetric("metric.current_overstrums", "Current Overstrums", JoinLines("Current Overstrums: {{value}}")),
        CreateMetric("metric.current_missed_notes", "Current Missed Notes", JoinLines("Current Missed Notes: {{value}}")),
        CreateMetric("metric.lifetime_ghosted_notes", "Song Lifetime Ghosted Notes", JoinLines("Song Lifetime Ghosted Notes: {{value}}")),
        CreateMetric("metric.global_lifetime_ghosted_notes", "Global Lifetime Ghosted Notes", JoinLines("Global Lifetime Ghosted Notes: {{value}}")),
        CreateMetric("metric.fc_achieved", "FC Achieved", JoinLines("FC Achieved: {{value}}")),

        CreateSection("section.current_summary", "Current Section Summary", JoinLines(
            "Section: {{section_name}}",
            "Attempts: {{attempts}}",
            "FCs Past: {{fcs_past}}",
            "Killed the Run: {{killed_the_run}}")),
        CreateSection("section.name", "Section Name", JoinLines("Section Name: {{section_name}}")),
        CreateSection("section.summary", "Section Summary", JoinLines(
            "Section: {{section_name}}",
            "Attempts: {{attempts}}",
            "FCs Past: {{fcs_past}}",
            "Killed the Run: {{killed_the_run}}")),
        CreateSection("section.attempts", "Section Attempts", JoinLines("{{attempts}}")),
        CreateSection("section.fcs_past", "Section FCs Past", JoinLines("FCs UP TO {{section_name}}: {{fcs_past}}")),
        CreateSection("section.killed_the_run", "Section Killed the Run", JoinLines("{{killed_the_run}}")),

        CreateRun("run.completed_at_utc", "Run Completed At UTC", JoinLines("Completed At UTC: {{completed_at_utc}}")),
        CreateRun("run.percent", "Run Percent", JoinLines("Percent: {{percent}}")),
        CreateRun("run.score", "Run Score", JoinLines("Score: {{score}}")),
        CreateRun("run.best_streak", "Run Best Streak", JoinLines("Best Streak: {{best_streak}}")),
        CreateRun("run.first_miss_streak", "Run First Miss Streak", JoinLines("First Miss Streak: {{first_miss_streak}}")),
        CreateRun("run.ghosted_notes", "Run Ghosted Notes", JoinLines("Ghosted Notes: {{ghosted_notes}}")),
        CreateRun("run.overstrums", "Run Overstrums", JoinLines("Overstrums: {{overstrums}}")),
        CreateRun("run.missed_notes", "Run Missed Notes", JoinLines("Missed Notes: {{missed_notes}}")),
        CreateRun("run.fc_achieved", "Run FC Achieved", JoinLines("FC Achieved: {{fc_achieved}}")),
        CreateRun("run.final_section", "Run Final Section", JoinLines("Final Section: {{final_section}}")),
        CreateRun("run.summary", "Run Summary", JoinLines("Run Summary: run {{run_index}} | percent: {{percent}} | misses: {{missed_notes}} | first miss streak: {{first_miss_streak}} | score: {{score}} | best streak: {{best_streak}} | ghosts: {{ghosted_notes}} | overstrums: {{overstrums}} | FC: {{fc_yes_no}}"))
    };

    private static readonly Dictionary<string, ExportTemplateDefinition> ById = All.ToDictionary(definition => definition.TemplateId, StringComparer.Ordinal);

    public static ExportTemplateDefinition? TryGet(string templateId)
    {
        return ById.TryGetValue(templateId, out ExportTemplateDefinition? definition)
            ? definition
            : null;
    }

    public static string GetCategoryLabel(ExportTemplateCategory category)
    {
        return category switch
        {
            ExportTemplateCategory.Metric => "Metric",
            ExportTemplateCategory.Section => "Section",
            ExportTemplateCategory.Run => "Run",
            _ => "Other"
        };
    }

    private static ExportTemplateDefinition CreateMetric(string templateId, string label, string defaultTemplate)
    {
        return new ExportTemplateDefinition(templateId, label, ExportTemplateCategory.Metric, ExportTemplatePreviewContextKind.Metric, defaultTemplate, MetricTokens);
    }

    private static ExportTemplateDefinition CreateSection(string templateId, string label, string defaultTemplate)
    {
        return new ExportTemplateDefinition(templateId, label, ExportTemplateCategory.Section, ExportTemplatePreviewContextKind.Section, defaultTemplate, SectionTokens);
    }

    private static ExportTemplateDefinition CreateRun(string templateId, string label, string defaultTemplate)
    {
        return new ExportTemplateDefinition(templateId, label, ExportTemplateCategory.Run, ExportTemplatePreviewContextKind.Run, defaultTemplate, RunTokens);
    }

    private static string JoinLines(params string[] lines)
    {
        return string.Join(Environment.NewLine, lines ?? Array.Empty<string>());
    }
}

internal static class ExportTemplateEngine
{
    private static readonly Regex TokenRegex = new(@"\{\{([A-Za-z0-9_]+)\}\}", RegexOptions.Compiled);

    public static bool TryCompile(
        ExportTemplateDefinition definition,
        string? templateText,
        out CompiledExportTemplate? compiledTemplate,
        out string? errorMessage,
        out int errorLineNumber)
    {
        string sourceText = templateText ?? string.Empty;
        string normalized = NormalizeLineEndings(sourceText);
        string[] lines = normalized.Split(new[] { '\n' }, StringSplitOptions.None);
        List<CompiledExportTemplateLine> compiledLines = new(lines.Length);

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex] ?? string.Empty;
            if (!TryCompileLine(definition, line, lineIndex + 1, out CompiledExportTemplateLine? compiledLine, out errorMessage, out errorLineNumber))
            {
                compiledTemplate = null;
                return false;
            }

            compiledLines.Add(compiledLine!);
        }

        compiledTemplate = new CompiledExportTemplate
        {
            TemplateId = definition.TemplateId,
            SourceText = sourceText,
            Lines = compiledLines
        };
        errorMessage = null;
        errorLineNumber = 0;
        return true;
    }

    public static string Render(CompiledExportTemplate template, ExportTemplateRenderContext context)
    {
        if (template.Lines.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        bool wroteAnyLine = false;
        for (int i = 0; i < template.Lines.Count; i++)
        {
            CompiledExportTemplateLine line = template.Lines[i];
            if (!ShouldRenderLine(line, context))
            {
                continue;
            }

            if (wroteAnyLine)
            {
                builder.Append(Environment.NewLine);
            }

            foreach (CompiledExportTemplateSegment segment in line.Segments)
            {
                builder.Append(segment.IsToken ? context.GetValue(segment.Value) : segment.Value);
            }

            wroteAnyLine = true;
        }

        return builder.ToString();
    }

    private static bool TryCompileLine(
        ExportTemplateDefinition definition,
        string line,
        int lineNumber,
        out CompiledExportTemplateLine? compiledLine,
        out string? errorMessage,
        out int errorLineNumber)
    {
        ExportTemplateDirectiveKind directiveKind = ExportTemplateDirectiveKind.None;
        string? conditionToken = null;
        string body = line;

        if (line.StartsWith("[[", StringComparison.Ordinal))
        {
            int directiveEnd = line.IndexOf("]]", StringComparison.Ordinal);
            if (directiveEnd < 0)
            {
                compiledLine = null;
                errorMessage = "Missing closing ]] for conditional directive.";
                errorLineNumber = lineNumber;
                return false;
            }

            string directiveBody = line.Substring(2, directiveEnd - 2).Trim();
            string[] directiveParts = directiveBody.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (directiveParts.Length != 2)
            {
                compiledLine = null;
                errorMessage = "Conditional directives must be [[if token_name]] or [[ifnot token_name]].";
                errorLineNumber = lineNumber;
                return false;
            }

            directiveKind = directiveParts[0] switch
            {
                "if" => ExportTemplateDirectiveKind.If,
                "ifnot" => ExportTemplateDirectiveKind.IfNot,
                _ => ExportTemplateDirectiveKind.None
            };

            if (directiveKind == ExportTemplateDirectiveKind.None)
            {
                compiledLine = null;
                errorMessage = $"Unknown directive '{directiveParts[0]}'.";
                errorLineNumber = lineNumber;
                return false;
            }

            conditionToken = directiveParts[1];
            if (!IsValidTokenName(conditionToken))
            {
                compiledLine = null;
                errorMessage = $"Invalid token name '{conditionToken}'.";
                errorLineNumber = lineNumber;
                return false;
            }

            if (!definition.AllowedTokenSet.Contains(conditionToken))
            {
                compiledLine = null;
                errorMessage = $"Unknown token '{conditionToken}' for template '{definition.Label}'.";
                errorLineNumber = lineNumber;
                return false;
            }

            body = line.Substring(directiveEnd + 2);
        }

        List<CompiledExportTemplateSegment> segments = new();
        int lastIndex = 0;
        foreach (Match match in TokenRegex.Matches(body))
        {
            if (match.Index > lastIndex)
            {
                segments.Add(new CompiledExportTemplateSegment
                {
                    IsToken = false,
                    Value = body.Substring(lastIndex, match.Index - lastIndex)
                });
            }

            string tokenName = match.Groups[1].Value;
            if (!definition.AllowedTokenSet.Contains(tokenName))
            {
                compiledLine = null;
                errorMessage = $"Unknown token '{tokenName}' for template '{definition.Label}'.";
                errorLineNumber = lineNumber;
                return false;
            }

            segments.Add(new CompiledExportTemplateSegment
            {
                IsToken = true,
                Value = tokenName
            });
            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < body.Length)
        {
            segments.Add(new CompiledExportTemplateSegment
            {
                IsToken = false,
                Value = body.Substring(lastIndex)
            });
        }

        compiledLine = new CompiledExportTemplateLine
        {
            DirectiveKind = directiveKind,
            ConditionToken = conditionToken,
            Segments = segments
        };
        errorMessage = null;
        errorLineNumber = 0;
        return true;
    }

    private static string NormalizeLineEndings(string value)
    {
        return value
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');
    }

    private static bool ShouldRenderLine(CompiledExportTemplateLine line, ExportTemplateRenderContext context)
    {
        if (line.DirectiveKind == ExportTemplateDirectiveKind.None)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(line.ConditionToken))
        {
            return true;
        }

        string conditionToken = line.ConditionToken!;
        bool truthy = context.IsTruthy(conditionToken);
        return line.DirectiveKind == ExportTemplateDirectiveKind.If ? truthy : !truthy;
    }

    private static bool IsValidTokenName(string tokenName)
    {
        if (string.IsNullOrWhiteSpace(tokenName))
        {
            return false;
        }

        for (int i = 0; i < tokenName.Length; i++)
        {
            char character = tokenName[i];
            if (!(char.IsLetterOrDigit(character) || character == '_'))
            {
                return false;
            }
        }

        return true;
    }
}
}
