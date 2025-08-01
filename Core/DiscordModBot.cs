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
using System.Runtime.Loader;

namespace ModBot.Core
{
    /// <summary>General program entry and handler.</summary>
    public class DiscordModBot
    {
        /// <summary>The relevant WarningCommands instance.</summary>
        public static WarningCommands WarningCommandHandler;

        /// <summary>The relevant TempBanManager instance.</summary>
        public static TempBanManager TempBanHandler;

        /// <summary>The internal database handler.</summary>
        public static ModBotDatabaseHandler DatabaseHandler;

        /// <summary>List of bot commander user IDs.</summary>
        public static HashSet<ulong> BotCommanders;

        /// <summary>The special-roles command handler.</summary>
        public static SpecialRoleCommands SpecialRoleCommandHandler = new();

        public static class Internal
        {
            public static volatile string LastSpamMessage;

            public static long LastSpamTime = 0;

            public static ulong LastSpamID = 0;
        }

        /// <summary>Gets the config for a specified guild.</summary>
        public static GuildConfig GetConfig(ulong guildId)
        {
            return DatabaseHandler.GetDatabase(guildId).Config;
        }

        /// <summary>Software entry point - starts the bot.</summary>
        static void Main(string[] args)
        {
            AssemblyLoadContext.Default.Unloading += (context) =>
            {
                DatabaseHandler.Shutdown();
            };
            AppDomain.CurrentDomain.ProcessExit += (obj, e) =>
            {
                DatabaseHandler.Shutdown();
            };
            CancellationTokenSource cancel = new();
            Task consoleThread = Task.Run(RunConsole, cancel.Token);
            DiscordBotBaseHelper.StartBotHandler(args, new DiscordBotConfig()
            {
                CommandPrefix = null,
                CacheSize = 50,
                EnsureCaching = true,
                AllowDMs = false,
                UnknownCommandMessage = null,
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMessages | GatewayIntents.GuildMembers | GatewayIntents.GuildMessageReactions | GatewayIntents.MessageContent,
                ShouldIgnoreBot = (message) =>
                {
                    if (message.Channel is not IGuildChannel channel)
                    {
                        return true;
                    }
                    GuildConfig config = GetConfig(channel.Guild.Id);
                    return !config.AllowBotCommands;
                },
                Initialize = (bot) =>
                {
                    LoadConfig(bot.ConfigFile);
                    InitCommands(bot);
                    DatabaseHandler?.Shutdown();
                    DatabaseHandler = new ModBotDatabaseHandler();
                    TempBanHandler = new TempBanManager();
                    bot.Client.Ready += async () =>
                    {
                        DatabaseHandler.Init(bot);
                        await bot.Client.SetGameAsync("Guardian Over The People");
                        // Check for any missed users
                        try
                        {
                            int count = 0, modified = 0;
                            foreach (SocketGuild guild in bot.Client.Guilds)
                            {
                                await guild.GetUsersAsync().ForEachAwaitAsync(users =>
                                {
                                    foreach (IGuildUser user in users)
                                    {
                                        count++;
                                        WarnableUser warnUser = WarningUtilities.GetWarnableUser(guild.Id, user.Id);
                                        if (warnUser.LastKnownUsername == null)
                                        {
                                            warnUser.SeenUsername(NameUtilities.Username(user), out _);
                                            modified++;
                                        }
                                    }
                                    return Task.CompletedTask;
                                });
                            }
                            Console.WriteLine($"Scanned {count} users and updated {modified}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"User list scan error {ex}");
                        }
                        try
                        {
                            TempBanHandler.Scan();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Ban-handler scan error {ex}");
                        }
                    };
                    bot.Client.ReactionAdded += (message, channel, reaction) =>
                    {
                        if (channel.GetOrDownloadAsync().Result is not SocketGuildChannel guildChannel)
                        {
                            return Task.CompletedTask;
                        }
                        try
                        {
                            GuildConfig config = GetConfig(guildChannel.Guild.Id);
                            if (config.ReactRoles.TryGetValue(message.Id, out GuildConfig.ReactRoleData reactData))
                            {
                                if (reactData.ReactToRole.TryGetValue(reaction.Emote.Name.ToLowerFast(), out ulong role))
                                {
                                    SocketGuildUser user = guildChannel.GetUser(reaction.UserId);
                                    user?.AddRoleAsync(role).Wait();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Reaction adding error {ex}");
                        }
                        return Task.CompletedTask;
                    };
                    bot.Client.ReactionRemoved += (message, channel, reaction) =>
                    {
                        if (channel.GetOrDownloadAsync().Result is not SocketGuildChannel guildChannel)
                        {
                            return Task.CompletedTask;
                        }
                        try
                        {
                            GuildConfig config = GetConfig(guildChannel.Guild.Id);
                            if (config.ReactRoles.TryGetValue(message.Id, out GuildConfig.ReactRoleData reactData))
                            {
                                if (reactData.ReactToRole.TryGetValue(reaction.Emote.Name.ToLowerFast(), out ulong role))
                                {
                                    SocketGuildUser user = guildChannel.GetUser(reaction.UserId);
                                    user?.RemoveRoleAsync(role).Wait();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Reaction adding error {ex}");
                        }
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
                        if (message.Channel is not SocketGuildChannel)
                        {
                            return;
                        }
                        // TODO: helper ping on first post (never posted on the discord guild prior to 10 minutes ago,
                        // -> never posted in any other channel, pings a helper/dev/bot,
                        // -> and nobody else has posted in that channel since their first post) reaction,
                        // -> and if not in a help lobby redirect to help lobby (in same response)
                        SocketGuild guild = (message.Channel as SocketGuildChannel).Guild;
                        GuildConfig config = GetConfig(guild.Id);
                        SocketGuildUser author = message.Author as SocketGuildUser;
                        TrackUsernameFor(author, guild);
                        // TODO: General post-spam detection (rapid posts, many pings, etc)
                        NameUtilities.AsciiNameRuleCheck(message, author);
                        bool shouldSpamCheck = config.AutomuteSpambots && !author.IsBot && !author.IsWebhook && !author.Roles.Any(r => config.NonSpambotRoles.Contains(r.Id));
                        void timeout()
                        {
                            try
                            {
                                author.SetTimeOutAsync(TimeSpan.FromMinutes(2)).Wait();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to timeout user {author.Id} in {guild.Id}: {ex}");
                            }
                        }
                        void domute(string reason)
                        {
                            Internal.LastSpamMessage = message.Content;
                            Internal.LastSpamTime = Environment.TickCount64;
                            Internal.LastSpamID = author.Id;
                            WarnableUser warnable = WarningUtilities.GetWarnableUser(guild.Id, author.Id);
                            if (!warnable.IsMuted)
                            {
                                warnable.IsMuted = true;
                                warnable.Save();
                                if (config.MuteRole.HasValue)
                                {
                                    IRole role = guild.GetRole(config.MuteRole.Value);
                                    if (role is null)
                                    {
                                        Console.WriteLine($"Failed To Auto-Mute in {guild.Id}: no muted role found.");
                                        return;
                                    }
                                    try
                                    {
                                        author.AddRoleAsync(role).Wait();
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Failed To add auto-mute role in {guild.Id}: {ex}");
                                    }
                                }
                                IUserMessage automutenotice = message.Channel.SendMessageAsync($"User <@{author.Id}> has been muted automatically by spambot-detection.\n{config.AttentionNotice}", embed: new EmbedBuilder().WithTitle("Spambot Auto-Mute Notice").WithColor(255, 128, 0)
                                    .WithDescription($"This mute was applied because: {reason}. If this is in error, contact a moderator in the incident handling channel.").Build()).Result;
                                Warning warning = new() { GivenTo = author.Id, GivenBy = guild.CurrentUser.Id, TimeGiven = DateTimeOffset.UtcNow, Level = WarningLevel.AUTO, Reason = $"Auto-muted by spambot detection.", Link = UserCommands.LinkToMessage(automutenotice) };
                                warnable.AddWarning(warning);
                                warnable.Save();
                                SocketThreadChannel thread = WarningCommands.GenerateThreadFor(config, guild, author, warnable);
                                if (thread is not null)
                                {
                                    thread.SendMessageAsync(embed: new EmbedBuilder().WithTitle("SpamBot Auto-Mute Notice").WithColor(255, 128, 0)
                                        .WithDescription($"You are muted because: {reason}. If this is in error, ask a moderator to unmute you.").Build(), text: $"<@{author.Id}>").Wait();
                                }
                                else
                                {
                                    ModBotLoggers.SendEmbedToAllFor(guild, config.IncidentChannel, new EmbedBuilder().WithTitle("SpamBot Auto-Mute Notice").WithColor(255, 128, 0)
                                        .WithDescription($"You are muted because: {reason}. If this is in error, ask a moderator to unmute you.").Build(), $"<@{author.Id}>");
                                }
                            }
                        }
                        if (shouldSpamCheck && LooksSpambotty(message.Content, author.Id))
                        {
                            timeout();
                            if (Environment.TickCount64 < Internal.LastSpamTime + 60 * 1000 && Internal.LastSpamMessage == message.Content && Internal.LastSpamID == author.Id)
                            {
                                try
                                {
                                    message.DeleteAsync().Wait();
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Failed to delete spam duplicate message from {author.Id} in {guild.Id}: {ex}");
                                }
                            }
                            domute("message resembles known spambot messages");
                        }
                        else if (shouldSpamCheck)
                        {
                            GuildSpamMonitor monitor = SpamMonitorByGuild.GetOrCreate(guild.Id, () => new GuildSpamMonitor(new(), []));
                            lock (monitor.Locker)
                            {
                                if (monitor.LastMessages.Any())
                                {
                                    IUserMessage prev = monitor.LastMessages.Peek();
                                    if (prev.Author.Id != message.Author.Id || Math.Abs(prev.Timestamp.Subtract(DateTimeOffset.UtcNow).TotalSeconds) > 20 || message.Content != prev.Content || message.Attachments.Any())
                                    {
                                        monitor.LastMessages.Clear();
                                    }
                                }
                                if (monitor.LastMessages.Count > 2)
                                {
                                    timeout();
                                    domute("posted the same message rapidly 4 or more times. Earlier copies of the message will be deleted.");
                                    List<Task> deletes = [];
                                    foreach (IUserMessage prev in monitor.LastMessages)
                                    {
                                        try
                                        {
                                            deletes.Add(prev.DeleteAsync());
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Failed to delete spam message from {author.Id} in {guild.Id}: {ex}");
                                        }
                                    }
                                    monitor.LastMessages.Clear();
                                    Task.WaitAll([.. deletes]);
                                }
                                else
                                {
                                    monitor.LastMessages.Enqueue(message);
                                }
                            }
                        }
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

        /// <summary>Runs a simple console monitoring thread to allow some basic console commands.</summary>
        public static async void RunConsole()
        {
            while (true)
            {
                string line = await Console.In.ReadLineAsync();
                if (line is null)
                {
                    return;
                }
                string[] split = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (split.IsEmpty())
                {
                    continue;
                }
                switch (split[0])
                {
                    case "stop":
                        {
                            DatabaseHandler.Shutdown();
                            Environment.Exit(0);
                        }
                        break;
                    default:
                        Console.WriteLine("Unknown command.");
                        break;
                }
            }
        }

        /// <summary>Tracks a username change for a user, when a message is sent or when they join.</summary>
        public static void TrackUsernameFor(IUser user, SocketGuild guild)
        {
            GuildConfig config = GetConfig(guild.Id);
            string authorName = user is null || user.Username is null ? "" : user.Username;
            if (WarningUtilities.GetWarnableUser(guild.Id, user.Id).SeenUsername(authorName, out string oldName) && config.NameChangeNotifChannel.Any())
            {
                if (oldName == $"{authorName}#0000")
                {
                    return;
                }
                EmbedBuilder embed = new EmbedBuilder().WithTitle("User Changed Username").WithColor(0, 255, 255);
                if (oldName is not null)
                {
                    embed.AddField("Old Username", $"`{UserCommands.EscapeUserInput(oldName)}`");
                }
                embed.AddField("New Username", $"`{UserCommands.EscapeUserInput(authorName)}`");
                embed.Description = $"User <@{user.Id}> changed their base username.";
                ModBotLoggers.SendEmbedToAllFor(guild, config.NameChangeNotifChannel, embed.Build());
            }
        }

        private static List<ulong> GetIDList(FDSSection section, string key)
        {
            return section.GetDataList(key)?.Select(d => d.AsULong.Value)?.ToList() ?? [];
        }

        /// <summary>Load the config file to static field.</summary>
        public static void LoadConfig(FDSSection configFile)
        {
            BotCommanders = [.. GetIDList(configFile, "bot_commanders")];
        }

        /// <summary>initialize all user commands on a Discord bot.</summary>
        public static void InitCommands(DiscordBot bot)
        {
            WarningCommandHandler = new WarningCommands() { Bot = bot };
            InfoCommands infoCommands = new() { Bot = bot };
            AdminCommands adminCommands = new() { Bot = bot };
            CoreCommands coreCommands = new(IsBotCommander) { Bot = bot };
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
            bot.RegisterCommand(WarningCommandHandler.CMD_Unban, "unban");
            bot.RegisterCommand(WarningCommandHandler.CMD_FindSimilarNames, "findsimilarnames");
            bot.RegisterCommand(WarningCommandHandler.CMD_TempBan, "tempban", "tmpban", "ban", "bantmp", "bantemp", "temporaryban", "bantemporary");
            bot.RegisterCommand(WarningCommandHandler.CMD_Timeout, "timeout", "time_out", "tempmute", "temptimeout");
            bot.RegisterCommand(SpecialRoleCommandHandler.CMD_ClearSpecialRoles, "clearspecialroles", "removeallspecialroles", "specialroleclear", "specialroleremovall");
            // Admin
            bot.RegisterCommand(adminCommands.CMD_AdminConfigure, "admin-configure");
            bot.RegisterCommand(adminCommands.CMD_Sweep, "sweep");
            bot.RegisterCommand(adminCommands.CMD_TestName, "testname");
            bot.RegisterCommand(adminCommands.CMD_FillHistory, "fillhistory");
            bot.RegisterCommand(coreCommands.CMD_Restart, "restart");
        }

        /// <summary>Returns whether a Discord user is a moderator (via role check with role set in config).</summary>
        public static bool IsModerator(SocketGuildUser user)
        {
            GuildConfig config = GetConfig(user.Guild.Id);
            if (config.ModeratorRoles.IsEmpty())
            {
                return false;
            }
            return user.Roles.Any((role) => config.ModeratorRoles.Contains(role.Id));
        }

        /// <summary>Returns whether a Discord user is a bot commander (via config check).</summary>
        public static bool IsBotCommander(IUser user)
        {
            return BotCommanders.Contains(user.Id);
        }

        public record class GuildSpamMonitor(LockObject Locker, Queue<IUserMessage> LastMessages);

        public static ConcurrentDictionary<ulong, GuildSpamMonitor> SpamMonitorByGuild = new();

        /// <summary>Returns whether this message text looks like it might be a spam-bot message.</summary>
        public static bool LooksSpambotty(string message, ulong id)
        {
            if (Internal.LastSpamMessage is not null && message == Internal.LastSpamMessage && id == Internal.LastSpamID && Environment.TickCount64 < Internal.LastSpamTime + 60 * 1000)
            {
                return true;
            }
            message = message.ToLowerFast().Replace('\r', '\n').Replace("\n", "")
                .Replace("||", "") // Some bots have been spamming "|||||||||" (times a lot) to bypass filters, so this replace will exclude that from spam detection
                .Replace("�", "").Replace("'", ""); // Funky unicode and trickery
            while (message.Contains("  "))
            {
                message = message.Replace("  ", " "); // double spaces
            }
            if (message.Contains("drop a message lets get started by asking (how)") // all of these just to catch variants of that one crypto spambot going around a lot
                || message.Contains("only interested people should apply, by asking (how)")
                || message.Contains("only interested people should contact me (how)")
                || message.Contains("send me a dm! ask me (how)")
                || message.Contains("ask me (how) or via telegram")
                || message.Contains("get started by asking \"how\" on whatsapp at")
                || message.Contains("only interested people should message me")
                || message.Contains("only interested people should massage me")
                || message.Contains("only interested people should send a friend request")
                || message.Contains("teach 10 interested people on how to earn")
                || message.Contains("teach 10 interested people on how to start earning")
                || message.Contains("teach anyone interested on how to earn $100k within a week but you will reimburse")
                || message.Contains("help 20 people earn $100k in just")
                || message.Contains("but youll promise to pay me 10% of the profit")
                || message.Contains("i'll help anyone interested on how to earn 10k in just 72 hours")
                )
            {
                return true;
            }
            if (message.Length > 500) // Most seen spambot messages have been fairly short
            {
                return false;
            }
            if (!message.Contains("http://") && !message.Contains("https://")) // obviously only messages with links qualify for the normal possible spam bot detection
            {
                return false;
            }
            if (message.Contains("nitro") || message.Contains("trade offer") // the obvious ones
                || message.Contains("[steamcommunity.com") // Fake steam link spambots
                || message.Contains("who is first? :)") || message.Contains("take it guys :)") // seen in the wild from a few bots
                || message.Contains("adobe full espanol gratis 2024") // this one specific bot keeps coming back with this one dumb link wtf
                )
            {
                return true;
            }
            bool containsInvite = message.Contains("discord.gg/") || message.Contains("discord.com/invite/");
            if (message.Contains("@everyone") && containsInvite) // A lot of recent bots only have this pair in common
            {
                return true;
            }
            if (message.Contains("@everyone") && message.Contains("](http")) // pinging everyone with a disguised link
            {
                return true;
            }
            if (message.Trim().StartsWith("@everyone") && message.Replace('\r', '\n').Split('\n').Last(s => !string.IsNullOrWhiteSpace(s)).Trim().StartsWith("https://"))
            {
                return true;
            }
            return false;
        }
    }
}
