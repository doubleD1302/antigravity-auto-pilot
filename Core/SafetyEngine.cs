using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using AntigravityAutoPilot.Models;

namespace AntigravityAutoPilot.Core;

public class SafetyEngine
{
    public class EvaluationResult
    {
        public bool IsAllowed { get; set; }
        public ActionKind Kind { get; set; }
        public string CleanText { get; set; } = string.Empty;
        public bool IsBlocked { get; set; }
        public string? BlockReason { get; set; }
        public bool IsTerminalApproval { get; set; }
        public string? DangerousCommand { get; set; }
    }

    /// <summary>
    /// Evaluates a candidate AutomationElement button against blacklist, whitelist,
    /// operating mode, and terminal command destructive checks.
    /// </summary>
    public static EvaluationResult Evaluate(AutomationElement element, AppSettings settings)
    {
        var result = new EvaluationResult();

        string rawText = string.Empty;
        try
        {
            rawText = element.Current.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawText))
            {
                // Try HelpText
                rawText = element.Current.HelpText ?? string.Empty;
            }
            if (string.IsNullOrWhiteSpace(rawText))
            {
                // Fallback to first child element (e.g. icon + text label span in webview)
                var firstChild = TreeWalker.ControlViewWalker.GetFirstChild(element);
                if (firstChild != null)
                {
                    rawText = firstChild.Current.Name ?? string.Empty;
                }
            }
        }
        catch
        {
            result.IsAllowed = false;
            return result;
        }

        string cleanText = NormalizeText(rawText);
        result.CleanText = cleanText;

        if (string.IsNullOrWhiteSpace(cleanText))
        {
            result.IsAllowed = false;
            return result;
        }

        // Exclude Menus, MenuBars, TitleBars, and MenuItems (e.g. top IDE menu bar "Run")
        try
        {
            var ctrlType = element.Current.ControlType;
            if (ctrlType == ControlType.MenuItem ||
                ctrlType == ControlType.Menu ||
                ctrlType == ControlType.MenuBar ||
                ctrlType == ControlType.TitleBar)
            {
                result.IsAllowed = false;
                return result;
            }
        }
        catch { }

        // ==========================================
        // 1. ABSOLUTE BLACKLIST CHECK (HIGHEST PRIORITY)
        // ==========================================
        foreach (var blacklisted in AppSettings.AbsoluteBlacklist)
        {
            if (ContainsWord(cleanText, blacklisted))
            {
                result.IsAllowed = false;
                result.IsBlocked = true;
                result.BlockReason = $"Trùng khớp danh sách đen tuyệt đối: '{blacklisted}'";
                result.Kind = ActionKind.Blocked;
                return result;
            }
        }

        // Custom Blacklist
        foreach (var customBlack in settings.CustomBlacklist)
        {
            if (string.IsNullOrWhiteSpace(customBlack)) continue;
            if (ContainsWord(cleanText, customBlack))
            {
                result.IsAllowed = false;
                result.IsBlocked = true;
                result.BlockReason = $"Trùng khớp danh sách đen tùy chỉnh: '{customBlack}'";
                result.Kind = ActionKind.Blocked;
                return result;
            }
        }

        // ==========================================
        // 1.5. EXCLUDED NON-AGENT IDE / EDITOR ACTIONS
        // Prevent clicking editor actions like "Run Code (Ctrl+Alt+N)", "Run and Debug", etc.
        // ==========================================
        if (IsExcludedIdeAction(cleanText))
        {
            result.IsAllowed = false;
            return result;
        }

        // ==========================================
        // 2. WHITELIST MATCHING
        // ==========================================
        var matchedKind = MatchWhitelist(cleanText, settings);
        if (matchedKind == null)
        {
            result.IsAllowed = false;
            return result;
        }

        result.Kind = matchedKind.Value;

        // Check if this action represents terminal command execution
        bool isTerminal = result.Kind == ActionKind.Run || 
                          cleanText.Contains("run command", StringComparison.OrdinalIgnoreCase) ||
                          cleanText.Contains("run in terminal", StringComparison.OrdinalIgnoreCase);
        result.IsTerminalApproval = isTerminal;

        // ==========================================
        // 3. OPERATING MODE CHECKS
        // ==========================================
        if (settings.Mode == OperatingMode.Safe)
        {
            // SAFE mode: Only Accept file edit, Accept changes, Continue, Submit.
            // Terminal commands (Run / Run command / Allow) are strictly prohibited in Safe mode.
            if (isTerminal || result.Kind == ActionKind.Run || result.Kind == ActionKind.Allow)
            {
                result.IsAllowed = false;
                result.IsBlocked = true;
                result.BlockReason = "Chế độ An Toàn: Lệnh terminal / thực thi mã bị vô hiệu hóa";
                result.Kind = ActionKind.Blocked;
                return result;
            }

            bool isSafeAllowed = result.Kind == ActionKind.Accept ||
                                 result.Kind == ActionKind.AcceptAll ||
                                 result.Kind == ActionKind.Continue ||
                                 result.Kind == ActionKind.Proceed ||
                                 result.Kind == ActionKind.Submit ||
                                 result.Kind == ActionKind.Apply;

            if (!isSafeAllowed)
            {
                result.IsAllowed = false;
                result.IsBlocked = true;
                result.BlockReason = $"Chế độ An Toàn: Hành động '{result.Kind}' không được phép";
                result.Kind = ActionKind.Blocked;
                return result;
            }
        }

        // ==========================================
        // 4. DESTRUCTIVE TERMINAL COMMAND CHECK & LEGITIMATE AGENT ACTION VALIDATION
        // (Applies in Normal & Full Auto modes for Run/Terminal buttons)
        // ==========================================
        if (isTerminal || result.Kind == ActionKind.Run)
        {
            var (isDangerous, dangerousCmd, context) = CommandAnalyzer.AnalyzeContext(element, settings);
            if (isDangerous)
            {
                result.IsAllowed = false;
                result.IsBlocked = true;
                result.DangerousCommand = dangerousCmd;
                result.BlockReason = $"ĐÃ CHẶN — phát hiện lệnh có nguy cơ phá hủy: {dangerousCmd}";
                result.Kind = ActionKind.Blocked;
                return result;
            }

            // Verify this is a genuine agent terminal approval rather than an IDE editor action or menu
            if (!IsLegitimateTerminalApproval(element, cleanText, context ?? string.Empty))
            {
                result.IsAllowed = false;
                return result;
            }
        }

        // All checks passed!
        result.IsAllowed = true;
        return result;
    }

    /// <summary>
    /// Evaluates whether an interactive modal question form (ask_question) is safe to auto-answer.
    /// Checks blacklist, operating mode, and destructive terminal commands in context.
    /// </summary>
    public static EvaluationResult EvaluateQuestionContext(AutomationElement submitOrCardElement, AppSettings settings, string contextText)
    {
        var result = new EvaluationResult
        {
            Kind = ActionKind.AutoAnswer,
            CleanText = "Tự Động Trả Lời Câu Hỏi"
        };

        // 1. Custom Blacklist check (if user configured specific prohibited words)
        foreach (var customBlack in settings.CustomBlacklist)
        {
            if (string.IsNullOrWhiteSpace(customBlack)) continue;
            if (ContainsWord(contextText, customBlack))
            {
                result.IsAllowed = false;
                result.IsBlocked = true;
                result.BlockReason = $"Nội dung câu hỏi chứa từ trong danh sách đen tùy chỉnh: '{customBlack}'";
                result.Kind = ActionKind.Blocked;
                return result;
            }
        }

        // 2. Destructive terminal command check
        var (isDangerous, dangerousCmd, snippet) = CommandAnalyzer.AnalyzeContext(submitOrCardElement, settings);
        if (isDangerous)
        {
            result.IsAllowed = false;
            result.IsBlocked = true;
            result.DangerousCommand = dangerousCmd;
            result.BlockReason = $"ĐÃ CHẶN — phát hiện lệnh nguy hiểm trong câu hỏi: {dangerousCmd}";
            result.Kind = ActionKind.Blocked;
            return result;
        }

        // 3. Operating Mode check: Terminal command detection in question
        bool appearsToBeTerminalApproval = contextText.Contains("findstr", StringComparison.OrdinalIgnoreCase) ||
                                           contextText.Contains("netstat", StringComparison.OrdinalIgnoreCase) ||
                                           contextText.Contains("grep", StringComparison.OrdinalIgnoreCase) ||
                                           contextText.Contains("powershell", StringComparison.OrdinalIgnoreCase) ||
                                           contextText.Contains("run command", StringComparison.OrdinalIgnoreCase) ||
                                           contextText.Contains("Allow checking", StringComparison.OrdinalIgnoreCase) ||
                                           contextText.Contains("Allow running", StringComparison.OrdinalIgnoreCase) ||
                                           contextText.Contains("Allow executing", StringComparison.OrdinalIgnoreCase) ||
                                           contextText.Contains("Allow command", StringComparison.OrdinalIgnoreCase);

        result.IsTerminalApproval = appearsToBeTerminalApproval;

        if (settings.Mode == OperatingMode.Safe && appearsToBeTerminalApproval)
        {
            result.IsAllowed = false;
            result.IsBlocked = true;
            result.BlockReason = "Chế độ An Toàn: Duyệt lệnh terminal trong biểu mẫu câu hỏi bị vô hiệu hóa";
            result.Kind = ActionKind.Blocked;
            return result;
        }

        result.IsAllowed = true;
        return result;
    }

    public static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        // Strip codicons / private use area unicode characters (\uE000-\uF8FF, \uFFF0-\uFFFF)
        string cleaned = Regex.Replace(text, @"[\uE000-\uF8FF\uFFF0-\uFFFF]", "");
        // Strip common accelerator ampersands
        cleaned = cleaned.Replace("&", "");
        // Normalize whitespace (including non-breaking spaces, newlines, tabs)
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned;
    }

    private static bool ContainsWord(string text, string word)
    {
        // Whole word or token boundary match (case-insensitive)
        string pattern = $@"\b{Regex.Escape(word)}\b";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
    }

    private static ActionKind? MatchWhitelist(string cleanText, AppSettings settings)
    {
        // Priority order matching

        // 1. Accept All (Match any variation of "accept all")
        if (settings.EnableAcceptAll)
        {
            if (cleanText.Equals("accept all", StringComparison.OrdinalIgnoreCase) ||
                cleanText.StartsWith("accept all", StringComparison.OrdinalIgnoreCase) ||
                cleanText.Equals("acceptall", StringComparison.OrdinalIgnoreCase) ||
                cleanText.Equals("accept all changes", StringComparison.OrdinalIgnoreCase) ||
                cleanText.StartsWith("accept all changes", StringComparison.OrdinalIgnoreCase) ||
                cleanText.Equals("accept all edits", StringComparison.OrdinalIgnoreCase) ||
                cleanText.StartsWith("accept all edits", StringComparison.OrdinalIgnoreCase) ||
                cleanText.Contains("accept all", StringComparison.OrdinalIgnoreCase))
            {
                return ActionKind.AcceptAll;
            }
        }

        // 2. Accept (single file / change / diff)
        if (settings.EnableAccept)
        {
            if (cleanText.Equals("accept", StringComparison.OrdinalIgnoreCase) ||
                cleanText.StartsWith("accept ", StringComparison.OrdinalIgnoreCase) ||
                cleanText.StartsWith("accept(", StringComparison.OrdinalIgnoreCase) ||
                cleanText.StartsWith("accept[", StringComparison.OrdinalIgnoreCase) ||
                cleanText.Contains("accept change", StringComparison.OrdinalIgnoreCase) ||
                cleanText.Contains("accept edit", StringComparison.OrdinalIgnoreCase) ||
                cleanText.Contains("accept diff", StringComparison.OrdinalIgnoreCase) ||
                cleanText.Contains("accept file", StringComparison.OrdinalIgnoreCase))
            {
                return ActionKind.Accept;
            }
        }

        // 3. Continue
        if (settings.EnableContinue &&
            (MatchesPhrase(cleanText, "continue") ||
             MatchesPhrase(cleanText, "continue anyway") ||
             MatchesPhrase(cleanText, "continue without review")))
        {
            return ActionKind.Continue;
        }

        // 3.5. Proceed (Plan approvals)
        if (settings.EnableProceed &&
            (MatchesPhrase(cleanText, "proceed") ||
             MatchesPhrase(cleanText, "proceed plan") ||
             MatchesPhrase(cleanText, "proceed with plan") ||
             cleanText.Equals("proceed", StringComparison.OrdinalIgnoreCase)))
        {
            return ActionKind.Proceed;
        }

        // 4. Submit
        // NOTE: Standalone "Submit" without options is the chat prompt input Send button!
        // Question forms with Submit are specifically handled by HandleQuestionFormAsync in AutomationScanner.
        if (settings.EnableSubmit &&
            (MatchesPhrase(cleanText, "submit query") ||
             MatchesPhrase(cleanText, "submit prompt") ||
             MatchesPhrase(cleanText, "send feedback")))
        {
            return ActionKind.Submit;
        }

        // 5. Run
        if (settings.EnableRun &&
            (MatchesPhrase(cleanText, "run") ||
             MatchesPhrase(cleanText, "run command") ||
             MatchesPhrase(cleanText, "run in terminal") ||
             MatchesPhrase(cleanText, "run anyway") ||
             MatchesPhrase(cleanText, "execute")))
        {
            return ActionKind.Run;
        }

        // 6. Retry
        if (settings.EnableRetry &&
            (MatchesPhrase(cleanText, "retry") ||
             MatchesPhrase(cleanText, "retry request") ||
             MatchesPhrase(cleanText, "try again")))
        {
            return ActionKind.Retry;
        }

        // 7. Allow
        if (settings.EnableAllow &&
            (MatchesPhrase(cleanText, "allow") ||
             MatchesPhrase(cleanText, "allow once") ||
             MatchesPhrase(cleanText, "allow always") ||
             MatchesPhrase(cleanText, "grant")))
        {
            return ActionKind.Allow;
        }

        // 8. Apply
        if (settings.EnableApply &&
            (MatchesPhrase(cleanText, "apply") ||
             MatchesPhrase(cleanText, "apply changes") ||
             MatchesPhrase(cleanText, "apply all") ||
             MatchesPhrase(cleanText, "apply diff")))
        {
            return ActionKind.Apply;
        }

        // 9. Confirm
        if (settings.EnableConfirm &&
            (MatchesPhrase(cleanText, "confirm") ||
             MatchesPhrase(cleanText, "confirm changes") ||
             MatchesPhrase(cleanText, "approve")))
        {
            return ActionKind.Confirm;
        }

        // 10. Custom Whitelist
        foreach (var custom in settings.CustomWhitelist)
        {
            if (string.IsNullOrWhiteSpace(custom)) continue;
            if (MatchesPhrase(cleanText, custom))
            {
                return ActionKind.Custom;
            }
        }

        return null;
    }

    private static readonly string[] ExcludedIdePhrases = new[]
    {
        "Run Code",
        "Run and Debug",
        "Run Without Debugging",
        "Run Test",
        "Run Tests",
        "Run Task",
        "Run Build",
        "Run File",
        "Run Cell",
        "Run Selected",
        "Run Python",
        "Run in Interactive Window",
        "Run Active File In Terminal",
        "Run Selected Text In Active Terminal",
        "Run All",
        "Run Above",
        "Run Below",
        "Run...",
        "Debug",
        "Start Debugging",
        "Stop Debugging",
        "Restart Debugging",
        "Open Configurations",
        "Add Configuration",
        "More Actions",
        "Split Editor",
        "Toggle",
        "Open Changes",
        "Send message",
        "Send Message",
        "Send prompt",
        "Submit prompt",
        "Send"
    };

    private static bool IsExcludedIdeAction(string text)
    {
        foreach (var phrase in ExcludedIdePhrases)
        {
            if (text.StartsWith(phrase, StringComparison.OrdinalIgnoreCase) ||
                text.Equals(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Verifies if a button matching "Run" or terminal execution is a legitimate agent approval,
    /// rather than an IDE editor action, menu bar item, or debug control.
    /// </summary>
    public static bool IsLegitimateTerminalApproval(AutomationElement element, string cleanText, string context)
    {
        // Explicit terminal phrases are specific to terminal command execution
        if (cleanText.Contains("run command", StringComparison.OrdinalIgnoreCase) ||
            cleanText.Contains("run in terminal", StringComparison.OrdinalIgnoreCase) ||
            cleanText.Contains("run anyway", StringComparison.OrdinalIgnoreCase) ||
            cleanText.StartsWith("allow", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // For standalone "Run" or "Execute":
        // Must have terminal/command execution context or approval siblings
        if (string.IsNullOrWhiteSpace(context))
        {
            return false;
        }

        // Check if context contains terminal / command execution indicators
        bool hasTerminalIndicator =
            context.Contains("command", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("terminal", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("powershell", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("pwsh", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("bash", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("npm", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("dotnet", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("git", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("python", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("node", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("npx", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("pip", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("yarn", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("cargo", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("docker", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("Always allow", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("Do you want to run", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("Allow running", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("Allow command", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("Execute", StringComparison.OrdinalIgnoreCase);

        // Also check for standard approval card sibling buttons (Cancel, Skip, Reject, Always Allow)
        bool hasApprovalCardSiblings =
            context.Contains("Cancel", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("Skip", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("Always Allow", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("Reject", StringComparison.OrdinalIgnoreCase);

        return hasTerminalIndicator || hasApprovalCardSiblings;
    }

    private static bool MatchesPhrase(string text, string targetPhrase)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(targetPhrase))
            return false;

        // 1. Exact match
        if (string.Equals(text, targetPhrase, StringComparison.OrdinalIgnoreCase))
            return true;

        // 2. Shortcut suffix match (e.g. "Run (Ctrl+Enter)", "Accept (Enter)", "Submit [Enter]")
        // Must only be followed by whitespace + parentheses or brackets containing shortcut
        string pattern = $@"^{Regex.Escape(targetPhrase)}\s*[\(\[].*[\)\]]$";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
    }
}
