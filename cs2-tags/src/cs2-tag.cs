using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using TagsApi;
using static Tags.TagExtensions;
using static TagsApi.Tags;

namespace Tags;

public class Tags : BasePlugin, IPluginConfig<Config>
{
    public override string ModuleName => "Tags";
    public override string ModuleVersion => "1.14";
    public override string ModuleAuthor => "schwarper";

    public static readonly Dictionary<ulong, Tag> PlayerTagsList = [];
    public static readonly TagsAPI Api = new();
    public static Tags Instance { get; set; } = new();
    public Config Config { get; set; } = new();
    public static DatabaseService? Database { get; private set; }

    private static readonly HashSet<string> ValidColors = new(StringComparer.OrdinalIgnoreCase)
    {
        "White", "TeamColor", "DarkRed", "Green", "LightYellow", "LightBlue",
        "Olive", "Lime", "Red", "LightPurple", "Purple", "Grey", "Yellow",
        "Gold", "Silver", "Blue", "DarkBlue", "BlueGrey", "Magenta",
        "LightRed", "Orange"
    };

    private static bool IsValidColor(string color)
    {
        return ValidColors.Contains(color);
    }

    public override void Load(bool hotReload)
    {
        Instance = this;
        Capabilities.RegisterPluginCapability(ITagApi.Capability, () => Api);

        foreach (string command in Config.Commands.TagsReload)
            AddCommand(command, "Tags Reload", Command_Tags_Reload);

        foreach (string command in Config.Commands.Visibility)
            AddCommand(command, "Visibility", Command_Visibility);

        foreach (string command in Config.Commands.NameColor)
            AddCommand(command, "Change name color", Command_NameColor);

        foreach (string command in Config.Commands.ChatColor)
            AddCommand(command, "Change chat color", Command_ChatColor);

        HookUserMessage(118, OnMessage, HookMode.Pre);
        AddCommandListener("css_admins_reload", Command_Admins_Reloads, HookMode.Pre);

        if (hotReload)
            ReloadTags();
    }

    public override void Unload(bool hotReload)
    {
        UnhookUserMessage(118, OnMessage, HookMode.Pre);
        RemoveCommandListener("css_admins_reload", Command_Admins_Reloads, HookMode.Pre);
    }

    public void OnConfigParsed(Config config)
    {
        config.Settings.Init();
        Config = config;
        
        // Initialize database
        Database = new DatabaseService(config.Database);
        Task.Run(async () => await Database.InitializeAsync());
    }

    public static HookResult Command_Admins_Reloads(CCSPlayerController? player, CommandInfo info)
    {
        ReloadConfig();
        ReloadTags();
        return HookResult.Continue;
    }

    [RequiresPermissions("@css/root")]
    public static void Command_Tags_Reload(CCSPlayerController? player, CommandInfo info)
    {
        ReloadConfig();
        ReloadTags();
    }

    [RequiresPermissions("@css/admin")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void Command_Visibility(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null)
        {
            return;
        }

        if (player.GetVisibility())
        {
            player.SetVisibility(false);
            info.ReplyToCommand(Config.Settings.Tag + Localizer.ForPlayer(player, "Tags are now hidden"));
        }
        else
        {
            player.SetVisibility(true);
            info.ReplyToCommand(Config.Settings.Tag + Localizer.ForPlayer(player, "Tags are now visible"));
        }
    }

