using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using DiscordBotBase;
using DiscordBotBase.CommandHandlers;
using Discord;
using Discord.WebSocket;
using FreneticUtilities.FreneticToolkit;
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
            }
            else
            {
                SendGenericNegativeMessageReply(message, "Cannot Unmute", $"User {warnable.LastKnownUsername} is already not muted.");
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
            warning.Reason = string.Join(" ", cmdsToSave).Replace('\\', '/').Replace('`', '\'');
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
            warning.Reason = string.Join(" ", cmds.Skip(1)).Replace('\\', '/').Replace('`', '\'');
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
            int argPos = message.MentionedUserIds.Count == 2 ? 0 : 1;
            if ((cmds.Length <= argPos) || !int.TryParse(cmds[argPos], out int min))
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
                reason = reason.Replace('\\', '/').Replace('`', '\'');
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
                IUserMessage sentMessage = message.Channel.SendMessageAsync(embed: GetGenericPositiveMessageEmbed($"{warnID} Warnings Found", $"User {user.LastKnownUsername} has the following warnings logged:\n{warnStringOutput}")).Result;
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
