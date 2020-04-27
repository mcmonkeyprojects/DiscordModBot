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
using DiscordModBot.CommandHandlers;
using DiscordBotBase;
using DiscordBotBase.CommandHandlers;

namespace DiscordModBot
{
    /// <summary>
    /// General program entry and handler.
    /// </summary>
    public class DiscordModBot
    {
        /// <summary>
        /// Configuration value: the name of the role used for helpers.
        /// </summary>
        public static string HelperRoleName;

        /// <summary>
        /// Configuration value: what text to use to 'get attention' when a mute is given (eg. an @ mention to an admin).
        /// </summary>
        public static string AttentionNotice;

        /// <summary>
        /// Configuration value: The name of the role given to muted users.
        /// </summary>
        public static string MuteRoleName;

        /// <summary>
        /// Configuration value: The ID of the incident notice channel.
        /// </summary>
        public static List<ulong> IncidentChannel;

        /// <summary>
        /// Configuration value: The ID of the join log message channel.
        /// </summary>
        public static List<ulong> JoinNotifChannel;

        /// <summary>
        /// Configuration value: The ID of the voicechannel join/leave log message channel.
        /// </summary>
        public static List<ulong> VoiceChannelJoinNotifs;

        /// <summary>
        /// Configuration value: The ID of the role-change log message channel.
        /// </summary>
        public static List<ulong> RoleChangeNotifChannel;

        /// <summary>
        /// Configuration value: whether the ASCII name rule should be enforced by the bot.
        /// </summary>
        public static bool EnforceAsciiNameRule = true;

        /// <summary>
        /// Configuration value: whether the A-Z first character name rule should be enforced by the bot.
        /// </summary>
        public static bool EnforceNameStartRule = false;

        /// <summary>
        /// Configuration value: channels to log, mapping from (channel being logged) to (channel that shows the logs).
        /// </summary>
        public static Dictionary<ulong, ulong> LogChannels = new Dictionary<ulong, ulong>(512);

        /// <summary>
        /// The relevant WarningCommands instance.
        /// </summary>
        public static WarningCommands WarningCommandHandler;

        /// <summary>
        /// Software entry point - starts the bot.
        /// </summary>
        static void Main(string[] args)
        {
            DiscordBotBaseHelper.StartBotHandler(args, new DiscordBotConfig()
            {
                CommandPrefix = null,
                Initialize = (bot) =>
                {
                    LoadConfig(bot.ConfigFile);
                    InitCommands(bot);
                    bot.Client.Ready += () =>
                    {
                        bot.Client.SetGameAsync("Guardian Over The People").Wait();
                        return Task.CompletedTask;
                    };
                    new ModBotLoggers().InitLoggers(bot);
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
                        string authorName = NameUtilities.Username(message.Author);
                        if (WarningUtilities.GetWarnableUser((message.Channel as SocketGuildChannel).Guild.Id, message.Author.Id).SeenUsername(authorName, out string oldName))
                        {
                            UserCommands.SendGenericPositiveMessageReply(message, "Rename Notice", $"Notice: User <@{message.Author.Id}> changed their base username from `{oldName}` to `{authorName}`.");
                        }
                        // TODO: Spam detection
                        NameUtilities.AsciiNameRuleCheck(message as IUserMessage, message.Author as SocketGuildUser);

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error while processing a message: {ex}");
                    }
                }
            });
        }

        /// <summary>
        /// Load the config file to static field.
        /// </summary>
        static void LoadConfig(FDSSection configFile)
        {
            List<ulong> getChannelList(string name) => configFile.GetDataList(name)?.Select(d => ObjectConversionHelper.ObjectToULong(d.Internal).Value)?.ToList() ?? new List<ulong>();
            HelperRoleName = configFile.GetString("helper_role_name").ToLowerInvariant();
            MuteRoleName = configFile.GetString("mute_role_name").ToLowerInvariant();
            AttentionNotice = configFile.GetString("attention_notice");
            IncidentChannel = configFile.GetDataList("incidents_channel").Select(d => ObjectConversionHelper.ObjectToULong(d.Internal).Value).ToList();
            EnforceAsciiNameRule = configFile.GetBool("enforce_ascii_name_rule", EnforceAsciiNameRule).Value;
            EnforceNameStartRule = configFile.GetBool("enforce_name_start_rule", EnforceNameStartRule).Value;
            JoinNotifChannel = getChannelList("join_notif_channel");
            RoleChangeNotifChannel = getChannelList("role_change_notif_channel");
            VoiceChannelJoinNotifs = getChannelList("voice_join_notif_channel");
            FDSSection logChannelsSection = configFile.GetSection("log_channels");
            LogChannels = logChannelsSection.GetRootKeys().ToDictionary(key => ulong.Parse(key), key => logChannelsSection.GetUlong(key).Value);
        }

        /// <summary>
        /// initialize all user commands on a Discord bot.
        /// </summary>
        static void InitCommands(DiscordBot bot)
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
            // Admin
            bot.RegisterCommand(adminCommands.CMD_Sweep, "sweep");
            bot.RegisterCommand(adminCommands.CMD_TestName, "testname");
            bot.RegisterCommand(coreCommands.CMD_Restart, "restart");
        }

        /// <summary>
        /// Returns whether a Discord user is a helper (via role check with role set in config).
        /// </summary>
        public static bool IsHelper(SocketGuildUser user)
        {
            return user.Roles.Any((role) => role.Name.ToLowerInvariant() == HelperRoleName);
        }

        /// <summary>
        /// Returns whether a Discord user is a bot commander (via role check).
        /// </summary>
        public static bool IsBotCommander(IUser user)
        {
            return (user as SocketGuildUser).Roles.Any((role) => role.Name.ToLowerInvariant() == "botcommander");
        }
    }
}
