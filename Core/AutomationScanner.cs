using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using AntigravityAutoPilot.Models;

namespace AntigravityAutoPilot.Core;

public class AutomationScanner : IDisposable
{
    private readonly AntigravityDetector _detector;
    private readonly ActionEngine _actionEngine;
    private AppSettings _settings;

    private CancellationTokenSource? _cts;
    private Task? _scanTask;
    private readonly object _lock = new();

    public EngineStatus Status { get; private set; } = EngineStatus.Stopped;
    public int TotalActionsClicked { get; private set; } = 0;
    public int TotalBlocked { get; private set; } = 0;
    public string LatestAction { get; private set; } = "Sẵn sàng";

    public event Action<EngineStatus>? StatusChanged;
    public event Action<AutomationAction, string>? ActionExecuted;
    public event Action<AutomationAction, string>? ActionBlocked;
    public event Action<AntigravityConnectionState, string>? AntigravityStateChanged;
    public event Action? AntigravityCompleted;
    public event Action<AutomationAction>? SubmitApproved;
    public event Action<AutomationAction>? AcceptApproved;
    public event Action<string>? PlanDetected;
    public event Action<string>? PlanTabClosed;
    public event Action<string>? DiagnosticLogged;

    public Func<DangerousCommandPromptRequest, Task<bool>>? ConfirmDangerousCommandAsync { get; set; }

    private bool _hasActiveGenerationObserved = false;
    private bool _stopButtonPreviouslyPresent = false;
    private DateTime? _stopDisappearedTime = null;
    private DateTime _lastActionExecutedTime = DateTime.MinValue;
    private DateTime _lastCompletionAlertTime = DateTime.MinValue;
    private DateTime _lastPlanAlertTime = DateTime.MinValue;
    private bool _hasPendingPlan = false;
    private DateTime _planDetectedTime = DateTime.MinValue;
    private readonly HashSet<string> _staleActionIds = new();
    private readonly HashSet<string> _detectedPlanIds = new();
    private bool _staleSnapshotTaken = false;

    private readonly HashSet<string> _seenResponseIds = new();
    private string? _lastCompletedResponseId = null;
    private bool _staleResponseSnapshotTaken = false;
    private double _highestSeenResponseY = double.MinValue;
    private bool _isLatestResponseInProgress = false;
    private string? _lastInProgressResponseSnippet = null;

    public AutomationScanner(AppSettings settings)
    {
        _settings = settings;
        _detector = new AntigravityDetector();
        _actionEngine = new ActionEngine();

        _detector.StateChanged += (state, msg) =>
        {
            AntigravityStateChanged?.Invoke(state, msg);
        };
    }

