using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading;
using DiscordBotBase.CommandHandlers;
using Discord;
using Discord.WebSocket;
using DiscordBotBase;
using ModBot.Core;
using FreneticUtilities.FreneticExtensions;
using ModBot.Database;
using ModBot.WarningHandlers;

namespace ModBot.CommandHandlers
{
    /// <summary>
    /// Commands for admin usage only.
    /// </summary>
    public class AdminCommands : UserCommands
    {
        /// <summary>
        /// Outputs an ASCII name rule test name.
        /// </summary>
        public void CMD_TestName(CommandData command)
        {
            if (!DiscordModBot.IsBotCommander(command.Message.Author as SocketGuildUser))
            {
                SendErrorMessageReply(command.Message, "Not Authorized", "You're not allowed to do that.");
                return;
            }
            string name = NameUtilities.GenerateAsciiName(string.Join(" ", command.CleanedArguments));
            SendGenericPositiveMessageReply(command.Message, "Test Name", $"Test of ASCII-Name-Rule name generator: {name}");
        }

        /// <summary>
        /// User command to sweep through all current names.
        /// </summary>
        public void CMD_Sweep(CommandData command)
        {
            if (!DiscordModBot.IsBotCommander(command.Message.Author as SocketGuildUser))
            {
                SendErrorMessageReply(command.Message, "Not Authorized", "You're not allowed to do that.");
                return;
            }
            SocketGuildChannel channel = command.Message.Channel as SocketGuildChannel;
            channel.Guild.DownloadUsersAsync().Wait();
            foreach (SocketGuildUser user in channel.Guild.Users)
            {
                if (NameUtilities.AsciiNameRuleCheck(command.Message, user))
                {
                    Thread.Sleep(400);
                }
            }
        }

