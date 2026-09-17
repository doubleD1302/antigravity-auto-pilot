using System;
using System.Windows;
using System.Windows.Automation;

namespace AntigravityAutoPilot.Models;

public class AutomationAction
{
    public required AutomationElement Element { get; set; }
    public string ButtonText { get; set; } = string.Empty;
    public ActionKind Kind { get; set; }
    public string ElementIdentifier { get; set; } = string.Empty;
    public Rect BoundingRectangle { get; set; }
    public bool IsTerminalApproval { get; set; }
    public string? DetectedCommand { get; set; }
    public string? SurroundingContext { get; set; }
    public bool IsBlocked { get; set; }
    public string? BlockReason { get; set; }

    public static string GenerateElementId(AutomationElement element, string buttonText)
    {
        try
        {
            var runtimeId = element.GetRuntimeId();
            if (runtimeId != null && runtimeId.Length > 0)
            {
                return $"{string.Join("-", runtimeId)}:{buttonText}";
            }
        }
        catch
        {
            // Fallback
        }

        try
        {
            var autoId = element.Current.AutomationId;
            var rect = element.Current.BoundingRectangle;
            return $"{autoId}:{rect.Left:F0},{rect.Top:F0},{rect.Width:F0},{rect.Height:F0}:{buttonText}";
        }
        catch
        {
            return $"{buttonText}_{Guid.NewGuid():N}";
        }
    }
}