    public void UpdateSettings(AppSettings settings)
    {
        lock (_lock)
        {
            _settings = settings;
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (Status == EngineStatus.Running) return;

            if (Status == EngineStatus.Paused && _scanTask != null && !_scanTask.IsCompleted)
            {
                Status = EngineStatus.Running;
                StatusChanged?.Invoke(Status);
                return;
            }

            _staleActionIds.Clear();
            _staleSnapshotTaken = false;
            _detectedPlanIds.Clear();
            _lastPlanAlertTime = DateTime.MinValue;
            _hasActiveGenerationObserved = false;
            _stopButtonPreviouslyPresent = false;
            _stopDisappearedTime = null;
            _lastActionExecutedTime = DateTime.MinValue;
            _hasPendingPlan = false;
            _planDetectedTime = DateTime.MinValue;
            _seenResponseIds.Clear();
            _lastCompletedResponseId = null;
            _staleResponseSnapshotTaken = false;
            _highestSeenResponseY = double.MinValue;
            _isLatestResponseInProgress = false;
            _lastInProgressResponseSnippet = null;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            Status = EngineStatus.Running;
            StatusChanged?.Invoke(Status);

            _scanTask = Task.Run(() => ScanLoopAsync(_cts.Token));
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (Status == EngineStatus.Running)
            {
                Status = EngineStatus.Paused;
                StatusChanged?.Invoke(Status);
            }
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            if (Status == EngineStatus.Paused)
            {
                Status = EngineStatus.Running;
                StatusChanged?.Invoke(Status);
            }
            else if (Status == EngineStatus.Stopped)
            {
                Start();
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (Status == EngineStatus.Stopped) return;

            Status = EngineStatus.Stopped;
            _cts?.Cancel();
            StatusChanged?.Invoke(Status);
            _actionEngine.ResetDebounce();
            _staleActionIds.Clear();
            _staleSnapshotTaken = false;
            _detectedPlanIds.Clear();
            _lastPlanAlertTime = DateTime.MinValue;
            _hasActiveGenerationObserved = false;
            _stopButtonPreviouslyPresent = false;
            _stopDisappearedTime = null;
            _lastActionExecutedTime = DateTime.MinValue;
            _hasPendingPlan = false;
            _planDetectedTime = DateTime.MinValue;
            _isLatestResponseInProgress = false;
            _lastInProgressResponseSnippet = null;
        }
    }

    private async Task ScanLoopAsync(CancellationToken ct)
    {
        // Condition for candidate action elements (Buttons, RadioButtons, Hyperlinks, SplitButtons, and InvokePattern elements)
        var candidateCondition = new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.RadioButton),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.SplitButton),
            new PropertyCondition(AutomationElement.IsInvokePatternAvailableProperty, true)
        );

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (Status == EngineStatus.Paused)
                {
                    await Task.Delay(250, ct);
                    continue;
                }

                int interval;
                AppSettings currentSettings;
                lock (_lock)
                {
                    currentSettings = _settings;
                    interval = currentSettings.ScanIntervalMs;
                }

                // 1. Locate all active Antigravity root windows
                var windows = _detector.GetAntigravityWindows();
                if (windows.Count == 0)
                {
                    // Antigravity not found; sleep and retry
                    await Task.Delay(1000, ct);
                    continue;
                }

                bool stopButtonFoundInCurrentScan = false;
                bool proceedButtonFoundInCurrentScan = false;
                bool clickedInThisPass = false;
                bool hasPendingInteractiveActions = false;
                var allDiscoveredToolbars = new List<ResponseFeedbackToolbar>();

                // 2. Discover and execute actions across ALL active Antigravity windows
                foreach (var window in windows)
                {
                    if (ct.IsCancellationRequested || Status != EngineStatus.Running) break;

                    IntPtr windowHwnd = IntPtr.Zero;
                    Rect windowRect = Rect.Empty;
                    try
                    {
                        windowHwnd = (IntPtr)window.Current.NativeWindowHandle;
                        windowRect = window.Current.BoundingRectangle;
                    }
                    catch { }

                    AutomationElementCollection? candidates = null;
                    try
                    {
                        candidates = window.FindAll(TreeScope.Descendants, candidateCondition);
                    }
                    catch (ElementNotAvailableException)
                    {
                        // Window reloaded or closed
                        continue;
                    }
                    catch (COMException)
                    {
                        continue;
                    }

                    if (candidates != null && candidates.Count > 0)
                    {
                    var candidateList = new List<AutomationElement>();
                    foreach (AutomationElement el in candidates)
                    {
                        candidateList.Add(el);
                    }

                    // Sort candidates descending by Y coordinate (bottom-most / latest first)
                    candidateList.Sort((a, b) =>
                    {
                        try
                        {
                            double yA = a.Current.BoundingRectangle.Bottom;
                            double yB = b.Current.BoundingRectangle.Bottom;
                            return yB.CompareTo(yA);
                        }
                        catch { return 0; }
                    });

                    // Discover chat response feedback toolbars (Copy, Good response, Bad response) in this window
                    var toolbarsInWin = FindResponseFeedbackToolbars(candidateList, windowRect);
                    allDiscoveredToolbars.AddRange(toolbarsInWin);

                    // Snapshot stale elements on startup if configured
                    if (!_staleSnapshotTaken)
                    {
                        _staleSnapshotTaken = true;
                        if (currentSettings.IgnoreStaleButtonsOnStartup)
                        {
                            foreach (var el in candidateList)
                            {
                                try
                                {
                                    string name = el.Current.Name ?? string.Empty;
                                    string id = AutomationAction.GenerateElementId(el, name);
                                    if (name.IndexOf("proceed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        name.IndexOf("run", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        name.IndexOf("submit", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        _staleActionIds.Add(id);
                                        _actionEngine.MarkConsumed(id);
                                    }
                                }
                                catch { }
                            }
                        }
                    }

                    foreach (AutomationElement element in candidateList)
                    {
                        if (ct.IsCancellationRequested || Status != EngineStatus.Running) break;

                        try
                        {
                            string rawName = string.Empty;
                            try { rawName = element.Current.Name ?? string.Empty; } catch { }

                            string candidateId = AutomationAction.GenerateElementId(element, rawName);

                            // Skip if marked stale on startup or permanently consumed
                            if (_staleActionIds.Contains(candidateId) || _actionEngine.IsConsumed(candidateId))
                            {
                                continue;
                            }

                            var rect = Rect.Empty;
                            try { rect = element.Current.BoundingRectangle; } catch { }
                            bool isTopBarArea = !windowRect.IsEmpty && windowRect.Height > 300 && !rect.IsEmpty && (rect.Top - windowRect.Top) < 60;

                            // Check if agent is currently generating (Stop button present in chat / agent panel, never in top bar)
                            if (!isTopBarArea && (
                                rawName.Equals("Stop generating", StringComparison.OrdinalIgnoreCase) ||
                                rawName.Contains("Stop generating", StringComparison.OrdinalIgnoreCase) ||
                                rawName.Equals("Stop", StringComparison.OrdinalIgnoreCase)))
                            {
                                stopButtonFoundInCurrentScan = true;
                                _hasActiveGenerationObserved = true;
                            }

                            // ========================================================
                            // SPECIAL HANDLING: AUTO-ANSWER INTERACTIVE QUESTION FORM
                            // ========================================================
                            // SPECIAL HANDLING: AUTO-ANSWER INTERACTIVE QUESTION FORM
                            // (e.g. ask_question modal: Option 1 / Yes + Submit button)
                            // ========================================================
                            if (IsSubmitButton(rawName))
                            {
                                DiagnosticLogged?.Invoke($"[SCAN_SUBMIT] Detected button '{rawName}' (id={candidateId}). Evaluating question form...");
                                if (currentSettings.EnableAutoAnswerQuestion)
                                {
                                    var (isQuestion, option1Elem) = FindQuestionOption(element, currentSettings);
                                    DiagnosticLogged?.Invoke($"[SCAN_SUBMIT] Result: isQuestion={isQuestion}, option='{option1Elem?.Current.Name}'");
                                    if (isQuestion)
                                    {
                                        hasPendingInteractiveActions = true;
                                        bool handled = await HandleQuestionFormAsync(element, option1Elem, currentSettings, windowHwnd, ct);
                                        DiagnosticLogged?.Invoke($"[SCAN_SUBMIT] HandleQuestionFormAsync handled={handled}");
                                        if (handled)
                                        {
                                            clickedInThisPass = true;
                                            _lastActionExecutedTime = DateTime.UtcNow;
                                            _hasActiveGenerationObserved = true;
                                            _stopDisappearedTime = null;
                                            break;
                                        }
                                    }
                                }

                                // Standalone Submit buttons that are NOT part of a valid question form
                                // are the chat prompt input's Send button and must be strictly skipped!
                                continue;
                            }

                            if (currentSettings.EnableAutoAnswerQuestion)
                            {
                                // Case 2: Element is a RadioButton matching Option 1 / Yes / Allow
                                bool isRadio = false;
                                try { isRadio = element.Current.ControlType == ControlType.RadioButton; } catch { }

                                if (isRadio && IsPreferredOption(rawName, currentSettings))
                                {
                                    DiagnosticLogged?.Invoke($"[SCAN_RADIO] Radio '{rawName}' matches preferred option. Finding submit nearby...");
                                    var submitNearby = FindSubmitButtonNearby(element);
                                    DiagnosticLogged?.Invoke($"[SCAN_RADIO] submitNearby='{submitNearby?.Current.Name}'");
                                    if (submitNearby != null)
                                    {
                                        hasPendingInteractiveActions = true;
                                        bool handled = await HandleQuestionFormAsync(submitNearby, element, currentSettings, windowHwnd, ct);
                                        DiagnosticLogged?.Invoke($"[SCAN_RADIO] HandleQuestionFormAsync handled={handled}");
                                        if (handled)
                                        {
                                            clickedInThisPass = true;
                                            _lastActionExecutedTime = DateTime.UtcNow;
                                            _hasActiveGenerationObserved = true;
                                            _stopDisappearedTime = null;
                                            break;
                                        }
                                        continue;
                                    }
                                }
                            }

                            // Filter disabled elements (do NOT filter on IsOffscreen as Chromium Electron returns buggy IsOffscreen on webviews)
                            if (!element.Current.IsEnabled)
                            {
                                continue;
                            }

                            if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
                            {
                                continue;
                            }

                            // TOP BAR & MENU BAR FILTER:
                            // Exclude any element located in the top bar / menu bar area of the Antigravity window.
                            // In VS Code / Electron, the window titlebar and main menu (File, Edit, Selection, View, Go, Run, Terminal, Help)
                            // occupy the top ~50 pixels. Agent approval actions (Accept all, Run command, ask_question) NEVER appear here.
                            if (isTopBarArea)
                            {
                                continue;
                            }

                            // ControlType exclusions: Agent approvals are never MenuItems, Menus, MenuBars, or TitleBars.
                            ControlType? cType = null;
                            try { cType = element.Current.ControlType; } catch { }
                            if (cType == ControlType.MenuItem || cType == ControlType.Menu || cType == ControlType.MenuBar || cType == ControlType.TitleBar)
                            {
                                continue;
                            }

                            // Exclude elements whose parent is MenuBar, Menu, or TitleBar
                            try
                            {
                                var walker = TreeWalker.ControlViewWalker;
                                var p = walker.GetParent(element);
                                if (p != null)
                                {
                                    var pType = p.Current.ControlType;
                                    if (pType == ControlType.MenuBar || pType == ControlType.Menu || pType == ControlType.TitleBar)
                                    {
                                        continue;
                                    }
                                }
                            }
                            catch { }

                            // 3. Evaluate safety & whitelist
                            var eval = SafetyEngine.Evaluate(element, currentSettings);

                            if (eval.IsBlocked)
                            {
                                // If blocked due to destructive command or mode restriction on terminal
                                if (eval.DangerousCommand != null || eval.IsTerminalApproval)
                                {
                                    if (eval.DangerousCommand != null && ConfirmDangerousCommandAsync != null)
                                    {
                                        var promptAction = new AutomationAction
                                        {
                                            Element = element,
                                            ButtonText = eval.CleanText,
                                            Kind = eval.Kind == ActionKind.Blocked ? ActionKind.Run : eval.Kind,
                                            ElementIdentifier = candidateId,
                                            BoundingRectangle = rect,
                                            IsTerminalApproval = eval.IsTerminalApproval,
                                            DetectedCommand = eval.DangerousCommand,
                                            SurroundingContext = eval.SurroundingContext
                                        };

                                        var request = new DangerousCommandPromptRequest
                                        {
                                            Action = promptAction,
                                            DangerousKeyword = eval.DangerousCommand,
                                            FullCommand = eval.FullCommand ?? eval.DangerousCommand,
                                            SurroundingContext = eval.SurroundingContext,
                                            BlockReason = eval.BlockReason,
                                            IsQuestionForm = false
                                        };

                                        bool approved = await ConfirmDangerousCommandAsync(request);
                                        if (approved)
                                        {
                                            // USER APPROVED: proceed with executing action!
                                            var approvedClickResult = await _actionEngine.ExecuteAsync(promptAction, currentSettings, ct);
                                            DiagnosticLogged?.Invoke($"[DANGEROUS_APPROVED] Click '{promptAction.ButtonText}': success={approvedClickResult.Success}, method={approvedClickResult.MethodUsed}");

                                            _actionEngine.MarkConsumed(candidateId);
                                            _staleActionIds.Add(candidateId);

                                            TotalActionsClicked++;
                                            LatestAction = $"Đã duyệt & click: {promptAction.ButtonText}";
                                            _lastActionExecutedTime = DateTime.UtcNow;
                                            _hasActiveGenerationObserved = true;
                                            _stopDisappearedTime = null;
                                            _isLatestResponseInProgress = false;
                                            ActionExecuted?.Invoke(promptAction, approvedClickResult.MethodUsed);

                                            if (promptAction.Kind == ActionKind.Submit || promptAction.ButtonText.Equals("Submit", StringComparison.OrdinalIgnoreCase))
                                            {
                                                SubmitApproved?.Invoke(promptAction);
                                            }
                                            else if (promptAction.Kind == ActionKind.Accept || promptAction.Kind == ActionKind.AcceptAll ||
                                                     promptAction.ButtonText.IndexOf("accept", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                AcceptApproved?.Invoke(promptAction);
                                            }

                                            clickedInThisPass = true;
                                            break;
                                        }
                                        else
                                        {
                                            // USER BLOCKED: "nếu chặn thì tạm dừng tool"
                                            TotalBlocked++;
                                            LatestAction = $"ĐÃ CHẶN: {eval.DangerousCommand}";
                                            var blockedAction = new AutomationAction
                                            {
                                                Element = element,
                                                ButtonText = eval.CleanText,
                                                Kind = ActionKind.Blocked,
                                                IsBlocked = true,
                                                BlockReason = eval.BlockReason,
                                                DetectedCommand = eval.DangerousCommand,
                                                SurroundingContext = eval.SurroundingContext,
                                                BoundingRectangle = rect
                                            };
                                            ActionBlocked?.Invoke(blockedAction, eval.BlockReason ?? "Người dùng đã chặn lệnh nguy hiểm");
                                            Pause();
                                            break;
                                        }
                                    }

                                    TotalBlocked++;
                                    LatestAction = $"ĐÃ CHẶN: {eval.DangerousCommand ?? eval.BlockReason}";

                                    var defaultBlockedAction = new AutomationAction
                                    {
                                        Element = element,
                                        ButtonText = eval.CleanText,
                                        Kind = ActionKind.Blocked,
                                        IsBlocked = true,
                                        BlockReason = eval.BlockReason,
                                        DetectedCommand = eval.DangerousCommand,
                                        SurroundingContext = eval.SurroundingContext,
                                        BoundingRectangle = rect
                                    };

                                    ActionBlocked?.Invoke(defaultBlockedAction, eval.BlockReason ?? "Phát hiện lệnh nguy hiểm / phá hủy");
                                }
                                continue;
                            }

                            // Detect Implementation Plan presence and fire notification
                            if (eval.Kind == ActionKind.Proceed ||
                                rawName.Contains("implementation_plan", StringComparison.OrdinalIgnoreCase) ||
                                rawName.Contains("Implementation Plan", StringComparison.OrdinalIgnoreCase) ||
                                rawName.StartsWith("Proceed", StringComparison.OrdinalIgnoreCase))
                            {
                                proceedButtonFoundInCurrentScan = true;
                                _hasPendingPlan = true;
                                _planDetectedTime = DateTime.UtcNow;

                                string planKey = $"plan_{candidateId}";
                                if (!_detectedPlanIds.Contains(planKey))
                                {
                                    _detectedPlanIds.Add(planKey);
                                    DateTime nowUtc = DateTime.UtcNow;
                                    if ((nowUtc - _lastPlanAlertTime).TotalSeconds >= 10)
                                    {
                                        _lastPlanAlertTime = nowUtc;
                                        PlanDetected?.Invoke(eval.CleanText);
                                    }
                                }
                            }

                            if (!eval.IsAllowed)
                            {
                                continue;
                            }

                            hasPendingInteractiveActions = true;

                            // 4. Construct Action and Execute
                            string elemId = AutomationAction.GenerateElementId(element, eval.CleanText);
                            var action = new AutomationAction
                            {
                                Element = element,
                                ButtonText = eval.CleanText,
                                Kind = eval.Kind,
                                ElementIdentifier = elemId,
                                BoundingRectangle = rect,
                                IsTerminalApproval = eval.IsTerminalApproval
                            };

                            var clickResult = await _actionEngine.ExecuteAsync(action, currentSettings, ct);

                            if (clickResult.Success)
                            {
                                TotalActionsClicked++;
                                LatestAction = $"Đã click: {action.ButtonText}";
                                _lastActionExecutedTime = DateTime.UtcNow;
                                _hasActiveGenerationObserved = true;
                                _stopDisappearedTime = null;
                                _isLatestResponseInProgress = false;
                                ActionExecuted?.Invoke(action, clickResult.MethodUsed);

                                if (action.Kind == ActionKind.Submit || action.ButtonText.Equals("Submit", StringComparison.OrdinalIgnoreCase))
                                {
                                    SubmitApproved?.Invoke(action);
                                }
                                else if (action.Kind == ActionKind.Accept || action.Kind == ActionKind.AcceptAll ||
                                         action.ButtonText.IndexOf("accept", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    AcceptApproved?.Invoke(action);
                                }

                                // If action is Proceed, consume all other Proceed buttons on screen and close plan tab
                                if (action.Kind == ActionKind.Proceed)
                                {
                                    ConsumeAllProceedButtons(candidateList);
                                    _hasPendingPlan = false;
                                    if (currentSettings.AutoCloseProceededPlans)
                                    {
                                        ScheduleCloseProceededPlanTabs(window, windowHwnd, 1200);
                                    }
                                }

                                clickedInThisPass = true;
                                break; // Allow UI to update before next action
                            }
                            else if (clickResult.WasDebounced)
                            {
                                // Silently skip during cooldown
                                continue;
                            }
                        }
                        catch (ElementNotAvailableException)
                        {
                            // Element disappeared as UI changed; move to next
                            continue;
                        }
                        catch (Exception)
                        {
                            // Continue scanning remaining candidates
                            continue;
                        }
                    }

                    if (clickedInThisPass)
                    {
                        // UI will take a moment to transition
                        await Task.Delay(200, ct);
                        break; // Let UI settle before next full cycle
                    }
                }
            }

                // 4.5 Process discovered chat response feedback toolbars
                allDiscoveredToolbars.Sort((a, b) => b.Y.CompareTo(a.Y));
                var latestToolbar = allDiscoveredToolbars.FirstOrDefault();

                // Snapshot existing chat responses on initial scan pass so they are never falsely alerted
                if (!_staleResponseSnapshotTaken && allDiscoveredToolbars.Count > 0)
                {
                    _staleResponseSnapshotTaken = true;
                    foreach (var tb in allDiscoveredToolbars)
                    {
                        _seenResponseIds.Add(tb.Identifier);
                        if (tb.BoundingBox.Bottom > _highestSeenResponseY)
                        {
                            _highestSeenResponseY = tb.BoundingBox.Bottom;
                        }
                    }
                    if (latestToolbar != null)
                    {
                        _lastCompletedResponseId = latestToolbar.Identifier;
                        DiagnosticLogged?.Invoke($"[RESPONSE_DETECTOR] Khởi tạo: Đã ghi nhận {allDiscoveredToolbars.Count} response chat cũ trên màn hình. Bỏ qua các response này.");
                    }
                }

                // 4.6 Auto-close Proceeded Plans Check (if user clicked Proceed manually or Proceed disappeared during generation)
                if (_hasPendingPlan && !proceedButtonFoundInCurrentScan && currentSettings.AutoCloseProceededPlans)
                {
                    if (_hasActiveGenerationObserved || stopButtonFoundInCurrentScan || (DateTime.UtcNow - _planDetectedTime).TotalSeconds >= 2.5)
                    {
                        _hasPendingPlan = false;
                        ScheduleCloseProceededPlanTabs(null, IntPtr.Zero, 800);
                    }
                }

                // 5. Completion State Transition Check
                if (stopButtonFoundInCurrentScan)
                {
                    _hasActiveGenerationObserved = true;
                    _stopDisappearedTime = null;
                    _isLatestResponseInProgress = false;
                }
                else if (_stopButtonPreviouslyPresent && !stopButtonFoundInCurrentScan)
                {
                    // Transition: Stop button was present, now disappeared
                    _stopDisappearedTime = DateTime.UtcNow;
                }

                _stopButtonPreviouslyPresent = stopButtonFoundInCurrentScan;

                DateTime now = DateTime.UtcNow;

                // PRIMARY COMPLETION DETECTION:
                // When Antigravity completes a task, it renders the final chat response with interactive action icons
                // (Copy, Good response / Thumbs up, Bad response / Thumbs down) at the bottom-right of that response.
                bool canTriggerCompletion = _hasActiveGenerationObserved ||
                                            (_lastActionExecutedTime != DateTime.MinValue && (now - _lastActionExecutedTime).TotalSeconds <= 60);

                if (canTriggerCompletion &&
                    latestToolbar != null &&
                    !stopButtonFoundInCurrentScan &&
                    !hasPendingInteractiveActions &&
                    !clickedInThisPass &&
                    (now - _lastActionExecutedTime).TotalMilliseconds >= 1200)
                {
                    bool isNewResponse = !_seenResponseIds.Contains(latestToolbar.Identifier);
                    bool isDifferentFromLastAlert = latestToolbar.Identifier != _lastCompletedResponseId;

                    if (isNewResponse || isDifferentFromLastAlert)
                    {
                        bool isCurrentResponseInProgress = false;
                        string? inProgressReason = null;
                        string? lastSentence = null;

                        if (currentSettings.FilterInProgressResponsesOnCompletion)
                        {
                            try
                            {
                                string responseText = ResponseProgressAnalyzer.ExtractResponseText(latestToolbar);
                                isCurrentResponseInProgress = ResponseProgressAnalyzer.IsInProgressResponse(
                                    responseText,
                                    currentSettings,
                                    out inProgressReason,
                                    out lastSentence);
                            }
                            catch (Exception ex)
                            {
                                DiagnosticLogged?.Invoke($"[RESPONSE_DETECTOR] Lỗi khi phân tích nội dung chat: {ex.Message}");
                            }
                        }

                        if (isCurrentResponseInProgress)
                        {
                            _isLatestResponseInProgress = true;
                            _lastInProgressResponseSnippet = lastSentence;
                            _seenResponseIds.Add(latestToolbar.Identifier);
                            _lastCompletedResponseId = latestToolbar.Identifier;
                            if (latestToolbar.BoundingBox.Bottom > _highestSeenResponseY)
                            {
                                _highestSeenResponseY = latestToolbar.BoundingBox.Bottom;
                            }

                            DiagnosticLogged?.Invoke($"[TASK_IN_PROGRESS] Đoạn phản hồi tại Y={latestToolbar.BoundingBox.Bottom:0} đang thực hiện tác vụ dở dang: \"{lastSentence}\" ({inProgressReason}). Tác vụ chưa hoàn thành, tiếp tục theo dõi.");
                        }
                        else
                        {
                            _isLatestResponseInProgress = false;
                            _seenResponseIds.Add(latestToolbar.Identifier);
                            _lastCompletedResponseId = latestToolbar.Identifier;
                            if (latestToolbar.BoundingBox.Bottom > _highestSeenResponseY)
                            {
                                _highestSeenResponseY = latestToolbar.BoundingBox.Bottom;
                            }

                            DiagnosticLogged?.Invoke($"[TASK_COMPLETE] Phát hiện đoạn chat hoàn thành với cụm icon phản hồi ({latestToolbar.GetDescription()}) tại Y={latestToolbar.BoundingBox.Bottom:0}. Báo hoàn tất tác vụ!");
                            TriggerCompletion();
                        }
                    }
                }
                // FALLBACK COMPLETION DETECTION:
                // If active generation was observed, Stop button disappeared > 5s ago, no pending actions remain,
                // and the latest response is NOT an in-progress / ongoing action.
                else if (_hasActiveGenerationObserved &&
                    !_isLatestResponseInProgress &&
                    !stopButtonFoundInCurrentScan &&
                    !hasPendingInteractiveActions &&
                    !clickedInThisPass &&
                    (now - _lastActionExecutedTime).TotalMilliseconds >= 6000 &&
                    _stopDisappearedTime.HasValue &&
                    (now - _stopDisappearedTime.Value).TotalMilliseconds >= 5000)
                {
                    DiagnosticLogged?.Invoke("[TASK_COMPLETE] Kích hoạt hoàn tất tác vụ qua cơ chế dự phòng (Stop button biến mất > 5s).");
                    TriggerCompletion();
                }

                // Wait scan interval
                await Task.Delay(interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // Scanner resiliency: never crash the loop
                await Task.Delay(500, ct);
            }
        }
    }

    private void TriggerCompletion()
    {
        _hasActiveGenerationObserved = false;
        _stopDisappearedTime = null;
        _isLatestResponseInProgress = false;
        DateTime now = DateTime.UtcNow;
        if ((now - _lastCompletionAlertTime).TotalSeconds >= 5)
        {
            _lastCompletionAlertTime = now;
            AntigravityCompleted?.Invoke();
        }
    }

    public void TriggerCompletionTest()
    {
        AntigravityCompleted?.Invoke();
    }

    #region Question Form & Option Selection Helpers

    private static bool IsSubmitButton(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string clean = SafetyEngine.NormalizeText(text);
        return clean.Equals("Submit", StringComparison.OrdinalIgnoreCase) ||
               clean.StartsWith("Submit ", StringComparison.OrdinalIgnoreCase) ||
               clean.StartsWith("Submit [", StringComparison.OrdinalIgnoreCase) ||
               clean.StartsWith("Submit (", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPreferredOption(string? text, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string clean = SafetyEngine.NormalizeText(text);

        // Check configured regex pattern
        try
        {
            if (Regex.IsMatch(clean, settings.PreferredQuestionOptionPattern, RegexOptions.IgnoreCase))
            {
                // Ensure it's not a negative option like "No", "Reject"
                if (!clean.Contains("No (tell", StringComparison.OrdinalIgnoreCase) &&
                    !clean.StartsWith("No", StringComparison.OrdinalIgnoreCase) &&
                    !clean.StartsWith("Decline", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch { }

        return clean.StartsWith("(Recommended)", StringComparison.OrdinalIgnoreCase) ||
               clean.StartsWith("Option 1", StringComparison.OrdinalIgnoreCase) ||
               clean.StartsWith("1.", StringComparison.OrdinalIgnoreCase) ||
               clean.StartsWith("1:", StringComparison.OrdinalIgnoreCase) ||
               clean.StartsWith("1)", StringComparison.OrdinalIgnoreCase) ||
               clean.StartsWith("1 -", StringComparison.OrdinalIgnoreCase) ||
               clean.Contains("Yes, allow", StringComparison.OrdinalIgnoreCase) ||
               clean.Equals("Yes", StringComparison.OrdinalIgnoreCase);
    }

    private static (bool IsQuestion, AutomationElement? OptionElem) FindQuestionOption(AutomationElement submitElem, AppSettings settings)
    {
        try
        {
            // 1. Direct inspection: If the submit element itself has HelpText or Tooltip indicating the chat Send button
            string helpText = string.Empty;
            try { helpText = submitElem.Current.HelpText ?? string.Empty; } catch { }
            string rawName = string.Empty;
            try { rawName = submitElem.Current.Name ?? string.Empty; } catch { }

            if (helpText.Contains("Send message", StringComparison.OrdinalIgnoreCase) ||
                helpText.Contains("Send Message", StringComparison.OrdinalIgnoreCase) ||
                helpText.Contains("Send prompt", StringComparison.OrdinalIgnoreCase) ||
                helpText.Contains("Submit prompt", StringComparison.OrdinalIgnoreCase) ||
                helpText.Contains("Ctrl+Enter", StringComparison.OrdinalIgnoreCase) ||
                rawName.Contains("Ctrl+Enter", StringComparison.OrdinalIgnoreCase) ||
                rawName.Contains("Send prompt", StringComparison.OrdinalIgnoreCase) ||
                rawName.Contains("Submit prompt", StringComparison.OrdinalIgnoreCase))
            {
                return (false, null);
            }

            var walker = TreeWalker.ControlViewWalker;
            var current = submitElem;

            // 2. An interactive ask_question modal/card is self-contained.
            // An ask_question dialog in Antigravity MUST contain a "Skip" button alongside "Submit".
            // If there is NO "Skip" button in the immediate container hierarchy, it is NOT an ask_question form!
            for (int level = 0; level < 2; level++)
            {
                var parent = walker.GetParent(current);
                if (parent == null || parent == AutomationElement.RootElement) break;

                // Safety check: if parent container contains a chat prompt input (Edit box with "Ask anything"),
                // then this is the chat prompt input bar, NOT an ask_question form!
                var editCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
                var edits = parent.FindAll(TreeScope.Descendants, editCondition);
                if (edits != null && edits.Count > 0)
                {
                    return (false, null);
                }

                // Check for "Skip" button - this is the mandatory hallmark of ask_question dialog
                var buttonCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                var buttons = parent.FindAll(TreeScope.Descendants, buttonCondition);
                bool hasSkipButton = false;
                if (buttons != null)
                {
                    foreach (AutomationElement btn in buttons)
                    {
                        string btnName = string.Empty;
                        try { btnName = btn.Current.Name ?? string.Empty; } catch { }
                        string cleanBtn = SafetyEngine.NormalizeText(btnName);
                        if (cleanBtn.Equals("Skip", StringComparison.OrdinalIgnoreCase) ||
                            cleanBtn.StartsWith("Skip ", StringComparison.OrdinalIgnoreCase) ||
                            cleanBtn.Equals("Skip question", StringComparison.OrdinalIgnoreCase))
                        {
                            hasSkipButton = true;
                            break;
                        }
                    }
                }

                // If no Skip button is present at this container level, it is not an ask_question dialog
                if (!hasSkipButton)
                {
                    current = parent;
                    continue;
                }

                // Look strictly for radio buttons or checkboxes in this container
                var optionCondition = new OrCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.RadioButton),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox)
                );

                var candidates = parent.FindAll(TreeScope.Descendants, optionCondition);
                if (candidates != null && candidates.Count > 0)
                {
                    AutomationElement? firstValidCandidate = null;

                    foreach (AutomationElement candidate in candidates)
                    {
                        if (candidate.Equals(submitElem)) continue;

                        string name = string.Empty;
                        try { name = candidate.Current.Name ?? string.Empty; } catch { }
                        string clean = SafetyEngine.NormalizeText(name);

                        // Ignore file checkboxes from diff review lists
                        if (clean.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                            clean.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                            clean.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                            clean.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                            clean.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                            clean.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                            clean.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                            clean.Contains('\\') || clean.Contains('/'))
                        {
                            continue;
                        }

                        if (clean.Equals("Skip", StringComparison.OrdinalIgnoreCase) ||
                            clean.Equals("Submit", StringComparison.OrdinalIgnoreCase) ||
                            clean.Equals("Cancel", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (firstValidCandidate == null)
                        {
                            firstValidCandidate = candidate;
                        }

                        if (IsPreferredOption(name, settings))
                        {
                            return (true, candidate);
                        }
                    }

                    if (firstValidCandidate != null)
                    {
                        return (true, firstValidCandidate);
                    }
                }

                current = parent;
            }
        }
        catch { }

        return (false, null);
    }

    private static AutomationElement? FindSubmitButtonNearby(AutomationElement optionElem)
    {
        try
        {
            var walker = TreeWalker.ControlViewWalker;
            var current = optionElem;

            for (int level = 0; level < 2; level++)
            {
                var parent = walker.GetParent(current);
                if (parent == null || parent == AutomationElement.RootElement) break;

                var buttonCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                var buttons = parent.FindAll(TreeScope.Descendants, buttonCondition);
                if (buttons == null) continue;

                bool hasSkip = false;
                AutomationElement? submitBtn = null;

                foreach (AutomationElement btn in buttons)
                {
                    string name = string.Empty;
                    try { name = btn.Current.Name ?? string.Empty; } catch { }
                    string clean = SafetyEngine.NormalizeText(name);

                    if (clean.Equals("Skip", StringComparison.OrdinalIgnoreCase) ||
                        clean.StartsWith("Skip ", StringComparison.OrdinalIgnoreCase))
                    {
                        hasSkip = true;
                    }
                    else if (IsSubmitButton(name))
                    {
                        string helpText = string.Empty;
                        try { helpText = btn.Current.HelpText ?? string.Empty; } catch { }
                        if (!helpText.Contains("Send message", StringComparison.OrdinalIgnoreCase) &&
                            !helpText.Contains("Send prompt", StringComparison.OrdinalIgnoreCase))
                        {
                            submitBtn = btn;
                        }
                    }
                }

                if (hasSkip && submitBtn != null)
                {
                    return submitBtn;
                }

                current = parent;
            }
        }
        catch { }

        return null;
    }

    private async Task<bool> HandleQuestionFormAsync(
        AutomationElement submitElem,
        AutomationElement? optionElem,
        AppSettings settings,
        IntPtr windowHwnd,
        CancellationToken ct)
    {
        try
        {
            // 1. Gather context & perform safety check
            string context = CommandAnalyzer.ExtractSurroundingContext(submitElem);
            var eval = SafetyEngine.EvaluateQuestionContext(submitElem, settings, context);

            if (eval.IsBlocked)
            {
                Rect rect = Rect.Empty;
                try { rect = submitElem.Current.BoundingRectangle; } catch { }

                var blockedAction = new AutomationAction
                {
                    Element = submitElem,
                    ButtonText = "Submit",
                    Kind = ActionKind.Submit,
                    IsBlocked = true,
                    BlockReason = eval.BlockReason,
                    DetectedCommand = eval.DangerousCommand,
                    SurroundingContext = context,
                    BoundingRectangle = rect
                };

                if (eval.DangerousCommand != null && ConfirmDangerousCommandAsync != null)
                {
                    var request = new DangerousCommandPromptRequest
                    {
                        Action = blockedAction,
                        DangerousKeyword = eval.DangerousCommand,
                        FullCommand = eval.FullCommand ?? eval.DangerousCommand,
                        SurroundingContext = context,
                        BlockReason = eval.BlockReason,
                        IsQuestionForm = true
                    };

                    bool approved = await ConfirmDangerousCommandAsync(request);
                    if (approved)
                    {
                        // USER APPROVED: proceed with selecting option & submitting!
                        eval.IsBlocked = false;
                        eval.IsAllowed = true;
                    }
                    else
                    {
                        // USER BLOCKED: pause tool!
                        TotalBlocked++;
                        LatestAction = $"ĐÃ CHẶN: {eval.DangerousCommand}";
                        ActionBlocked?.Invoke(blockedAction, eval.BlockReason ?? "Người dùng đã chặn submit biểu mẫu nguy hiểm");
                        Pause();
                        return false;
                    }
                }
                else
                {
                    TotalBlocked++;
                    LatestAction = $"ĐÃ CHẶN: {eval.DangerousCommand ?? eval.BlockReason}";
                    ActionBlocked?.Invoke(blockedAction, eval.BlockReason ?? "Phát hiện lệnh nguy hiểm trong biểu mẫu câu hỏi");
                    return false;
                }
            }

            if (!eval.IsAllowed)
            {
                return false;
            }

            // 2. Select option if present and not already selected
            if (optionElem != null)
            {
                bool isSelected = false;
                try
                {
                    if (optionElem.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? selObj) &&
                        selObj is SelectionItemPattern selPattern)
                    {
                        isSelected = selPattern.Current.IsSelected;
                    }
                }
                catch { }

                string optName = "Option 1";
                try { optName = optionElem.Current.Name ?? "Option 1"; } catch { }

                DiagnosticLogged?.Invoke($"[QUESTION_FORM] Target option '{optName}', initially isSelected={isSelected}");

                if (!isSelected)
                {
                    var selectRes = await _actionEngine.SelectOptionAsync(optionElem, settings, ct);
                    DiagnosticLogged?.Invoke($"[QUESTION_FORM] SelectOptionAsync: success={selectRes.Success}, method={selectRes.MethodUsed}");

                    LatestAction = $"Selected {optName}";
                    // Allow IDE UI to register selection and enable the Submit button
                    await Task.Delay(350, ct);
                }
            }

            // 3. Click Submit button
            Rect submitRect = Rect.Empty;
            try { submitRect = submitElem.Current.BoundingRectangle; } catch { }

            string elemId = AutomationAction.GenerateElementId(submitElem, "Submit");
            var action = new AutomationAction
            {
                Element = submitElem,
                ButtonText = "Submit",
                Kind = ActionKind.Submit,
                ElementIdentifier = elemId,
                BoundingRectangle = submitRect,
                IsTerminalApproval = eval.IsTerminalApproval
            };

            var clickResult = await _actionEngine.ExecuteAsync(action, settings, ct);
            DiagnosticLogged?.Invoke($"[QUESTION_FORM] Submit ExecuteAsync: success={clickResult.Success}, method={clickResult.MethodUsed}");

            // Send Enter key to dialog window (modal quick-inputs in Electron require Enter to submit)
            _actionEngine.SendEnterKey(windowHwnd);
            DiagnosticLogged?.Invoke($"[QUESTION_FORM] Sent Enter key to window {windowHwnd}");

            // Always mark consumed and stale for both submitElem and optionElem to prevent re-triggering
            _actionEngine.MarkConsumed(elemId);
            _staleActionIds.Add(elemId);
            if (optionElem != null)
            {
                try
                {
                    string optName = optionElem.Current.Name ?? "Option";
                    string optId = AutomationAction.GenerateElementId(optionElem, optName);
                    _actionEngine.MarkConsumed(optId);
                    _staleActionIds.Add(optId);
                }
                catch { }
            }

            if (clickResult.Success)
            {
                TotalActionsClicked++;
                LatestAction = "Đã chọn phương án 1 & Gửi Submit câu hỏi";
                _lastActionExecutedTime = DateTime.UtcNow;
                ActionExecuted?.Invoke(action, clickResult.MethodUsed);
                SubmitApproved?.Invoke(action);
                return true;
            }
            else
            {
                // Fallback: Enter key was dispatched to submit dialog
                TotalActionsClicked++;
                LatestAction = "Đã chọn phương án 1 & Gửi Submit câu hỏi (Enter)";
                _lastActionExecutedTime = DateTime.UtcNow;
                ActionExecuted?.Invoke(action, "SendEnterKey");
                SubmitApproved?.Invoke(action);
                return true;
            }
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void ConsumeAllProceedButtons(List<AutomationElement> candidates)
    {
        foreach (var el in candidates)
        {
            try
            {
                string name = el.Current.Name ?? string.Empty;
                if (name.IndexOf("proceed", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string id = AutomationAction.GenerateElementId(el, name);
                    _actionEngine.MarkConsumed(id);
                    _staleActionIds.Add(id);
                }
            }
            catch { }
        }
    }

    #region Auto-close Proceeded Plans

    private void ScheduleCloseProceededPlanTabs(AutomationElement? specificWindow, IntPtr specificHwnd, int delayMs = 1200)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs);
                if (specificWindow != null && specificHwnd != IntPtr.Zero)
                {
                    CloseProceededPlanTabs(specificWindow, specificHwnd);
                }
                else
                {
                    CloseAllProceededPlanTabs();
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogged?.Invoke($"[AUTO_CLOSE_PLAN] Lỗi trong tiến trình đóng tab: {ex.Message}");
            }
        });
    }

    public void CloseAllProceededPlanTabs()
    {
        var windows = _detector.GetAntigravityWindows();
        foreach (var win in windows)
        {
            IntPtr hwnd = IntPtr.Zero;
            try { hwnd = (IntPtr)win.Current.NativeWindowHandle; } catch { }
            CloseProceededPlanTabs(win, hwnd);
        }
    }

    public int CloseProceededPlanTabs(AutomationElement window, IntPtr windowHwnd)
    {
        int closedCount = 0;
        try
        {
            var btnCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);

            // 1. Direct close buttons with implementation_plan in the window
            try
            {
                var allButtons = window.FindAll(TreeScope.Descendants, btnCond);
                if (allButtons != null)
                {
                    foreach (AutomationElement btn in allButtons)
                    {
                        try
                        {
                            string btnName = btn.Current.Name ?? string.Empty;
                            string btnId = btn.Current.AutomationId ?? string.Empty;

                            bool isPlanCloseBtn = (btnName.IndexOf("implementation_plan", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                   btnId.IndexOf("implementation_plan", StringComparison.OrdinalIgnoreCase) >= 0) &&
                                                  (btnName.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                   btnName.IndexOf("đóng", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                   btnId.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0);

                            if (isPlanCloseBtn)
                            {
                                if (btn.TryGetCurrentPattern(InvokePattern.Pattern, out object? invObj) && invObj is InvokePattern inv)
                                {
                                    inv.Invoke();
                                    closedCount++;
                                    DiagnosticLogged?.Invoke($"[AUTO_CLOSE_PLAN] Đã đóng tab kế hoạch qua nút '{btnName}'.");
                                    PlanTabClosed?.Invoke(btnName);
                                    Thread.Sleep(100);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // 2. Scan TabItem elements
            var tabCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem);
            AutomationElementCollection? tabs = null;
            try
            {
                tabs = window.FindAll(TreeScope.Descendants, tabCond);
            }
            catch { }

            if (tabs != null && tabs.Count > 0)
            {
                foreach (AutomationElement tab in tabs)
                {
                    try
                    {
                        string tabName = tab.Current.Name ?? string.Empty;
                        string tabId = tab.Current.AutomationId ?? string.Empty;

                        bool isPlanTab = tabName.IndexOf("implementation_plan", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         tabId.IndexOf("implementation_plan", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (!isPlanTab) continue;

                        bool closedViaChildBtn = false;
                        try
                        {
                            var tabChildButtons = tab.FindAll(TreeScope.Descendants, btnCond);
                            if (tabChildButtons != null)
                            {
                                foreach (AutomationElement cb in tabChildButtons)
                                {
                                    try
                                    {
                                        string cbName = cb.Current.Name ?? string.Empty;
                                        string cbId = cb.Current.AutomationId ?? string.Empty;

                                        if (cbName.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            cbName.IndexOf("đóng", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            cbId.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            tabChildButtons.Count == 1)
                                        {
                                            if (cb.TryGetCurrentPattern(InvokePattern.Pattern, out object? invObj) && invObj is InvokePattern inv)
                                            {
                                                inv.Invoke();
                                                closedViaChildBtn = true;
                                                closedCount++;
                                                DiagnosticLogged?.Invoke($"[AUTO_CLOSE_PLAN] Đã đóng tab '{tabName}' qua nút đóng con.");
                                                PlanTabClosed?.Invoke(tabName);
                                                Thread.Sleep(100);
                                                break;
                                            }
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch { }

                        if (!closedViaChildBtn)
                        {
                            // Select the tab then send Ctrl+W
                            try
                            {
                                if (tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? selObj) && selObj is SelectionItemPattern sel)
                                {
                                    sel.Select();
                                    Thread.Sleep(120);
                                }
                                else
                                {
                                    _actionEngine.TryPhysicalClick(tab);
                                    Thread.Sleep(120);
                                }

                                _actionEngine.SendCtrlW(windowHwnd);
                                closedCount++;
                                DiagnosticLogged?.Invoke($"[AUTO_CLOSE_PLAN] Đã đóng tab '{tabName}' bằng phím tắt Ctrl+W.");
                                PlanTabClosed?.Invoke(tabName);
                                Thread.Sleep(100);
                            }
                            catch (Exception ex)
                            {
                                DiagnosticLogged?.Invoke($"[AUTO_CLOSE_PLAN] Lỗi khi gửi Ctrl+W đóng tab: {ex.Message}");
                            }
                        }
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogged?.Invoke($"[AUTO_CLOSE_PLAN] Không thể quét đóng tab: {ex.Message}");
        }

        return closedCount;
    }

    #endregion

    #endregion

    #region Response Feedback Toolbar Detection

    public class ResponseFeedbackToolbar
    {
        public AutomationElement? CopyButton { get; set; }
        public AutomationElement GoodButton { get; set; } = null!;
        public AutomationElement? BadButton { get; set; }
        public string Identifier { get; set; } = string.Empty;
        public Rect BoundingBox { get; set; }
        public double Y => BoundingBox.Bottom;

        public string GetDescription()
        {
            var parts = new List<string>();
            if (CopyButton != null) parts.Add("Sao chép (Copy)");
            if (GoodButton != null) parts.Add("Hài lòng (👍 Good)");
            if (BadButton != null) parts.Add("Chưa hài lòng (👎 Bad)");
            return string.Join(", ", parts);
        }
    }

    private List<ResponseFeedbackToolbar> FindResponseFeedbackToolbars(List<AutomationElement> candidateList, Rect windowRect)
    {
        var toolbars = new List<ResponseFeedbackToolbar>();
        var goodButtons = new List<(AutomationElement elem, Rect rect, string text)>();
        var badButtons = new List<(AutomationElement elem, Rect rect, string text)>();
        var copyButtons = new List<(AutomationElement elem, Rect rect, string text)>();

        foreach (var el in candidateList)
        {
            try
            {
                string rawName = el.Current.Name ?? string.Empty;
                string aid = el.Current.AutomationId ?? string.Empty;
                string help = el.Current.HelpText ?? string.Empty;
                string text = !string.IsNullOrEmpty(rawName) ? rawName : (!string.IsNullOrEmpty(help) ? help : aid);

                if (string.IsNullOrWhiteSpace(text)) continue;

                // Explicitly ignore "Copy code" buttons inside code blocks
                if (text.Contains("code", StringComparison.OrdinalIgnoreCase)) continue;

                var rect = el.Current.BoundingRectangle;
                // Exclude elements outside reasonable icon size (standard buttons are ~31x31)
                if (rect.Width < 15 || rect.Width > 70 || rect.Height < 15 || rect.Height > 70) continue;

                // Exclude top bar / menu bar area
                if (!windowRect.IsEmpty && (rect.Top - windowRect.Top) < 60) continue;

                if (IsGoodResponseButton(text, aid))
                {
                    goodButtons.Add((el, rect, text));
                }
                else if (IsBadResponseButton(text, aid))
                {
                    badButtons.Add((el, rect, text));
                }
                else if (IsCopyResponseButton(text, aid))
                {
                    copyButtons.Add((el, rect, text));
                }
            }
            catch { }
        }

        // Match Good and Bad / Copy buttons that belong to the same response message footer
        foreach (var good in goodButtons)
        {
            // Bad response is on the same horizontal line, to the right within 80px
            var matchingBad = badButtons.FirstOrDefault(b =>
                Math.Abs(b.rect.Top - good.rect.Top) <= 8 &&
                Math.Abs(b.rect.Left - good.rect.Left) <= 80);

            // Copy button is on the same horizontal line, to the left within 80px
            var matchingCopy = copyButtons.FirstOrDefault(c =>
                Math.Abs(c.rect.Top - good.rect.Top) <= 8 &&
                Math.Abs(good.rect.Left - c.rect.Left) <= 80);

            if (matchingBad.elem != null || matchingCopy.elem != null)
            {
                var unionRect = good.rect;
                if (matchingBad.elem != null) unionRect.Union(matchingBad.rect);
                if (matchingCopy.elem != null) unionRect.Union(matchingCopy.rect);

                string id = AutomationAction.GenerateElementId(good.elem, "GoodResponse");
                if (matchingBad.elem != null)
                {
                    id += "_" + AutomationAction.GenerateElementId(matchingBad.elem, "BadResponse");
                }

                toolbars.Add(new ResponseFeedbackToolbar
                {
                    GoodButton = good.elem,
                    BadButton = matchingBad.elem,
                    CopyButton = matchingCopy.elem,
                    Identifier = id,
                    BoundingBox = unionRect
                });
            }
        }

        // Sort descending by Y (bottom-most / latest first)
        toolbars.Sort((a, b) => b.Y.CompareTo(a.Y));
        return toolbars;
    }

    private static bool IsGoodResponseButton(string text, string automationId)
    {
        return text.Equals("Good response", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("good response", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("thumbs up", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("Helpful", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("thích", StringComparison.OrdinalIgnoreCase) ||
               automationId.Contains("goodresponse", StringComparison.OrdinalIgnoreCase) ||
               automationId.Contains("thumbsup", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBadResponseButton(string text, string automationId)
    {
        return text.Equals("Bad response", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("bad response", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("thumbs down", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("Unhelpful", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("không thích", StringComparison.OrdinalIgnoreCase) ||
               automationId.Contains("badresponse", StringComparison.OrdinalIgnoreCase) ||
               automationId.Contains("thumbsdown", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCopyResponseButton(string text, string automationId)
    {
        if (text.Contains("code", StringComparison.OrdinalIgnoreCase)) return false;

        return text.Equals("Copy", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("Copy message", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("Copy response", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("Sao chép", StringComparison.OrdinalIgnoreCase) ||
               automationId.Equals("copy", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
