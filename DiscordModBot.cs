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
using FreneticUtilities.FreneticToolkit;
using FreneticUtilities.FreneticDataSyntax;

namespace ModBot
{
    /// <summary>
    /// Discord bot for handling helper-given warnings and other moderation tools.
    /// </summary>
    public class DiscordModBot
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
        public const string SUCCESS_PREFIX = "+ ModBot: ";

        /// <summary>
        /// Prefix for when the bot refuses user input.
        /// </summary>
        public const string REFUSAL_PREFIX = "- ModBot: ";

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
                "`help` shows help output, `hello` shows a source code link, "
                + "`listnotes` views your own notes and warnings (if any), "
                + "`listnames` views known past names of a user - in format `listnames @User`, "
                + "...";

        /// <summary>
        /// Simple output string for helper commands.
        /// </summary>
        public static string CmdsHelperHelp =
                "`note` leaves a note about a user - in format `note @User [message...]`, "
                + "`warn` issues a warning to a user - in format `warn @User [level] [reason...]` with valid levels: `minor`, `normal`, `serious`, or `instant_mute` allowed, "
                + "`listnotes` lists notes and warnings for any user - in format `listnotes @User`, "
                + "`unmute` removes the Muted role from a user - in format `unmute @User`, "
                + "`sweep` sweeps current usernames on the Discord and applies corrections as needed, "
                + "...";

