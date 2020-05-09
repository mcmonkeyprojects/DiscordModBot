using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using DiscordBotBase;
using DiscordBotBase.CommandHandlers;
using Discord;
using Discord.WebSocket;
using FreneticUtilities.FreneticToolkit;
using FreneticUtilities.FreneticExtensions;
using DiscordBotBase.Reactables;

namespace DiscordModBot.CommandHandlers
{
    /// <summary>
    /// Commands related to handling warnings and notes.
    /// </summary>
    public class WarningCommands : UserCommands
    {
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
        /// User command to temporarily ban a user.
        /// </summary>
        public void CMD_TempBan(string[] cmds, IUserMessage message)
        {
            if (!DiscordModBot.IsHelper(message.Author as SocketGuildUser))
            {
                SendErrorMessageReply(message, "Not Authorized", "You're not allowed to do that.");
                return;
            }
            if ((message.MentionedUserIds.Count() < 2 && cmds.Length < 2) || cmds.Length < 1)
            {
                SendErrorMessageReply(message, "Invalid Input", "Usage: tempban [user] [duration]");
                return;
            }
            if (!DiscordModBot.WarningCommandHandler.GetTargetUser(cmds, message, out ulong userID))
            {
                return;
            }
            SocketGuildUser guildUser = (message.Channel as SocketGuildChannel).GetUser(userID);
            if (guildUser != null && DiscordModBot.IsHelper(guildUser))
            {
                SendErrorMessageReply(message, "I Can't Let You Do That", "That user is too powerful to be banned.");
                return;
            }
            string durationText = cmds[message.MentionedUserIds.Count == 2 ? 0 : 1];
            TimeSpan realDuration;
            if (durationText.EndsWith("h") && double.TryParse(durationText.Before('h'), out double hours))
            {
                realDuration = TimeSpan.FromHours(hours);
            }
            else if (durationText.EndsWith("d") && double.TryParse(durationText.Before('d'), out double days))
            {
                realDuration = TimeSpan.FromDays(days);
            }
            else if (durationText.EndsWith("w") && double.TryParse(durationText.Before('w'), out double weeks))
            {
                realDuration = TimeSpan.FromDays(weeks * 7);
            }
            else if (durationText.EndsWith("m") && double.TryParse(durationText.Before('m'), out double months))
            {
                realDuration = TimeSpan.FromDays(months * 31);
            }
            else if (durationText.EndsWith("y") && double.TryParse(durationText.Before('y'), out double years))
            {
                realDuration = TimeSpan.FromDays(years * 365);
            }
            else
            {
                SendErrorMessageReply(message, "Invalid Input", "Duration must be formatted like '1d' (for 1 day). Allowed type: 'h' for hours, 'd' for days, 'w' for weeks, 'm' for months, 'y' for years.");
                return;
            }
            if (realDuration.TotalMinutes < 1)
            {
                SendErrorMessageReply(message, "Invalid Input", "Duration must be a positive value greater than one minute.");
                return;
            }
            if (realDuration.TotalDays > 365 * 2)
            {
                SendErrorMessageReply(message, "Invalid Input", "Duration must be less than 2 years.");
                return;
            }
            DiscordModBot.TempBanHandler.TempBan((message.Channel as SocketGuildChannel).Guild.Id, userID, realDuration);
            string durationFormat = realDuration.SimpleFormat(false);
            ModBotLoggers.SendEmbedToAllFor((message.Channel as SocketGuildChannel).Guild, DiscordModBot.ModLogsChannel, new EmbedBuilder().WithTitle("User Temporarily Banned").WithColor(255, 0, 0).WithDescription($"User <@{userID}> was temporarily banned for {durationFormat}.").Build());
            SendGenericPositiveMessageReply(message, "Temporary Ban Applied", $"<@{message.Author.Id}> has temporarily banned <@{userID}> for {durationFormat}.");
        }

