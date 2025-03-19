using System;

namespace BuffNotifications;

/// <summary>
/// Configuration settings for BuffNotifications.
/// </summary>
public class ModConfig
{
    /// <summary>
    /// The time threshold (in seconds) before a buff expires to show a warning.
    /// </summary>
    public int WarningThresholdSeconds { get; set; } = 10;
    
    /// <summary>
    /// Whether to show notifications when buffs are activated.
    /// </summary>
    public bool ShowBuffStartNotifications { get; set; } = true;
    
    /// <summary>
    /// Whether to show warnings when buffs are about to expire.
    /// </summary>
    public bool ShowBuffExpiringWarnings { get; set; } = true;
    
    /// <summary>
    /// Whether to show notifications when buffs have ended.
    /// </summary>
    public bool ShowBuffEndNotifications { get; set; } = true;
}
