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
            AssemblyLoadContext.Default.Unloading += (context) =>
            {
                DatabaseHandler.Shutdown();
            };
            AppDomain.CurrentDomain.ProcessExit += (obj, e) =>
            {
                DatabaseHandler.Shutdown();
            };
            CancellationTokenSource cancel = new CancellationTokenSource();
            Task consoleThread = Task.Run(RunConsole, cancel.Token);
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
                    if (DatabaseHandler != null)
                    {
                        DatabaseHandler.Shutdown();
                    }
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

        /// <summary>Runs a simple console monitoring thread to allow some basic console commands.</summary>
        public static async void RunConsole()
        {
            while (true)
            {
                string line = await Console.In.ReadLineAsync();
                if (line == null)
                {
                    return;
                }
                string[] split = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                switch (split[0])
                {
                    case "stop":
                        {
                            DatabaseHandler.Shutdown();
                            Environment.Exit(0);
                        }
                        break;
                    case "count_legacy":
                        {
                            foreach (ModBotDatabaseHandler.Guild guild in DatabaseHandler.Guilds.Values)
                            {
                                Console.WriteLine($"Guild {guild.ID} has {guild.Users.Count()} valid users and {guild.Users_Outdated.Count()} legacy users");
                            }
                        }
                        break;
                    default:
                        Console.WriteLine("Unknown command.");
                        break;
                }
            }
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