        /// <summary>
        /// User command to remove a user's muted status.
        /// </summary>
        public void CMD_Unmute(string[] cmds, IUserMessage message)
        {
            if (!DiscordModBot.IsHelper(message.Author as SocketGuildUser))
            {
                SendErrorMessageReply(message, "Not Authorized", "You're not allowed to do that.");
                return;
            }
            if (!DiscordModBot.WarningCommandHandler.GetTargetUser(cmds, message, out ulong userID))
            {
                return;
            }
            SocketGuildUser guildUserToUnmute = (message.Channel as SocketGuildChannel).GetUser(userID);
            WarnableUser warnable = WarningUtilities.GetWarnableUser((message.Channel as IGuildChannel).GuildId, userID);
            bool wasMuted = false;
            if (warnable.IsMuted)
            {
                wasMuted = true;
                warnable.IsMuted = false;
                warnable.Save();
            }
            if (guildUserToUnmute != null)
            {
                IRole role = guildUserToUnmute.Roles.FirstOrDefault((r) => r.Name.ToLowerInvariant() == DiscordModBot.MuteRoleName);
                if (role != null)
                {
                    guildUserToUnmute.RemoveRoleAsync(role).Wait();
                    wasMuted = true;
                }
            }
            if (wasMuted)
            {
                SendGenericPositiveMessageReply(message, "Unmuted", $"<@{message.Author.Id}> has unmuted <@{userID}>.");
                ModBotLoggers.SendEmbedToAllFor((message.Channel as SocketGuildChannel).Guild, DiscordModBot.ModLogsChannel, new EmbedBuilder().WithTitle("User Unmuted").WithColor(0, 255, 0).WithDescription($"User <@{userID}> was unmuted.").Build());
            }
            else
            {
                SendGenericNegativeMessageReply(message, "Cannot Unmute", $"User {warnable.LastKnownUsername} is already not muted.");
            }
        }

        /// <summary>
        /// User command to mark a user as do-not-support.
        /// </summary>
        public void CMD_DoNotSupport(string[] cmds, IUserMessage message)
        {
            if (!DiscordModBot.IsHelper(message.Author as SocketGuildUser))
            {
                SendErrorMessageReply(message, "Not Authorized", "You're not allowed to do that.");
                return;
            }
            if (!DiscordModBot.WarningCommandHandler.GetTargetUser(cmds, message, out ulong userID))
            {
                return;
            }
            SocketGuildUser guildUser = (message.Channel as SocketGuildChannel).GetUser(userID);
            WarnableUser warnable = WarningUtilities.GetWarnableUser((message.Channel as IGuildChannel).GuildId, userID);
            bool wasDNS = warnable.IsDoNotSupport;
            if (!wasDNS)
            {
                warnable.IsDoNotSupport = true;
                warnable.Save();
            }
            if (guildUser != null)
            {
                IRole role = guildUser.Guild.Roles.FirstOrDefault((r) => r.Name.ToLowerInvariant() == DiscordModBot.DoNotSupportRoleName);
                if (role == null)
                {
                    SendErrorMessageReply(message, "Failed To Apply", "Cannot apply Do-Not-Support: no matching role found.");
                    return;
                }
                guildUser.AddRoleAsync(role).Wait();
            }
            if (!wasDNS)
            {
                Warning warning = new Warning() { GivenTo = userID, GivenBy = message.Author.Id, TimeGiven = DateTimeOffset.UtcNow, Level = WarningLevel.NOTE };
                warning.Reason = "Marked as Do-Not-Support. User should not receive support unless this status is rescinded.";
                IUserMessage sentMessage = message.Channel.SendMessageAsync(embed: new EmbedBuilder().WithTitle("Do Not Support Status Applied").WithDescription($"<@{message.Author.Id}> has marked <@{userID}> as do-not-support.\n{DiscordModBot.DoNotSupportMessage}").Build()).Result;
                warning.Link = LinkToMessage(sentMessage);
                WarningUtilities.Warn((message.Channel as SocketGuildChannel).Guild.Id, userID, warning);
            }
            else
            {
                SendGenericNegativeMessageReply(message, "Cannot Apply", $"User {warnable.LastKnownUsername} is already marked as Do-Not-Support.");
            }
        }

