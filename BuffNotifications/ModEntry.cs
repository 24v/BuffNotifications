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
    private readonly Dictionary<string, List<Buff>> _currentSources = new();
    private readonly HashSet<string> _notifiedExpiringSources = new();

    private ModConfig _config = null!;

    private const int CHECK_FREQUENCY_TICKS = 30;
    private const int WARNING_ICON_NUM = 2;
    private const int ENDING_ICON_NUM = 3;
    private const int STARTING_ICON_NUM = 1;
    private const int MS_TO_SECONDS = 1000;

    public override void Entry(IModHelper helper)
    {
        _config = helper.ReadConfig<ModConfig>();
        Logging.Monitor = Monitor;
        
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        
        this.Monitor.Log("Buff Notifications mod initialized", LogLevel.Info);
    }

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
    
    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (e.IsMultipleOf(CHECK_FREQUENCY_TICKS) && Context.IsWorldReady)
        {
            CheckBuffs();
        }
    }
    

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        _currentSources.Clear();
        _notifiedExpiringSources.Clear();
    }
    
    private void CheckBuffs()
    {
        if (Game1.player.buffs == null)
            return;
            
        Dictionary<string, List<Buff>> currentAppliedSources = [];
        Dictionary<string, int> sourcesExpiring = [];
        
        foreach (var buffId in Game1.player.buffs.AppliedBuffs.Keys)
        {
            if (Game1.player.buffs.AppliedBuffs.TryGetValue(buffId, out Buff? buff) 
                && buff != null 
                && buff.millisecondsDuration > 0)
            {

                int secondsRemaining = buff.millisecondsDuration / MS_TO_SECONDS;
                string source = GetBuffSource(buff);

                if (!currentAppliedSources.TryAdd(source, new List<Buff> { buff }))
                {
                    currentAppliedSources[source].Add(buff);
                }

                if (secondsRemaining <= _config.WarningThresholdSeconds)
                {
                    if (_config.ShowBuffExpiringWarnings)
                    {
                        sourcesExpiring.Add(source, secondsRemaining);
                    }
                }
            }
        }

        foreach (var (source, buffs) in currentAppliedSources)
        {
            if (!_currentSources.ContainsKey(source))
            {
                _currentSources.Add(source, buffs);
                NotifyBuffSourceStarted(source, buffs);
            }
        }

        foreach (var source in _currentSources.Keys)
        {
            if (!currentAppliedSources.ContainsKey(source))
            {
                _notifiedExpiringSources.Remove(source);
                _currentSources.Remove(source);
                NotifyBuffSourceEnded(source);
            }
        }

        foreach (var (source, secondsRemaining) in sourcesExpiring)
        {
            if (!_notifiedExpiringSources.Contains(source))
            {
                _notifiedExpiringSources.Add(source);
                NotifyBuffSourceExpiringSoon(source, secondsRemaining);
            }
        }
    }
    
    private static string GetBuffSource(Buff buff)
    {
        if (buff.source != null && !string.IsNullOrEmpty(buff.source))
        {
            return buff.source;
        }
        else 
        {
            return "unknown source";
        }
    }
    
    private static string GetBuffEffectsDescription(Buff buff)
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
            return "";
        }
        else 
        {
            return string.Join(", ", effects);
        }
    }
    
    private void NotifyBuffSourceStarted(string source, List<Buff> buffs)
    {
        List<string> allEffects = new();
        
        foreach (var buff in buffs)
        {
            allEffects.Add(GetBuffEffectsDescription(buff));
        }
        
        string effectsList = string.Join(", ", allEffects);
        string message = $"{source} Buffs ({effectsList})";
        Game1.addHUDMessage(new HUDMessage(message, STARTING_ICON_NUM));
    }
    
    private void NotifyBuffSourceExpiringSoon(string source, int secondsRemaining)
    {
        string message = $"{source} buffs expiring in {secondsRemaining} seconds!";
        Game1.addHUDMessage(new HUDMessage(message, WARNING_ICON_NUM));
    }
    
    private void NotifyBuffSourceEnded(string source)
    {
        string message = $"{source} buffs have ended.";
        Game1.addHUDMessage(new HUDMessage(message, ENDING_ICON_NUM));
    }
}