using System;
using System.Text;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Discord.Net;
using Discord;
using Discord.WebSocket;
using System.Diagnostics;
using FreneticUtilities.FreneticExtensions;
using FreneticUtilities.FreneticDataSyntax;
using FreneticUtilities.FreneticToolkit;
using ModBot.CommandHandlers;
using ModBot.Database;
using ModBot.WarningHandlers;
using DiscordBotBase;
using DiscordBotBase.CommandHandlers;

namespace ModBot.Core
{
    /// <summary>
    /// General program entry and handler.
    /// </summary>
    public class DiscordModBot
    {
        /// <summary>
        /// The relevant WarningCommands instance.
        /// </summary>
        public static WarningCommands WarningCommandHandler;

        /// <summary>
        /// The relevant TempBanManager instance.
        /// </summary>
        public static TempBanManager TempBanHandler;

        /// <summary>
        /// The internal database handler.
        /// </summary>
        public static ModBotDatabaseHandler DatabaseHandler;

        /// <summary>
        /// The default configuration for new guilds.
        /// </summary>
        public static GuildConfig DefaultGuildConfig;

        /// <summary>
        /// List of bot commander user IDs.
        /// </summary>
        public static HashSet<ulong> BotCommanders;

        /// <summary>
        /// The special-roles command handler.
        /// </summary>
        public static SpecialRoleCommands SpecialRoleCommandHandler = new SpecialRoleCommands();

        /// <summary>
        /// Gets the config for a specified guild.
        /// </summary>
        public static GuildConfig GetConfig(ulong guildId)
        {
            return DatabaseHandler.GetDatabase(guildId).Config;
        }

        /// <summary>
        /// Software entry point - starts the bot.
        /// </summary>
        static void Main(string[] args)
        {
            DiscordBotBaseHelper.StartBotHandler(args, new DiscordBotConfig()
            {
                CommandPrefix = null,
                CacheSize = 1024,
                EnsureCaching = true,
                AllowDMs = false,
                UnknownCommandMessage = null,
                Initialize = (bot) =>
                {
                    LoadConfig(bot.ConfigFile);
                    InitCommands(bot);
                    DatabaseHandler = new ModBotDatabaseHandler();
                    TempBanHandler = new TempBanManager();
                    bot.Client.Ready += () =>
                    {
                        DatabaseHandler.Init(bot);
                        bot.Client.SetGameAsync("Guardian Over The People").Wait();
                        TempBanHandler.Scan();
                        return Task.CompletedTask;
                    };
                    new ModBotLoggers().InitLoggers(bot);
                },
                OnShutdown = () =>
                {
                    DatabaseHandler.Shutdown();
                },
                ShouldPayAttentionToMessage = (message) =>
                {
                    return message.Channel is IGuildChannel;
                },
                OtherMessageHandling = (message) =>
                {
                    try
                    {
                        // TODO: helper ping on first post (never posted on the discord guild prior to 10 minutes ago,
                        // -> never posted in any other channel, pings a helper/dev/bot,
                        // -> and nobody else has posted in that channel since their first post) reaction,
                        // -> and if not in a help lobby redirect to help lobby (in same response)
                        SocketGuild guild = (message.Channel as SocketGuildChannel).Guild;
                        TrackUsernameFor(message.Author, guild);
                        // TODO: Spam detection
                        NameUtilities.AsciiNameRuleCheck(message, message.Author as SocketGuildUser);

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error while processing a message: {ex}");
                    }
                },
                UnknownCommandHandler = (name, command) =>
                {
                    SocketGuild guild = (command.Message.Channel as SocketGuildChannel).Guild;
                    GuildConfig config = GetConfig(guild.Id);
                    foreach (GuildConfig.SpecialRole role in config.SpecialRoles.Values)
                    {
                        if (role.AddCommands.Contains(name))
                        {
                            SpecialRoleCommandHandler.CMD_AddSpecialRole(role, command);
                            return;
                        }
                        else if (role.RemoveCommands.Contains(name))
                        {
                            SpecialRoleCommandHandler.CMD_RemoveSpecialRole(role, command);
                            return;
                        }
                    }
                    if (command.WasBotMention)
                    {
                        UserCommands.SendErrorMessageReply(command.Message, "Unknown Command", "Unknown command. Consider the __**help**__ command?");
                    }
                }
            });
        }

