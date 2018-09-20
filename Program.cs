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
    public class DiscordWarningBot
    {
        public Random random = new Random();

        public const string CONFIG_FOLDER = "./config/";

        public const string TOKEN_FILE = CONFIG_FOLDER + "token.txt";

        public const string CONFIG_FILE = CONFIG_FOLDER + "config.fds";

        public const string SUCCESS_PREFIX = "+ WarningBot: ";

        public const string REFUSAL_PREFIX = "- WarningBot: ";

        public static readonly string TOKEN = File.ReadAllText(TOKEN_FILE);

        public FDSSection ConfigFile;

        public Object ConfigLock = new Object();

        public DiscordSocketClient client;

        public void Respond(SocketMessage message)
        {
            string[] mesdat = message.Content.Split(' ');
            StringBuilder resBuild = new StringBuilder(message.Content.Length);
            List<string> cmds = new List<string>();
            for (int i = 0; i < mesdat.Length; i++)
            {
                if (mesdat[i].Contains("<") && mesdat[i].Contains(">"))
                {
                    continue;
                }
                resBuild.Append(mesdat[i]).Append(" ");
                if (mesdat[i].Length > 0)
                {
                    cmds.Add(mesdat[i]);
                }
            }
            if (cmds.Count == 0)
            {
                Console.WriteLine("Empty input, ignoring: " + message.Author.Username);
                return;
            }
            string fullMsg = resBuild.ToString();
            Console.WriteLine("Found input from: (" + message.Author.Username + "), in channel: " + message.Channel.Name + ": " + fullMsg);
            string lowCmd = cmds[0].ToLowerInvariant();
            cmds.RemoveAt(0);
            if (CommonCmds.TryGetValue(lowCmd, out Action<string[], SocketMessage> acto))
            {
                acto.Invoke(cmds.ToArray(), message);
            }
            else
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Unknown command. Consider the __**help**__ command?").Wait();
            }
        }

        public Dictionary<string, Action<string[], SocketMessage>> CommonCmds = new Dictionary<string, Action<string[], SocketMessage>>(1024);

        public static string CmdsHelp = 
                "`help`, `hello`, "
                + "...";

        public static string CmdsHelperHelp =
                "`warn`, " // TODO: listwarnings
                + "...";

        public static string CmdsAdminHelp =
                "`restart`, "
                + "...";

        void CMD_Help(string[] cmds, SocketMessage message)
        {
            string outputMessage = "Available Commands: " + CmdsHelp;
            if (IsHelper(message.Author))
            {
                outputMessage += "\nAvailable helper commands: " + CmdsHelperHelp;
            }
            if (IsBotCommander(message.Author))
            {
                outputMessage += "\nAvailable admin commands: " + CmdsAdminHelp;
            }
            message.Channel.SendMessageAsync(SUCCESS_PREFIX + outputMessage).Wait();
        }

        void CMD_Hello(string[] cmds, SocketMessage message)
        {
            message.Channel.SendMessageAsync(SUCCESS_PREFIX + "Hi! I'm a bot! Find my source code at (TODO: ADD LINK)").Wait();
        }

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

        void CMD_Warn(string[] cmds, SocketMessage message)
        {
            if (!IsHelper(message.Author))
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "You're not allowed to do that.").Wait();
                return;
            }
            if (message.MentionedUsers.Count() != 2)
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Warnings must only `@` mention this bot and the user to be warned.").Wait();
                return;
            }
            SocketUser suFound = message.MentionedUsers.FirstOrDefault((su) => su.Id != client.CurrentUser.Id);
            if (suFound == null)
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Something went wrong - user mention not valid?").Wait();
                return;
            }
            if (cmds.Length == 0 || !LevelsTypable.TryGetValue(cmds[0].ToLowerInvariant(), out WarningLevel level))
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Unknown level. Valid levels: `minor`, `normal`, `serious`, `instant_mute`.").Wait();
                return;
            }
            Warning warning = new Warning() { GivenTo = suFound.Id, GivenBy = message.Author.Id, TimeGiven = DateTimeOffset.UtcNow, Level = level };
            warning.Reason = string.Join(" ", cmds.Skip(1));
            Discord.Rest.RestUserMessage sentMessage = message.Channel.SendMessageAsync(SUCCESS_PREFIX + "Warning from <@" + message.Author.Id + "> to <@" + suFound.Id + "> recorded.").Result;
            warning.Link = LinkToMessage(sentMessage);
            Warn(suFound.Id, warning);
            PossibleMute(suFound as SocketGuildUser, message.Channel, level);
        }

        public string LinkToMessage(Discord.Rest.RestMessage message)
        {
            return "https://discordapp.com/channels/" + (message.Channel as SocketGuildChannel).Guild.Id + "/" + message.Channel.Id + "/" + message.Id;
        }

        void PossibleMute(SocketGuildUser user, ISocketMessageChannel channel, WarningLevel newLevel)
        {
            if (IsMuted(user))
            {
                return;
            }
            bool needsMute = newLevel == WarningLevel.INSTANT_MUTE;
            if (newLevel == WarningLevel.NORMAL || newLevel == WarningLevel.SERIOUS)
            {
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

        public bool IsMuted(SocketUser usr)
        {
            return (usr as SocketGuildUser).Roles.Any((role) => role.Name.ToLowerInvariant() == MuteRoleName);
        }

        public enum WarningLevel
        {
            AUTO = 0,
            MINOR = 1,
            NORMAL = 2,
            SERIOUS = 3,
            INSTANT_MUTE = 4
        }

        public static Object WarnLock = new Object();

        public void Warn(ulong id, Warning warn)
        {
            lock (WarnLock)
            {
                GetWarnableUser(id).AddWarning(warn);
            }
        }

        public WarnableUser GetWarnableUser(ulong id)
        {
            string fname = "./warnings/" + id + ".fds";
            return new WarnableUser() { UserID = id, BasicSection = File.Exists(fname) ? FDSUtility.ReadFile(fname) : new FDSSection() };
        }

        public class WarnableUser
        {
            public FDSSection BasicSection;

            public ulong UserID;

            public IEnumerable<Warning> GetWarnings()
            {
                long? current = BasicSection.GetLong("current_id", null);
                if (current == null)
                {
                    yield break;
                }
                long currentValue = current.Value;
                for (long i = currentValue; i > 0; i--)
                {
                    if (BasicSection.HasKey("warnings." + i))
                    {
                        yield return Warning.FromSection(BasicSection.GetSection("warnings." + i), UserID);
                    }
                }
            }

            public void AddWarning(Warning warn)
            {
                long current = BasicSection.GetLong("current_id", 0).Value + 1;
                BasicSection.Set("current_id", current);
                FDSSection newSection = new FDSSection();
                warn.SaveToSection(newSection);
                BasicSection.Set("warnings." + current, newSection);
                Save();
            }

            public void Save()
            {
                Directory.CreateDirectory("./warnings/");
                FDSUtility.SaveToFile(BasicSection, "./warnings/" + UserID + ".fds");
            }
        }

        public class Warning
        {
            public DateTimeOffset TimeGiven;

            public ulong GivenBy;

            public ulong GivenTo;

            public string Reason;

            public WarningLevel Level;

            public string Link;

            public static Warning FromSection(FDSSection section, ulong userId)
            {
                Warning warn = new Warning();
                warn.GivenTo = userId;
                warn.TimeGiven = StringConversionHelper.StringToDateTime(section.GetString("time_given", "MISSING")).Value;
                warn.GivenTo = section.GetUlong("given_by").Value;
                warn.Reason = section.GetString("reason", "MISSING");
                warn.Level = EnumHelper<WarningLevel>.ParseIgnoreCase(section.GetString("level", "MISSING"));
                warn.Link = section.GetString("link");
                return warn;
            }

            public void SaveToSection(FDSSection section)
            {
                section.Set("time_given", StringConversionHelper.DateTimeToString(TimeGiven, false));
                section.Set("given_by", GivenBy);
                section.Set("reason", Reason);
                section.Set("level", Level.ToString());
                section.Set("link", Link);
            }
        }

        public string HelperRoleName;

        bool IsHelper(SocketUser usr)
        {
            return (usr as SocketGuildUser).Roles.Any((role) => role.Name.ToLowerInvariant() == HelperRoleName);
        }

        bool IsBotCommander(SocketUser usr)
        {
            return (usr as SocketGuildUser).Roles.Any((role) => role.Name.ToLowerInvariant() == "botcommander");
        }

        void CMD_Restart(string[] cmds, SocketMessage message)
        {
            // NOTE: This implies a one-guild bot. A multi-guild bot probably shouldn't have this "BotCommander" role-based verification.
            // But under current scale, a true-admin confirmation isn't worth the bother.
            if (!IsBotCommander(message.Author))
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Nope! That's not for you!").Wait();
                return;
            }
            if (!File.Exists("./start.sh"))
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Nope! That's not valid for my current configuration!").Wait();
            }
            message.Channel.SendMessageAsync(SUCCESS_PREFIX + "Yes, boss. Restarting now...").Wait();
            Process.Start("sh", "./start.sh " + message.Channel.Id);
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
            client.StopAsync().Wait();
        }
        
        public void SaveConfig()
        {
            lock (ConfigSaveLock)
            {
                ConfigFile.SaveToFile(CONFIG_FILE);
            }
        }

        public static Object ConfigSaveLock = new Object();

        void DefaultCommands()
        {
            // Various
            CommonCmds["help"] = CMD_Help;
            CommonCmds["halp"] = CMD_Help;
            CommonCmds["helps"] = CMD_Help;
            CommonCmds["halps"] = CMD_Help;
            CommonCmds["hel"] = CMD_Help;
            CommonCmds["hal"] = CMD_Help;
            CommonCmds["h"] = CMD_Help;
            CommonCmds["hello"] = CMD_Hello;
            CommonCmds["hi"] = CMD_Hello;
            CommonCmds["hey"] = CMD_Hello;
            CommonCmds["source"] = CMD_Hello;
            CommonCmds["src"] = CMD_Hello;
            CommonCmds["github"] = CMD_Hello;
            CommonCmds["git"] = CMD_Hello;
            CommonCmds["hub"] = CMD_Hello;
            // Helper
            CommonCmds["warn"] = CMD_Warn;
            CommonCmds["warning"] = CMD_Warn;
            // TODO: List Warnings command
            // Admin
            CommonCmds["restart"] = CMD_Restart;
        }

        public bool ConnectedOnce = false;

        public bool ConnectedCurrently = false;

        public static DiscordWarningBot CurrentBot = null;

        public string AttentionNotice;

        public string MuteRoleName;

        static void Main(string[] args)
        {
            CurrentBot = new DiscordWarningBot(args);
        }

        public DiscordWarningBot(string[] args)
        {
            Console.WriteLine("Preparing...");
            DefaultCommands();
            if (File.Exists(CONFIG_FILE))
            {
                ConfigFile = FDSUtility.ReadFile(CONFIG_FILE);
                HelperRoleName = ConfigFile.GetString("helper_role_name").ToLowerInvariant();
                MuteRoleName = ConfigFile.GetString("mute_role_name").ToLowerInvariant();
                AttentionNotice = ConfigFile.GetString("attention_notice");
            }
            Console.WriteLine("Loading Discord...");
            DiscordSocketConfig config = new DiscordSocketConfig();
            config.MessageCacheSize = 256;
            client = new DiscordSocketClient(config);
            client.Ready += () =>
            {
                if (StopAllLogic)
                {
                    return Task.CompletedTask;
                }
                ConnectedCurrently = true;
                client.SetGameAsync("Guardian Over The People").Wait();
                if (ConnectedOnce)
                {
                    return Task.CompletedTask;
                }
                Console.WriteLine("Args: " + args.Length);
                if (args.Length > 0 && ulong.TryParse(args[0], out ulong a1))
                {
                    ISocketMessageChannel chan = client.GetChannel(a1) as ISocketMessageChannel;
                    Console.WriteLine("Restarted as per request in channel: " + chan.Name);
                    chan.SendMessageAsync(SUCCESS_PREFIX + "Connected and ready!").Wait();
                }
                ConnectedOnce = true;
                return Task.CompletedTask;
            };
            client.MessageReceived += (message) =>
            {
                if (StopAllLogic)
                {
                    return Task.CompletedTask;
                }
                if (message.Author.Id == client.CurrentUser.Id)
                {
                    return Task.CompletedTask;
                }
                LoopsSilent = 0;
                if (message.Author.IsBot || message.Author.IsWebhook)
                {
                    return Task.CompletedTask;
                }
                if (message.Channel.Name.StartsWith("@") || !(message.Channel is SocketGuildChannel sgc))
                {
                    Console.WriteLine("Refused message from (" + message.Author.Username + "): (Invalid Channel: " + message.Channel.Name + "): " + message.Content);
                    return Task.CompletedTask;
                }
                bool mentionedMe = message.MentionedUsers.Any((su) => su.Id == client.CurrentUser.Id);
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
            Console.WriteLine("Prepping monitor...");
            Task.Factory.StartNew(() =>
            {
                while (true)
                {
                    Task.Delay(MonitorLoopTime).Wait();
                    if (StopAllLogic)
                    {
                        return;
                    }
                    try
                    {
                        MonitorLoop();
                    }
                    catch (Exception ex)
                    {
                        if (ex is ThreadAbortException)
                        {
                            throw;
                        }
                        Console.WriteLine("Connection monitor loop had exception: " + ex.ToString());
                    }
                }
            });
            Console.WriteLine("Logging in to Discord...");
            client.LoginAsync(TokenType.Bot, TOKEN).Wait();
            Console.WriteLine("Connecting to Discord...");
            client.StartAsync().Wait();
            Console.WriteLine("Running Discord!");
            while (true)
            {
                string read = Console.ReadLine();
                string[] dats = read.Split(new char[] { ' ' }, 2);
                string cmd = dats[0].ToLowerInvariant();
                if (cmd == "quit" || cmd == "stop" || cmd == "exit")
                {
                    client.StopAsync().Wait();
                    Environment.Exit(0);
                }
            }
        }

        public TimeSpan MonitorLoopTime = new TimeSpan(hours: 0, minutes: 1, seconds: 0);

        public bool MonitorWasFailedAlready = false;

        public bool StopAllLogic = false;

        public void ForceRestartBot()
        {
            lock (MonitorLock)
            {
                StopAllLogic = true;
            }
            Task.Factory.StartNew(() =>
            {
                client.StopAsync().Wait();
            });
            CurrentBot = new DiscordWarningBot(new string[0]);
        }

        public Object MonitorLock = new Object();

        public long LoopsSilent = 0;

        public long LoopsTotal = 0;

        public void MonitorLoop()
        {
            bool isConnected;
            lock (MonitorLock)
            {
                LoopsSilent++;
                LoopsTotal++;
                isConnected = ConnectedCurrently && client.ConnectionState == ConnectionState.Connected;
            }
            if (!isConnected)
            {
                Console.WriteLine("Monitor detected disconnected state!");
            }
            if (LoopsSilent > 60)
            {
                Console.WriteLine("Monitor detected over an hour of silence, and is assuming a disconnected state!");
                isConnected = false;
            }
            if (LoopsTotal > 60 * 12)
            {
                Console.WriteLine("Monitor detected that the bot has been running for over 12 hours, and will restart soon!");
                isConnected = false;
            }
            if (isConnected)
            {
                MonitorWasFailedAlready = false;
            }
            else
            {
                if (MonitorWasFailedAlready)
                {
                    Console.WriteLine("Monitor is enforcing a restart!");
                    ForceRestartBot();
                }
                MonitorWasFailedAlready = true;
            }
        }
    }
}
