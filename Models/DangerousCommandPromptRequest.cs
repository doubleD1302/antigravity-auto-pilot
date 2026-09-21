using System;
using AntigravityAutoPilot.Models;

namespace AntigravityAutoPilot.Models;

public class DangerousCommandPromptRequest
{
    public required AutomationAction Action { get; set; }
    public required string DangerousKeyword { get; set; }
    public required string FullCommand { get; set; }
    public string? SurroundingContext { get; set; }
    public string? BlockReason { get; set; }
    public bool IsQuestionForm { get; set; }
}
