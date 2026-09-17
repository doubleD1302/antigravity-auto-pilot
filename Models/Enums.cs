namespace AntigravityAutoPilot.Models;

public enum OperatingMode
{
    Safe,
    Normal,
    FullAuto
}

public enum EngineStatus
{
    Stopped,
    Running,
    Paused
}

public enum AntigravityConnectionState
{
    Disconnected,
    Connected
}

public enum LogLevel
{
    Info,
    Success,
    Warning,
    Danger
}

public enum ActionKind
{
    Accept,
    AcceptAll,
    Continue,
    Proceed,
    Run,
    Submit,
    Retry,
    Allow,
    Apply,
    Confirm,
    Custom,
    SelectOption,
    AutoAnswer,
    Blocked
}
