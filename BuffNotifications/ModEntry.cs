using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Buffs;
using StardewValley.Objects;

namespace BuffNotifications;

internal sealed class ModEntry : Mod
{
    /*********
    ** Private fields
    *********/
    /// <summary>Tracks active buffs and their remaining durations. Key is buffId:source.</summary>
    private Dictionary<string, int> _activeBuffs = new();
    
    /// <summary>Buffs that have been warned about expiring soon. Key is buffId:source.</summary>
    private HashSet<string> _warningIssued = new();
    
    /// <summary>The mod configuration.</summary>
    private ModConfig _config = null!;
    
    /// <summary>Tracks sources and their associated buffs. Key is source name, value is list of buff names.</summary>
    private Dictionary<string, HashSet<string>> _sourceBuffs = new();
    
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
        _sourceBuffs.Clear();
    }
    
    /// <summary>Raised after a new day starts.</summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event data.</param>
    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        // Reset tracking at the start of each day
        _activeBuffs.Clear();
        _warningIssued.Clear();
        _sourceBuffs.Clear();
    }
    
    /// <summary>Check for buff changes and notify the player accordingly.</summary>
    private void CheckBuffs()
    {
        if (Game1.player.buffs == null)
            return;
            
        Dictionary<string, int> currentBuffs = new();
        Dictionary<string, HashSet<string>> currentSourceBuffs = new();
        
        // Get all current buffs and their remaining durations
        foreach (var buffId in Game1.player.buffs.AppliedBuffs.Keys)
        {
            if (Game1.player.buffs.AppliedBuffs.TryGetValue(buffId, out Buff? buff) 
                && buff != null 
                && !string.IsNullOrEmpty(buff.displayName) 
                && buff.millisecondsDuration > 0)
            {
                int secondsRemaining = buff.millisecondsDuration / MS_TO_SECONDS;
                
                // Get the source of the buff
                string source = GetBuffSource(buff);
                
                // Create a unique key that combines the buff name and source
                string buffKey = $"{buff.displayName}:{source}";
                
                currentBuffs[buffKey] = secondsRemaining;
                
                // Track which buffs are from which sources
                if (!currentSourceBuffs.ContainsKey(source))
                {
                    currentSourceBuffs[source] = new HashSet<string>();
                }
                currentSourceBuffs[source].Add(buff.displayName);
                
                // Check for new buffs
                if (!_activeBuffs.ContainsKey(buffKey))
                {
                    // If this is a new source or a new buff from an existing source
                    bool isNewSource = !_sourceBuffs.ContainsKey(source);
                    bool isNewBuffFromSource = !isNewSource && !_sourceBuffs[source].Contains(buff.displayName);
                    
                    if (isNewSource)
                    {
                        // New source with its first buff
                        if (_config.ShowBuffStartNotifications)
                            NotifySourceBuffStarted(source, new List<Buff> { buff });
                    }
                    else if (isNewBuffFromSource)
                    {
                        // Existing source with a new buff
                        if (_config.ShowBuffStartNotifications)
                            NotifyAdditionalBuffFromSource(source, buff);
                    }
                }
                
                // Check for buffs about to expire
                if (secondsRemaining <= _config.WarningThresholdSeconds && !_warningIssued.Contains(buffKey))
                {
                    if (_config.ShowBuffExpiringWarnings)
                        NotifyBuffExpiringSoon(buff, source, secondsRemaining);
                    _warningIssued.Add(buffKey);
                }
            }
        }
        
        // Check for expired buffs and sources
        foreach (var buffEntry in _activeBuffs)
        {
            if (!currentBuffs.ContainsKey(buffEntry.Key))
            {
                string[] parts = buffEntry.Key.Split(':', 2);
                string buffName = parts[0];
                string source = parts.Length > 1 ? parts[1] : "unknown source";
                
                // Check if this was the last buff from this source
                bool isLastBuffFromSource = _sourceBuffs.ContainsKey(source) && 
                                          _sourceBuffs[source].Contains(buffName) &&
                                          (!currentSourceBuffs.ContainsKey(source) || 
                                           currentSourceBuffs[source].Count == 0);
                
                if (isLastBuffFromSource)
                {
                    // Last buff from this source has ended
                    if (_config.ShowBuffEndNotifications)
                        NotifySourceBuffsEnded(source);
                }
                else
                {
                    // Individual buff has ended
                    if (_config.ShowBuffEndNotifications)
                        NotifyBuffEnded(buffName, source);
                }
                
                _warningIssued.Remove(buffEntry.Key);
            }
        }
        
        // Update active buffs and sources
        _activeBuffs = currentBuffs;
        _sourceBuffs = currentSourceBuffs;
    }
    
    /// <summary>Get the source of a buff.</summary>
    /// <param name="buff">The buff to get the source for.</param>
    /// <returns>A string describing the source of the buff.</returns>
    private string GetBuffSource(Buff buff)
    {
        // Try to determine the source based on buff properties
        if (buff.source != null && !string.IsNullOrEmpty(buff.source))
        {
            return buff.source;
        }
        
        // Use description to infer source if available
        if (!string.IsNullOrEmpty(buff.description))
        {
            return buff.description;
        }
        
        // Default source if we can't determine it
        return "unknown source";
    }
    
    /// <summary>Get a description of the buff effects.</summary>
    /// <param name="buff">The buff to describe.</param>
    /// <returns>A string describing the effects of the buff.</returns>
    private string GetBuffEffectsDescription(Buff buff)
    {
        List<string> effects = new List<string>();
        
        // Check each possible buff effect
        if (buff.effects != null)
        {
            if (buff.effects.FarmingLevel.Value != 0) effects.Add($"Farming {(buff.effects.FarmingLevel.Value > 0 ? "+" : "")}{buff.effects.FarmingLevel.Value}");
            if (buff.effects.FishingLevel.Value != 0) effects.Add($"Fishing {(buff.effects.FishingLevel.Value > 0 ? "+" : "")}{buff.effects.FishingLevel.Value}");
            if (buff.effects.MiningLevel.Value != 0) effects.Add($"Mining {(buff.effects.MiningLevel.Value > 0 ? "+" : "")}{buff.effects.MiningLevel.Value}");
            if (buff.effects.LuckLevel.Value != 0) effects.Add($"Luck {(buff.effects.LuckLevel.Value > 0 ? "+" : "")}{buff.effects.LuckLevel.Value}");
            if (buff.effects.ForagingLevel.Value != 0) effects.Add($"Foraging {(buff.effects.ForagingLevel.Value > 0 ? "+" : "")}{buff.effects.ForagingLevel.Value}");
            if (buff.effects.MaxStamina.Value != 0) effects.Add($"Max Energy {(buff.effects.MaxStamina.Value > 0 ? "+" : "")}{buff.effects.MaxStamina.Value}");
            if (buff.effects.MagneticRadius.Value != 0) effects.Add($"Magnetism {(buff.effects.MagneticRadius.Value > 0 ? "+" : "")}{buff.effects.MagneticRadius.Value}");
            if (buff.effects.Speed.Value != 0) effects.Add($"Speed {(buff.effects.Speed.Value > 0 ? "+" : "")}{buff.effects.Speed.Value}");
            if (buff.effects.Defense.Value != 0) effects.Add($"Defense {(buff.effects.Defense.Value > 0 ? "+" : "")}{buff.effects.Defense.Value}");
            if (buff.effects.Attack.Value != 0) effects.Add($"Attack {(buff.effects.Attack.Value > 0 ? "+" : "")}{buff.effects.Attack.Value}");
        }
        
        if (effects.Count == 0)
        {
            return "No visible effects";
        }
        
        return string.Join(", ", effects);
    }
    
    /// <summary>Display a notification when buffs from a source are activated.</summary>
    /// <param name="source">The source of the buffs.</param>
    /// <param name="buffs">The buffs that were activated.</param>
    private void NotifySourceBuffStarted(string source, List<Buff> buffs)
    {
        // Create a description of all the buffs from this source
        List<string> buffDescriptions = new List<string>();
        foreach (var buff in buffs)
        {
            string effects = GetBuffEffectsDescription(buff);
            buffDescriptions.Add($"{buff.displayName} ({effects})");
        }
        
        string buffList = string.Join(", ", buffDescriptions);
        string message = $"Effects from {source} activated: {buffList}";
        
        // Try to find an item that matches the source name to use as the icon
        Item? sourceItem = FindItemByName(source);
        
        if (sourceItem != null)
        {
            // Use the item as the icon
            HUDMessage hudMessage = HUDMessage.ForItemGained(sourceItem, 1);
            hudMessage.message = message;
            Game1.addHUDMessage(hudMessage);
        }
        else
        {
            // Use a standard notification
            Game1.addHUDMessage(new HUDMessage(message, HUD_TYPE_BUFF));
        }
        
        this.Monitor.Log(message, LogLevel.Info);
    }
    
    /// <summary>Display a notification when an additional buff from a source is activated.</summary>
    /// <param name="source">The source of the buff.</param>
    /// <param name="buff">The additional buff that was activated.</param>
    private void NotifyAdditionalBuffFromSource(string source, Buff buff)
    {
        string effects = GetBuffEffectsDescription(buff);
        string message = $"Additional effect from {source}: {buff.displayName} ({effects})";
        
        // Try to find an item that matches the source name to use as the icon
        Item? sourceItem = FindItemByName(source);
        
        if (sourceItem != null)
        {
            // Use the item as the icon
            HUDMessage hudMessage = HUDMessage.ForItemGained(sourceItem, 1);
            hudMessage.message = message;
            Game1.addHUDMessage(hudMessage);
        }
        else
        {
            // Use a standard notification
            Game1.addHUDMessage(new HUDMessage(message, HUD_TYPE_BUFF));
        }
        
        this.Monitor.Log(message, LogLevel.Info);
    }
    
    /// <summary>Display a notification when a buff is about to expire.</summary>
    /// <param name="buff">The buff that's expiring soon.</param>
    /// <param name="source">The source of the buff.</param>
    /// <param name="secondsRemaining">Seconds remaining before the buff expires.</param>
    private void NotifyBuffExpiringSoon(Buff buff, string source, int secondsRemaining)
    {
        string effects = GetBuffEffectsDescription(buff);
        string message = $"{buff.displayName} effect from {source} expiring in {secondsRemaining} seconds! ({effects})";
        
        // Try to find an item that matches the source name to use as the icon
        Item? sourceItem = FindItemByName(source);
        
        if (sourceItem != null)
        {
            // Use the item as the icon
            HUDMessage hudMessage = HUDMessage.ForItemGained(sourceItem, 1);
            hudMessage.message = message;
            hudMessage.whatType = HUD_TYPE_WARNING; // Set the warning type
            Game1.addHUDMessage(hudMessage);
        }
        else
        {
            // Use a standard notification
            Game1.addHUDMessage(new HUDMessage(message, HUD_TYPE_WARNING));
        }
        
        this.Monitor.Log(message, LogLevel.Info);
    }
    
    /// <summary>Display a notification when a buff ends.</summary>
    /// <param name="buffName">The name of the buff that ended.</param>
    /// <param name="source">The source of the buff.</param>
    private void NotifyBuffEnded(string buffName, string source)
    {
        string message = $"{buffName} effect from {source} has ended.";
        
        // Try to find an item that matches the source name to use as the icon
        Item? sourceItem = FindItemByName(source);
        
        if (sourceItem != null)
        {
            // Use the item as the icon
            HUDMessage hudMessage = HUDMessage.ForItemGained(sourceItem, 1);
            hudMessage.message = message;
            hudMessage.whatType = HUD_TYPE_ERROR; // Set the error type
            Game1.addHUDMessage(hudMessage);
        }
        else
        {
            // Use a standard notification
            Game1.addHUDMessage(new HUDMessage(message, HUD_TYPE_ERROR));
        }
        
        this.Monitor.Log(message, LogLevel.Info);
    }
    
    /// <summary>Display a notification when all buffs from a source have ended.</summary>
    /// <param name="source">The source whose buffs have ended.</param>
    private void NotifySourceBuffsEnded(string source)
    {
        string message = $"All effects from {source} have ended.";
        
        // Try to find an item that matches the source name to use as the icon
        Item? sourceItem = FindItemByName(source);
        
        if (sourceItem != null)
        {
            // Use the item as the icon
            HUDMessage hudMessage = HUDMessage.ForItemGained(sourceItem, 1);
            hudMessage.message = message;
            hudMessage.whatType = HUD_TYPE_ERROR; // Set the error type
            Game1.addHUDMessage(hudMessage);
        }
        else
        {
            // Use a standard notification
            Game1.addHUDMessage(new HUDMessage(message, HUD_TYPE_ERROR));
        }
        
        this.Monitor.Log(message, LogLevel.Info);
    }
    
    /// <summary>Try to find an item by name to use as an icon.</summary>
    /// <param name="name">The name to search for.</param>
    /// <returns>An item if found, null otherwise.</returns>
    private Item? FindItemByName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
            
        string cleanName = name.ToLower();
        
        // Common buff sources - using ItemRegistry.Create to properly create items
        if (cleanName.Contains("coffee")) return ItemRegistry.Create("(O)395");
        if (cleanName.Contains("tea")) return ItemRegistry.Create("(O)614");
        if (cleanName.Contains("spicy eel")) return ItemRegistry.Create("(O)226");
        if (cleanName.Contains("lucky lunch")) return ItemRegistry.Create("(O)241");
        if (cleanName.Contains("maple bar")) return ItemRegistry.Create("(O)731");
        if (cleanName.Contains("crab cake")) return ItemRegistry.Create("(O)732");
        if (cleanName.Contains("pepper poppers")) return ItemRegistry.Create("(O)215");
        if (cleanName.Contains("tom kha soup")) return ItemRegistry.Create("(O)218");
        if (cleanName.Contains("triple shot espresso")) return ItemRegistry.Create("(O)253");
        if (cleanName.Contains("seafoam pudding")) return ItemRegistry.Create("(O)265");
        if (cleanName.Contains("algae soup")) return ItemRegistry.Create("(O)456");
        if (cleanName.Contains("pale broth")) return ItemRegistry.Create("(O)457");
        
        // Try to create a generic food item if we can't find a specific match
        return null;
    }
}