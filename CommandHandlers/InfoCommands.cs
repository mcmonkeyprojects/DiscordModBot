using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using DiscordBotBase;
using DiscordBotBase.CommandHandlers;
using Discord;
using Discord.WebSocket;
using FreneticUtilities.FreneticToolkit;
using ModBot.WarningHandlers;
using ModBot.Core;
using ModBot.Database;

namespace ModBot.CommandHandlers
{
    /// <summary>Handlers for information commands like 'help'.</summary>
    public class InfoCommands : UserCommands
    {
        /// <summary>Simple output string for general public commands.</summary>
        public static string CmdsHelp =
                "`help` shows help output, `hello` shows a source code link\n"
                + "`listnames` views known past names of a user - in format `listnames @User`\n";

        /// <summary>Simple output string for general user-facing warning commands.</summary>
        public static string CmdsWarningsUserHelp =
                "`listnotes [page]` views your own notes and warnings (if any)\n";

        /// <summary>Simple output string for warning-related commands.</summary>
        public static string CmdsWarningsHelp =
                "`note` leaves a note about a user - in format `note @User [message...]`\n"
                + "`warn` issues a warning to a user - in format `warn @User [level] [reason...]` with valid levels: `minor`, `normal`, `serious`, or `instant_mute` allowed\n"
                + "`timeout` applies a timeout to a user - in format `timeout @user [duration] (reason)`\n"
                + "`listnotes` lists notes and warnings for any user - in format `listnotes @User [page]`\n";

        /// <summary>Simple output string for mute-related commands.</summary>
        public static string CmdsMuteHelp =
                "`unmute` removes the Muted role from a user - in format `unmute @User`\n";

        /// <summary>Simple output string for ban-related commands.</summary>
        public static string CmdsBansHelp =
                "`ban` to temporarily ban a user - in the format `ban @User [duration] (reason)`\n"
                + "`unban` to remove a ban - in the format `unban @User`\n";

        /// <summary>Simple output string for admin commands.</summary>
        public static string CmdsAdminHelp =
                "`restart` restarts the bot\n"
                + "`testname` shows a test name\n"
                + "`sweep` sweeps current usernames on the Discord and applies corrections as needed\n"
                + "`admin-configure` configures per-guild admin settings\n";

        /// <summary>User command to get help (shows a list of valid bot commands).</summary>
        public void CMD_Help(CommandData command)
        {
            SocketGuild guild = (command.Message.Channel as SocketGuildChannel).Guild;
            GuildConfig config = DiscordModBot.GetConfig(guild.Id);
            EmbedBuilder embed = new EmbedBuilder().WithTitle("Mod Bot Usage Help").WithColor(255, 128, 0);
            string message = CmdsHelp;
            if (config.WarningsEnabled)
            {
                message += CmdsWarningsUserHelp;
            }
            embed.AddField("Available Commands", message);
            if (DiscordModBot.IsModerator(command.Message.Author as SocketGuildUser))
            {
                message = "";
                if (config.WarningsEnabled)
                {
                    message += CmdsWarningsHelp;
                }
                if (config.MuteRole.HasValue)
                {
                    message += CmdsMuteHelp;
                }
                if (config.BansEnabled)
                {
                    message += CmdsBansHelp;
                }
                if (message != "")
                {
                    embed.AddField("Available Moderator Commands", message);
                }
                if (config.SpecialRoles.Any())
                {
                    embed.AddField("Available Special-Role Commands", string.Join("\n", config.SpecialRoles.Values.Select(r => $"`{r.Name}` can be added with `{r.AddCommands[0]}` and removed with `{r.RemoveCommands[0]}`")));
                }
            }
            if (DiscordModBot.IsBotCommander(command.Message.Author as SocketGuildUser))
            {
                embed.AddField("Available Admin Commands", CmdsAdminHelp);
            }
            SendReply(command.Message, embed.Build());
        }

        /// <summary>User command to say 'hello' and get a source link.</summary>
        public void CMD_Hello(CommandData command)
        {
            SendGenericPositiveMessageReply(command.Message, "Discord Mod Bot", "Hi! I'm a bot! Find my source code at <https://github.com/mcmonkeyprojects/DiscordModBot>.");
        }

        /// <summary>User command to list user old names.</summary>
        public void CMD_ListNames(CommandData command)
        {
            if (!DiscordModBot.WarningCommandHandler.GetTargetUser(command, true, true, out ulong userID))
            {
                return;
            }
            WarnableUser user = WarningUtilities.GetWarnableUser((command.Message.Channel as SocketGuildChannel).Guild.Id, userID);
            List<StringBuilder> builders = new();
            StringBuilder nameStringOutput = new();
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            foreach (WarnableUser.OldName old_name in user.SeenNames.OrderBy((n) => n.FirstSeen.ToUnixTimeSeconds()))
            {
                if (nameStringOutput.Length > 1500)
                {
                    builders.Add(nameStringOutput);
                    nameStringOutput = new StringBuilder();
                }
                nameStringOutput.Append($"`{EscapeUserInput(old_name.Name)}` (first seen: {StringConversionHelper.DateTimeToString(old_name.FirstSeen, false)})\n");
            }
            builders.Add(nameStringOutput);
            if (nameStringOutput.Length == 0)
            {
                SendGenericNegativeMessageReply(command.Message, "Never Seen That User", $"User {EscapeUserInput(user.LastKnownUsername)} does not have any known names (never spoken here).");
            }
            else
            {
                SendGenericPositiveMessageReply(command.Message, "Seen Usernames", $"User {EscapeUserInput(user.LastKnownUsername)} has the following known usernames:\n{builders[0]}");
                for (int i = 1; i < builders.Count; i++)
                {
                    if (i == 2 && builders.Count > 4)
                    {
                        SendGenericPositiveMessageReply(command.Message, "Too Many Usernames", $"...(Skipped {(builders.Count - 3)} paragraphs of names)...");
                        continue;
                    }
                    if (i > 2 && i + 1 < builders.Count)
                    {
                        continue;
                    }
                    SendGenericPositiveMessageReply(command.Message, "Seen Usernames Continued", $"... continued:\n{builders[i]}");
                }
            }
        }
    }
}