    [RequiresPermissions("@css/vip")]
    [CommandHelper(minArgs: 1, usage: "<color>", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void Command_NameColor(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null)
        {
            return;
        }

        string color = info.GetArg(1);
        
        if (!IsValidColor(color))
        {
            info.ReplyToCommand(Config.Settings.Tag + $"Invalid color: {color}. Please use a valid color name.");
            return;
        }
        
        player.SetAttribute(TagType.NameColor, $"{{{color}}}");
        
        Tag tag = GetOrCreatePlayerTag(player, false);
        Task.Run(async () =>
        {
            if (Database != null)
                await Database.SavePlayerColorsAsync(player.SteamID, player.PlayerName, tag.ChatColor, tag.NameColor);
        });
        
        info.ReplyToCommand(Config.Settings.Tag + $"Name color changed to {color}");
    }

    [RequiresPermissions("@css/vip")]
    [CommandHelper(minArgs: 1, usage: "<color>", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void Command_ChatColor(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null)
        {
            return;
        }

        string color = info.GetArg(1);
        
        if (!IsValidColor(color))
        {
            info.ReplyToCommand(Config.Settings.Tag + $"Invalid color: {color}. Please use a valid color name.");
            return;
        }
        
        player.SetAttribute(TagType.ChatColor, $"{{{color}}}");
        
        Tag tag = GetOrCreatePlayerTag(player, false);
        Task.Run(async () =>
        {
            if (Database != null)
                await Database.SavePlayerColorsAsync(player.SteamID, player.PlayerName, tag.ChatColor, tag.NameColor);
        });
        
        info.ReplyToCommand(Config.Settings.Tag + $"Chat color changed to {color}");
    }

    [GameEventHandler]
    public HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
    {
        if (@event.Userid is not CCSPlayerController player || player.IsBot)
            return HookResult.Continue;

        PlayerTagsList[player.SteamID] = player.GetTag();
        
        // Load colors from database
        Task.Run(async () =>
        {
            if (Database == null)
                return;
                
            var (chatColor, nameColor) = await Database.LoadPlayerColorsAsync(player.SteamID);
            
            if (chatColor != null || nameColor != null)
            {
                Server.NextFrame(() =>
                {
                    if (chatColor != null)
                        player.SetAttribute(TagType.ChatColor, chatColor);
                    if (nameColor != null)
                        player.SetAttribute(TagType.NameColor, nameColor);
                });
            }
        });
        
        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Pre)]
    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        if (@event.Userid is not CCSPlayerController player || player.IsBot)
            return HookResult.Continue;

        PlayerTagsList.Remove(player.SteamID);
        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Pre)]
    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        if (@event.Userid is not { } player || player.IsBot)
            return HookResult.Continue;

        var tag = GetOrCreatePlayerTag(player, false);
        player.SetScoreTag(tag.ScoreTag);
        return HookResult.Continue;
    }

    public HookResult OnMessage(UserMessage um)
    {
        if (Utilities.GetPlayerFromIndex(um.ReadInt("entityindex")) is not CCSPlayerController player || player.IsBot)
            return HookResult.Continue;

        var tag = GetOrCreatePlayerTag(player, false);

        MessageProcess messageProcess = new()
        {
            Player = player,
            Tag = !player.GetVisibility() ? Config.Default.Clone() : tag.Clone(),
            Message = um.ReadString("param2").RemoveCurlyBraceContent(),
            PlayerName = um.ReadString("param1"),
            ChatSound = um.ReadBool("chat"),
            TeamMessage = !um.ReadString("messagename").Contains("All")
        };

        if (string.IsNullOrEmpty(messageProcess.Message))
            return HookResult.Handled;

        HookResult hookResult = Api.MessageProcessPre(messageProcess);

        if (hookResult >= HookResult.Handled)
            return hookResult;

        string deadname = player.PawnIsAlive ? string.Empty : Config.Settings.DeadName;
        string teamname = messageProcess.TeamMessage ? player.Team.Name() : string.Empty;

        Tag playerData = messageProcess.Tag;

        CsTeam team = player.Team;
        messageProcess.PlayerName = FormatMessage(team, deadname, teamname, playerData.ChatTag ?? string.Empty, playerData.NameColor ?? string.Empty, messageProcess.PlayerName);
        messageProcess.Message = FormatMessage(team, playerData.ChatColor ?? string.Empty, messageProcess.Message);

        hookResult = Api.MessageProcess(messageProcess);

        if (hookResult >= HookResult.Handled)
            return hookResult;

        um.SetString("messagename", $"{messageProcess.PlayerName}{ChatColors.White}: {messageProcess.Message}");
        um.SetBool("chat", playerData.ChatSound);

        Api.MessageProcessPost(messageProcess);

        return HookResult.Changed;
    }
}