        /// <summary>
        /// Tracks a username change for a user, when a message is sent or when they join.
        /// </summary>
        public static void TrackUsernameFor(IUser user, SocketGuild guild)
        {
            GuildConfig config = GetConfig(guild.Id);
            string authorName = NameUtilities.Username(user);
            if (WarningUtilities.GetWarnableUser(guild.Id, user.Id).SeenUsername(authorName, out string oldName) && config.NameChangeNotifChannel.Any())
            {
                EmbedBuilder embed = new EmbedBuilder().WithTitle("User Changed Username").WithColor(0, 255, 255);
                if (oldName != null)
                {
                    embed.AddField("Old Username", $"`{UserCommands.EscapeUserInput(oldName)}`");
                }
                embed.AddField("New Username", $"`{UserCommands.EscapeUserInput(authorName)}`");
                embed.Description = $"User <@{user.Id}> changed their base username.";
                ModBotLoggers.SendEmbedToAllFor(guild, config.NameChangeNotifChannel, embed.Build());
            }
        }

#warning TODO: Eventually remove legacy config updater.
        public static void LegacyConfigUpdate(FDSSection configFile)
        {
            string HelperRoleName = configFile.GetString("helper_role_name", "").ToLowerInvariant();
            string MuteRoleName = configFile.GetString("mute_role_name", "").ToLowerInvariant();
            string DoNotSupportRoleName = configFile.GetString("no_support_role_name", "").ToLowerInvariant();
            string DoNotSupportMessage = configFile.GetString("no_support_message", "");
            DiscordBotBaseHelper.CurrentBot.Client.Ready += () =>
            {
                foreach (SocketGuild guild in DiscordBotBaseHelper.CurrentBot.Client.Guilds)
                {
                    ModBotDatabaseHandler.Guild database = DatabaseHandler.GetDatabase(guild.Id);
                    GuildConfig config = database.Config;
                    SocketRole helperRole = guild.Roles.FirstOrDefault(r => r.Name.ToLowerFast() == HelperRoleName);
                    if (helperRole != null && !config.ModeratorRoles.Contains(helperRole.Id))
                    {
                        config.ModeratorRoles.Add(helperRole.Id);
                    }
                    SocketRole muteRole = guild.Roles.FirstOrDefault(r => r.Name.ToLowerFast() == MuteRoleName);
                    if (muteRole != null && !config.MuteRole.HasValue)
                    {
                        config.MuteRole = muteRole.Id;
                    }
                    SocketRole dnsRole = guild.Roles.FirstOrDefault(r => r.Name.ToLowerFast() == DoNotSupportRoleName);
                    if (dnsRole != null && !config.SpecialRoles.ContainsKey("do-not-support"))
                    {
                        GuildConfig.SpecialRole doNotSupport = new GuildConfig.SpecialRole
                        {
                            Name = "nosupport-other",
                            AddCommands = new List<string>() { "nosupport", "donotsupport", "crack", "cracked", "cracks" },
                            RemoveCommands = new List<string>() { "removenosupport", "removedonotsupport", "removecrack", "removecracked", "removecracks", "uncrack", "uncracked", "uncracks", "legitimate" },
                            AddLevel = WarningLevel.NORMAL,
                            RemoveLevel = WarningLevel.NOTE,
                            AddWarnText = "Marked as Do-Not-Support. User should not receive support unless this status is rescinded.",
                            RemoveWarnText = "Do-not-support status rescinded. The user may receive help going forward.",
                            AddExplanation = DoNotSupportMessage,
                            RemoveExplanation = "You are now allowed to receive support.",
                            RoleID = dnsRole.Id
                        };
                        config.SpecialRoles.Add("nosupport-other", doNotSupport);
                    }
                    database.SaveConfig();
                }
                return Task.CompletedTask;
            };
            if (!string.IsNullOrWhiteSpace(HelperRoleName)) // If this is set, definitely a legacy config
            {
                DefaultGuildConfig.WarningsEnabled = true;
                DefaultGuildConfig.BansEnabled = true;
            }
            DefaultGuildConfig.AttentionNotice = configFile.GetString("attention_notice", "");
            DefaultGuildConfig.IncidentChannel = GetIDList(configFile, "incidents_channel");
            DefaultGuildConfig.EnforceAsciiNameRule = configFile.GetBool("enforce_ascii_name_rule", false).Value;
            DefaultGuildConfig.EnforceNameStartRule = configFile.GetBool("enforce_name_start_rule", false).Value;
            DefaultGuildConfig.JoinNotifChannel = GetIDList(configFile, "join_notif_channel");
            DefaultGuildConfig.RoleChangeNotifChannel = GetIDList(configFile, "role_change_notif_channel");
            DefaultGuildConfig.VoiceChannelJoinNotifs = GetIDList(configFile, "voice_join_notif_channel");
            DefaultGuildConfig.ModLogsChannel = GetIDList(configFile, "mod_log_channel");
            FDSSection logChannelsSection = configFile.GetSection("log_channels");
            if (logChannelsSection != null)
            {
                DefaultGuildConfig.LogChannels = logChannelsSection.GetRootKeys().ToDictionary(key => ulong.Parse(key), key => logChannelsSection.GetUlong(key).Value);
            }
        }