        /// <summary>
        /// Admin command to configure guild settings.
        /// </summary>
        public void CMD_AdminConfigure(CommandData command)
        {
            SocketGuildUser user = command.Message.Author as SocketGuildUser;
            SocketGuild guild = user.Guild;
            if (user.Guild.OwnerId != user.Id && !user.Roles.Any(r => r.Permissions.Administrator))
            {
                SendErrorMessageReply(command.Message, "Not Authorized", "You're not allowed to do that.");
                return;
            }
            ModBotDatabaseHandler.Guild guildDB = DiscordModBot.DatabaseHandler.GetDatabase(guild.Id);
            GuildConfig config = guildDB.Config;
            string subCmd = command.RawArguments.IsEmpty() ? "help" : command.RawArguments[0].ToLowerFast();
            void SendHelpInfo(string description, string currentValue)
            {
                SendReply(command.Message, new EmbedBuilder().WithTitle($"Admin-Configure '{subCmd}'").WithColor(255, 128, 0).WithDescription($"Option '{subCmd}': {description}").AddField("Current Setting", $"`{currentValue}`").Build());
            }
            switch (subCmd)
            {
                case "mute_role":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("The ID of the role that indicates a user is muted. Is used by the `INSTANT_MUTE` warning level and the `unmute` command.", config.MuteRole.HasValue ? config.MuteRole.Value.ToString() : "null");
                            return;
                        }
                        string roleText = command.RawArguments[1];
                        if (roleText == "none" || roleText == "null")
                        {
                            config.MuteRole = null;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Mute role is now disabled.");
                        }
                        else if (ulong.TryParse(roleText, out ulong roleId))
                        {
                            SocketRole role = guild.GetRole(roleId);
                            if (role == null)
                            {
                                SendErrorMessageReply(command.Message, "Invalid Value", $"Role id `{roleId}` is not valid.");
                                return;
                            }
                            config.MuteRole = role.Id;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Mute role is now {config.MuteRole}.");
                        }
                        else
                        {
                            SendErrorMessageReply(command.Message, "Invalid Value", "Argument must be a Role ID, or 'none'.");
                            return;
                        }
                        break;
                    }
                case "moderator_roles":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("The IDs of the roles that indicate a user is a moderator. Only moderators can use warning commands. Format is a comma-separated list of IDs.", config.ModeratorRoles.IsEmpty() ? "None" : string.Join(",", config.ModeratorRoles));
                            return;
                        }
                        string roleText = command.RawArguments[1];
                        if (roleText == "none" || roleText == "null")
                        {
                            config.ModeratorRoles.Clear();
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Moderator role list emptied.");
                        }
                        else
                        {
                            try
                            {
                                config.ModeratorRoles = roleText.SplitFast(',').Select(s => ulong.Parse(s)).ToList();
                            }
                            catch (FormatException)
                            {
                                SendErrorMessageReply(command.Message, "Invalid Value", "Argument must be a comma-separated list of Role IDs, or 'none'.");
                                return;
                            }
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Moderator role list updated.");
                        }
                        break;
                    }
                case "attention_notice":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("The message to append when a user is auto-muted or in other special cases, to get the attention of an admin or similar.", config.AttentionNotice ?? "null");
                            return;
                        }
                        string firstArg = command.RawArguments[1];
                        if (firstArg == "none" || firstArg == "null")
                        {
                            config.ModeratorRoles.Clear();
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Attention notice disabled.");
                        }
                        else
                        {
                            config.AttentionNotice = string.Join(" ", command.RawArguments.Skip(1));
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Attention notice is now: {config.AttentionNotice}");
                        }
                        break;
                    }
                case "incident_channel":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("The channel(s) that should show mute notices for users. Format is a comma-separated list of IDs.", config.IncidentChannel.IsEmpty() ? "None" : string.Join(",", config.IncidentChannel));
                            return;
                        }
                        string channelText = command.RawArguments[1];
                        if (channelText == "none" || channelText == "null")
                        {
                            config.IncidentChannel.Clear();
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Incident channel list emptied.");
                        }
                        else
                        {
                            try
                            {
                                config.IncidentChannel = channelText.SplitFast(',').Select(s => ulong.Parse(s)).ToList();
                            }
                            catch (FormatException)
                            {
                                SendErrorMessageReply(command.Message, "Invalid Value", "Argument must be a comma-separated list of channel IDs, or 'none'.");
                                return;
                            }
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Incident channel list updated.");
                        }
                        break;
                    }
                case "join_notif_channel":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("The channel(s) that should show user join/leave notices. Format is a comma-separated list of IDs.", config.JoinNotifChannel.IsEmpty() ? "None" : string.Join(",", config.JoinNotifChannel));
                            return;
                        }
                        string channelText = command.RawArguments[1];
                        if (channelText == "none" || channelText == "null")
                        {
                            config.JoinNotifChannel.Clear();
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Join-Notif channel list emptied.");
                        }
                        else
                        {
                            try
                            {
                                config.JoinNotifChannel = channelText.SplitFast(',').Select(s => ulong.Parse(s)).ToList();
                            }
                            catch (FormatException)
                            {
                                SendErrorMessageReply(command.Message, "Invalid Value", "Argument must be a comma-separated list of channel IDs, or 'none'.");
                                return;
                            }
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Join-Notif channel list updated.");
                        }
                        break;
                    }
                case "voice_channel_join_notif_channel":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("The channel(s) that should show user voice channel join/leave/move notices. Format is a comma-separated list of IDs.", config.VoiceChannelJoinNotifs.IsEmpty() ? "None" : string.Join(",", config.VoiceChannelJoinNotifs));
                            return;
                        }
                        string channelText = command.RawArguments[1];
                        if (channelText == "none" || channelText == "null")
                        {
                            config.VoiceChannelJoinNotifs.Clear();
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Voice-Join-Notif channel list emptied.");
                        }
                        else
                        {
                            try
                            {
                                config.VoiceChannelJoinNotifs = channelText.SplitFast(',').Select(s => ulong.Parse(s)).ToList();
                            }
                            catch (FormatException)
                            {
                                SendErrorMessageReply(command.Message, "Invalid Value", "Argument must be a comma-separated list of channel IDs, or 'none'.");
                                return;
                            }
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Voice-Join-Notif channel list updated.");
                        }
                        break;
                    }
                case "role_change_notif_channel":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("The channel(s) that should show user role change notices. Format is a comma-separated list of IDs.", config.RoleChangeNotifChannel.IsEmpty() ? "None" : string.Join(",", config.RoleChangeNotifChannel));
                            return;
                        }
                        string channelText = command.RawArguments[1];
                        if (channelText == "none" || channelText == "null")
                        {
                            config.RoleChangeNotifChannel.Clear();
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Role-Change-Notif channel list emptied.");
                        }
                        else
                        {
                            try
                            {
                                config.RoleChangeNotifChannel = channelText.SplitFast(',').Select(s => ulong.Parse(s)).ToList();
                            }
                            catch (FormatException)
                            {
                                SendErrorMessageReply(command.Message, "Invalid Value", "Argument must be a comma-separated list of channel IDs, or 'none'.");
                                return;
                            }
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Role-Change-Notif channel list updated.");
                        }
                        break;
                    }
                case "name_change_notif_channel":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("The channel(s) that should show username/nickname change notices. Format is a comma-separated list of IDs.", config.NameChangeNotifChannel.IsEmpty() ? "None" : string.Join(",", config.NameChangeNotifChannel));
                            return;
                        }
                        string channelText = command.RawArguments[1];
                        if (channelText == "none" || channelText == "null")
                        {
                            config.NameChangeNotifChannel.Clear();
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Name-Change-Notif channel list emptied.");
                        }
                        else
                        {
                            try
                            {
                                config.NameChangeNotifChannel = channelText.SplitFast(',').Select(s => ulong.Parse(s)).ToList();
                            }
                            catch (FormatException)
                            {
                                SendErrorMessageReply(command.Message, "Invalid Value", "Argument must be a comma-separated list of channel IDs, or 'none'.");
                                return;
                            }
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Name-Change-Notif channel list updated.");
                        }
                        break;
                    }
                case "mod_logs_channel":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("The channel(s) that should show moderator action logs. Format is a comma-separated list of IDs.", config.ModLogsChannel.IsEmpty() ? "None" : string.Join(",", config.ModLogsChannel));
                            return;
                        }
                        string channelText = command.RawArguments[1];
                        if (channelText == "none" || channelText == "null")
                        {
                            config.ModLogsChannel.Clear();
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Mod-Logs channel list emptied.");
                        }
                        else
                        {
                            try
                            {
                                config.ModLogsChannel = channelText.SplitFast(',').Select(s => ulong.Parse(s)).ToList();
                            }
                            catch (FormatException)
                            {
                                SendErrorMessageReply(command.Message, "Invalid Value", "Argument must be a comma-separated list of channel IDs, or 'none'.");
                                return;
                            }
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Mod-Logs channel list updated.");
                        }
                        break;
                    }
                case "log_channels":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("The mapping of channels, where keys are normal text channels (or categories), and values are a logs channel where message edit/deletes in the first channel get logged to."
                                + " Format is a comma-separated list of colon-separated ID:ID pairs (like `123:456,789:012`). Set key `0` as a catch-all.",
                                config.LogChannels.IsEmpty() ? "None" : string.Join(",", config.LogChannels.Select(pair => $"{pair.Key}:{pair.Value}")));
                            return;
                        }
                        string channelText = command.RawArguments[1];
                        if (channelText == "none" || channelText == "null")
                        {
                            config.LogChannels.Clear();
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Logs channel map emptied.");
                        }
                        else
                        {
                            try
                            {
                                config.LogChannels = channelText.SplitFast(',').Select(s => new KeyValuePair<ulong, ulong>(ulong.Parse(s.BeforeAndAfter(':', out string after)), ulong.Parse(after))).ToDictionary(p => p.Key, p => p.Value);
                            }
                            catch (FormatException)
                            {
                                SendErrorMessageReply(command.Message, "Invalid Value", "Argument must be a comma-separated list of colon-separated ID:ID pairs, or 'none'.");
                                return;
                            }
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Logs channel map updated.");
                        }
                        break;
                    }
                case "enforce_ascii_name_rule":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("When true, the ASCII-only name rule is enforced.", config.EnforceAsciiNameRule ? "true" : "false");
                            return;
                        }
                        if (command.RawArguments[1] == "true")
                        {
                            config.EnforceAsciiNameRule = true;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Enforce-ASCII-Name-Rule is now enabled.");
                        }
                        else if (command.RawArguments[1] == "false")
                        {
                            config.EnforceAsciiNameRule = false;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Enforce-ASCII-Name-Rule is now disabled.");
                        }
                        else
                        {
                            SendErrorMessageReply(command.Message, "Invalid Value", "New value must be `true` or `false` only.");
                            return;
                        }
                        break;
                    }
                case "enforce_name_start_rule":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("When true, the 'names must start with a letter' rule is enforced.", config.EnforceNameStartRule ? "true" : "false");
                            return;
                        }
                        if (command.RawArguments[1] == "true")
                        {
                            config.EnforceNameStartRule = true;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Enforce-Name-Start-Rule is now enabled.");
                        }
                        else if (command.RawArguments[1] == "false")
                        {
                            config.EnforceNameStartRule = false;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Enforce-Name-Start-Rule is now disabled.");
                        }
                        else
                        {
                            SendErrorMessageReply(command.Message, "Invalid Value", "New value must be `true` or `false` only.");
                            return;
                        }
                        break;
                    }
                case "name_start_rule_lenient":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("When true, and 'enforce_name_start_rule' is also true, the 'names must start with a letter' rule is extra lenient, and allows numbers or unicode symbols in addition to names.", config.NameStartRuleLenient ? "true" : "false");
                            return;
                        }
                        if (command.RawArguments[1] == "true")
                        {
                            config.NameStartRuleLenient = true;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Name-Start-Rule-Leniency is now enabled.");
                        }
                        else if (command.RawArguments[1] == "false")
                        {
                            config.NameStartRuleLenient = false;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Name-Start-Rule-Leniency is now disabled.");
                        }
                        else
                        {
                            SendErrorMessageReply(command.Message, "Invalid Value", "New value must be `true` or `false` only.");
                            return;
                        }
                        break;
                    }
                case "warnings_enabled":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("Whether warnings are enabled at all.", config.WarningsEnabled ? "true" : "false");
                            return;
                        }
                        if (command.RawArguments[1] == "true")
                        {
                            config.WarningsEnabled = true;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Warnings are now enabled.");
                        }
                        else if (command.RawArguments[1] == "false")
                        {
                            config.WarningsEnabled = false;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Warnings are now disabled.");
                        }
                        else
                        {
                            SendErrorMessageReply(command.Message, "Invalid Value", "New value must be `true` or `false` only.");
                            return;
                        }
                        break;
                    }
                case "bans_enabled":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("Whether (temp)bans are enabled at all.", config.BansEnabled ? "true" : "false");
                            return;
                        }
                        if (command.RawArguments[1] == "true")
                        {
                            config.BansEnabled = true;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Bans are now enabled.");
                        }
                        else if (command.RawArguments[1] == "false")
                        {
                            config.BansEnabled = false;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Bans are now disabled.");
                        }
                        else
                        {
                            SendErrorMessageReply(command.Message, "Invalid Value", "New value must be `true` or `false` only.");
                            return;
                        }
                        break;
                    }
                case "max_ban_duration":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("The maximum tempban duration allowed (if any).", config.MaxBanDuration ?? "(unset)");
                            return;
                        }
                        if (command.RawArguments[1].ToLowerFast() == "none")
                        {
                            config.MaxBanDuration = null;
                            break;
                        }
                        TimeSpan? duration = WarningUtilities.ParseDuration(command.RawArguments[1]);
                        if (duration == null)
                        {
                            SendErrorMessageReply(command.Message, "Invalid Value", "Can be 'none', or a duration. A duration must be formatted like '1d' (for 1 day). Allowed type: 'h' for hours, 'd' for days, 'w' for weeks, 'm' for months, 'y' for years.");
                            return;
                        }
                        config.MaxBanDuration = command.RawArguments[1];
                        SendGenericPositiveMessageReply(command.Message, "Applied", $"Max ban duration set to ${duration.Value.SimpleFormat(false)}.");
                        break;
                    }
                case "notify_warns_in_dm":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("Whether warnings (when enabled) should send a notification to the warned user via Direct Message with the warning text.", config.NotifyWarnsInDM ? "true" : "false");
                            return;
                        }
                        if (command.RawArguments[1] == "true")
                        {
                            config.NotifyWarnsInDM = true;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"DM'd warning notifications are now enabled.");
                        }
                        else if (command.RawArguments[1] == "false")
                        {
                            config.NotifyWarnsInDM = false;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"DM'd warning notifications are now disabled.");
                        }
                        else
                        {
                            SendErrorMessageReply(command.Message, "Invalid Value", "New value must be `true` or `false` only.");
                            return;
                        }
                        break;
                    }
                case "add_special_role":
                    {
                        if (command.RawArguments.Length < 5)
                        {
                            SendHelpInfo("This command can be used to add a special role, with the format `add_special_role (name) (RoleID) (AddCommands) (RemoveCommands)` ... "
                                + "if the role should auto-warn, append also newline-separated `(AddLevel) (AddWarnText) (AddExplanation) (RemoveLevel) (RemoveWarnText) (RemoveExplanation)`\n"
                                + "For example, `add_special_role do-not-support 12345 donotsupport,nosupport,bad allowsupport,removenosupport,good\nNORMAL\nDo-Not-Support status applied\nYou are marked as do-not-support now\nNOTE\nDo-Not-Support status rescinded\nYou are allowed support again`", string.Join(", ", config.SpecialRoles.Keys));
                            return;
                        }
                        string name = command.RawArguments[1].ToLowerFast();
                        if (config.SpecialRoles.ContainsKey(name))
                        {
                            SendErrorMessageReply(command.Message, "Duplicate Role", "That special role already exists.");
                            return;
                        }
                        GuildConfig.SpecialRole role = new GuildConfig.SpecialRole() { Name = name };
                        string roleIdText = command.RawArguments[2];
                        if (!ulong.TryParse(roleIdText, out ulong roleId))
                        {
                            SendErrorMessageReply(command.Message, "Invalid Value", "That Role ID input is not a valid number.");
                            return;
                        }
                        if (guild.GetRole(roleId) == null)
                        {
                            SendErrorMessageReply(command.Message, "Invalid Value", "That Role ID doesn't exist.");
                            return;
                        }
                        role.RoleID = roleId;
                        role.AddCommands = command.RawArguments[3].SplitFast(',').Select(s => s.ToLowerFast()).ToList();
                        role.RemoveCommands = command.RawArguments[4].Before("\n").SplitFast(',').Select(s => s.ToLowerFast()).ToList();
                        if (command.RawArguments.Length > 5)
                        {
                            string[] reSplitArguments = string.Join(" ", command.RawArguments).SplitFast('\n').Skip(1).Select(s => s.Trim().Replace("\\n", "\n")).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                            if (reSplitArguments.Length >= 3)
                            {
                                if (!Enum.TryParse(reSplitArguments[0], out WarningLevel addLevel))
                                {
                                    SendErrorMessageReply(command.Message, "Invalid Value", $"That role add warn level, `{reSplitArguments[0]}` is invalid.");
                                    return;
                                }
                                role.AddLevel = addLevel;
                                role.AddWarnText = reSplitArguments[1];
                                role.AddExplanation = reSplitArguments[2];
                                if (reSplitArguments.Length >= 6)
                                {
                                    if (!Enum.TryParse(reSplitArguments[3], out WarningLevel removeLevel))
                                    {
                                        SendErrorMessageReply(command.Message, "Invalid Value", $"That role remove warn level, `{reSplitArguments[0]}` is invalid.");
                                        return;
                                    }
                                    role.RemoveLevel = removeLevel;
                                    role.RemoveWarnText = reSplitArguments[4];
                                    role.RemoveExplanation = reSplitArguments[5];
                                }
                            }
                        }
                        config.SpecialRoles.Add(role.Name, role);
                        string addCommandsMessage = string.Join(",", role.AddCommands);
                        string removeCommandsMessage = string.Join(",", role.RemoveCommands);
                        SendGenericPositiveMessageReply(command.Message, "Added", $"Special role `{name}` added.\nRole ID: `{roleId}`\nAdd commands: `{addCommandsMessage}`\nRemove commands: `{removeCommandsMessage}`");
                        break;
                    }
                case "remove_special_role":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("This command can be used to remove a special role by name.", string.Join(", ", config.SpecialRoles.Keys));
                            return;
                        }
                        string name = command.RawArguments[1].ToLowerFast();
                        if (config.SpecialRoles.ContainsKey(name))
                        {
                            config.SpecialRoles.Remove(name);
                            SendGenericPositiveMessageReply(command.Message, "Removed", $"Special role `{name}` removed.");
                        }
                        else
                        {
                            SendErrorMessageReply(command.Message, "Missing Role", "Can't remove that special role: it doesn't exist.");
                            return;
                        }
                        break;
                    }
                default:
                    {
                        EmbedBuilder embed = new EmbedBuilder().WithTitle("Admin-Configure Usage Help").WithColor(255, 128, 0);
                        embed.Description = "ModBot's admin-configure command exists as a temporary trick pending plans to build a web interface to control ModBot more easily."
                            + "\nAny sub-command without further arguments will show more info about current value.\nMost sub-command accept `null` to mean remove/clear any value (except where not possible).";
                        embed.AddField("Available configure sub-commands", "`mute_role`, `moderator_roles`, `attention_notice`, `incident_channel`, `join_notif_channel`, "
                            + "`voice_channel_join_notif_channel`, `role_change_notif_channel`, `name_change_notif_channel`, `mod_logs_channel`, `log_channels`, "
                            + "`enforce_ascii_name_rule`, `enforce_name_start_rule`, `name_start_rule_lenient`, `warnings_enabled`, `bans_enabled`, `max_ban_duration`, `notify_warns_in_dm`, `add_special_role`, `remove_special_role`");
                        SendReply(command.Message, embed.Build());
                        return;
                    }
            }
            guildDB.SaveConfig();
        }
    }
}