        /// <summary>
        /// User command to remove a Do-Not-Support status from a user.
        /// </summary>
        public void CMD_RemoveDoNotSupport(string[] cmds, IUserMessage message)
        {
            if (!DiscordModBot.IsHelper(message.Author as SocketGuildUser))
            {
                SendErrorMessageReply(message, "Not Authorized", "You're not allowed to do that.");
                return;
            }
            if (!DiscordModBot.WarningCommandHandler.GetTargetUser(cmds, message, out ulong userID))
            {
                return;
            }
            SocketGuildUser guildUser = (message.Channel as SocketGuildChannel).GetUser(userID);
            WarnableUser warnable = WarningUtilities.GetWarnableUser((message.Channel as IGuildChannel).GuildId, userID);
            bool wasDNS = warnable.IsDoNotSupport;
            if (wasDNS)
            {
                warnable.IsDoNotSupport = false;
                warnable.Save();
            }
            if (guildUser != null)
            {
                IRole role = guildUser.Roles.FirstOrDefault((r) => r.Name.ToLowerInvariant() == DiscordModBot.DoNotSupportRoleName);
                if (role != null)
                {
                    guildUser.RemoveRoleAsync(role).Wait();
                    wasDNS = true;
                }
            }
            if (wasDNS)
            {
                Warning warning = new Warning() { GivenTo = userID, GivenBy = message.Author.Id, TimeGiven = DateTimeOffset.UtcNow, Level = WarningLevel.NOTE };
                warning.Reason = "Do-not-support status rescinded. The user may receive help going forward.";
                IUserMessage sentMessage = message.Channel.SendMessageAsync(embed: new EmbedBuilder().WithTitle("Do Not Support Status Removed").WithDescription($"<@{message.Author.Id}> has rescinded the DoNotSupport status of <@{userID}>.\nYou are now allowed to receive support.").Build()).Result;
                warning.Link = LinkToMessage(sentMessage);
                WarningUtilities.Warn((message.Channel as SocketGuildChannel).Guild.Id, userID, warning);
            }
            else
            {
                SendGenericNegativeMessageReply(message, "Cannot Remove", $"User {warnable.LastKnownUsername} already is not marked as Do-Not-Support.");
            }
        }

        /// <summary>
        /// User command to add a note to a user.
        /// </summary>
        public void CMD_Note(string[] cmds, IUserMessage message)
        {
            if (!DiscordModBot.IsHelper(message.Author as SocketGuildUser))
            {
                SendErrorMessageReply(message, "Not Authorized", "You're not allowed to do that.");
                return;
            }
            if (message.MentionedUserIds.Count() < 2 && cmds.Length < 2)
            {
                SendErrorMessageReply(message, "Invalid Input", "Usage: note [user] [message]");
                return;
            }
            if (!DiscordModBot.WarningCommandHandler.GetTargetUser(cmds, message, out ulong userID))
            {
                return;
            }
            IEnumerable<string> cmdsToSave = message.MentionedUserIds.Count == 2 ? cmds : cmds.Skip(1);
            Warning warning = new Warning() { GivenTo = userID, GivenBy = message.Author.Id, TimeGiven = DateTimeOffset.UtcNow, Level = WarningLevel.NOTE };
            warning.Reason = EscapeUserInput(string.Join(" ", cmdsToSave));
            IUserMessage sentMessage = message.Channel.SendMessageAsync(embed: new EmbedBuilder().WithTitle("Note Recorded").WithDescription($"Note from <@{message.Author.Id}> to <@{userID}> recorded.").Build()).Result;
            warning.Link = LinkToMessage(sentMessage);
            WarningUtilities.Warn((message.Channel as SocketGuildChannel).Guild.Id, userID, warning);
        }

