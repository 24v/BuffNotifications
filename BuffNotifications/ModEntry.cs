using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Buffs;

namespace BuffNotifications;

internal sealed class ModEntry : Mod
{
    /*********
    ** Private fields
    *********/
    /// <summary>Tracks active buffs and their remaining durations.</summary>
    private Dictionary<string, int> _activeBuffs = new();
    
    /// <summary>Buffs that have been warned about expiring soon.</summary>
    private HashSet<string> _warningIssued = new();
    
    /// <summary>The mod configuration.</summary>
    private ModConfig _config = null!;
    
    /*********
    ** Constants
    *********/
    /// <summary>How often to check for buff changes (in game ticks).</summary>
    private const int CHECK_FREQUENCY_TICKS = 30;
    
    /// <summary>HUD message type for buff activation (green).</summary>
    private const int HUD_TYPE_BUFF = 2;
    
    /// <summary>HUD message type for warnings (yellow).</summary>
    private const int HUD_TYPE_WARNING = 3;
    
    /// <summary>HUD message type for buff expiration (red).</summary>
    private const int HUD_TYPE_ERROR = 1;
    
    /// <summary>Conversion factor from milliseconds to seconds.</summary>
    private const int MS_TO_SECONDS = 1000;

    /*********
    ** Public methods
    *********/
    /// <summary>The mod entry point, called after the mod is first loaded.</summary>
    /// <param name="helper">Provides simplified APIs for writing mods.</param>
    public override void Entry(IModHelper helper)
    {
        // Load config
        _config = helper.ReadConfig<ModConfig>();
        
        // Register event handlers
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        
        this.Monitor.Log("Buff Notifications mod initialized", LogLevel.Info);
    }

    /*********
    ** Private methods
    *********/
    /// <summary>Raised after the game is launched, right before the first update tick.</summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event data.</param>
    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        // Register Generic Mod Config Menu if it's installed
        var configMenu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (configMenu is null)
            return;

        // Register mod
        configMenu.Register(
            mod: this.ModManifest,
            reset: () => this._config = new ModConfig(),
            save: () => this.Helper.WriteConfig(this._config)
        );

        // Add config options
        configMenu.AddSectionTitle(
            mod: this.ModManifest,
            text: () => "General Settings"
        );
        
        configMenu.AddNumberOption(
            mod: this.ModManifest,
            name: () => "Warning Threshold (seconds)",
            tooltip: () => "How many seconds before a buff expires to show a warning",
            getValue: () => this._config.WarningThresholdSeconds,
            setValue: value => this._config.WarningThresholdSeconds = value,
            min: 1,
            max: 60,
            interval: 1
        );
        
        configMenu.AddSectionTitle(
            mod: this.ModManifest,
            text: () => "Notification Settings"
        );
        
        configMenu.AddBoolOption(
            mod: this.ModManifest,
            name: () => "Show Buff Start Notifications",
            tooltip: () => "Whether to show notifications when buffs are activated",
            getValue: () => this._config.ShowBuffStartNotifications,
            setValue: value => this._config.ShowBuffStartNotifications = value
        );
        
        configMenu.AddBoolOption(
            mod: this.ModManifest,
            name: () => "Show Buff Expiring Warnings",
            tooltip: () => "Whether to show warnings when buffs are about to expire",
            getValue: () => this._config.ShowBuffExpiringWarnings,
            setValue: value => this._config.ShowBuffExpiringWarnings = value
        );
        
        configMenu.AddBoolOption(
            mod: this.ModManifest,
            name: () => "Show Buff End Notifications",
            tooltip: () => "Whether to show notifications when buffs have ended",
            getValue: () => this._config.ShowBuffEndNotifications,
            setValue: value => this._config.ShowBuffEndNotifications = value
        );
    }
    
    /// <summary>Raised after the game state is updated (≈60 times per second).</summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event data.</param>
    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        // Only process every CHECK_FREQUENCY_TICKS ticks to reduce overhead
        if (e.IsMultipleOf(CHECK_FREQUENCY_TICKS) && Context.IsWorldReady)
        {
            CheckBuffs();
        }
    }
    
    /// <summary>Raised after a save is loaded.</summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event data.</param>
    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        // Reset tracking when a save is loaded
        _activeBuffs.Clear();
        _warningIssued.Clear();
    }
    
    /// <summary>Raised after a new day starts.</summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event data.</param>
    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        // Reset tracking at the start of each day
        _activeBuffs.Clear();
        _warningIssued.Clear();
    }
    
    /// <summary>Check for buff changes and notify the player accordingly.</summary>
    private void CheckBuffs()
    {
        if (Game1.player.buffs == null)
            return;
            
        Dictionary<string, int> currentBuffs = new();
        
        // Get all current buffs and their remaining durations
        foreach (var buffId in Game1.player.buffs.AppliedBuffs.Keys)
        {
            if (Game1.player.buffs.AppliedBuffs.TryGetValue(buffId, out Buff? buff) 
                && buff != null 
                && !string.IsNullOrEmpty(buff.displayName) 
                && buff.millisecondsDuration > 0)
            {
                int secondsRemaining = buff.millisecondsDuration / MS_TO_SECONDS;
                currentBuffs[buff.displayName] = secondsRemaining;
                
                // Check for new buffs
                if (!_activeBuffs.ContainsKey(buff.displayName))
                {
                    if (_config.ShowBuffStartNotifications)
                        NotifyBuffStarted(buff);
                }
                
                // Check for buffs about to expire
                if (secondsRemaining <= _config.WarningThresholdSeconds && !_warningIssued.Contains(buff.displayName))
                {
                    if (_config.ShowBuffExpiringWarnings)
                        NotifyBuffExpiringSoon(buff, secondsRemaining);
                    _warningIssued.Add(buff.displayName);
                }
            }
        }
        
        // Check for expired buffs
        foreach (var buffEntry in _activeBuffs)
        {
            if (!currentBuffs.ContainsKey(buffEntry.Key))
            {
                if (_config.ShowBuffEndNotifications)
                    NotifyBuffEnded(buffEntry.Key);
                _warningIssued.Remove(buffEntry.Key);
            }
        }
        
        // Update active buffs
        _activeBuffs = currentBuffs;
    }
    
    /// <summary>Display a notification when a buff starts.</summary>
    /// <param name="buff">The buff that started.</param>
    private void NotifyBuffStarted(Buff buff)
    {
        string message = $"{buff.displayName} buff activated!";
        Game1.addHUDMessage(new HUDMessage(message, HUD_TYPE_BUFF));
        this.Monitor.Log(message, LogLevel.Info);
    }
    
    /// <summary>Display a notification when a buff is about to expire.</summary>
    /// <param name="buff">The buff that's expiring soon.</param>
    /// <param name="secondsRemaining">Seconds remaining before the buff expires.</param>
    private void NotifyBuffExpiringSoon(Buff buff, int secondsRemaining)
    {
        string message = $"{buff.displayName} buff expiring in {secondsRemaining} seconds!";
        Game1.addHUDMessage(new HUDMessage(message, HUD_TYPE_WARNING));
        this.Monitor.Log(message, LogLevel.Info);
    }
    
    /// <summary>Display a notification when a buff ends.</summary>
    /// <param name="buffName">The name of the buff that ended.</param>
    private void NotifyBuffEnded(string buffName)
    {
        string message = $"{buffName} buff has ended.";
        Game1.addHUDMessage(new HUDMessage(message, HUD_TYPE_ERROR));
        this.Monitor.Log(message, LogLevel.Info);
    }
}