using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using AntigravityAutoPilot.Models;

namespace AntigravityAutoPilot.Core;

public class CommandAnalysisResult
{
    public bool IsDangerous { get; set; }
    public string? DangerousKeyword { get; set; }
    public string? FullCommand { get; set; }
    public string? ContextSnippet { get; set; }
    public string? FullContext { get; set; }

    public void Deconstruct(out bool isDangerous, out string? dangerousKeyword, out string? contextSnippet)
    {
        isDangerous = IsDangerous;
        dangerousKeyword = DangerousKeyword;
        contextSnippet = ContextSnippet;
    }
}

public class CommandAnalyzer
{
    private static readonly Regex DestructiveRegex = new Regex(
        @"\b(rm\s+-[rfRF]{1,2}|del\s+/[sS]|rmdir\s+/[sS]|Remove-Item.*-Recurse|format\s+[a-zA-Z]:|diskpart|shutdown(\.exe)?|reboot|git\s+reset\s+--hard|git\s+clean\s+-[fF]dx?|drop\s+(database|table)|truncate\s+table|delete\s+from|docker\s+(system|volume)\s+prune|kubectl\s+delete|terraform\s+destroy)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] NonCommandLineKeywords = new[]
    {
        "Allow", "Allow always", "Always allow", "Run", "Run in terminal", "Run anyway",
        "Cancel", "Reject", "Dismiss", "Submit", "Skip", "Proceed", "Accept",
        "Don't ask again", "Never ask", "Remember my choice", "Close", "OK"
    };

    /// <summary>
    /// Checks whether the button and its surrounding context in the UI tree
    /// contains dangerous/destructive commands.
    /// </summary>
    public static CommandAnalysisResult AnalyzeContext(
        AutomationElement element,
        AppSettings settings)
    {
        var result = new CommandAnalysisResult();

        try
        {
            string surroundingText = ExtractSurroundingContext(element);
            result.FullContext = surroundingText;

            // 1. Check against user-configured dangerous command strings
            foreach (var cmd in settings.DangerousCommands)
            {
                if (string.IsNullOrWhiteSpace(cmd)) continue;

                string trimmedCmd = cmd.Trim();
                bool isMatch = false;
                int index = -1;

                if (!trimmedCmd.Contains(' ') && !trimmedCmd.Contains('-') && !trimmedCmd.Contains('/'))
                {
                    var m = Regex.Match(surroundingText, $@"\b{Regex.Escape(trimmedCmd)}\b", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        isMatch = true;
                        index = m.Index;
                    }
                }
                else
                {
                    index = surroundingText.IndexOf(trimmedCmd, StringComparison.OrdinalIgnoreCase);
                    if (index >= 0)
                    {
                        isMatch = true;
                    }
                }

                if (isMatch && index >= 0)
                {
                    result.IsDangerous = true;
                    result.DangerousKeyword = trimmedCmd;
                    result.FullCommand = ExtractFullCommand(surroundingText, trimmedCmd);
                    result.ContextSnippet = ExtractSnippet(surroundingText, index, trimmedCmd.Length);
                    return result;
                }
            }

            // 2. Check against compiled regex for complex destructive patterns
            var match = DestructiveRegex.Match(surroundingText);
            if (match.Success)
            {
                result.IsDangerous = true;
                result.DangerousKeyword = match.Value;
                result.FullCommand = ExtractFullCommand(surroundingText, match.Value);
                result.ContextSnippet = ExtractSnippet(surroundingText, match.Index, match.Length);
                return result;
            }

            result.IsDangerous = false;
            result.FullCommand = null;
            result.ContextSnippet = surroundingText;
            return result;
        }
        catch (Exception)
        {
            // If extracting context throws, be conservative
            return result;
        }
    }

    /// <summary>
    /// Traverses parent elements and siblings to collect surrounding text (e.g. terminal command preview, dialog message).
    /// </summary>
    public static string ExtractSurroundingContext(AutomationElement buttonElement)
    {
        var sb = new StringBuilder();
        var seenLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            string trimmed = line.Trim();
            if (seenLines.Add(trimmed))
            {
                sb.AppendLine(trimmed);
            }
        }

        try
        {
            // Collect button's own text & help text
            try { AddLine(buttonElement.Current.Name); } catch { }
            try
            {
                if (!string.IsNullOrEmpty(buttonElement.Current.HelpText))
                {
                    AddLine(buttonElement.Current.HelpText);
                }
            }
            catch { }

            // Climb up to 4 parent levels to gather context within the containing dialog or card
            var walker = TreeWalker.ControlViewWalker;
            var current = buttonElement;

            for (int level = 0; level < 4; level++)
            {
                var parent = walker.GetParent(current);
                if (parent == null || parent == AutomationElement.RootElement)
                {
                    break;
                }

                // Check parent name
                try
                {
                    string parentName = parent.Current.Name;
                    AddLine(parentName);
                }
                catch { }

                // Collect text from siblings and immediate children of the parent
                try
                {
                    var textCondition = new OrCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document),
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Group),
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.RadioButton),
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)
                    );

                    var textElements = parent.FindAll(TreeScope.Children, textCondition);
                    foreach (AutomationElement textElem in textElements)
                    {
                        if (textElem.Equals(buttonElement)) continue;

                        try
                        {
                            string t = textElem.Current.Name;
                            AddLine(t);

                            // If it's an Edit / Document control, check ValuePattern
                            if (textElem.TryGetCurrentPattern(ValuePattern.Pattern, out object? valPatternObj) &&
                                valPatternObj is ValuePattern valPattern)
                            {
                                string val = valPattern.Current.Value;
                                AddLine(val);
                            }
                        }
                        catch { }
                    }
                }
                catch { }

                current = parent;
            }
        }
        catch
        {
            // Ignore UIAutomation tree traversal errors
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Extracts the full command line or block surrounding the detected dangerous keyword.
    /// </summary>
    public static string ExtractFullCommand(string text, string keyword)
    {
        if (string.IsNullOrWhiteSpace(text)) return keyword;

        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .ToList();

        // 1. Find the specific line that contains the dangerous keyword
        int matchIndex = lines.FindIndex(l => l.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        if (matchIndex >= 0)
        {
            string matchedLine = lines[matchIndex];

            // If the matched line contains the command, check if adjacent lines are command continuations
            var commandLines = new List<string> { matchedLine };

            // Check following lines for continuation (ends with \, ^, |, && or starts with flags)
            int next = matchIndex + 1;
            while (next < lines.Count)
            {
                string nextLine = lines[next];
                if (IsNonCommandLine(nextLine)) break;

                string prev = commandLines.Last();
                if (prev.EndsWith("\\") || prev.EndsWith("^") || prev.EndsWith("`") || prev.EndsWith("|") || prev.EndsWith("&&") ||
                    nextLine.StartsWith("-") || nextLine.StartsWith("/") || nextLine.StartsWith("--"))
                {
                    commandLines.Add(nextLine);
                    next++;
                }
                else
                {
                    break;
                }
            }

            string full = string.Join(Environment.NewLine, commandLines).Trim();
            full = CleanCommandPrefixes(full);

            if (!string.IsNullOrWhiteSpace(full))
            {
                return full;
            }
        }

        // 2. Fallback: extract surrounding snippet
        int idx = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            return ExtractSnippet(text, idx, keyword.Length);
        }

        return keyword;
    }

    private static bool IsNonCommandLine(string line)
    {
        foreach (var keyword in NonCommandLineKeywords)
        {
            if (line.Equals(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string CleanCommandPrefixes(string cmd)
    {
        string trimmed = cmd.Trim();
        if (trimmed.StartsWith("$ ")) trimmed = trimmed.Substring(2).Trim();
        else if (trimmed.StartsWith("> ")) trimmed = trimmed.Substring(2).Trim();
        else if (trimmed.StartsWith("# ")) trimmed = trimmed.Substring(2).Trim();
        else if (trimmed.StartsWith("Command: ", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed.Substring(9).Trim();
        else if (trimmed.StartsWith("Lệnh: ", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed.Substring(6).Trim();
        return trimmed;
    }

    private static string ExtractSnippet(string text, int index, int length)
    {
        int start = Math.Max(0, index - 60);
        int end = Math.Min(text.Length, index + length + 60);
        string snippet = text.Substring(start, end - start).Replace("\r", " ").Replace("\n", " ");
        return snippet.Trim();
    }
}
