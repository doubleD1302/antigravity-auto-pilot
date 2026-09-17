using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using AntigravityAutoPilot.Models;

namespace AntigravityAutoPilot.Core;

public class CommandAnalyzer
{
    private static readonly Regex DestructiveRegex = new Regex(
        @"\b(rm\s+-[rfRF]{1,2}|del\s+/[sS]|rmdir\s+/[sS]|Remove-Item.*-Recurse|format\s+[a-zA-Z]:|diskpart|shutdown(\.exe)?|reboot|git\s+reset\s+--hard|git\s+clean\s+-[fF]dx?|drop\s+(database|table)|truncate\s+table|delete\s+from|docker\s+(system|volume)\s+prune|kubectl\s+delete|terraform\s+destroy)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Checks whether the button and its surrounding context in the UI tree
    /// contains dangerous/destructive commands.
    /// </summary>
    public static (bool IsDangerous, string? DangerousCommand, string? ContextSnippet) AnalyzeContext(
        AutomationElement element,
        AppSettings settings)
    {
        try
        {
            string surroundingText = ExtractSurroundingContext(element);
            
            // 1. Check against user-configured dangerous command strings
            foreach (var cmd in settings.DangerousCommands)
            {
                if (string.IsNullOrWhiteSpace(cmd)) continue;

                int index = surroundingText.IndexOf(cmd, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    string snippet = ExtractSnippet(surroundingText, index, cmd.Length);
                    return (true, cmd, snippet);
                }
            }

            // 2. Check against compiled regex for complex destructive patterns
            var match = DestructiveRegex.Match(surroundingText);
            if (match.Success)
            {
                string snippet = ExtractSnippet(surroundingText, match.Index, match.Length);
                return (true, match.Value, snippet);
            }

            return (false, null, surroundingText);
        }
        catch (Exception)
        {
            // If extracting context throws, be conservative
            return (false, null, null);
        }
    }

    /// <summary>
    /// Traverses parent elements and siblings to collect surrounding text (e.g. terminal command preview, dialog message).
    /// </summary>
    public static string ExtractSurroundingContext(AutomationElement buttonElement)
    {
        var sb = new StringBuilder();

        try
        {
            // Collect button's own text & help text
            sb.AppendLine(buttonElement.Current.Name);
            if (!string.IsNullOrEmpty(buttonElement.Current.HelpText))
            {
                sb.AppendLine(buttonElement.Current.HelpText);
            }

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
                    if (!string.IsNullOrWhiteSpace(parentName))
                    {
                        sb.AppendLine(parentName);
                    }
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
                            if (!string.IsNullOrWhiteSpace(t))
                            {
                                sb.AppendLine(t);
                            }

                            // If it's an Edit / Document control, check ValuePattern
                            if (textElem.TryGetCurrentPattern(ValuePattern.Pattern, out object? valPatternObj) &&
                                valPatternObj is ValuePattern valPattern)
                            {
                                string val = valPattern.Current.Value;
                                if (!string.IsNullOrWhiteSpace(val))
                                {
                                    sb.AppendLine(val);
                                }
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

        return sb.ToString();
    }

    private static string ExtractSnippet(string text, int index, int length)
    {
        int start = Math.Max(0, index - 40);
        int end = Math.Min(text.Length, index + length + 40);
        string snippet = text.Substring(start, end - start).Replace("\r", " ").Replace("\n", " ");
        return snippet.Trim();
    }
}
