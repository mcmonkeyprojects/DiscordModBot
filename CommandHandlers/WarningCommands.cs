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
using ModBot.Database;
using ModBot.WarningHandlers;
using ModBot.Core;

namespace ModBot.CommandHandlers
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
        public void CMD_TempBan(CommandData command)
        {
            SocketGuild guild = (command.Message.Channel as SocketGuildChannel).Guild;
            GuildConfig config = DiscordModBot.GetConfig(guild.Id);
            if (!config.BansEnabled)
            {
                return;
            }
            if (!DiscordModBot.IsModerator(command.Message.Author as SocketGuildUser))
            {
                SendErrorMessageReply(command.Message, "Not Authorized", "You're not allowed to do that.");
                return;
            }
            if (command.RawArguments.Length < 2)
            {
                SendErrorMessageReply(command.Message, "Invalid Input", "Usage: tempban [user] [duration] ... Duration can be formatted like '1d' (for 1 day). Allowed type: 'h' for hours, 'd' for days, 'w' for weeks, 'm' for months, 'y' for years.");
                return;
            }
            if (!DiscordModBot.WarningCommandHandler.GetTargetUser(command, true, true, out ulong userID))
            {
                return;
            }
            SocketGuildUser guildUser = guild.GetUser(userID);
            if (guildUser != null && DiscordModBot.IsModerator(guildUser))
            {
                SendErrorMessageReply(command.Message, "I Can't Let You Do That", "That user is too powerful to be banned.");
                return;
            }
            WarnableUser warnable = WarningUtilities.GetWarnableUser(guild.Id, userID);
            if (warnable.SeenNames.IsEmpty())
            {
                SendErrorMessageReply(command.Message, "Invalid Input", "Cannot ban that user: user has never been seen before.");
                return;
            }
            string durationText = command.RawArguments[1];
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
                SendErrorMessageReply(command.Message, "Invalid Input", "Duration must be formatted like '1d' (for 1 day). Allowed type: 'h' for hours, 'd' for days, 'w' for weeks, 'm' for months, 'y' for years.");
                return;
            }
            if (realDuration.TotalMinutes < 1)
            {
                SendErrorMessageReply(command.Message, "Invalid Input", "Duration must be a positive value greater than one minute.");
                return;
            }
            if (realDuration.TotalDays > 365 * 2)
            {
                SendErrorMessageReply(command.Message, "Invalid Input", "Duration must be less than 2 years.");
                return;
            }
            DiscordModBot.TempBanHandler.TempBan(guild.Id, userID, realDuration);
            string durationFormat = realDuration.SimpleFormat(false);
            ModBotLoggers.SendEmbedToAllFor((command.Message.Channel as SocketGuildChannel).Guild, DiscordModBot.GetConfig(guild.Id).ModLogsChannel, new EmbedBuilder().WithTitle("User Temporarily Banned").WithColor(255, 0, 0).WithDescription($"User <@{userID}> was temporarily banned for {durationFormat}.").Build());
            IUserMessage banNotice = SendGenericPositiveMessageReply(command.Message, "Temporary Ban Applied", $"<@{command.Message.Author.Id}> has temporarily banned <@{userID}> for {durationFormat}.");
            Warning warning = new Warning() { GivenTo = userID, GivenBy = command.Message.Author.Id, TimeGiven = DateTimeOffset.UtcNow, Level = WarningLevel.NOTE, Reason = $"BANNED for {durationFormat}." };
            warning.Link = LinkToMessage(banNotice);
            warnable.AddWarning(warning);
        }

        /// <summary>
        /// User command to remove a user's muted status.
        /// </summary>
        public void CMD_Unmute(CommandData command)
        {
            SocketGuild guild = (command.Message.Channel as SocketGuildChannel).Guild;
            GuildConfig config = DiscordModBot.GetConfig(guild.Id);
            if (!config.MuteRole.HasValue)
            {
                return;
            }
            if (!DiscordModBot.IsModerator(command.Message.Author as SocketGuildUser))
            {
                SendErrorMessageReply(command.Message, "Not Authorized", "You're not allowed to do that.");
                return;
            }
            if (!DiscordModBot.WarningCommandHandler.GetTargetUser(command, true, true, out ulong userID))
            {
                return;
            }
            SocketGuildUser guildUserToUnmute = guild.GetUser(userID);
            WarnableUser warnable = WarningUtilities.GetWarnableUser(guild.Id, userID);
            bool wasMuted = false;
            if (warnable.IsMuted)
            {
                wasMuted = true;
                warnable.IsMuted = false;
                warnable.Save();
            }
            if (guildUserToUnmute != null)
            {
                SocketRole role = guild.GetRole(config.MuteRole.Value);
                if (role == null)
                {
                    SendErrorMessageReply(command.Message, "Error", "Mute role is misconfigured.");
                    return;
                }
                IRole userRole = guildUserToUnmute.Roles.FirstOrDefault((r) => r.Id == role.Id);
                if (userRole != null)
                {
                    guildUserToUnmute.RemoveRoleAsync(userRole).Wait();
                    wasMuted = true;
                }
            }
            if (wasMuted)
            {
                SendGenericPositiveMessageReply(command.Message, "Unmuted", $"<@{command.Message.Author.Id}> has unmuted <@{userID}>.");
                ModBotLoggers.SendEmbedToAllFor((command.Message.Channel as SocketGuildChannel).Guild, config.ModLogsChannel, new EmbedBuilder().WithTitle("User Unmuted").WithColor(0, 255, 0).WithDescription($"User <@{userID}> was unmuted.").Build());
            }
            else
            {
                SendGenericNegativeMessageReply(command.Message, "Cannot Unmute", $"User {EscapeUserInput(warnable.LastKnownUsername)} is already not muted.");
            }
        }

        /// <summary>
        /// User command to add a note to a user.
        /// </summary>
        public void CMD_Note(CommandData command)
        {
            SocketGuild guild = (command.Message.Channel as SocketGuildChannel).Guild;
            GuildConfig config = DiscordModBot.GetConfig(guild.Id);
            if (!config.WarningsEnabled)
            {
                return;
            }
            if (!DiscordModBot.IsModerator(command.Message.Author as SocketGuildUser))
            {
                SendErrorMessageReply(command.Message, "Not Authorized", "You're not allowed to do that.");
                return;
            }
            if (command.RawArguments.Length < 2)
            {
                SendErrorMessageReply(command.Message, "Invalid Input", "Usage: note [user] [message]");
                return;
            }
            if (!DiscordModBot.WarningCommandHandler.GetTargetUser(command, true, true, out ulong userID))
            {
                return;
            }
            WarnableUser user = WarningUtilities.GetWarnableUser(guild.Id, userID);
            if (user.SeenNames.IsEmpty())
            {
                SendErrorMessageReply(command.Message, "Invalid Input", "Cannot add note on that user: user has never been seen before.");
                return;
            }
            IEnumerable<string> cmdsToSave = command.RawArguments.Skip(1);
            Warning warning = new Warning() { GivenTo = userID, GivenBy = command.Message.Author.Id, TimeGiven = DateTimeOffset.UtcNow, Level = WarningLevel.NOTE };
            warning.Reason = EscapeUserInput(string.Join(" ", cmdsToSave));
            IUserMessage sentMessage = command.Message.Channel.SendMessageAsync(embed: new EmbedBuilder().WithTitle("Note Recorded").WithDescription($"Note from <@{command.Message.Author.Id}> to <@{userID}> recorded.").Build()).Result;
            warning.Link = LinkToMessage(sentMessage);
            try
            {
                user.AddWarning(warning);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while storing note: {ex}");
                SendErrorMessageReply(command.Message, "Internal Error", $"ModBot encountered an internal error while saving that user-note. Check the bot console for details.\n{config.AttentionNotice}");
            }
        }

        /// <summary>
        /// User command to give a warning to a user.
        /// </summary>
        public void CMD_Warn(CommandData command)
        {
            SocketGuild guild = (command.Message.Channel as SocketGuildChannel).Guild;
            GuildConfig config = DiscordModBot.GetConfig(guild.Id);
            if (!config.WarningsEnabled)
            {
                return;
            }
            if (!DiscordModBot.IsModerator(command.Message.Author as SocketGuildUser))
            {
                SendErrorMessageReply(command.Message, "Not Authorized", "You're not allowed to do that.");
                return;
            }
            if (command.RawArguments.Length < 3)
            {
                SendErrorMessageReply(command.Message, "Invalid Input", "Usage: warn [user] [level] [reason] - Valid levels: `minor`, `normal`, `serious`, or `instant_mute`");
                return;
            }
            if (!DiscordModBot.WarningCommandHandler.GetTargetUser(command, true, true, out ulong userID))
            {
                return;
            }
            int argsSkip = 2;
            if (!LevelsTypable.TryGetValue(command.RawArguments[1].ToLowerInvariant(), out WarningLevel level))
            {
                if (StringConversionHelper.FindClosestString(LevelsTypable.Keys, command.RawArguments[1].ToLowerInvariant(), maxDistance: 3) != null)
                {
                    SendErrorMessageReply(command.Message, "Invalid Input", "First argument is not a valid level name, but looks similar to one. Valid levels: `minor`, `normal`, `serious`, or `instant_mute`.");
                    return;
                }
                argsSkip = 1;
                level = WarningLevel.NORMAL;
            }
            WarnableUser warnUser = WarningUtilities.GetWarnableUser(guild.Id, userID);
            if (warnUser.SeenNames.IsEmpty())
            {
                SendErrorMessageReply(command.Message, "Invalid Input", "Cannot warn on that user: user has never been seen before.");
                return;
            }
            Warning warning = new Warning() { GivenTo = userID, GivenBy = command.Message.Author.Id, TimeGiven = DateTimeOffset.UtcNow, Level = level };
            warning.Reason = EscapeUserInput(string.Join(" ", command.RawArguments.Skip(argsSkip)));
            IUserMessage sentMessage = command.Message.Channel.SendMessageAsync(embed: new EmbedBuilder().WithTitle("Warning Recorded").WithDescription($"Warning from <@{command.Message.Author.Id}> to <@{userID}> recorded.\nReason: {warning.Reason}\n{warnUser.GetPastWarningsText()}").Build()).Result;
            warning.Link = LinkToMessage(sentMessage);
            try
            {
                warnUser.AddWarning(warning);
                SocketGuildUser socketUser = guild.GetUser(userID);
                if (socketUser != null)
                {
                    PossibleMute(socketUser, command.Message, level);
                }
                else if (level == WarningLevel.INSTANT_MUTE)
                {
                    warnUser.IsMuted = true;
                    warnUser.Save();
                    SendGenericPositiveMessageReply(command.Message, "Mute Recorded", "Mute applied for next rejoin.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while warning: {ex}");
                SendErrorMessageReply(command.Message, "Internal Error", $"ModBot encountered an internal error while saving that warning. Check the bot console for details.\n{config.AttentionNotice}");
            }
            if (config.NotifyWarnsInDM)
            {
                try
                {
                    SocketGuildUser user = guild.GetUser(userID);
                    if (user != null)
                    {
                        user.GetOrCreateDMChannelAsync().Result.SendMessageAsync(embed: new EmbedBuilder().WithTitle("Notification Of Moderator Warning").WithColor(255, 128, 0)
                            .WithDescription($"You have received a warning in {guild.Name}.").AddField("Level", $"`{warning.Level.ToString().ToLowerFast()}`").AddField("Reason", warning.Reason).Build()).Wait();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error while DMing user {userID} about warning: {ex}");
                }
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
            SocketGuild guild = (message.Channel as SocketGuildChannel).Guild;
            GuildConfig config = DiscordModBot.GetConfig(guild.Id);
            if (!config.MuteRole.HasValue)
            {
                return;
            }
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
                foreach (Warning oldWarn in WarningUtilities.GetWarnableUser(guild.Id, user.Id).Warnings)
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
                IRole role = user.Guild.GetRole(config.MuteRole.Value);
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
                message.Channel.SendMessageAsync($"User <@{user.Id}> has been muted automatically by the warning system.\n{config.AttentionNotice}", embed: new EmbedBuilder().WithTitle("Mute Notice").WithColor(255, 128, 0).WithDescription(muteMessage).Build());
                foreach (ulong id in config.IncidentChannel)
                {
                    SocketGuildChannel incidentChan = (user.Guild as SocketGuild).GetChannel(id);
                    if (incidentChan != null && incidentChan is ISocketMessageChannel incidentChanText)
                    {
                        incidentChanText.SendMessageAsync("<@" + user.Id + ">", embed: new EmbedBuilder().WithTitle("Mute Notice").WithColor(255, 128, 0)
                            .WithDescription("You have been automatically muted by the system due to warnings received. You may discuss the situation in this channel only, until a moderator unmutes you.").Build());
                        break;
                    }
                }
                ModBotLoggers.SendEmbedToAllFor(user.Guild as SocketGuild, config.ModLogsChannel, new EmbedBuilder().WithTitle("User Muted").WithColor(255, 0, 0).WithDescription($"User <@{user.Id}> was muted.").Build());
            }
        }

        /// <summary>
        /// Helper for the ListWarn command to dump warnings information about a user to a channel.
        /// </summary>
        public static void SendWarningList(WarnableUser user, int startId, IMessageChannel channel, IUserMessage message)
        {
            bool hasMore = false;
            StringBuilder warnStringOutput = new StringBuilder();
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            int warnID = 0;
            foreach (Warning warned in user.Warnings.OrderByDescending(w => (int)w.Level).Skip(startId * 5))
            {
                if (warnID == 5)
                {
                    if (message == null)
                    {
                        warnStringOutput.Append($"... And more warnings. Use the `listwarn` command to show more.");
                    }
                    else
                    {
                        warnStringOutput.Append($"... And more warnings. Click the {Constants.ACCEPT_EMOJI} react to show more.");
                        hasMore = true;
                    }
                    break;
                }
                warnID++;
                SocketUser giver = DiscordBotBaseHelper.CurrentBot.Client.GetUser(warned.GivenBy);
                string giverLabel = (giver == null) ? ("DiscordID:" + warned.GivenBy) : (giver.Username + "#" + giver.Discriminator);
                string reason = (warned.Reason.Length > 250) ? (warned.Reason.Substring(0, 250) + "(... trimmed ...)") : warned.Reason;
                reason = EscapeUserInput(reason);
                warnStringOutput.Append($"**... {warned.Level}{(warned.Level == WarningLevel.NOTE ? "" : " warning")}** given at `{StringConversionHelper.DateTimeToString(warned.TimeGiven, false)}` by {giverLabel} with reason: `{reason}`. [Click For Detail]({warned.Link})\n");
            }
            if (startId == 0 && user.SpecialRoles.Any())
            {
                string rolesText = string.Join(", ", user.SpecialRoles.Select(s => $"`{s}`"));
                SendGenericPositiveMessageReply(message, "Special Roles", $"User `{EscapeUserInput(user.LastKnownUsername)}` has the following special roles applied:\n{rolesText}");
            }
            if (warnID == 0)
            {
                if (startId > 0)
                {
                    SendGenericPositiveMessageReply(message, "Nothing Found", $"User `{EscapeUserInput(user.LastKnownUsername)}` does not have that page of warnings.");
                }
                else
                {
                    SendGenericPositiveMessageReply(message, "Nothing Found", $"User `{EscapeUserInput(user.LastKnownUsername)}` does not have any warnings logged.");
                }
            }
            else
            {
                int warnCount = user.Warnings.Count;
                IUserMessage sentMessage = channel.SendMessageAsync(embed: GetGenericPositiveMessageEmbed($"{warnCount} Warnings Found", $"User `{EscapeUserInput(user.LastKnownUsername)}` has the following warnings logged:\n{warnStringOutput}")).Result;
                if (hasMore && sentMessage != null && message != null)
                {
                    sentMessage.AddReactionsAsync(new IEmote[] { new Emoji(Constants.ACCEPT_EMOJI), new Emoji(Constants.DENY_EMOJI) }).Wait();
                    ReactionsHandler.AddReactable(message, sentMessage, $"listwarn {user.UserID()} {startId + 2}");
                }
            }
        }

        /// <summary>
        /// User command to list user warnings.
        /// </summary>
        public void CMD_ListWarnings(CommandData command)
        {
            SocketGuild guild = (command.Message.Channel as SocketGuildChannel).Guild;
            GuildConfig config = DiscordModBot.GetConfig(guild.Id);
            if (!config.WarningsEnabled)
            {
                return;
            }
            ulong userID = command.Message.Author.Id;
            if (DiscordModBot.IsModerator(command.Message.Author as SocketGuildUser))
            {
                DiscordModBot.WarningCommandHandler.GetTargetUser(command, false, true, out userID);
            }
            int pageArg = command.RawArguments.Length - 1;
            if (pageArg < 0 || !int.TryParse(command.RawArguments[pageArg], out int min))
            {
                min = 1;
            }
            min--;
            if (min < 0)
            {
                SendErrorMessageReply(command.Message, "Invalid Input", "Page input invalid.");
                return;
            }
            WarnableUser user = WarningUtilities.GetWarnableUser(guild.Id, userID);
            SendWarningList(user, min, command.Message.Channel, command.Message);
        }

        /// <summary>
        /// Utility method to get the target of a command that allows targeting commands at others instead of self.
        /// </summary>
        public bool GetTargetUser(CommandData command, bool errorIfNone, bool errorIfInvalid, out ulong userId)
        {
            userId = command.Message.Author.Id;
            if (command.RawArguments.Length == 0)
            {
                if (errorIfNone)
                {
                    SendErrorMessageReply(command.Message, "Input Invalid", "This command requires a user mention or ID as the first argument, but none was given.\nWho are you trying to use this command on?");
                }
                return false;
            }
            string id = command.RawArguments[0];
            if (id.StartsWith("<@") && id.EndsWith(">"))
            {
                id = id[2..^1].Replace("!", "");
            }
            if (ulong.TryParse(id, out ulong inputId))
            {
                userId = inputId;
                Console.WriteLine("Current command will target user ID: " + userId);
                return true;
            }
            if (errorIfInvalid)
            {
                SendErrorMessageReply(command.Message, "Input Invalid", "Something went wrong - user ID not valid? Check that you properly input the user mention or ID as the first argument.");
            }
            return false;
        }
    }
}
