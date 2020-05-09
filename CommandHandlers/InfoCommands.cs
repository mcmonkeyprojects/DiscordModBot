using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using DiscordBotBase.CommandHandlers;
using Discord;
using Discord.WebSocket;
using FreneticUtilities.FreneticToolkit;

namespace DiscordModBot.CommandHandlers
{
    /// <summary>
    /// Handlers for information commands like 'help'.
    /// </summary>
    public class InfoCommands : UserCommands
    {
        /// <summary>
        /// Simple output string for general public commands.
        /// </summary>
        public static string CmdsHelp =
                "`help` shows help output, `hello` shows a source code link, "
                + "`listnotes [page]` views your own notes and warnings (if any), "
                + "`listnames` views known past names of a user - in format `listnames @User`, "
                + "...";

        /// <summary>
        /// Simple output string for helper commands.
        /// </summary>
        public static string CmdsHelperHelp =
                "`note` leaves a note about a user - in format `note @User [message...]`, "
                + "`warn` issues a warning to a user - in format `warn @User [level] [reason...]` with valid levels: `minor`, `normal`, `serious`, or `instant_mute` allowed, "
                + "`listnotes` lists notes and warnings for any user - in format `listnotes @User [page]`, "
                + "`unmute` removes the Muted role from a user - in format `unmute @User`, "
                + "`nosupport` marks a user as DoNotSupport - in the format `nosupport @User`, "
                + "`removenosupport` removes the 'DoNotSupport' mark from a user - in the format `removenosupport @User`, "
                + "...";

        /// <summary>
        /// Simple output string for admin commands.
        /// </summary>
        public static string CmdsAdminHelp =
                "`restart` restarts the bot, "
                + "`testname` shows a test name, "
                + "`sweep` sweeps current usernames on the Discord and applies corrections as needed, "
                + "...";

        /// <summary>
        /// User command to get help (shows a list of valid bot commands).
        /// </summary>
        public void CMD_Help(string[] cmds, IUserMessage message)
        {
            EmbedBuilder embed = new EmbedBuilder().WithTitle("Mod Bot Usage Help").WithColor(255, 128, 0);
            embed.AddField("Available Commands", CmdsHelp);
            if (DiscordModBot.IsHelper(message.Author as SocketGuildUser))
            {
                embed.AddField("Available Helper Commands", CmdsHelperHelp);
            }
            if (DiscordModBot.IsBotCommander(message.Author as SocketGuildUser))
            {
                embed.AddField("Available Admin Commands", CmdsAdminHelp);
            }
            SendReply(message, embed.Build());
        }

        /// <summary>
        /// User command to say 'hello' and get a source link.
        /// </summary>
        public void CMD_Hello(string[] cmds, IUserMessage message)
        {
            SendGenericPositiveMessageReply(message, "Discord Mod Bot", "Hi! I'm a bot! Find my source code at <https://github.com/mcmonkeyprojects/DiscordModBot>.");
        }

        /// <summary>
        /// User command to list user old names.
        /// </summary>
        public void CMD_ListNames(string[] cmds, IUserMessage message)
        {
            if (!DiscordModBot.WarningCommandHandler.GetTargetUser(cmds, message, out ulong userID))
            {
                return;
            }
            WarnableUser user = WarningUtilities.GetWarnableUser((message.Channel as SocketGuildChannel).Guild.Id, userID);
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
                nameStringOutput.Append($"`{old_name.Key}` (first seen: {StringConversionHelper.DateTimeToString(old_name.Value, false)})\n");
            }
            builders.Add(nameStringOutput);
            if (nameStringOutput.Length == 0)
            {
                SendGenericNegativeMessageReply(message, "Never Seen That User", $"User {user.LastKnownUsername} does not have any known names (never spoken here).");
            }
            else
            {
                SendGenericPositiveMessageReply(message, "Seen Usernames", $"User {user.LastKnownUsername} has the following known usernames:\n{builders[0]}");
                for (int i = 1; i < builders.Count; i++)
                {
                    if (i == 2 && builders.Count > 4)
                    {
                        SendGenericPositiveMessageReply(message, "Too Many Usernames", $"...(Skipped {(builders.Count - 3)} paragraphs of names)...");
                        continue;
                    }
                    if (i > 2 && i + 1 < builders.Count)
                    {
                        continue;
                    }
                    SendGenericPositiveMessageReply(message, "Seen Usernames Continued", $"... continued:\n{builders[i]}");
                }
            }
        }
    }
}
