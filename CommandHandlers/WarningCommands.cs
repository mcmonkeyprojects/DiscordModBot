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
using System.Threading.Tasks;
using static ModBot.Database.ModBotDatabaseHandler;

namespace ModBot.CommandHandlers
{
    /// <summary>Commands related to handling warnings and notes.</summary>
    public class WarningCommands : UserCommands
    {
        /// <summary>A mapping of typable names to warning level enumeration values.</summary>
        public static Dictionary<string, WarningLevel> LevelsTypable = new()
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

        /// <summary>User command to temporarily ban a user.</summary>
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
                SendErrorMessageReply(command.Message, "Invalid Input", "Usage: tempban [user] [duration] (reason) ... Duration can be formatted like '1d' (for 1 day). Allowed type: 'h' for hours, 'd' for days, 'w' for weeks, 'm' for months, 'y' for years.");
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
            if (warnable.SeenNames.IsEmpty() && !config.AllowWarningUnknownUsers)
            {
                SendErrorMessageReply(command.Message, "Invalid Input", $"Cannot ban that user: user <@{userID}> has never been seen before. Did you reference a user that hasn't joined this guild yet, or accidentally copy a message ID instead of user ID?");
                return;
            }
            string durationText = command.RawArguments[1];
            TimeSpan? realDuration = WarningUtilities.ParseDuration(durationText);
            if (!realDuration.HasValue)
            {
                SendErrorMessageReply(command.Message, "Invalid Input", "Duration must be formatted like '1d' (for 1 day). Allowed type: 'h' for hours, 'd' for days, 'w' for weeks, 'm' for months, 'y' for years.");
                return;
            }
            if (realDuration.Value.TotalMinutes < 1)
            {
                SendErrorMessageReply(command.Message, "Invalid Input", "Duration must be a positive value greater than one minute.");
                return;
            }
            string reason = "";
            if (command.RawArguments.Length > 2)
            {
                reason = EscapeUserInput(string.Join(" ", command.RawArguments.Skip(2)));
            }
            if (!string.IsNullOrWhiteSpace(config.MaxBanDuration) && realDuration.Value.TotalMinutes > (WarningUtilities.ParseDuration(config.MaxBanDuration) ?? new TimeSpan(0, 0, 0)).TotalMinutes)
            {
                SendErrorMessageReply(command.Message, "Invalid Input", $"Duration must be less than limit of `{config.MaxBanDuration}`.");
                return;
            }
            DiscordModBot.TempBanHandler.TempBan(guild.Id, userID, realDuration.Value, command.Message.Author.Id, reason);
            bool isForever = realDuration.Value.TotalDays > (365 * 50);
            string durationFormat = isForever ? "indefinitely" : $"for {realDuration.Value.SimpleFormat(false)}";
            string tempText = isForever ? "" : " temporarily";
            EmbedBuilder embed = new EmbedBuilder().WithTitle("User Banned").WithColor(255, 0, 0).WithDescription($"User ban applied and recorded.").AddField("User", $"<@{userID}>", true).AddField("Duration", durationFormat, true);
            if (!string.IsNullOrWhiteSpace(reason))
            {
                embed.AddField("Reason", $"`{reason}`");
                reason = $" Reason: {reason}";
            }
            ModBotLoggers.SendEmbedToAllFor((command.Message.Channel as SocketGuildChannel).Guild, DiscordModBot.GetConfig(guild.Id).ModLogsChannel, embed.Build());
            IUserMessage banNotice = SendGenericPositiveMessageReply(command.Message, "Temporary Ban Applied", $"<@{command.Message.Author.Id}> has{tempText} banned <@{userID}> {durationFormat}.");
            Warning warning = new() { GivenTo = userID, GivenBy = command.Message.Author.Id, TimeGiven = DateTimeOffset.UtcNow, Level = WarningLevel.BAN, Reason = $"BANNED {durationFormat}.{reason}", Link = LinkToMessage(banNotice) };
            warnable.AddWarning(warning);
        }