        private static List<ulong> GetIDList(FDSSection section, string key)
        {
            return section.GetDataList(key)?.Select(d => d.AsULong.Value)?.ToList() ?? new List<ulong>();
        }

        /// <summary>
        /// Load the config file to static field.
        /// </summary>
        public static void LoadConfig(FDSSection configFile)
        {
            BotCommanders = new HashSet<ulong>(GetIDList(configFile, "bot_commanders"));
            DefaultGuildConfig = new GuildConfig();
            DefaultGuildConfig.Ensure();
            LegacyConfigUpdate(configFile);
        }

        /// <summary>
        /// initialize all user commands on a Discord bot.
        /// </summary>
        public static void InitCommands(DiscordBot bot)
        {
            WarningCommandHandler = new WarningCommands() { Bot = bot };
            InfoCommands infoCommands = new InfoCommands() { Bot = bot };
            AdminCommands adminCommands = new AdminCommands() { Bot = bot };
            CoreCommands coreCommands = new CoreCommands(IsBotCommander) { Bot = bot };
            // User
            bot.RegisterCommand(infoCommands.CMD_Help, "help", "halp", "helps", "halps", "hel", "hal", "h");
            bot.RegisterCommand(infoCommands.CMD_Hello, "hello", "hi", "hey", "source", "src", "github", "git", "hub");
            bot.RegisterCommand(infoCommands.CMD_ListNames, "names", "listname", "listnames", "namelist", "nameslist");
            // Helper and User
            bot.RegisterCommand(WarningCommandHandler.CMD_ListWarnings, "list", "listnote", "listnotes", "listwarn", "listwarns", "listwarning", "listwarnings", "warnlist", "warninglist", "warningslist");
            // Helper
            bot.RegisterCommand(WarningCommandHandler.CMD_Note, "note");
            bot.RegisterCommand(WarningCommandHandler.CMD_Warn, "warn", "warning");
            bot.RegisterCommand(WarningCommandHandler.CMD_Unmute, "unmute");
            bot.RegisterCommand(WarningCommandHandler.CMD_TempBan, "tempban", "tmpban", "ban", "bantmp", "bantemp", "temporaryban", "bantemporary");
            // Admin
            bot.RegisterCommand(adminCommands.CMD_AdminConfigure, "admin-configure");
            bot.RegisterCommand(adminCommands.CMD_Sweep, "sweep");
            bot.RegisterCommand(adminCommands.CMD_TestName, "testname");
            bot.RegisterCommand(coreCommands.CMD_Restart, "restart");
        }

        /// <summary>
        /// Returns whether a Discord user is a moderator (via role check with role set in config).
        /// </summary>
        public static bool IsModerator(SocketGuildUser user)
        {
            GuildConfig config = GetConfig(user.Guild.Id);
            if (config.ModeratorRoles.IsEmpty())
            {
                return false;
            }
            return user.Roles.Any((role) => config.ModeratorRoles.Contains(role.Id));
        }

        /// <summary>
        /// Returns whether a Discord user is a bot commander (via config check).
        /// </summary>
        public static bool IsBotCommander(IUser user)
        {
            return BotCommanders.Contains(user.Id);
        }
    }
}
