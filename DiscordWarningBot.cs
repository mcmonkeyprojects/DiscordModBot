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

namespace WarningBot
{
    /// <summary>
    /// Discord bot for handling helper-given warnings.
    /// </summary>
    public class DiscordWarningBot
    {
        /// <summary>
        /// Configuration folder path.
        /// </summary>
        public const string CONFIG_FOLDER = "./config/";

        /// <summary>
        /// Bot token file path.
        /// </summary>
        public const string TOKEN_FILE = CONFIG_FOLDER + "token.txt";

        /// <summary>
        /// Configuration file path.
        /// </summary>
        public const string CONFIG_FILE = CONFIG_FOLDER + "config.fds";

        /// <summary>
        /// Prefix for when the bot successfully handles user input.
        /// </summary>
        public const string SUCCESS_PREFIX = "+ WarningBot: ";

        /// <summary>
        /// Prefix for when the bot refuses user input.
        /// </summary>
        public const string REFUSAL_PREFIX = "- WarningBot: ";

        /// <summary>
        /// Bot token, read from config data.
        /// </summary>
        public static readonly string TOKEN = File.ReadAllText(TOKEN_FILE).Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Replace(" ", "");

        /// <summary>
        /// The configuration file section.
        /// </summary>
        public FDSSection ConfigFile;

        /// <summary>
        /// Internal Discord API bot Client handler.
        /// </summary>
        public DiscordSocketClient Client;

        /// <summary>
        /// Bot command response handler.
        /// </summary>
        public void Respond(SocketMessage message)
        {
            string[] messageDataSplit = message.Content.Split(' ');
            StringBuilder resultBuilder = new StringBuilder(message.Content.Length);
            List<string> cmds = new List<string>();
            for (int i = 0; i < messageDataSplit.Length; i++)
            {
                if (messageDataSplit[i].Contains("<") && messageDataSplit[i].Contains(">"))
                {
                    continue;
                }
                resultBuilder.Append(messageDataSplit[i]).Append(" ");
                if (messageDataSplit[i].Length > 0)
                {
                    cmds.Add(messageDataSplit[i]);
                }
            }
            if (cmds.Count == 0)
            {
                Console.WriteLine("Empty input, ignoring: " + message.Author.Username);
                return;
            }
            string fullMessageCleaned = resultBuilder.ToString();
            Console.WriteLine("Found input from: (" + message.Author.Username + "), in channel: " + message.Channel.Name + ": " + fullMessageCleaned);
            string commandNameLowered = cmds[0].ToLowerInvariant();
            cmds.RemoveAt(0);
            if (UserCommands.TryGetValue(commandNameLowered, out Action<string[], SocketMessage> acto))
            {
                acto.Invoke(cmds.ToArray(), message);
            }
            else
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Unknown command. Consider the __**help**__ command?").Wait();
            }
        }

        /// <summary>
        /// All valid user commands in a map of typable command name -> command method.
        /// </summary>
        public readonly Dictionary<string, Action<string[], SocketMessage>> UserCommands = new Dictionary<string, Action<string[], SocketMessage>>(1024);

        /// <summary>
        /// Simple output string for general public commands.
        /// </summary>
        public static string CmdsHelp = 
                "`help` shows help output, `hello` shows a source code link, `listwarnings` views your own warnings (if any), "
                + "...";

        /// <summary>
        /// Simple output string for helper commands.
        /// </summary>
        public static string CmdsHelperHelp =
                "`warn` issues a warning to a user - in format `warn @User [level] [reason...]`, `listwarnings` lists warnings for any user - in format `listwarnings @User`, "
                + "...";

        /// <summary>
        /// Simple output string for admin commands.
        /// </summary>
        public static string CmdsAdminHelp =
                "`restart` restarts the bot, "
                + "...";

        /// <summary>
        /// User command to get help (shows a list of valid bot commands).
        /// </summary>
        void CMD_Help(string[] cmds, SocketMessage message)
        {
            string outputMessage = "Available Commands: " + CmdsHelp;
            if (IsHelper(message.Author as SocketGuildUser))
            {
                outputMessage += "\nAvailable helper commands: " + CmdsHelperHelp;
            }
            if (IsBotCommander(message.Author as SocketGuildUser))
            {
                outputMessage += "\nAvailable admin commands: " + CmdsAdminHelp;
            }
            message.Channel.SendMessageAsync(SUCCESS_PREFIX + outputMessage).Wait();
        }