        /// <summary>User command to temporarily timeout a user.</summary>
        public void CMD_Timeout(CommandData command)
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
                SendErrorMessageReply(command.Message, "Invalid Input", "Usage: timeout [user] [duration] (reason) ... Duration must be formatted like '5m' (for 5 minutes). Allowed type: 'm' for minutes, 'h' for hours, 'd' for days. Or '0' to remove a timeout.");
                return;
            }
            if (!DiscordModBot.WarningCommandHandler.GetTargetUser(command, true, true, out ulong userID))
            {
                return;
            }
            SocketGuildUser guildUser = guild.GetUser(userID);
            if (guildUser is null)
            {
                SendErrorMessageReply(command.Message, "Invalid Input", $"Cannot timeout that user: user <@{userID}> has never been seen before. Did you reference a user that hasn't joined this guild yet, or accidentally copy a message ID instead of user ID?");
                return;
            }
            WarnableUser warnable = WarningUtilities.GetWarnableUser(guild.Id, userID);
            string durationText = command.RawArguments[1];
            TimeSpan? realDuration = WarningUtilities.ParseDuration(durationText, mForMinutes: true);
            if (!realDuration.HasValue)
            {
                SendErrorMessageReply(command.Message, "Invalid Input", "Duration must be formatted like '5m' (for 5 minutes). Allowed type: 'm' for minutes, 'h' for hours, 'd' for days. Or '0' to remove a timeout.");
                return;
            }
            if (realDuration.Value.TotalSeconds == 0)
            {
                if (!guildUser.TimedOutUntil.HasValue)
                {
                    SendGenericNegativeMessageReply(command.Message, "Invalid Target", "That user isn't timed out, and so timeout cannot be removed.");
                    return;
                }
                guildUser.RemoveTimeOutAsync().Wait();
                SendGenericPositiveMessageReply(command.Message, "Timeout Removed", $"<@{command.Message.Author.Id}> has removed time out from <@{userID}>.");
                return;
            }
            if (realDuration.Value.TotalSeconds < 10)
            {
                SendErrorMessageReply(command.Message, "Invalid Input", "Duration must be a positive value greater than ten seconds (or 0 to remove).");
                return;
            }
            if (realDuration.Value.TotalDays > 28d)
            {
                SendErrorMessageReply(command.Message, "Invalid Input", "Duration is longer than Discord's maximum timeout. If you need to timeout for more that a few weeks, consider a ban instead.");
                return;
            }
            string reason = "";
            if (command.RawArguments.Length > 2)
            {
                reason = EscapeUserInput(string.Join(" ", command.RawArguments.Skip(2)));
            }
            guildUser.SetTimeOutAsync(realDuration.Value).Wait();
            string durationFormatted = realDuration.Value.SimpleFormat(false);
            EmbedBuilder embed = new EmbedBuilder().WithTitle("User Timed Out").WithColor(255, 128, 0).WithDescription($"User timed out.").AddField("User", $"<@{userID}>", true).AddField("By", $"<@{command.Message.Author.Id}>", true).AddField("Duration", durationFormatted, true);
            if (!string.IsNullOrWhiteSpace(reason))
            {
                embed.AddField("Reason", $"`{reason}`");
                reason = $" Reason: {reason}";
            }
            ModBotLoggers.SendEmbedToAllFor((command.Message.Channel as SocketGuildChannel).Guild, DiscordModBot.GetConfig(guild.Id).ModLogsChannel, embed.Build());
            IUserMessage timeoutNotice = SendGenericPositiveMessageReply(command.Message, "Timeout Applied", $"<@{command.Message.Author.Id}> has timed out <@{userID}> for {durationFormatted}.");
            Warning warning = new() { GivenTo = userID, GivenBy = command.Message.Author.Id, TimeGiven = DateTimeOffset.UtcNow, Level = WarningLevel.TIMEOUT, Reason = $"TIMED OUT for {durationFormatted}.{reason}", Link = LinkToMessage(timeoutNotice) };
            warnable.AddWarning(warning);
        }

        /// <summary>User command to remove a user's ban.</summary>
        public void CMD_Unban(CommandData command)
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
            if (command.RawArguments.Length < 1)
            {
                SendErrorMessageReply(command.Message, "Invalid Input", "Usage: unban [user]");
                return;
            }
            if (!DiscordModBot.WarningCommandHandler.GetTargetUser(command, true, true, out ulong userID))
            {
                return;
            }
            if (guild.GetBanAsync(userID).Result == null)
            {
                SendErrorMessageReply(command.Message, "Not Banned", $"User <@{userID}> isn't banned, or the ID you gave is invalid.");
                return;
            }
            guild.RemoveBanAsync(userID).Wait();
            DiscordModBot.TempBanHandler.DisableTempBansFor(guild.Id, userID);
            SendGenericPositiveMessageReply(command.Message, "Unbanned", $"<@{command.Message.Author.Id}> has unbanned <@{userID}>.");
            ModBotLoggers.SendEmbedToAllFor((command.Message.Channel as SocketGuildChannel).Guild, config.ModLogsChannel, new EmbedBuilder().WithTitle("User Unbanned").WithColor(0, 255, 0).WithDescription($"User <@{userID}> was unbanned by <@{command.Message.Author.Id}>.").Build());
        }

        /// <summary>User command to remove a user's muted status.</summary>
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
                if (warnable.SeenNames.IsEmpty())
                {
                    SendErrorMessageReply(command.Message, "Invalid Input", $"Cannot unmute that user: user <@{userID}> has never been seen before. Did you reference a user that hasn't joined this guild yet, or accidentally copy a message ID instead of user ID?");
                    return;
                }
                SendGenericNegativeMessageReply(command.Message, "Cannot Unmute", $"User {EscapeUserInput(warnable.LastKnownUsername)} is already not muted.");
            }
        }

        /// <summary>User command to add a note to a user.</summary>
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
            if (user.SeenNames.IsEmpty() && !config.AllowWarningUnknownUsers)
            {
                SendErrorMessageReply(command.Message, "Invalid Input", $"Cannot add note on that user: user <@{userID}> has never been seen before. Did you reference a user that hasn't joined this guild yet, or accidentally copy a message ID instead of user ID?");
                return;
            }
            IEnumerable<string> cmdsToSave = command.RawArguments.Skip(1);
            Warning warning = new() { GivenTo = userID, GivenBy = command.Message.Author.Id, TimeGiven = DateTimeOffset.UtcNow, Level = WarningLevel.NOTE, Reason = EscapeForPlainText(string.Join(" ", cmdsToSave)) };
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

        /// <summary>User command to give a warning to a user.</summary>
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
            if (warnUser.SeenNames.IsEmpty() && !config.AllowWarningUnknownUsers)
            {
                SendErrorMessageReply(command.Message, "Invalid Input", $"Cannot warn that user: user <@{userID}> has never been seen before. Did you reference a user that hasn't joined this guild yet, or accidentally copy a message ID instead of user ID?");
                return;
            }
            Warning warning = new() { GivenTo = userID, GivenBy = command.Message.Author.Id, TimeGiven = DateTimeOffset.UtcNow, Level = level, Reason = EscapeForPlainText(string.Join(" ", command.RawArguments.Skip(argsSkip))) };
            IUserMessage sentMessage = command.Message.Channel.SendMessageAsync(embed: new EmbedBuilder().WithTitle("Warning Recorded").WithDescription($"Warning from <@{command.Message.Author.Id}> to <@{userID}> recorded.\nReason: \"*{warning.Reason}*\"\n{warnUser.GetPastWarningsText()}").Build()).Result;
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
                        user.CreateDMChannelAsync().Result.SendMessageAsync(embed: new EmbedBuilder().WithTitle("Notification Of Moderator Warning").WithColor(255, 128, 0)
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
                ModBotLoggers.SendEmbedToAllFor(user.Guild as SocketGuild, config.ModLogsChannel, new EmbedBuilder().WithTitle("User Muted").WithColor(255, 0, 0).WithDescription($"User <@{user.Id}> was muted.").Build());
                SocketThreadChannel thread = GenerateThreadFor(config, guild, user, warnable);
                if (thread is null)
                {
                    ModBotLoggers.SendEmbedToAllFor(guild, config.IncidentChannel, embed: new EmbedBuilder().WithTitle("Mute Notice").WithColor(255, 128, 0).WithDescription(config.MuteNoticeMessage ?? GuildConfig.MUTE_NOTICE_DEFAULT).Build(), text: $"<@{user.Id}>");
                }
                else
                {
                    thread.SendMessageAsync(embed: new EmbedBuilder().WithTitle("Mute Notice").WithColor(255, 128, 0).WithDescription(config.MuteNoticeMessage ?? GuildConfig.MUTE_NOTICE_DEFAULT).Build(), text: $"<@{user.Id}>").Wait();
                    if (config.SendWarnListToIncidentThread)
                    {
                        WarningCommands.SendWarningList(warnable, 0, thread, null);
                    }
                }
            }
        }

        public static SocketThreadChannel GenerateThreadFor(GuildConfig config, SocketGuild guild, IGuildUser user, WarnableUser warnable)
        {
            if (!config.IncidentChannelCreateThreads || !guild.Features.HasPrivateThreads || config.IncidentChannel.Count != 1)
            {
                return null;
            }
            SocketGuildChannel targetChannel = guild.GetChannel(config.IncidentChannel.First());
            if (targetChannel is not SocketTextChannel textChannel || targetChannel is SocketThreadChannel)
            {
                return null;
            }
            SocketThreadChannel thread = null;
            if (warnable.IncidentThread != 0)
            {
                thread = DiscordBotBaseHelper.CurrentBot.Client.GetChannelAsync(warnable.IncidentThread).Normalize().Result as SocketThreadChannel;
                if (thread is not null)
                {
                    if (thread.ParentChannel.Id != textChannel.Id)
                    {
                        thread = null;
                    }
                    else
                    {
                        thread.ModifyAsync(t =>
                        {
                            t.Locked = false;
                            t.Archived = false;
                        }).Wait();
                    }
                }
                else
                {
                    Console.WriteLine($"Debug notice: incident thread {warnable.IncidentThread} was stored but was not able to be looked up");
                }
            }
            if (thread is null)
            {
                string name = USERNAME_SIMPLIFIER_MATCHER.TrimToMatches(user.Username);
                if (name.Length > 16)
                {
                    name = name[..16];
                }
                thread = textChannel.CreateThreadAsync($"[Incident] {name}", ThreadType.PrivateThread, ThreadArchiveDuration.OneWeek).Result;
                if (thread is null)
                {
                    return null;
                }
                warnable.IncidentThread = thread.Id;
                warnable.Save();
            }
            List<Task> addTasks = new()
            {
                thread.AddUserAsync(user)
            };
            foreach (SocketGuildUser mod in config.IncidentThreadAutoAdd.Select(u => guild.GetUser(u)).Where(u => u is not null))
            {
                addTasks.Add(thread.AddUserAsync(mod));
            }
            try
            {
                foreach (Task task in addTasks)
                {
                    task.Wait();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to add users to incident thread: {ex}");
            }
            return thread;
        }

        public static AsciiMatcher USERNAME_SIMPLIFIER_MATCHER = new(AsciiMatcher.BothCaseLetters + AsciiMatcher.Digits + "_ ");

        /// <summary>Helper for the ListWarn command to dump warnings information about a user to a channel.</summary>
        public static void SendWarningList(WarnableUser user, int startId, IMessageChannel channel, IUserMessage message)
        {
            bool hasMore = false;
            StringBuilder warnStringOutput = new();
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            int warnID = 0;
            foreach (Warning warned in user.Warnings.OrderByDescending(w => (int)w.Level).Skip(startId * 10))
            {
                if (warnID == 10)
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
                string giverLabel = giver is null ? $"<@{warned.GivenBy}>" : $"<@{warned.GivenBy}> (`{EscapeUserInput(giver.Username)}#{giver.Discriminator}`)";
                string reason = (warned.Reason.Length > 350) ? (warned.Reason[..340] + "(... trimmed ...)") : warned.Reason;
                string reftext = string.IsNullOrWhiteSpace(warned.RefLink) ? "" : $" [Manual Reference Link]({warned.RefLink})";
                warnStringOutput.Append($"**... {warned.Level}{(warned.Level == WarningLevel.NOTE ? "" : " warning")}** given at `{StringConversionHelper.DateTimeToString(warned.TimeGiven, false)}` by {giverLabel} with reason: \"*{reason}*\"{reftext}. [Click For Detail]({warned.Link})\n");
            }
            if (startId == 0 && user.SpecialRoles.Any())
            {
                string rolesText = string.Join(", ", user.SpecialRoles.Select(s => $"`{s}`"));
                SendGenericPositiveMessageReply(message, "Special Roles", $"User <@{user.UserID()}> (`{EscapeUserInput(user.LastKnownUsername)}`) has the following special roles applied:\n{rolesText}", channel);
            }
            if (warnID == 0)
            {
                if (startId > 0)
                {
                    SendGenericPositiveMessageReply(message, "Nothing Found", $"User <@{user.UserID()}> (`{EscapeUserInput(user.LastKnownUsername)}`) does not have that page of warnings.", channel);
                }
                else
                {
                    SendGenericPositiveMessageReply(message, "Nothing Found", $"User <@{user.UserID()}> (`{EscapeUserInput(user.LastKnownUsername)}`) does not have any warnings logged.", channel);
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

        /// <summary>User command to list user warnings.</summary>
        public void CMD_ListWarnings(CommandData command)
        {
            SocketGuild guild = (command.Message.Channel as SocketGuildChannel).Guild;
            GuildConfig config = DiscordModBot.GetConfig(guild.Id);
            if (!config.WarningsEnabled)
            {
                return;
            }
            ulong userID = command.Message.Author.Id;
            if (DiscordModBot.IsModerator(command.Message.Author as SocketGuildUser) && command.RawArguments.Length > 0)
            {
                if (!DiscordModBot.WarningCommandHandler.GetTargetUser(command, false, true, out userID))
                {
                    return;
                }
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
            if (user.SeenNames.IsEmpty() && user.Warnings.IsEmpty())
            {
                SendErrorMessageReply(command.Message, "Invalid Input", $"Cannot listwarn on that user: user <@{userID}> has never been seen before. Did you reference a user that hasn't joined this guild yet, or accidentally copy a message ID instead of user ID?");
                return;
            }
            SendWarningList(user, min, command.Message.Channel, command.Message);
        }

        /// <summary>User command to find users with similar names.</summary>
        public void CMD_FindSimilarNames(CommandData command)
        {
            if (!DiscordModBot.IsModerator(command.Message.Author as SocketGuildUser))
            {
                SendErrorMessageReply(command.Message, "Not Authorized", "You're not allowed to do that.");
                return;
            }
            if (command.RawArguments.Length == 0)
            {
                SendErrorMessageReply(command.Message, "Input Invalid", "Must give a name to search for.");
                return;
            }
            SocketGuild guild = (command.Message.Channel as SocketGuildChannel).Guild;
            string arg = command.RawArguments[0];
            if (arg.StartsWith("<@") && arg.EndsWithFast('>') && ulong.TryParse(WarningUtilities.DigitMatcher.TrimToMatches(arg), out ulong id))
            {
                SocketGuildUser usr = guild.GetUser(id);
                if (usr is null)
                {
                    SendErrorMessageReply(command.Message, "Input Invalid", "Tried to use invalid tag.");
                    return;
                }
                arg = usr.Username;
            }
            int maxDiff = arg.Length / 2;
            Guild guildData = DiscordModBot.DatabaseHandler.GetDatabase(guild.Id);
            arg = arg.ToLowerFast();
            List<(ulong, int, string, int)> matches = new();
            foreach (WarnableUser user in guildData.Users.FindAll())
            {
                if (user.LastKnownUsername is null)
                {
                    continue;
                }
                int similarity = NameUtilities.GetSimilarityEstimate(arg, user.LastKnownUsername);
                int min = similarity;
                string mostSim = user.LastKnownUsername;
                foreach (WarnableUser.OldName oldName in user.SeenNames)
                {
                    int newSim = NameUtilities.GetSimilarityEstimate(arg, oldName.Name);
                    if (newSim < min)
                    {
                        mostSim = oldName.Name;
                        min = newSim;
                    }
                }
                if (min < maxDiff)
                {
                    matches.Add((user.UserID(), min, mostSim, user.Warnings.Count));
                    if (matches.Count > 15)
                    {
                        maxDiff--;
                        if (maxDiff < -2)
                        {
                            break;
                        }
                    }
                    else if (matches.Count > 5)
                    {
                        int newMax = matches.MaxBy(e => e.Item2).Item2;
                        if (newMax < maxDiff && newMax > 1)
                        {
                            maxDiff = newMax;
                        }
                    }
                }
            }
            if (matches.Count == 0)
            {
                SendErrorMessageReply(command.Message, "No Matches", "Could not find any similar names.");
                return;
            }
            matches = matches.OrderBy(e => e.Item2).ToList();
            SendGenericPositiveMessageReply(command.Message, "Matches Found", $"Found **{matches.Count}** matches for `{EscapeUserInput(arg)}`:\n"
                + (maxDiff < -2 || (maxDiff < 2 && matches.Count > 15) ? "*Note: list cuts off before complete search due to too many results.*\n" : "")
                + string.Join('\n', matches.Select(e => $"<@{e.Item1}> (diff={e.Item2}): `{EscapeUserInput(e.Item3)}`" + (e.Item4 > 0 ? $" has {e.Item4} warnings" : ""))));
        }

        /// <summary>Utility method to get the target of a command that allows targeting commands at others instead of self.</summary>
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
