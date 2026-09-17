using System;
using System.Collections.Generic;

namespace AntigravityAutoPilot.Models;

public class AppSettings
{
    // Whitelist toggles
    public bool EnableAccept { get; set; } = true;
    public bool EnableAcceptAll { get; set; } = true;
    public bool EnableContinue { get; set; } = true;
    public bool EnableRun { get; set; } = true;
    public bool EnableSubmit { get; set; } = true;
    public bool EnableRetry { get; set; } = true;
    public bool EnableAllow { get; set; } = true;
    public bool EnableApply { get; set; } = true;
    public bool EnableConfirm { get; set; } = true;
    public bool EnableProceed { get; set; } = true;
    public bool EnableAutoAnswerQuestion { get; set; } = true;
    public string PreferredQuestionOptionPattern { get; set; } = @"^(1\b|Yes\b|Allow\b)";
    public bool IgnoreStaleButtonsOnStartup { get; set; } = true;

    // Operating mode
    public OperatingMode Mode { get; set; } = OperatingMode.Normal;

    // Scan & Debounce Timers
    public int ScanIntervalMs { get; set; } = 400; // 100 - 3000 ms
    public int DebounceMs { get; set; } = 1200;    // 1000 - 2000 ms default 1200 ms

    // Safety & Click Policy
    public bool PreventFocusStealing { get; set; } = true;
    public bool AllowPhysicalClickFallback { get; set; } = false;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool StartMinimized { get; set; } = false;

    // Sound Notification on Task Completion (done.mp3)
    public bool EnableSoundNotification { get; set; } = true;
    public string SoundFilePath { get; set; } = "Sounds/done.mp3";
    public int SoundVolumePercent { get; set; } = 100;

    // Sound Notification on Submit Approval (submit.mp3)
    public bool EnableSubmitSoundNotification { get; set; } = true;
    public string SubmitSoundFilePath { get; set; } = "Sounds/submit.mp3";

    // Sound Notification on Accept/Accept All (accept_all.mp3)
    public bool EnableAcceptSoundNotification { get; set; } = true;
    public string AcceptSoundFilePath { get; set; } = "Sounds/accept_all.mp3";

    // Sound Notification on Implementation Plan Detection (plan.mp3)
    public bool EnablePlanSoundNotification { get; set; } = true;
    public string PlanSoundFilePath { get; set; } = "Sounds/plan.mp3";

    // Auto-close editor tab of proceeded implementation plans (without deleting file)
    public bool AutoCloseProceededPlans { get; set; } = true;

    // Custom Lists
    public List<string> CustomWhitelist { get; set; } = new();
    public List<string> CustomBlacklist { get; set; } = new();

    // Dangerous terminal commands to block
    public List<string> DangerousCommands { get; set; } = new()
    {
        "rm -rf",
        "rm -r",
        "del /s",
        "rmdir /s",
        "Remove-Item -Recurse",
        "Remove-Item -r",
        "format ",
        "diskpart",
        "shutdown",
        "reboot",
        "git reset --hard",
        "git clean -fd",
        "git clean -fdx",
        "DROP DATABASE",
        "DROP TABLE",
        "TRUNCATE TABLE",
        "DELETE FROM",
        "docker system prune",
        "docker volume prune",
        "kubectl delete",
        "terraform destroy"
    };

    // Hardcoded absolute blacklist words (always active)
    public static readonly string[] AbsoluteBlacklist = new[]
    {
        "Reject",
        "Decline",
        "Delete",
        "Remove",
        "Discard",
        "Cancel",
        "Reset",
        "Terminate",
        "Stop",
        "Kill",
        "Erase",
        "Drop",
        "Revert",
        "Undo All"
    };

    public AppSettings Clone()
    {
        return (AppSettings)MemberwiseClone();
    }
}