        /// <summary>
        /// Simple output string for admin commands.
        /// </summary>
        public static string CmdsAdminHelp =
                "`restart` restarts the bot, "
                + "`testname` shows a test name, "
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
            message.Channel.SendMessageAsync(SUCCESS_PREFIX + "Hi! I'm a bot! Find my source code at https://github.com/mcmonkeyprojects/DiscordModBot").Wait();
        }

        /// <summary>
        /// A mapping of typable names to warning level enumeration values.
        /// </summary>
        public static Dictionary<string, WarningLevel> LevelsTypable = new Dictionary<string, WarningLevel>()
        {
            { "note", WarningLevel.NOTE },
            { "minor", WarningLevel.MINOR },
            { "normal", WarningLevel.NORMAL },
            { "serious", WarningLevel.SERIOUS },
            { "major", WarningLevel.SERIOUS },
            { "instant_mute", WarningLevel.INSTANT_MUTE },
            { "instantmute", WarningLevel.INSTANT_MUTE },
            { "instant", WarningLevel.INSTANT_MUTE },
            { "mute", WarningLevel.INSTANT_MUTE }
        };

        /// <summary>
        /// User command to sweep through all current names.
        /// </summary>
        void CMD_Sweep(string[] cmds, SocketMessage message)
        {
            if (!IsHelper(message.Author as SocketGuildUser))
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "You're not allowed to do that.").Wait();
                return;
            }
            SocketGuildChannel channel = message.Channel as SocketGuildChannel;
            channel.Guild.DownloadUsersAsync();
            foreach (SocketGuildUser user in channel.Guild.Users)
            {
                if (AsciiNameRuleCheck(message.Channel, user))
                {
                    Thread.Sleep(400);
                }
            }
        }

        /// <summary>
        /// User command to remove a user's muted status.
        /// </summary>
        void CMD_Unmute(string[] cmds, SocketMessage message)
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
            SocketUser userToUnmute = message.MentionedUsers.FirstOrDefault((su) => su.Id != Client.CurrentUser.Id);
            if (userToUnmute == null)
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Something went wrong - user mention not valid?").Wait();
                return;
            }
            SocketGuildUser guildUserToUnmute = userToUnmute as SocketGuildUser;
            WarnableUser warnable = GetWarnableUser(guildUserToUnmute.Guild.Id, guildUserToUnmute.Id);
            warnable.IsMuted = false;
            warnable.Save();
            IRole role = guildUserToUnmute.Roles.FirstOrDefault((r) => r.Name.ToLowerInvariant() == MuteRoleName);
            if (role == null)
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "User " + guildUserToUnmute.Username + "#" + guildUserToUnmute.Discriminator + " is not muted.").Wait();
                return;
            }
            guildUserToUnmute.RemoveRoleAsync(role).Wait();
            message.Channel.SendMessageAsync(SUCCESS_PREFIX + "<@" + message.Author.Id + "> has unmuted <@" + userToUnmute.Id + ">.").Wait();
        }

        /// <summary>
        /// User command to add a note to a user.
        /// </summary>
        void CMD_Note(string[] cmds, SocketMessage message)
        {
            if (!IsHelper(message.Author as SocketGuildUser))
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "You're not allowed to do that.").Wait();
                return;
            }
            if (message.MentionedUsers.Count() < 2 && cmds.Length < 2)
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Usage: note [user] [message]").Wait();
                return;
            }
            ulong userToWarnID;
            SocketUser userToWarn;
            IEnumerable<string> cmdsToSave = cmds;
            if (message.MentionedUsers.Count == 1 && cmds.Length > 0)
            {
                userToWarn = null;
                if (!ulong.TryParse(cmds[0], out userToWarnID))
                {
                    message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Something went wrong - user ID not valid?").Wait();
                    return;
                }
                cmdsToSave = cmdsToSave.Skip(1);
            }
            else if (message.MentionedUsers.Count == 2)
            {
                userToWarn = message.MentionedUsers.FirstOrDefault((su) => su.Id != Client.CurrentUser.Id);
                if (userToWarn == null)
                {
                    message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Something went wrong - user mention not valid?").Wait();
                    return;
                }
                userToWarnID = userToWarn.Id;
            }
            else
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Notes must only `@` mention this bot and the user to leave a note on.").Wait();
                return;
            }
            Warning warning = new Warning() { GivenTo = userToWarnID, GivenBy = message.Author.Id, TimeGiven = DateTimeOffset.UtcNow, Level = WarningLevel.NOTE };
            warning.Reason = string.Join(" ", cmdsToSave);
            Discord.Rest.RestUserMessage sentMessage = message.Channel.SendMessageAsync(SUCCESS_PREFIX + "Note from <@" + message.Author.Id + "> to <@" + userToWarnID + "> recorded.").Result;
            warning.Link = LinkToMessage(sentMessage);
            Warn((message.Channel as SocketGuildChannel).Guild.Id, userToWarnID, warning);
        }

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
            if (message.MentionedUsers.Count() < 2 && cmds.Length < 2)
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Usage: warn [user] [level] [reason] - Valid levels: `minor`, `normal`, `serious`, or `instant_mute`").Wait();
                return;
            }
            ulong userToWarnID;
            SocketUser userToWarn;
            int cmdPos = 0;
            if (message.MentionedUsers.Count == 1 && cmds.Length > 0)
            {
                userToWarn = null;
                if (!ulong.TryParse(cmds[0], out userToWarnID))
                {
                    message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Something went wrong - user ID not valid?").Wait();
                    return;
                }
                userToWarn = (message.Channel as SocketGuildChannel).Guild.GetUser(userToWarnID);
                cmdPos = 1;
            }
            else if (message.MentionedUsers.Count == 2)
            {
                userToWarn = message.MentionedUsers.FirstOrDefault((su) => su.Id != Client.CurrentUser.Id);
                if (userToWarn == null)
                {
                    message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Something went wrong - user mention not valid?").Wait();
                    return;
                }
                userToWarnID = userToWarn.Id;
            }
            else
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Warnings must only `@` mention this bot and the user to be warned.").Wait();
                return;
            }
            if (cmds.Length <= cmdPos || !LevelsTypable.TryGetValue(cmds[cmdPos].ToLowerInvariant(), out WarningLevel level))
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Unknown level. Valid levels: `minor`, `normal`, `serious`, or `instant_mute`.").Wait();
                return;
            }
            Warning warning = new Warning() { GivenTo = userToWarnID, GivenBy = message.Author.Id, TimeGiven = DateTimeOffset.UtcNow, Level = level };
            warning.Reason = string.Join(" ", cmds.Skip(1));
            Discord.Rest.RestUserMessage sentMessage = message.Channel.SendMessageAsync(SUCCESS_PREFIX + "Warning from <@" + message.Author.Id + "> to <@" + userToWarnID + "> recorded.").Result;
            warning.Link = LinkToMessage(sentMessage);
            WarnableUser warnUser = Warn((message.Channel as SocketGuildChannel).Guild.Id, userToWarnID, warning);
            if (userToWarn != null)
            {
                PossibleMute(userToWarn as SocketGuildUser, message.Channel, level);
            }
            else if (level == WarningLevel.INSTANT_MUTE)
            {
                lock (WarnLock)
                {
                    warnUser.IsMuted = true;
                    warnUser.Save();
                }
                message.Channel.SendMessageAsync(SUCCESS_PREFIX + "Mute applied for next rejoin.").Wait();
            }
        }

        /// <summary>
        /// User command to list user warnings.
        /// </summary>
        void CMD_ListWarnings(string[] cmds, SocketMessage message)
        {
            SocketUser userToList = message.Author;
            ulong userToListID = userToList.Id;
            if (IsHelper(message.Author as SocketGuildUser))
            {
                if (message.MentionedUsers.Count == 1 && cmds.Length > 0)
                {
                    userToList = null;
                    if (!ulong.TryParse(cmds[0], out userToListID))
                    {
                        message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Something went wrong - user ID not valid?").Wait();
                        return;
                    }
                }
                else if (message.MentionedUsers.Count == 2)
                {
                    userToList = message.MentionedUsers.FirstOrDefault((su) => su.Id != Client.CurrentUser.Id);
                    if (userToList == null)
                    {
                        message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Something went wrong - user mention not valid?").Wait();
                        return;
                    }
                    userToListID = userToList.Id;
                }
                else if (message.MentionedUsers.Count > 2)
                {
                    message.Channel.SendMessageAsync(REFUSAL_PREFIX + "You must only `@` mention this bot and the user to check warnings for.").Wait();
                    return;
                }
            }
            WarnableUser user = GetWarnableUser((message.Channel as SocketGuildChannel).Guild.Id, userToListID);
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
                warnStringOutput.Append("... " + warned.Level + (warned.Level == WarningLevel.NOTE ? "" : " warning") + " given at " + StringConversionHelper.DateTimeToString(warned.TimeGiven, false)
                    + " by " + giverLabel + " with reason: `" + reason + "` ... for detail see: " + warned.Link + "\n");
            }
            if (warnStringOutput.Length == 0)
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "User " + user.LastKnownUsername + " does not have any warnings logged.").Wait();
            }
            else
            {
                message.Channel.SendMessageAsync(SUCCESS_PREFIX + "User " + user.LastKnownUsername + " has the following warnings logged:\n" + warnStringOutput).Wait();
            }
        }

        /// <summary>
        /// User command to list user old names.
        /// </summary>
        void CMD_ListNames(string[] cmds, SocketMessage message)
        {
            SocketUser userToList = message.Author;
            ulong userToListID = userToList.Id;
            if (message.MentionedUsers.Count == 1 && cmds.Length > 0)
            {
                userToList = null;
                if (!ulong.TryParse(cmds[0], out userToListID))
                {
                    message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Something went wrong - user ID not valid?").Wait();
                    return;
                }
            }
            else if (message.MentionedUsers.Count == 2)
            {
                userToList = message.MentionedUsers.FirstOrDefault((su) => su.Id != Client.CurrentUser.Id);
                if (userToList == null)
                {
                    message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Something went wrong - user mention not valid?").Wait();
                    return;
                }
                userToListID = userToList.Id;
            }
            else if (message.MentionedUsers.Count > 2)
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "You must only `@` mention this bot and the user to check names for.").Wait();
                return;
            }
            WarnableUser user = GetWarnableUser((message.Channel as SocketGuildChannel).Guild.Id, userToListID);
            List<StringBuilder> builders = new List<StringBuilder>();
            StringBuilder nameStringOutput = new StringBuilder();
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            IEnumerable<KeyValuePair<string, DateTimeOffset>> oldNames = user.OldNames();
            foreach (KeyValuePair<string, DateTimeOffset> old_name in user.OldNames().OrderBy((n) => n.Value.ToUnixTimeSeconds()))
            {
                if (nameStringOutput.Length > 1500)
                {
                    builders.Add(nameStringOutput);
                    nameStringOutput = new StringBuilder();
                }
                nameStringOutput.Append("`" + old_name.Key + "` (first seen: " + StringConversionHelper.DateTimeToString(old_name.Value, false) + ") ... ");
            }
            builders.Add(nameStringOutput);
            if (nameStringOutput.Length == 0)
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "User " + user.LastKnownUsername + " does not have any known names (never spoken here).").Wait();
            }
            else
            {
                message.Channel.SendMessageAsync(SUCCESS_PREFIX + "User " + user.LastKnownUsername + " has the following known usernames:\n" + builders[0]).Wait();
                for (int i = 1; i < builders.Count; i++)
                {
                    if (i == 2 && builders.Count > 4)
                    {
                        message.Channel.SendMessageAsync("...(Skipped " + (builders.Count - 3) + " paragraphs of names)...").Wait();
                        continue;
                    }
                    if (i > 2 && i + 1 < builders.Count)
                    {
                        continue;
                    }
                    message.Channel.SendMessageAsync("...Continued: " + builders[0]).Wait();
                }
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
            int normalWarns = 0;
            int seriousWarns = 0;
            if (newLevel == WarningLevel.NORMAL || newLevel == WarningLevel.SERIOUS)
            {
                double warningNeed = 0.0;
                foreach (Warning oldWarn in GetWarnableUser((channel as SocketGuildChannel).Guild.Id, user.Id).GetWarnings())
                {
                    TimeSpan relative = DateTimeOffset.UtcNow.Subtract(oldWarn.TimeGiven);
                    if (relative.TotalDays > 30)
                    {
                        break;
                    }
                    if (oldWarn.Level == WarningLevel.NORMAL)
                    {
                        normalWarns++;
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
                        seriousWarns++;
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
                WarnableUser warnable = GetWarnableUser(user.Guild.Id, user.Id);
                warnable.IsMuted = true;
                warnable.Save();
                channel.SendMessageAsync(SUCCESS_PREFIX + "User <@" + user.Id + "> has been muted automatically by the warning system."
                    + (newLevel == WarningLevel.INSTANT_MUTE ? " This mute was applied by moderator request (INSTANT_MUTE)." :
                    " This mute was applied automatically due to have multiple warnings in a short period of time. User has " + normalWarns + " NORMAL and " + seriousWarns + " SERIOUS warnings within the past 30 days.")
                    + " You may not speak except in the incident handling channel."
                    + " This mute lasts until an administrator removes it, which may in some cases take a while. " + AttentionNotice
                    + "\nAny user may review warnings against them at any time by typing `@ModBot listwarnings`.").Wait();
                foreach (ulong id in IncidentChannel)
                {
                    SocketGuildChannel incidentChan = user.Guild.GetChannel(id);
                    if (incidentChan != null && incidentChan is ISocketMessageChannel incidentChanText)
                    {
                        incidentChanText.SendMessageAsync(SUCCESS_PREFIX + "<@" + user.Id + ">, you have been automatically muted by the system due to warnings received. You may discuss the situation in this channel only, until a moderator unmutes you.").Wait();
                        break;
                    }
                }
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
        public WarnableUser Warn(ulong serverId, ulong id, Warning warn)
        {
            lock (WarnLock)
            {
                WarnableUser user = GetWarnableUser(serverId, id);
                user.AddWarning(warn);
                return user;
            }
        }

        /// <summary>
        /// Gets the <see cref="WarnableUser"/> object for a Discord user (by Discord ID).
        /// </summary>
        public WarnableUser GetWarnableUser(ulong serverId, ulong id)
        {
            string fname = "./warnings/" + serverId + "/" + id + ".fds";
            return new WarnableUser() { UserID = id, ServerID = serverId, WarningFileSection = File.Exists(fname) ? FDSUtility.ReadFile(fname) : new FDSSection() };
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
        /// Outputs an ASCII name rule test name.
        /// </summary>
        void CMD_TestName(string[] cmds, SocketMessage message)
        {
            if (!IsBotCommander(message.Author as SocketGuildUser))
            {
                message.Channel.SendMessageAsync(REFUSAL_PREFIX + "Nope! That's not for you!").Wait();
                return;
            }
            string name = GenerateAsciiName(string.Join(" ", cmds));
            message.Channel.SendMessageAsync(SUCCESS_PREFIX + "Test of ASCII-Name-Rule name generator: " + name);
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
            // User
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
            UserCommands["names"] = CMD_ListNames;
            UserCommands["listnames"] = CMD_ListNames;
            UserCommands["listname"] = CMD_ListNames;
            UserCommands["namelist"] = CMD_ListNames;
            UserCommands["nameslist"] = CMD_ListNames;
            // Helper and User
            UserCommands["list"] = CMD_ListWarnings;
            UserCommands["listnote"] = CMD_ListWarnings;
            UserCommands["listnotes"] = CMD_ListWarnings;
            UserCommands["listwarn"] = CMD_ListWarnings;
            UserCommands["listwarns"] = CMD_ListWarnings;
            UserCommands["listwarning"] = CMD_ListWarnings;
            UserCommands["listwarnings"] = CMD_ListWarnings;
            UserCommands["warnlist"] = CMD_ListWarnings;
            UserCommands["warninglist"] = CMD_ListWarnings;
            UserCommands["warningslist"] = CMD_ListWarnings;
            // Helper
            UserCommands["note"] = CMD_Note;
            UserCommands["warn"] = CMD_Warn;
            UserCommands["warning"] = CMD_Warn;
            UserCommands["unmute"] = CMD_Unmute;
            UserCommands["sweep"] = CMD_Sweep;
            // Admin
            UserCommands["restart"] = CMD_Restart;
            UserCommands["testname"] = CMD_TestName;
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
        /// The ID of the incident notice channel.
        /// </summary>
        public List<ulong> IncidentChannel;

        /// <summary>
        /// The ID of the join log message channel.
        /// </summary>
        public List<ulong> JoinNotifChannel;

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
        /// Gets the full proper username for a user.
        /// </summary>
        public string Username(IUser user)
        {
            return user.Username.Replace('\\', '/').Replace("\r", "/r").Replace("\n", "/n").Replace('`', '\'') + "#" + user.Discriminator;
        }

        public static readonly string[] ASCII_NAME_PART1 = new string[] { "HEY", "hey", "YO", "yo", "YOU", "you", "EY", "ey", "" };
        public static readonly string[] ASCII_NAME_PART2 = new string[] { "PLEASE", "please", "PLIS", "plis", "PLZ", "plz", "" };
        public static readonly string[] ASCII_NAME_PART3 = new string[] { "USE", "use", "useA", "USEa", "TAKE", "take", "TAKEa", "takeA", "" };
        public static readonly string[] ASCII_NAME_PART4 = new string[] { "ASCII", "ascii", "ENGLISH", "english", "us-en", "US-EN", "TYPABLE", "typable", "" };
        public static readonly string[] ASCII_NAME_PART5 = new string[] { "NAME", "name", "USERNAME", "username", "NICKNAME", "nickname", "NICK", "nick", "" };

        public Random random = new Random();

        public string GenerateAsciiName(string currentName)
        {
            StringBuilder preLetters = new StringBuilder();
            for (int i = 0; i < currentName.Length; i++)
            {
                if (IsAsciiSymbol(currentName[i]))
                {
                    preLetters.Append(currentName[i]);
                }
            }
            string result = "NameRule" + preLetters
                + ASCII_NAME_PART1[random.Next(ASCII_NAME_PART1.Length)]
                + ASCII_NAME_PART2[random.Next(ASCII_NAME_PART2.Length)]
                + ASCII_NAME_PART3[random.Next(ASCII_NAME_PART3.Length)]
                + ASCII_NAME_PART4[random.Next(ASCII_NAME_PART4.Length)]
                + ASCII_NAME_PART5[random.Next(ASCII_NAME_PART5.Length)]
                + random.Next(1000, 9999);
            if (result.Length > 30)
            {
                result = result.Substring(0, 30);
            }
            return result;
        }

        public const int MIN_ASCII_LETTERS_ROW = 3;

        public const string ANTI_LIST_TOP_SYMBOL = "·";

        public bool IsAsciiSymbol(char c)
        {
            return (c >= 'a' && c <= 'z')
                || (c >= 'A' && c <= 'Z')
                || (c >= '0' && c <= '9');
        }

        public bool IsValidFirstChar(string name)
        {
            if (!EnforceNameStartRule)
            {
                return true;
            }
            if (name.StartsWith(ANTI_LIST_TOP_SYMBOL))
            {
                return true;
            }
            char c = name[0];
            return (c >= 'a' && c <= 'z')
                || (c >= 'A' && c <= 'Z');
        }

        public bool IsValidAsciiName(string name)
        {
            if (!EnforceAsciiNameRule)
            {
                return true;
            }
            if (name.Length < 2)
            {
                return false;
            }
            if (name.Length == 2)
            {
                return IsAsciiSymbol(name[0]) && IsAsciiSymbol(name[1]);
            }
            if (name.Length == 3)
            {
                return IsAsciiSymbol(name[0]) && IsAsciiSymbol(name[1]) && IsAsciiSymbol(name[2]);
            }
            for (int i = 0; i < name.Length; i++)
            {
                if (IsAsciiSymbol(name[i]))
                {
                    int x;
                    for (x = i; x < name.Length; x++)
                    {
                        if (!IsAsciiSymbol(name[x]))
                        {
                            break;
                        }
                    }
                    if (x - i >= MIN_ASCII_LETTERS_ROW)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public bool AsciiNameRuleCheck(ISocketMessageChannel channel, SocketGuildUser user)
        {
            string nick = user.Nickname;
            string username = user.Username;
            if (nick != null)
            {
                if (!IsValidAsciiName(nick))
                {
                    if (IsValidAsciiName(username))
                    {
                        user.ModifyAsync(u => u.Nickname = "").Wait();
                        channel.SendMessageAsync(SUCCESS_PREFIX + $"Non-ASCII nickname for <@{user.Id}> removed. Please only use a readable+typable US-English ASCII nickname.").Wait();
                        return true;
                    }
                    else
                    {
                        user.ModifyAsync(u => u.Nickname = GenerateAsciiName(user.Username)).Wait();
                        channel.SendMessageAsync(SUCCESS_PREFIX + $"Non-ASCII nickname for <@{user.Id}> change to a placeholder. Please change to a readable+typable US-English ASCII nickname or username.").Wait();
                        return true;
                    }
                }
                else if (!IsValidFirstChar(nick))
                {
                    if (nick.Length > 30)
                    {
                        nick = nick.Substring(0, 30);
                    }
                    user.ModifyAsync(u => u.Nickname = ANTI_LIST_TOP_SYMBOL + nick).Wait();
                    channel.SendMessageAsync(SUCCESS_PREFIX + $"Name patch: <@{user.Id}> had a nickname that started with a symbol or number..." 
                        + "applied a special first symbol in place. Please start your name with a letter from A to Z. (This is to prevent users from artificially appearing at the top of the userlist).").Wait();
                        return true;
                }
            }
            else
            {
                if (!IsValidAsciiName(username))
                {
                    user.ModifyAsync(u => u.Nickname = GenerateAsciiName(user.Username)).Wait();
                    channel.SendMessageAsync(SUCCESS_PREFIX + $"Non-ASCII username for <@{user.Id}> has been overriden with a placeholder nickname. Please change to a readable+typable US-English ASCII nickname or username.").Wait();
                        return true;
                }
                else if (!IsValidFirstChar(username))
                {
                    if (username.Length > 30)
                    {
                        username = username.Substring(0, 30);
                    }
                    user.ModifyAsync(u => u.Nickname = ANTI_LIST_TOP_SYMBOL + username).Wait();
                    channel.SendMessageAsync(SUCCESS_PREFIX + "Name patch: <@" + user.Id + "> had a nickname that started with a symbol or number..." 
                        + "applied a special first symbol in place. Please start your name with a letter from A to Z. (This is to prevent users from artificially appearing at the top of the userlist).").Wait();
                        return true;
                }
            }
            return false;
        }

        public bool EnforceAsciiNameRule = true;

        public bool EnforceNameStartRule = false;

        public Dictionary<ulong, ulong> LogChannels = new Dictionary<ulong, ulong>(512);

        public void LogChannelActivity(ulong channelId, Action<EmbedBuilder> message)
        {
            if (!LogChannels.TryGetValue(channelId, out ulong logChannel))
            {
                return;
            }
            if (!(Client.GetChannel(logChannel) is SocketTextChannel channel))
            {
                Console.WriteLine($"Bad channel log output ID: {logChannel}");
                return;
            }
            EmbedBuilder embed = new EmbedBuilder
            {
                Title = "Mod Bot Log",
                Timestamp = DateTimeOffset.Now,
                Color = new Color(255, 128, 0)
            };
            message(embed);
            channel.SendMessageAsync(embed: embed.Build()).Wait();
        }

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
                IncidentChannel = ConfigFile.GetDataList("incidents_channel").Select(d => ObjectConversionHelper.ObjectToULong(d.Internal).Value).ToList();
                EnforceAsciiNameRule = ConfigFile.GetBool("enforce_ascii_name_rule", EnforceAsciiNameRule).Value;
                EnforceNameStartRule = ConfigFile.GetBool("enforce_name_start_rule", EnforceNameStartRule).Value;
                JoinNotifChannel = ConfigFile.GetDataList("join_notif_channel")?.Select(d => ObjectConversionHelper.ObjectToULong(d.Internal).Value)?.ToList() ?? new List<ulong>();
                FDSSection logChannelsSection = ConfigFile.GetSection("log_channels");
                foreach (string key in logChannelsSection.GetRootKeys())
                {
                    LogChannels.Add(ulong.Parse(key), logChannelsSection.GetUlong(key).Value);
                }
            }
            Console.WriteLine("Loading Discord...");
            DiscordSocketConfig config = new DiscordSocketConfig
            {
                MessageCacheSize = 256
            };
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
            Client.UserJoined += (user) =>
            {
                if (BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                if (user.Id == Client.CurrentUser.Id)
                {
                    return Task.CompletedTask;
                }
                WarnableUser warnable = GetWarnableUser(user.Guild.Id, user.Id);
                IReadOnlyCollection<SocketTextChannel> channels = user.Guild.TextChannels;
                foreach (ulong chan in JoinNotifChannel)
                {
                    IEnumerable<SocketTextChannel> possibles = channels.Where(schan => schan.Id == chan);
                    if (possibles.Any())
                    {
                        possibles.First().SendMessageAsync($"User `{Username(user)}` (`{user.Id}`) joined.").Wait();
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
                        possibles.First().SendMessageAsync($"User <@{ user.Id}> (`{Username(user)}`) just joined, and has prior warnings. Use the `listwarnings` command to see details." + "").Wait();
                        if (warnable.IsMuted)
                        {
                            possibles.First().SendMessageAsync(SUCCESS_PREFIX + $"<@{user.Id}>, you have been automatically muted by the system due to being muted and then rejoining the Discord."
                                + "You may discuss the situation in this channel only, until a moderator unmutes you.").Wait();
                        }
                        return Task.CompletedTask;
                    }
                }
                Console.WriteLine("Failed to warn of dangerous user-join: " + user.Id + " to " + user.Guild.Id + "(" + user.Guild.Name + ")");
                return Task.CompletedTask;
            };
            Client.MessageReceived += (message) =>
            {
                try
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
                        Console.WriteLine($"Refused message from ({message.Author.Username}): (Invalid Channel: {message.Channel.Name}): {message.Content}");
                        return Task.CompletedTask;
                    }
                    Console.WriteLine($"Parsing message from ({message.Author.Username}), in channel: {message.Channel.Name}: {message.Content}");
                    // TODO: helper ping on first post (never posted on the discord guild prior to 10 minutes ago,
                    // -> never posted in any other channel, pings a helper/dev/bot,
                    // -> and nobody else has posted in that channel since their first post) reaction,
                    // -> and if not in a help lobby redirect to help lobby (in same response)
                    string authorName = Username(message.Author);
                    if (GetWarnableUser((message.Channel as SocketGuildChannel).Guild.Id, message.Author.Id).SeenUsername(authorName, out string oldName))
                    {
                        message.Channel.SendMessageAsync(SUCCESS_PREFIX + $"Notice: User <@{message.Author.Id}> changed their base username from `{oldName}` to `{authorName}`.").Wait();
                    }
                    // TODO: Spam detection
                    AsciiNameRuleCheck(message.Channel, message.Author as SocketGuildUser);
                    if (message.MentionedUsers.Any((su) => su.Id == Client.CurrentUser.Id))
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
                            Console.WriteLine($"Error handling command: {ex.ToString()}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error while processing a message: {ex}");
                }
                return Task.CompletedTask;
            };
            Client.MessageUpdated += (cache, message, channel) =>
            {
                LogChannelActivity(channel.Id, (embed) =>
                {
                    embed.Title = "Message Edited";
                    embed.AddField("Author", $"<@{message.Author.Id}>", true);
                    embed.AddField("Channel", $"<#{channel.Id}>", true);
                    if (!cache.HasValue)
                    {
                        embed.AddField("Original Post", "(Not cached)");
                    }
                    else
                    {
                        string content = cache.Value.Content.Replace('`', '\'').Replace('\\', '/');
                        if (content.Length > 700)
                        {
                            content = content.Substring(0, 650) + "...";
                        }
                        embed.AddField("Original Post", $"```{content}```");
                    }
                    string newContent = message.Content.Replace('`', '\'').Replace('\\', '/');
                    if (newContent.Length > 1300)
                    {
                        newContent = newContent.Substring(0, 1250) + "...";
                    }
                    embed.AddField("New Post", $"```{newContent}```");
                });
                return Task.CompletedTask;
            };
            Client.MessageDeleted += (cache, channel) =>
            {
                LogChannelActivity(channel.Id, (embed) =>
                {
                    embed.Title = "Message Deleted";
                    embed.AddField("Channel", $"<#{channel.Id}>", true);
                    if (!cache.HasValue)
                    {
                        embed.AddField("Original Post", "(Not cached)");
                    }
                    else
                    {
                        embed.AddField("Author", $"<@{cache.Value.Author.Id}>", true);
                        string content = cache.Value.Content.Replace('`', '\'').Replace('\\', '/');
                        if (content.Length > 700)
                        {
                            content = content.Substring(0, 650) + "...";
                        }
                        embed.AddField("Original Post", $"```{content}```");
                    }
                });
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