        /// <summary>
        /// User command to give a warning to a user.
        /// </summary>
        public void CMD_Warn(string[] cmds, IUserMessage message)
        {
            if (!DiscordModBot.IsHelper(message.Author as SocketGuildUser))
            {
                SendErrorMessageReply(message, "Not Authorized", "You're not allowed to do that.");
                return;
            }
            if (message.MentionedUserIds.Count() < 2 && cmds.Length < 2)
            {
                SendErrorMessageReply(message, "Invalid Input", "Usage: warn [user] [level] [reason] - Valid levels: `minor`, `normal`, `serious`, or `instant_mute`");
                return;
            }
            if (!DiscordModBot.WarningCommandHandler.GetTargetUser(cmds, message, out ulong userID))
            {
                return;
            }
            int cmdPos = message.MentionedUserIds.Count == 2 ? 0 : 1;
            if (cmds.Length <= cmdPos || !LevelsTypable.TryGetValue(cmds[cmdPos].ToLowerInvariant(), out WarningLevel level))
            {
                SendErrorMessageReply(message, "Invalid Input", "Unknown level. Valid levels: `minor`, `normal`, `serious`, or `instant_mute`.");
                return;
            }
            WarnableUser warnUser = WarningUtilities.GetWarnableUser((message.Channel as SocketGuildChannel).Guild.Id, userID);
            int warningCount = warnUser.GetWarnings().Count();
            string pastWarningsText = warningCount == 0 ? "" : $"\nUser has {warningCount} previous warnings or notes.";
            Warning warning = new Warning() { GivenTo = userID, GivenBy = message.Author.Id, TimeGiven = DateTimeOffset.UtcNow, Level = level };
            warning.Reason = EscapeUserInput(string.Join(" ", cmds.Skip(1)));
            IUserMessage sentMessage = message.Channel.SendMessageAsync(embed: new EmbedBuilder().WithTitle("Warning Recorded").WithDescription($"Warning from <@{message.Author.Id}> to <@{userID}> recorded.\nReason: {warning.Reason}{pastWarningsText}").Build()).Result;
            warning.Link = LinkToMessage(sentMessage);
            lock (WarningUtilities.WarnLock)
            {
                warnUser.AddWarning(warning);
            }
            SocketGuildUser socketUser = (message.Channel as SocketGuildChannel).GetUser(userID);
            if (socketUser != null)
            {
                PossibleMute(socketUser, message, level);
            }
            else if (level == WarningLevel.INSTANT_MUTE)
            {
                lock (WarningUtilities.WarnLock)
                {
                    warnUser.IsMuted = true;
                    warnUser.Save();
                }
                SendGenericPositiveMessageReply(message, "Mute Recorded", "Mute applied for next rejoin.");
            }
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
        public static void PossibleMute(IGuildUser user, IUserMessage message, WarningLevel newLevel)
        {
            if (WarningUtilities.IsMuted(user))
            {
                return;
            }
            bool needsMute = newLevel == WarningLevel.INSTANT_MUTE;
            int normalWarns = 0;
            int seriousWarns = 0;
            if (newLevel == WarningLevel.NORMAL || newLevel == WarningLevel.SERIOUS)
            {
                double warningNeed = 0.0;
                foreach (Warning oldWarn in WarningUtilities.GetWarnableUser((message.Channel as SocketGuildChannel).Guild.Id, user.Id).GetWarnings())
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
                IRole role = user.Guild.Roles.FirstOrDefault((r) => r.Name.ToLowerInvariant() == DiscordModBot.MuteRoleName);
                if (role == null)
                {
                    SendErrorMessageReply(message, "Failed To Mute", "Cannot apply mute: no muted role found.");
                    return;
                }
                user.AddRoleAsync(role).Wait();
                WarnableUser warnable = WarningUtilities.GetWarnableUser(user.Guild.Id, user.Id);
                warnable.IsMuted = true;
                warnable.Save();
                string muteMessage = (newLevel == WarningLevel.INSTANT_MUTE ? "This mute was applied by moderator request (INSTANT_MUTE)." :
                    $"This mute was applied automatically due to have multiple warnings in a short period of time. User has {normalWarns} NORMAL and {seriousWarns} SERIOUS warnings within the past 30 days.")
                    + " You may not speak except in the incident handling channel."
                    + " This mute lasts until an administrator removes it, which may in some cases take a while."
                    + "\nAny user may review warnings against them at any time by typing `@ModBot listwarnings`.";
                message.Channel.SendMessageAsync($"User <@{user.Id}> has been muted automatically by the warning system.\n{DiscordModBot.AttentionNotice}", embed: new EmbedBuilder().WithTitle("Mute Notice").WithColor(255, 128, 0).WithDescription(muteMessage).Build());
                foreach (ulong id in DiscordModBot.IncidentChannel)
                {
                    SocketGuildChannel incidentChan = (user.Guild as SocketGuild).GetChannel(id);
                    if (incidentChan != null && incidentChan is ISocketMessageChannel incidentChanText)
                    {
                        incidentChanText.SendMessageAsync("<@" + user.Id + ">", embed: new EmbedBuilder().WithTitle("Mute Notice").WithColor(255, 128, 0)
                            .WithDescription("You have been automatically muted by the system due to warnings received. You may discuss the situation in this channel only, until a moderator unmutes you.").Build());
                        break;
                    }
                }
                ModBotLoggers.SendEmbedToAllFor(user.Guild as SocketGuild, DiscordModBot.ModLogsChannel, new EmbedBuilder().WithTitle("User Muted").WithColor(255, 0, 0).WithDescription($"User <@{user.Id}> was muted.").Build());
            }
        }

        /// <summary>
        /// User command to list user warnings.
        /// </summary>
        public void CMD_ListWarnings(string[] cmds, IUserMessage message)
        {
            ulong userID = message.Author.Id;
            if (DiscordModBot.IsHelper(message.Author as SocketGuildUser))
            {
                if (!DiscordModBot.WarningCommandHandler.GetTargetUser(cmds, message, out userID))
                {
                    return;
                }
            }
            if (!(cmds.Length == 2 && int.TryParse(cmds[1], out int min) || (cmds.Length == 1 && int.TryParse(cmds[0], out min))))
            {
                min = 1;
            }
            min--;
            if (min < 0)
            {
                SendErrorMessageReply(message, "Invalid Input", "Page input invalid.");
                return;
            }
            bool hasMore = false;
            WarnableUser user = WarningUtilities.GetWarnableUser((message.Channel as SocketGuildChannel).Guild.Id, userID);
            StringBuilder warnStringOutput = new StringBuilder();
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            int warnID = 0;
            foreach (Warning warned in user.GetWarnings().OrderByDescending(w => (int)w.Level).Skip(min * 5))
            {
                if (warnID == 5)
                {
                    warnStringOutput.Append($"... And more warnings. Click the {Constants.ACCEPT_EMOJI} react to show more.");
                    hasMore = true;
                    break;
                }
                warnID++;
                SocketUser giver = Bot.Client.GetUser(warned.GivenBy);
                string giverLabel = (giver == null) ? ("DiscordID:" + warned.GivenBy) : (giver.Username + "#" + giver.Discriminator);
                string reason = (warned.Reason.Length > 250) ? (warned.Reason.Substring(0, 250) + "(... trimmed ...)") : warned.Reason;
                reason = EscapeUserInput(reason);
                warnStringOutput.Append($"**... {warned.Level}{(warned.Level == WarningLevel.NOTE ? "" : " warning")}** given at `{StringConversionHelper.DateTimeToString(warned.TimeGiven, false)}` by {giverLabel} with reason: `{reason}`. [Click For Detail]({warned.Link})\n");
            }
            if (warnID == 0)
            {
                if (min > 0)
                {
                    SendGenericPositiveMessageReply(message, "Nothing Found", $"User {user.LastKnownUsername} does not have that page of warnings.");
                }
                else
                {
                    SendGenericPositiveMessageReply(message, "Nothing Found", $"User {user.LastKnownUsername} does not have any warnings logged.");
                }
            }
            else
            {
                int warnCount = user.GetWarnings().Count();
                IUserMessage sentMessage = message.Channel.SendMessageAsync(embed: GetGenericPositiveMessageEmbed($"{warnCount} Warnings Found", $"User {user.LastKnownUsername} has the following warnings logged:\n{warnStringOutput}")).Result;
                if (hasMore && sentMessage != null)
                {
                    sentMessage.AddReactionsAsync(new IEmote[] { new Emoji(Constants.ACCEPT_EMOJI), new Emoji(Constants.DENY_EMOJI) }).Wait();
                    ReactionsHandler.AddReactable(message, sentMessage, $"listwarn {userID} {min + 2}");
                }
            }
        }

        /// <summary>
        /// Utility method to get the target of a command that allows targeting commands at others instead of self.
        /// </summary>
        public bool GetTargetUser(string[] cmds, IUserMessage message, out ulong userId)
        {
            userId = message.Author.Id;
            IReadOnlyCollection<ulong> mentioned = message.MentionedUserIds;
            if (mentioned.Count == 1 && cmds.Length > 0)
            {
                if (!ulong.TryParse(cmds[0], out userId))
                {
                    SendErrorMessageReply(message, "Input Invalid", "Something went wrong - user ID not valid? Check that you properly input the user ID as the first argument.");
                    return false;
                }
            }
            else if (mentioned.Count == 2)
            {
                userId = mentioned.FirstOrDefault((su) => su != Bot.Client.CurrentUser.Id);
                if (userId == default)
                {
                    SendErrorMessageReply(message, "Input Invalid", "Something went wrong - user mention not valid?");
                    return false;
                }
            }
            else if (mentioned.Count > 2)
            {
                SendErrorMessageReply(message, "Input Invalid", "You must only `@` mention this bot and the user to target.");
                return false;
            }
            return true;
        }
    }
}
