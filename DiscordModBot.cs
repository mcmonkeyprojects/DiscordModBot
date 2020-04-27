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
                    InitLoggers(bot);
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
            HelperRoleName = configFile.GetString("helper_role_name").ToLowerInvariant();
            MuteRoleName = configFile.GetString("mute_role_name").ToLowerInvariant();
            AttentionNotice = configFile.GetString("attention_notice");
            IncidentChannel = configFile.GetDataList("incidents_channel").Select(d => ObjectConversionHelper.ObjectToULong(d.Internal).Value).ToList();
            EnforceAsciiNameRule = configFile.GetBool("enforce_ascii_name_rule", EnforceAsciiNameRule).Value;
            EnforceNameStartRule = configFile.GetBool("enforce_name_start_rule", EnforceNameStartRule).Value;
            JoinNotifChannel = configFile.GetDataList("join_notif_channel")?.Select(d => ObjectConversionHelper.ObjectToULong(d.Internal).Value)?.ToList() ?? new List<ulong>();
            RoleChangeNotifChannel = configFile.GetDataList("role_change_notif_channel")?.Select(d => ObjectConversionHelper.ObjectToULong(d.Internal).Value)?.ToList() ?? new List<ulong>();
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
        /// initialize all logger events on a Discord bot.
        /// </summary>
        static void InitLoggers(DiscordBot bot)
        {
            bot.Client.UserJoined += (user) =>
            {
                if (bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                if (user.Id == bot.Client.CurrentUser.Id)
                {
                    return Task.CompletedTask;
                }
                WarnableUser warnable = WarningUtilities.GetWarnableUser(user.Guild.Id, user.Id);
                IReadOnlyCollection<SocketTextChannel> channels = user.Guild.TextChannels;
                foreach (ulong chan in JoinNotifChannel)
                {
                    IEnumerable<SocketTextChannel> possibles = channels.Where(schan => schan.Id == chan);
                    if (possibles.Any())
                    {
                        int nameCount = warnable.OldNames().Count();
                        string seenNameText = nameCount < 1 ? "" : $" User has {nameCount} previously seen name(s).";
                        string createdDateText = $"`{StringConversionHelper.DateTimeToString(user.CreatedAt, false)}` ({user.CreatedAt.Subtract(DateTimeOffset.Now).SimpleFormat(true)})";
                        string message = $"User <@{user.Id}> (name: `{NameUtilities.Username(user)}`, ID: `{user.Id}`) joined. User account first created {createdDateText}.{seenNameText}";
                        possibles.First().SendMessageAsync(embed: new EmbedBuilder().WithTitle("User Join").WithDescription(message).Build()).Wait();
                    }
                }
                if (!warnable.GetWarnings().Any())
                {
                    Console.WriteLine($"Pay no mind to user-join: {user.Id} to {user.Guild.Id} ({user.Guild.Name})");
                    return Task.CompletedTask;
                }
                if (warnable.IsMuted)
                {
                    SocketRole role = user.Guild.Roles.FirstOrDefault((r) => r.Name.ToLowerInvariant() == MuteRoleName);
                    if (role == null)
                    {
                        Console.WriteLine("Cannot apply mute: no muted role found.");
                    }
                    else
                    {
                        user.AddRoleAsync(role).Wait();
                    }
                }
                foreach (ulong chan in IncidentChannel)
                {
                    IEnumerable<SocketTextChannel> possibles = channels.Where(schan => schan.Id == chan);
                    if (possibles.Any())
                    {
                        string message = $"User <@{ user.Id}> (`{NameUtilities.Username(user)}`) just joined, and has prior warnings. Use the `listwarnings` command to see details.";
                        possibles.First().SendMessageAsync(embed: new EmbedBuilder().WithTitle("Warned User Join").WithDescription(message).Build()).Wait();
                        if (warnable.IsMuted)
                        {
                            possibles.First().SendMessageAsync($"<@{user.Id}>", embed: new EmbedBuilder().WithTitle("Automatic Mute Applied").WithDescription("You have been automatically muted by the system due to being muted and then rejoining the Discord."
                                + " You may discuss the situation in this channel only, until a moderator unmutes you.").Build()).Wait();
                        }
                        return Task.CompletedTask;
                    }
                }
                Console.WriteLine("Failed to warn of dangerous user-join: " + user.Id + " to " + user.Guild.Id + "(" + user.Guild.Name + ")");
                return Task.CompletedTask;
            };
            bot.Client.UserLeft += (user) =>
            {
                if (bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                if (user.Id == bot.Client.CurrentUser.Id)
                {
                    return Task.CompletedTask;
                }
                IReadOnlyCollection<SocketTextChannel> channels = user.Guild.TextChannels;
                foreach (ulong chan in JoinNotifChannel)
                {
                    IEnumerable<SocketTextChannel> possibles = channels.Where(schan => schan.Id == chan);
                    if (possibles.Any())
                    {
                        string message = $"User <@{user.Id}> (name: `{NameUtilities.Username(user)}`, ID: `{user.Id}`) left.";
                        possibles.First().SendMessageAsync(embed: new EmbedBuilder().WithTitle("User Left").WithDescription(message).Build()).Wait();
                    }
                }
                return Task.CompletedTask;
            };
            bot.Client.MessageUpdated += (cache, message, channel) =>
            {
                if (bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                if (message.Author.Id == bot.Client.CurrentUser.Id)
                {
                    return Task.CompletedTask;
                }
                if (cache.HasValue && cache.Value.Content == message.Content)
                {
                    // Its a reaction/embed-load/similar, ignore it.
                    return Task.CompletedTask;
                }
                string originalText = cache.HasValue ? UserCommands.EscapeUserInput(cache.Value.Content) : "(not cached)";
                string newText = UserCommands.EscapeUserInput(message.Content);
                int longerLength = Math.Max(originalText.Length, newText.Length);
                int firstDifference = StringConversionHelper.FindFirstDifference(originalText, newText);
                int lastDifference = longerLength - StringConversionHelper.FindFirstDifference(originalText.ReverseFast(), newText.ReverseFast());
                if (firstDifference == -1 || lastDifference == -1)
                {
                    // Shouldn't be possible.
                    return Task.CompletedTask;
                }
                originalText = TrimForDifferencing(originalText, 700, firstDifference, lastDifference, longerLength);
                newText = TrimForDifferencing(newText, 900, firstDifference, lastDifference, longerLength);
                LogChannelActivity(channel.Id, $"+> Message from `{NameUtilities.Username(message.Author)}` (`{message.Author.Id}`) **edited** in <#{channel.Id}>:\n{originalText} Became:\n{newText}");
                return Task.CompletedTask;
            };
            bot.Client.MessageDeleted += (cache, channel) =>
            {
                if (bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                if (cache.HasValue && cache.Value.Author.Id == bot.Client.CurrentUser.Id)
                {
                    return Task.CompletedTask;
                }
                string originalText = cache.HasValue ? UserCommands.EscapeUserInput(cache.Value.Content) : "(not cached)";
                string author = cache.HasValue ? $"`{NameUtilities.Username(cache.Value.Author)}` (`{cache.Value.Author.Id}`)" : "(unknown)";
                LogChannelActivity(channel.Id, $"+> Message from {author} **deleted** in <#{channel.Id}>: `{originalText}`");
                return Task.CompletedTask;
            };
            bot.Client.GuildMemberUpdated += (oldUser, newUser) =>
            {
                if (bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                if (newUser.Id == bot.Client.CurrentUser.Id)
                {
                    return Task.CompletedTask;
                }
                bool lostRoles = oldUser.Roles.Any(r => !newUser.Roles.Contains(r));
                bool gainedRoles = newUser.Roles.Any(r => !oldUser.Roles.Contains(r));
                if (lostRoles || gainedRoles)
                {
                    EmbedBuilder roleChangeEmbed = new EmbedBuilder().WithTitle("User Role Change").WithDescription($"User <@{newUser.Id}> had roles updated.");
                    if (lostRoles)
                    {
                        roleChangeEmbed.AddField("Roles Removed", string.Join(", ", oldUser.Roles.Where(r => !newUser.Roles.Contains(r)).Select(r => $"`{r.Name}`")));
                    }
                    if (gainedRoles)
                    {
                        roleChangeEmbed.AddField("Roles Added", string.Join(", ", oldUser.Roles.Where(r => !newUser.Roles.Contains(r)).Select(r => $"`{r.Name}`")));
                    }
                    IReadOnlyCollection<SocketTextChannel> channels = newUser.Guild.TextChannels;
                    foreach (ulong chan in RoleChangeNotifChannel)
                    {
                        IEnumerable<SocketTextChannel> possibles = channels.Where(schan => schan.Id == chan);
                        if (possibles.Any())
                        {
                            possibles.First().SendMessageAsync(embed: roleChangeEmbed.Build()).Wait();
                        }
                    }
                }
                return Task.CompletedTask;
            };
        }

        /// <summary>
        /// Utility for edit notifications.
        /// </summary>
        public static string TrimForDifferencing(string text, int cap, int firstDiff, int lastDiff, int longerLength)
        {
            int initialFirstDiff = firstDiff;
            int initialLastDiff = lastDiff;
            if (text.Length > cap)
            {
                if (firstDiff > 100)
                {
                    text = "..." + text.Substring(firstDiff - 50);
                    lastDiff -= (firstDiff - 50 - "...".Length);
                    firstDiff = 50 + "...".Length;
                }
                if (text.Length > cap)
                {
                    text = text.Substring(0, Math.Min(lastDiff + 50, (cap - 50))) + "...";
                    lastDiff = Math.Min(lastDiff, (cap - 50));
                }
            }
            if (initialFirstDiff > 10 || initialLastDiff < longerLength - 10)
            {
                string preText = firstDiff == 0 ? "" : $"`{text.Substring(0, firstDiff)}`";
                string lastText = lastDiff >= text.Length ? "" : $"`{text.Substring(lastDiff)}`";
                text = $"{preText} **__`{text.Substring(firstDiff, Math.Min(lastDiff, text.Length) - firstDiff)}`__** {lastText}";
            }
            else
            {
                text = $"`{text}`";
            }
            return text;
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

        /// <summary>
        /// Sends a log message to a log channel (if applicable).
        /// </summary>
        /// <param name="channelId">The channel where a loggable action happened.</param>
        /// <param name="message">A message to log.</param>
        public static void LogChannelActivity(ulong channelId, string message)
        {
            if (!LogChannels.TryGetValue(channelId, out ulong logChannel) && !LogChannels.TryGetValue(0, out logChannel))
            {
                return;
            }
            if (!(DiscordBotBaseHelper.CurrentBot.Client.GetChannel(logChannel) is SocketTextChannel channel))
            {
                Console.WriteLine($"Bad channel log output ID: {logChannel}");
                return;
            }
            channel.SendMessageAsync(message).Wait();
        }
    }
}
