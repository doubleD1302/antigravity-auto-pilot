using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using AntigravityAutoPilot.Models;

namespace AntigravityAutoPilot.Core;

public static class ResponseProgressAnalyzer
{
    #region Regex Patterns

    // Vietnamese in-progress patterns:
    // 1. Starts with or predicate contains "Đang" followed by a common action verb
    private static readonly Regex VietnameseActionRegex = new Regex(
        @"\b[Đđ]ang\s+(tải|cài|chạy|xử\s*lý|thực\s*hiện|tiến\s*hành|kiểm\s*tra|tạo|build|compile|clone|giải\s*nén|huấn\s*luyện|train|chuẩn\s*bị|quét|khởi\s*động|chờ|đợi|cập\s*nhật|update|chuyển|đọc|ghi|cấu\s*hình|kết\s*nối|lấy|đồng\s*bộ|sync|pull|push|fetch|download|install|run|start)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 2. Starts with "Đang" (e.g. "Đang tải...", "Đang phân tích...")
    private static readonly Regex VietnameseStartsWithDangRegex = new Regex(
        @"^[#\s*\->]*[Đđ]ang\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 3. Subject + đang (e.g. "Tôi đang...", "Hệ thống đang...", "Agent đang...")
    private static readonly Regex VietnameseSubjectDangRegex = new Regex(
        @"\b(tôi|mình|hệ\s*thống|agent|chúng\s*tôi|script|tiến\s*trình)\s+đang\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 4. Status + đang (e.g. "Hiện đang...", "Vẫn đang...", "Đang trong quá trình...")
    private static readonly Regex VietnameseStatusDangRegex = new Regex(
        @"\b(hiện|vẫn|đang)\s+đang\b|\bhiện\s*tại\s+đang\b|\bđang\s+trong\s+quá\s*trình\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 5. Waiting phrases (e.g. "Vui lòng chờ...", "Chờ trong giây lát...")
    private static readonly Regex VietnameseWaitRegex = new Regex(
        @"\b(vui\s*lòng|xin\s*vui\s*lòng)\s+(chờ|đợi)\b|\b(chờ|đợi)\s+trong\s+giây\s*lát\b|\bquá\s*trình\s+này\s+(có\s+thể\s+)?mất\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // English in-progress patterns:
    // 1. Starts with progressive present participle action verb
    private static readonly Regex EnglishActionStartRegex = new Regex(
        @"^[#\s*\->]*(downloading|installing|running|executing|processing|building|compiling|generating|fetching|cloning|extracting|setting\s+up|preparing|waiting(\s+for|\s+on)?|configuring|training|migrating|updating|deleting|copying|moving|checking|analyzing|syncing|pulling|pushing|starting)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 2. Contains currently/presently/now + V-ing
    private static readonly Regex EnglishCurrentlyActionRegex = new Regex(
        @"\b(currently|presently|now)\s+(downloading|installing|running|executing|processing|building|compiling|generating|fetching|cloning|working|waiting|in\s+progress)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 3. Subject + is/am + V-ing / currently
    private static readonly Regex EnglishSubjectInProgressRegex = new Regex(
        @"\b(i\s+am|i'm|we\s+are|we're|agent\s+is|system\s+is)\s+(currently\s+)?(downloading|installing|running|executing|processing|building|compiling|generating|fetching|working\s+on|waiting)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 4. Explicit in-progress phrases
    private static readonly Regex EnglishInProgressPhrasesRegex = new Regex(
        @"\b(in\s+progress|work\s+in\s+progress|still\s+running|still\s+in\s+progress)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 5. English waiting phrases
    private static readonly Regex EnglishWaitRegex = new Regex(
        @"\b(please\s+wait|hold\s+on|this\s+may\s+take\s+(a\s+while|some\s+time|a\s+few\s+minutes|a\s+moment))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Explicit completion indicators that supersede earlier in-progress mentions IF at the very end
    private static readonly Regex ExplicitCompletionEndRegex = new Regex(
        @"\b(đã\s+hoàn\s*thành|đã\s+xong|hoàn\s*tất|thành\s*công|completed\s+successfully|finished\s+successfully|all\s+done|ready\s+to\s+use)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Ignorable UI text (timestamps, buttons, git diff badges)
    private static readonly Regex TimestampRegex = new Regex(
        @"^\d{1,2}:\d{2}(:\d{2})?(\s*(AM|PM|am|pm))?$",
        RegexOptions.Compiled);

    private static readonly Regex GitChangedFilesRegex = new Regex(
        @"^\d+\s+files?\s+changed.*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GitDiffStatsRegex = new Regex(
        @"^[+-]\d+(\s+[+-]\d+)*$",
        RegexOptions.Compiled);

    #endregion

    /// <summary>
    /// Analyzes the extracted response text to determine if the final sentence/statement indicates
    /// that the agent is currently working on an ongoing / time-consuming task.
    /// </summary>
    public static bool IsInProgressResponse(
        string? responseText,
        AppSettings? settings,
        out string? matchedReason,
        out string? lastSentence)
    {
        matchedReason = null;
        lastSentence = null;

        if (string.IsNullOrWhiteSpace(responseText))
        {
            return false;
        }

        // 1. Extract the last meaningful sentence from the response
        lastSentence = GetLastMeaningfulSentence(responseText);
        if (string.IsNullOrWhiteSpace(lastSentence))
        {
            return false;
        }

        string cleanSentence = lastSentence.Trim();

        // 2. Check user-configured custom in-progress keywords first
        if (settings?.CustomInProgressKeywords != null)
        {
            foreach (var kw in settings.CustomInProgressKeywords)
            {
                if (string.IsNullOrWhiteSpace(kw)) continue;
                if (cleanSentence.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matchedReason = $"Trùng khớp từ khóa tùy chỉnh '{kw}'";
                    return true;
                }
            }
        }

        // 3. Check Vietnamese in-progress patterns
        if (VietnameseActionRegex.IsMatch(cleanSentence))
        {
            var match = VietnameseActionRegex.Match(cleanSentence);
            if (!ConcludesWithCompletion(cleanSentence, match.Index))
            {
                matchedReason = $"Phát hiện hành động tiến trình tiếng Việt: '{match.Value}'";
                return true;
            }
        }

        if (VietnameseStartsWithDangRegex.IsMatch(cleanSentence))
        {
            var match = VietnameseStartsWithDangRegex.Match(cleanSentence);
            if (!ConcludesWithCompletion(cleanSentence, match.Index))
            {
                matchedReason = "Câu cuối bắt đầu bằng từ khóa tiến trình 'Đang'";
                return true;
            }
        }

        if (VietnameseSubjectDangRegex.IsMatch(cleanSentence))
        {
            var match = VietnameseSubjectDangRegex.Match(cleanSentence);
            if (!ConcludesWithCompletion(cleanSentence, match.Index))
            {
                matchedReason = $"Phát hiện trạng thái tiến trình: '{match.Value}'";
                return true;
            }
        }

        if (VietnameseStatusDangRegex.IsMatch(cleanSentence))
        {
            var match = VietnameseStatusDangRegex.Match(cleanSentence);
            if (!ConcludesWithCompletion(cleanSentence, match.Index))
            {
                matchedReason = $"Phát hiện trạng thái tiến trình: '{match.Value}'";
                return true;
            }
        }

        if (VietnameseWaitRegex.IsMatch(cleanSentence))
        {
            var match = VietnameseWaitRegex.Match(cleanSentence);
            if (!ConcludesWithCompletion(cleanSentence, match.Index))
            {
                matchedReason = $"Phát hiện yêu cầu chờ tác vụ: '{match.Value}'";
                return true;
            }
        }

        // 4. Check English / International in-progress patterns
        if (EnglishActionStartRegex.IsMatch(cleanSentence))
        {
            var match = EnglishActionStartRegex.Match(cleanSentence);
            if (!ConcludesWithCompletion(cleanSentence, match.Index))
            {
                matchedReason = $"Phát hiện hành động tiến trình tiếng Anh: '{match.Value.Trim()}'";
                return true;
            }
        }

        if (EnglishCurrentlyActionRegex.IsMatch(cleanSentence))
        {
            var match = EnglishCurrentlyActionRegex.Match(cleanSentence);
            if (!ConcludesWithCompletion(cleanSentence, match.Index))
            {
                matchedReason = $"Phát hiện tiến trình đang thực thi: '{match.Value}'";
                return true;
            }
        }

        if (EnglishSubjectInProgressRegex.IsMatch(cleanSentence))
        {
            var match = EnglishSubjectInProgressRegex.Match(cleanSentence);
            if (!ConcludesWithCompletion(cleanSentence, match.Index))
            {
                matchedReason = $"Phát hiện trạng thái đang thực thi: '{match.Value}'";
                return true;
            }
        }

        if (EnglishInProgressPhrasesRegex.IsMatch(cleanSentence))
        {
            var match = EnglishInProgressPhrasesRegex.Match(cleanSentence);
            if (!ConcludesWithCompletion(cleanSentence, match.Index))
            {
                matchedReason = $"Phát hiện cụm từ tiến trình: '{match.Value}'";
                return true;
            }
        }

        if (EnglishWaitRegex.IsMatch(cleanSentence))
        {
            var match = EnglishWaitRegex.Match(cleanSentence);
            if (!ConcludesWithCompletion(cleanSentence, match.Index))
            {
                matchedReason = $"Phát hiện thông báo chờ tác vụ tiếng Anh: '{match.Value}'";
                return true;
            }
        }

        // 5. Fallback check on full text: if entire response is short (e.g. <= 2 lines)
        // and contains "Đang" or progressive action anywhere, treat as in-progress
        var lines = responseText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 3)
        {
            foreach (var line in lines)
            {
                string cl = line.Trim();
                if (IsIgnorableUiLabel(cl)) continue;

                if (VietnameseActionRegex.IsMatch(cl))
                {
                    var match = VietnameseActionRegex.Match(cl);
                    if (!ConcludesWithCompletion(cl, match.Index))
                    {
                        matchedReason = $"Phát hiện từ khóa tiến trình 'Đang' trong phản hồi ngắn: '{cl}'";
                        return true;
                    }
                }
                else if (VietnameseStartsWithDangRegex.IsMatch(cl))
                {
                    var match = VietnameseStartsWithDangRegex.Match(cl);
                    if (!ConcludesWithCompletion(cl, match.Index))
                    {
                        matchedReason = $"Phát hiện từ khóa tiến trình 'Đang' trong phản hồi ngắn: '{cl}'";
                        return true;
                    }
                }
                else if (EnglishActionStartRegex.IsMatch(cl))
                {
                    var match = EnglishActionStartRegex.Match(cl);
                    if (!ConcludesWithCompletion(cl, match.Index))
                    {
                        matchedReason = $"Phát hiện hành động tiến trình tiếng Anh trong phản hồi ngắn: '{cl}'";
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool ConcludesWithCompletion(string sentence, int inProgressMatchIndex)
    {
        var matches = ExplicitCompletionEndRegex.Matches(sentence);
        if (matches.Count == 0) return false;

        var lastMatch = matches[matches.Count - 1];
        // If the completion clause is located AFTER the in-progress phrase,
        // it means the sentence concludes that work is finished.
        return lastMatch.Index > inProgressMatchIndex;
    }

    /// <summary>
    /// Splits response text into meaningful sentences and returns the last non-empty sentence.
    /// Filters out code block tails, timestamps, and UI tool widgets.
    /// </summary>
    public static string GetLastMeaningfulSentence(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText)) return string.Empty;

        var rawLines = responseText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var contentLines = new List<string>();

        foreach (var rawLine in rawLines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (IsIgnorableUiLabel(line)) continue;

            contentLines.Add(line);
        }

        if (contentLines.Count == 0) return string.Empty;

        // Group lines into sentences: if a line does not end with sentence-ending punctuation (.!?),
        // and next line continues it (e.g. file paths like "C:\AI\..."), merge them.
        var sentences = new List<string>();
        var currentSentence = new StringBuilder();

        for (int i = 0; i < contentLines.Count; i++)
        {
            string line = contentLines[i];

            if (currentSentence.Length > 0)
            {
                currentSentence.Append(' ');
            }
            currentSentence.Append(line);

            bool endsWithPunct = line.EndsWith('.') || line.EndsWith('!') || line.EndsWith('?') ||
                                 line.EndsWith(':') || line.EndsWith(';');

            // If line ends with period followed by common file extension or decimal (e.g. .safetensors, .json), do not split
            if (line.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase) ||
                line.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                line.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                line.EndsWith(".py", StringComparison.OrdinalIgnoreCase) ||
                line.EndsWith(".pth", StringComparison.OrdinalIgnoreCase) ||
                line.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            {
                endsWithPunct = false;
            }

            if (endsWithPunct || i == contentLines.Count - 1)
            {
                string s = currentSentence.ToString().Trim();
                if (!string.IsNullOrEmpty(s))
                {
                    // Also split internal sentences by ". " if multiple sentences were on the same line
                    var subSentences = Regex.Split(s, @"(?<=[.!?])\s+(?=[A-ZÀ-Ỹ\d])");
                    foreach (var sub in subSentences)
                    {
                        string cleanSub = sub.Trim();
                        if (!string.IsNullOrEmpty(cleanSub))
                        {
                            sentences.Add(cleanSub);
                        }
                    }
                }
                currentSentence.Clear();
            }
        }

        if (currentSentence.Length > 0)
        {
            string s = currentSentence.ToString().Trim();
            if (!string.IsNullOrEmpty(s))
            {
                sentences.Add(s);
            }
        }

        if (sentences.Count == 0) return string.Empty;

        // Return the last meaningful sentence
        return sentences[sentences.Count - 1];
    }

    /// <summary>
    /// Determines whether a given line is an ignorable UI element label (timestamp, button text, git diff badge).
    /// </summary>
    public static bool IsIgnorableUiLabel(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;

        string clean = text.Trim();

        // Timestamps (e.g. "7:07 AM", "07:07", "12:30:15 PM")
        if (TimestampRegex.IsMatch(clean)) return true;

        // Git diff summaries (e.g. "1 file changed +134 -2 >")
        if (GitChangedFilesRegex.IsMatch(clean)) return true;
        if (GitDiffStatsRegex.IsMatch(clean)) return true;

        // Common button labels inside chat responses
        if (clean.Equals("Review", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Submit", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Skip", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Copy", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Copy code", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Copy message", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Copy response", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Good response", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Bad response", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Helpful", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Unhelpful", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Thumbs up", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Thumbs down", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Sao chép", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Sao chép mã", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Hài lòng", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Chưa hài lòng", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Pure dividers or decorative characters
        if (clean.Equals("---") || clean.Equals("***") || clean.Equals("___") || clean.Equals(">"))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Traverses the UI Automation tree from the feedback toolbar buttons to extract the text
    /// of the specific chat response that owns this toolbar.
    /// </summary>
    public static string ExtractResponseText(AutomationScanner.ResponseFeedbackToolbar toolbar)
    {
        var anchor = toolbar.GoodButton ?? toolbar.CopyButton ?? toolbar.BadButton;
        if (anchor == null) return string.Empty;

        var collectedTexts = new List<(double Top, double Left, string Text)>();
        var seenTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var walker = TreeWalker.ControlViewWalker;
            var current = anchor;
            AutomationElement? messageContainer = null;

            // Climb up to 6 levels to locate the container of this single message
            for (int level = 1; level <= 6; level++)
            {
                var parent = walker.GetParent(current);
                if (parent == null || parent == AutomationElement.RootElement) break;

                // Stop if we reach a container that holds multiple feedback toolbars (e.g. the chat list)
                try
                {
                    var goodCond = new PropertyCondition(AutomationElement.NameProperty, "Good response");
                    var goods = parent.FindAll(TreeScope.Descendants, goodCond);
                    if (goods != null && goods.Count > 1)
                    {
                        // We reached the message list; stop climbing
                        break;
                    }
                }
                catch { }

                messageContainer = parent;
                current = parent;
            }

            if (messageContainer != null)
            {
                // Collect text nodes from this message container
                var textCond = new OrCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)
                );

                try
                {
                    var textElements = messageContainer.FindAll(TreeScope.Descendants, textCond);
                    if (textElements != null)
                    {
                        foreach (AutomationElement el in textElements)
                        {
                            try
                            {
                                string text = el.Current.Name ?? string.Empty;
                                if (string.IsNullOrWhiteSpace(text))
                                {
                                    if (el.TryGetCurrentPattern(ValuePattern.Pattern, out object? valPatternObj) &&
                                        valPatternObj is ValuePattern valPattern)
                                    {
                                        text = valPattern.Current.Value ?? string.Empty;
                                    }
                                }

                                if (string.IsNullOrWhiteSpace(text)) continue;

                                var rect = el.Current.BoundingRectangle;
                                // Exclude elements below toolbar (outside this message footer)
                                if (!rect.IsEmpty && !toolbar.BoundingBox.IsEmpty && rect.Top > toolbar.BoundingBox.Bottom + 10)
                                {
                                    continue;
                                }

                                if (IsIgnorableUiLabel(text)) continue;

                                string trimmed = text.Trim();
                                if (seenTexts.Add(trimmed))
                                {
                                    double top = rect.IsEmpty ? 0 : rect.Top;
                                    double left = rect.IsEmpty ? 0 : rect.Left;
                                    collectedTexts.Add((top, left, trimmed));
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }

                // Also check messageContainer's own Name property
                try
                {
                    string containerName = messageContainer.Current.Name ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(containerName) && !IsIgnorableUiLabel(containerName))
                    {
                        string trimmed = containerName.Trim();
                        if (seenTexts.Add(trimmed))
                        {
                            var r = messageContainer.Current.BoundingRectangle;
                            collectedTexts.Add((r.IsEmpty ? 0 : r.Top, r.IsEmpty ? 0 : r.Left, trimmed));
                        }
                    }
                }
                catch { }
            }
        }
        catch { }

        // Sort by Top, then Left to preserve visual reading order
        collectedTexts.Sort((a, b) =>
        {
            int cmp = a.Top.CompareTo(b.Top);
            return cmp != 0 ? cmp : a.Left.CompareTo(b.Left);
        });

        var sb = new StringBuilder();
        foreach (var item in collectedTexts)
        {
            sb.AppendLine(item.Text);
        }

        return sb.ToString().Trim();
    }
}