        /// <summary>
        /// User command to say 'hello' and get a source link.
        /// </summary>
        void CMD_Hello(string[] cmds, SocketMessage message)
        {
            message.Channel.SendMessageAsync(SUCCESS_PREFIX + "Hi! I'm a bot! Find my source code at https://github.com/mcmonkeyprojects/DiscordWarningBot").Wait();
        }

        /// <summary>
        /// A mapping of typable names to warning level enumeration values.
        /// </summary>
        public static Dictionary<string, WarningLevel> LevelsTypable = new Dictionary<string, WarningLevel>()
        {
            { "minor", WarningLevel.MINOR },
            { "normal", WarningLevel.NORMAL },
            { "serious", WarningLevel.SERIOUS },
            { "instant_mute", WarningLevel.INSTANT_MUTE },
            { "instantmute", WarningLevel.INSTANT_MUTE },
            { "instant", WarningLevel.INSTANT_MUTE },
            { "mute", WarningLevel.INSTANT_MUTE }
        };

        /// <summary>
        /// User command to give a warning to a user.
        /// </summary>
        void CMD_Warn(string[] cmds, SocketMessage message)
        {
            if (!IsHelper(message.Author as SocketGuildUser))
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "You're not allowed to do that.").Wait();
                return;
            }
            if (message.MentionedUsers.Count() != 2)
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Warnings must only `@` mention this bot and the user to be warned.").Wait();
                return;
            }
            SocketUser userToWarn = message.MentionedUsers.FirstOrDefault((su) => su.Id != Client.CurrentUser.Id);
            if (userToWarn == null)
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Something went wrong - user mention not valid?").Wait();
                return;
            }
            if (cmds.Length == 0 || !LevelsTypable.TryGetValue(cmds[0].ToLowerInvariant(), out WarningLevel level))
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Unknown level. Valid levels: `minor`, `normal`, `serious`, or `instant_mute`.").Wait();
                return;
            }
            Warning warning = new Warning() { GivenTo = userToWarn.Id, GivenBy = message.Author.Id, TimeGiven = DateTimeOffset.UtcNow, Level = level };
            warning.Reason = string.Join(" ", cmds.Skip(1));
            Discord.Rest.RestUserMessage sentMessage = message.Channel.SendMessageAsync(SUCCESS_PREFIX + "Warning from <@" + message.Author.Id + "> to <@" + userToWarn.Id + "> recorded.").Result;
            warning.Link = LinkToMessage(sentMessage);
            Warn(userToWarn.Id, warning);
            PossibleMute(userToWarn as SocketGuildUser, message.Channel, level);
        }

        /// <summary>
        /// User command to list user warnings.
        /// </summary>
        void CMD_ListWarnings(string[] cmds, SocketMessage message)
        {
            SocketUser userToList = message.Author;
            if (IsHelper(message.Author as SocketGuildUser) && message.MentionedUsers.Count() > 1)
            {
                if (message.MentionedUsers.Count() != 2)
                {
                    message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Warnings must only `@` mention this bot and the user to be warned.").Wait();
                    return;
                }
                userToList = message.MentionedUsers.FirstOrDefault((su) => su.Id != Client.CurrentUser.Id);
                if (userToList == null)
                {
                    message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Something went wrong - user mention not valid?").Wait();
                    return;
                }
            }
            WarnableUser user = GetWarnableUser(userToList.Id);
            StringBuilder warnStringOutput = new StringBuilder();
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            int id = 0;
            foreach (Warning warned in user.GetWarnings().OrderByDescending(w => (int)w.Level).Take(6))
            {
                if (id == 5)
                {
                    warnStringOutput.Append("... And more warnings (not able to list all).");
                    break;
                }
                id++;
                SocketUser giver = Client.GetUser(warned.GivenBy);
                string giverLabel = (giver == null) ? ("DiscordID:" + warned.GivenBy) : (giver.Username + "#" + giver.Discriminator);
                string reason = (warned.Reason.Length > 250) ? (warned.Reason.Substring(0, 250) + "(... trimmed ...)") : warned.Reason;
                reason = reason.Replace('\\', '/').Replace('`', '\'');
                warnStringOutput.Append("... " + warned.Level + " warning given at " + StringConversionHelper.DateTimeToString(warned.TimeGiven, false)
                    + " by " + giverLabel + " with reason: `" + reason + "` ... for detail see: " + warned.Link + "\n");
            }
            if (warnStringOutput.Length == 0)
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "User " + userToList.Username + "#" + userToList.Discriminator + " does not have any warnings logged.").Wait();
            }
            else
            {
                message.Channel.SendMessageAsync(SUCCESS_PREFIX + "User " + userToList.Username + "#" + userToList.Discriminator + " has the following warnings logged:\n" + warnStringOutput).Wait();
            }
        }

        /// <summary>
        /// Generates a link to a Discord message.
        /// </summary>
        public string LinkToMessage(Discord.Rest.RestMessage message)
        {
            return "https://discordapp.com/channels/" + (message.Channel as SocketGuildChannel).Guild.Id + "/" + message.Channel.Id + "/" + message.Id;
        }

        /// <summary>
        /// Calculates whether a user needs to be muted following a new warning, and applies the mute if needed.
        /// Logic:
        /// Any warning within the past 30 days counts towards the total, where more recent is more significant.
        /// INSTANT_MUTE => When given, muted instantly. For later calculations, same as SERIOUS.
        /// SERIOUS => 0-7 days = 2 points, 7-14 days = 1.5 points, 14-30 days = 1 point.
        /// NORMAL => 0-7 days = 1.5 points, 7-14 days = 1 point, 14-30 days = 0.75 points.
        /// 4 points or more = muted.
        /// </summary>
        void PossibleMute(SocketGuildUser user, ISocketMessageChannel channel, WarningLevel newLevel)
        {
            if (IsMuted(user))
            {
                return;
            }
            bool needsMute = newLevel == WarningLevel.INSTANT_MUTE;
            if (newLevel == WarningLevel.NORMAL || newLevel == WarningLevel.SERIOUS)
            {
                double warningNeed = 0.0;
                foreach (Warning oldWarn in GetWarnableUser(user.Id).GetWarnings())
                {
                    TimeSpan relative = DateTimeOffset.UtcNow.Subtract(oldWarn.TimeGiven);
                    if (relative.TotalDays > 30)
                    {
                        break;
                    }
                    if (oldWarn.Level == WarningLevel.NORMAL)
                    {
                        if (relative.TotalDays <= 7)
                        {
                            warningNeed += 1.5;
                        }
                        else if (relative.TotalDays <= 14)
                        {
                            warningNeed += 1.0;
                        }
                        else
                        {
                            warningNeed += 0.75;
                        }
                    }
                    else if (oldWarn.Level == WarningLevel.SERIOUS || oldWarn.Level == WarningLevel.INSTANT_MUTE)
                    {
                        if (relative.TotalDays <= 7)
                        {
                            warningNeed += 2.0;
                        }
                        else if (relative.TotalDays <= 14)
                        {
                            warningNeed += 1.5;
                        }
                        else
                        {
                            warningNeed += 1;
                        }
                    }
                }
                if (warningNeed >= 4.0)
                {
                    needsMute = true;
                }
            }
            if (needsMute)
            {
                SocketRole role = user.Guild.Roles.FirstOrDefault((r) => r.Name.ToLowerInvariant() == MuteRoleName);
                if (role == null)
                {
                    channel.SendMessageAsync(REFUSAL_PREFIX + "Cannot apply mute: no muted role found.").Wait();
                    return;
                }
                user.AddRoleAsync(role).Wait();
                channel.SendMessageAsync(SUCCESS_PREFIX + "User <@" + user.Id + "> has been muted automatically by the warning system."
                    + " You may not speak except in the incident handling channel."
                    + " This mute lasts until an administrator removes it, which may in some cases take a while. " + AttentionNotice).Wait();
            }
        }

        /// <summary>
        /// Returns whether a Discord user is current muted (checks via configuration value for the mute role name).
        /// </summary>
        public bool IsMuted(SocketGuildUser user)
        {
            return user.Roles.Any((role) => role.Name.ToLowerInvariant() == MuteRoleName);
        }

        public static Object WarnLock = new Object();

        /// <summary>
        /// Warns a user (by Discord ID and pre-completed warning object).
        /// </summary>
        public void Warn(ulong id, Warning warn)
        {
            lock (WarnLock)
            {
                GetWarnableUser(id).AddWarning(warn);
            }
        }

        /// <summary>
        /// Gets the <see cref="WarnableUser"/> object for a Discord user (by Discord ID).
        /// </summary>
        public WarnableUser GetWarnableUser(ulong id)
        {
            string fname = "./warnings/" + id + ".fds";
            return new WarnableUser() { UserID = id, WarningFileSection = File.Exists(fname) ? FDSUtility.ReadFile(fname) : new FDSSection() };
        }

        /// <summary>
        /// Configuration value: the name of the role used for helpers.
        /// </summary>
        public string HelperRoleName;

        /// <summary>
        /// Returns whether a Discord user is a helper (via role check with role set in config).
        /// </summary>
        bool IsHelper(SocketGuildUser user)
        {
            return user.Roles.Any((role) => role.Name.ToLowerInvariant() == HelperRoleName);
        }

        /// <summary>
        /// Returns whether a Discord user is a bot commander (via role check).
        /// </summary>
        bool IsBotCommander(SocketGuildUser user)
        {
            return user.Roles.Any((role) => role.Name.ToLowerInvariant() == "botcommander");
        }

        /// <summary>
        /// Bot restart user command.
        /// </summary>
        void CMD_Restart(string[] cmds, SocketMessage message)
        {
            // NOTE: This implies a one-guild bot. A multi-guild bot probably shouldn't have this "BotCommander" role-based verification.
            // But under current scale, a true-admin confirmation isn't worth the bother.
            if (!IsBotCommander(message.Author as SocketGuildUser))
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Nope! That's not for you!").Wait();
                return;
            }
            if (!File.Exists("./start.sh"))
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Nope! That's not valid for my current configuration!").Wait();
            }
            message.Channel.SendMessageAsync(SUCCESS_PREFIX + "Yes, boss. Restarting now...").Wait();
            Process.Start("bash", "./start.sh " + message.Channel.Id);
            Task.Factory.StartNew(() =>
            {
                Console.WriteLine("Shutdown start...");
                for (int i = 0; i < 15; i++)
                {
                    Console.WriteLine("T Minus " + (15 - i));
                    Task.Delay(1000).Wait();
                }
                Console.WriteLine("Shutdown!");
                Environment.Exit(0);
            });
            Client.StopAsync().Wait();
        }
        
        /// <summary>
        /// Saves the config file.
        /// </summary>
        public void SaveConfig()
        {
            lock (ConfigSaveLock)
            {
                ConfigFile.SaveToFile(CONFIG_FILE);
            }
        }

        /// <summary>
        /// Lock object for config file saving/loading.
        /// </summary>
        public static Object ConfigSaveLock = new Object();

        /// <summary>
        /// Generates default command name->method pairs.
        /// </summary>
        void DefaultCommands()
        {
            // Various
            UserCommands["help"] = CMD_Help;
            UserCommands["halp"] = CMD_Help;
            UserCommands["helps"] = CMD_Help;
            UserCommands["halps"] = CMD_Help;
            UserCommands["hel"] = CMD_Help;
            UserCommands["hal"] = CMD_Help;
            UserCommands["h"] = CMD_Help;
            UserCommands["hello"] = CMD_Hello;
            UserCommands["hi"] = CMD_Hello;
            UserCommands["hey"] = CMD_Hello;
            UserCommands["source"] = CMD_Hello;
            UserCommands["src"] = CMD_Hello;
            UserCommands["github"] = CMD_Hello;
            UserCommands["git"] = CMD_Hello;
            UserCommands["hub"] = CMD_Hello;
            // Helper and User
            UserCommands["list"] = CMD_ListWarnings;
            UserCommands["listwarn"] = CMD_ListWarnings;
            UserCommands["listwarns"] = CMD_ListWarnings;
            UserCommands["listwarning"] = CMD_ListWarnings;
            UserCommands["listwarnings"] = CMD_ListWarnings;
            UserCommands["warnlist"] = CMD_ListWarnings;
            UserCommands["warninglist"] = CMD_ListWarnings;
            UserCommands["warningslist"] = CMD_ListWarnings;
            // Helper
            UserCommands["warn"] = CMD_Warn;
            UserCommands["warning"] = CMD_Warn;
            // Admin
            UserCommands["restart"] = CMD_Restart;
        }

        /// <summary>
        /// Configuration value: what text to use to 'get attention' when a mute is given (eg. an @ mention to an admin).
        /// </summary>
        public string AttentionNotice;

        /// <summary>
        /// The name of the role given to muted users.
        /// </summary>
        public string MuteRoleName;

        /// <summary>
        /// Shuts the bot down entirely.
        /// </summary>
        public void Shutdown()
        {
            Client.StopAsync().Wait();
            Client.Dispose();
            StoppedEvent.Set();
        }

        /// <summary>
        /// Signaled when the bot is stopped.
        /// </summary>
        public ManualResetEvent StoppedEvent = new ManualResetEvent(false);

        /// <summary>
        /// Monitor object to help restart the bot as needed.
        /// </summary>
        public ConnectionMonitor BotMonitor;

        /// <summary>
        /// Initializes the bot object, connects, and runs the active loop.
        /// </summary>
        public void InitAndRun(string[] args)
        {
            Console.WriteLine("Preparing...");
            BotMonitor = new ConnectionMonitor(this);
            DefaultCommands();
            if (File.Exists(CONFIG_FILE))
            {
                lock (ConfigSaveLock)
                {
                    ConfigFile = FDSUtility.ReadFile(CONFIG_FILE);
                }
                HelperRoleName = ConfigFile.GetString("helper_role_name").ToLowerInvariant();
                MuteRoleName = ConfigFile.GetString("mute_role_name").ToLowerInvariant();
                AttentionNotice = ConfigFile.GetString("attention_notice");
            }
            Console.WriteLine("Loading Discord...");
            DiscordSocketConfig config = new DiscordSocketConfig();
            config.MessageCacheSize = 256;
            //config.LogLevel = LogSeverity.Debug;
            Client = new DiscordSocketClient(config);
            /*Client.Log += (m) =>
            {
                Console.WriteLine(m.Severity + ": " + m.Source + ": " + m.Exception + ": "  + m.Message);
                return Task.CompletedTask;
            };*/
            Client.Ready += () =>
            {
                if (BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                BotMonitor.ConnectedCurrently = true;
                Client.SetGameAsync("Guardian Over The People").Wait();
                if (BotMonitor.ConnectedOnce)
                {
                    return Task.CompletedTask;
                }
                Console.WriteLine("Args: " + args.Length);
                if (args.Length > 0 && ulong.TryParse(args[0], out ulong argument1))
                {
                    ISocketMessageChannel channelToNotify = Client.GetChannel(argument1) as ISocketMessageChannel;
                    Console.WriteLine("Restarted as per request in channel: " + channelToNotify.Name);
                    channelToNotify.SendMessageAsync(SUCCESS_PREFIX + "Connected and ready!").Wait();
                }
                BotMonitor.ConnectedOnce = true;
                return Task.CompletedTask;
            };
            Client.MessageReceived += (message) =>
            {
                if (BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                if (message.Author.Id == Client.CurrentUser.Id)
                {
                    return Task.CompletedTask;
                }
                BotMonitor.LoopsSilent = 0;
                if (message.Author.IsBot || message.Author.IsWebhook)
                {
                    return Task.CompletedTask;
                }
                if (message.Channel.Name.StartsWith("@") || !(message.Channel is SocketGuildChannel sgc))
                {
                    Console.WriteLine("Refused message from (" + message.Author.Username + "): (Invalid Channel: " + message.Channel.Name + "): " + message.Content);
                    return Task.CompletedTask;
                }
                bool mentionedMe = message.MentionedUsers.Any((su) => su.Id == Client.CurrentUser.Id);
                Console.WriteLine("Parsing message from (" + message.Author.Username + "), in channel: " + message.Channel.Name + ": " + message.Content);
                // TODO: Spam detection
                if (mentionedMe)
                {
                    try
                    {
                        Respond(message);
                    }
                    catch (Exception ex)
                    {
                        if (ex is ThreadAbortException)
                        {
                            throw;
                        }
                        Console.WriteLine("Error handling command: " + ex.ToString());
                    }
                }
                return Task.CompletedTask;
            };
            Console.WriteLine("Starting monitor...");
            BotMonitor.StartMonitorLoop();
            Console.WriteLine("Logging in to Discord...");
            Client.LoginAsync(TokenType.Bot, TOKEN).Wait();
            Console.WriteLine("Connecting to Discord...");
            Client.StartAsync().Wait();
            Console.WriteLine("Running Discord!");
            StoppedEvent.WaitOne();
        }
    }
}
