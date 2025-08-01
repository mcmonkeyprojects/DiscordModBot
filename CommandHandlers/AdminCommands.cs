﻿using System;
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
using FreneticUtilities.FreneticToolkit;
using System.Threading.Tasks;
using LiteDB;

namespace ModBot.CommandHandlers
{
    /// <summary>Commands for admin usage only.</summary>
    public class AdminCommands : UserCommands
    {
        /// <summary>Outputs an ASCII name rule test name.</summary>
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

        public static AsciiMatcher ChannelIDMatcher = new("<>#" + AsciiMatcher.Digits), DigitsMatcher = new(AsciiMatcher.Digits);

        /// <summary>Fills message history specified channels.</summary>
        public void CMD_FillHistory(CommandData command)
        {
            if (!DiscordModBot.IsBotCommander(command.Message.Author as SocketGuildUser))
            {
                SendErrorMessageReply(command.Message, "Not Authorized", "You're not allowed to do that.");
                return;
            }
            Task.Factory.StartNew(() =>
            {
                try
                {
                    foreach (string argument in command.RawArguments)
                    {
                        if (Bot.BotMonitor.ShouldStopAllLogic())
                        {
                            return;
                        }
                        if (ChannelIDMatcher.IsOnlyMatches(argument))
                        {
                            if (!ulong.TryParse(DigitsMatcher.TrimToMatches(argument), out ulong channelID))
                            {
                                SendErrorMessageReply(command.Message, "Error", $"Invalid channel ID `{argument}` - not a number.");
                                return;
                            }
                            SocketGuildChannel channel = (command.Message.Channel as SocketGuildChannel).Guild.GetChannel(channelID);
                            if (channel is null)
                            {
                                SendErrorMessageReply(command.Message, "Error", $"Invalid channel ID `{argument}` - not a known channel.");
                                return;
                            }
                            if (channel is not SocketTextChannel textChannel)
                            {
                                SendErrorMessageReply(command.Message, "Error", $"Invalid channel ID `{argument}` - not a text channel.");
                                return;
                            }
                            if (channel is SocketThreadChannel)
                            {
                                SendErrorMessageReply(command.Message, "Error", $"Invalid channel ID `{argument}` - is a thread.");
                                return;
                            }
                            ILiteCollection<StoredMessage> history = DiscordModBot.DatabaseHandler.GetDatabase(channel.Guild.Id).GetMessageHistory(channelID);
                            try
                            {
                                SendGenericPositiveMessageReply(command.Message, "History Fill Starting...", $"Starting history fill of <#{channel.Id}>");
                                void yoinkAll(SocketTextChannel channel)
                                {
                                    int thusFar = 0;
                                    channel.GetMessagesAsync(10_000_000).ForEachAwaitAsync(async col =>
                                    {
                                        foreach (IMessage message in col)
                                        {
                                            history.Upsert(new StoredMessage(message));
                                            if (thusFar++ % 10_000 == 0 && thusFar > 0)
                                            {
                                                Console.WriteLine($"Have prefilled read {thusFar} messages in {channel.Id} thus far");
                                            }
                                        }
                                        await Task.Delay(100);
                                    }).Wait();
                                }
                                yoinkAll(textChannel);
                                Task.Delay(100).Wait();
                                foreach (SocketThreadChannel thread in textChannel.Threads)
                                {
                                    yoinkAll(thread);
                                    Task.Delay(100).Wait();
                                }
                                SendGenericPositiveMessageReply(command.Message, "History Filled", $"Completed message history fill for channel <#{channel.Id}> with `{history.Count()}` messages stored");
                            }
                            catch (Exception ex)
                            {
                                if (ex.Message.Contains("error 50001: Missing Access"))
                                {
                                    SendGenericNegativeMessageReply(command.Message, "Channel Failure", $"Cannot read message history of <#{channel.Id}>: Error 50001: Missing Access");
                                }
                                else
                                {
                                    SendGenericNegativeMessageReply(command.Message, "Channel Failure", $"Cannot fill history for <#{channel.Id}>: internal exception (see console)");
                                    Console.WriteLine($"Error while reading history in in channel {channel.Id} ({channel.Name}): {ex}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.Write($"FillHistory fail: {ex}");
                }
            });
        }

        /// <summary>User command to sweep through all current names.</summary>
        public void CMD_Sweep(CommandData command)
        {
            if (!DiscordModBot.IsBotCommander(command.Message.Author as SocketGuildUser))
            {
                SendErrorMessageReply(command.Message, "Not Authorized", "You're not allowed to do that.");
                return;
            }
            int oneInEvery = 1;
            if (command.RawArguments.Length > 0)
            {
                if (!int.TryParse(command.RawArguments[0], out oneInEvery))
                {
                    SendErrorMessageReply(command.Message, "Error", "Invalid one-in-every number specified.");
                    return;
                }
            }
            SocketGuildChannel channel = command.Message.Channel as SocketGuildChannel;
            channel.Guild.DownloadUsersAsync().Wait();
            int count = Random.Shared.Next(1000);
            int usersChecked = 0, usersModified = 0;
            foreach (SocketGuildUser user in channel.Guild.Users)
            {
                count++;
                if (count % oneInEvery == 0)
                {
                    usersChecked++;
                    if (NameUtilities.AsciiNameRuleCheck(command.Message, user))
                    {
                        Thread.Sleep(400);
                        usersModified++;
                    }
                }
            }
            SendGenericPositiveMessageReply(command.Message, "Sweep Complete", $"Checked `{usersChecked}` users and modified `{usersModified}`.");
        }

        /// <summary>Admin command to configure guild settings.</summary>
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
                            SendHelpInfo("The ID of the role that indicates a user is muted. Is used by the `INSTANT_MUTE` warning level and the `unmute` command. Input `none` to disable.", config.MuteRole.HasValue ? config.MuteRole.Value.ToString() : "null");
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
                            SendErrorMessageReply(command.Message, "Invalid Value", "Argument must be a Role ID, or `none`.");
                            return;
                        }
                        break;
                    }
                case "moderator_roles":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("The IDs of the roles that indicate a user is a moderator. Only moderators can use warning commands. Format is a comma-separated list of IDs. Input `none` to disable.", config.ModeratorRoles.IsEmpty() ? "None" : string.Join(",", config.ModeratorRoles));
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
                                config.ModeratorRoles = [.. roleText.SplitFast(',').Select(ulong.Parse)];
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Invalid moderator_roles input, had exception: {ex}");
                                SendErrorMessageReply(command.Message, "Invalid Value", "Argument must be a comma-separated list of Role IDs, or `none`.");
                                return;
                            }
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Moderator role list updated.");
                        }
                        break;
                    }
                case "incident_thread_auto_add_ids":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("The IDs of the users to auto-add to incident threads.", config.IncidentThreadAutoAdd.IsEmpty() ? "None" : string.Join(",", config.IncidentThreadAutoAdd));
                            return;
                        }
                        string roleText = command.RawArguments[1];
                        if (roleText == "none" || roleText == "null")
                        {
                            config.IncidentThreadAutoAdd.Clear();
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Incident-thread-auto-add user list emptied.");
                        }
                        else
                        {
                            try
                            {
                                config.IncidentThreadAutoAdd = [.. roleText.SplitFast(',').Select(ulong.Parse)];
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Invalid incident-thread-auto-add input, had exception: {ex}");
                                SendErrorMessageReply(command.Message, "Invalid Value", "Argument must be a comma-separated list of Role IDs, or 'none'.");
                                return;
                            }
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Incident-thread-auto-add role list updated.");
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
                            config.AttentionNotice = "";
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
                            SendHelpInfo("The channel(s) that should show mute notices for users. Format is a comma-separated list of IDs. Input `none` to disable.", config.IncidentChannel.IsEmpty() ? "None" : string.Join(",", config.IncidentChannel));
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
                                config.IncidentChannel = [.. channelText.SplitFast(',').Select(ulong.Parse)];
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Invalid incident_channel input, had exception: {ex}");
                                SendErrorMessageReply(command.Message, "Invalid Value", "Argument must be a comma-separated list of channel IDs, or `none`.");
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
                            SendHelpInfo("The channel(s) that should show user join/leave notices. Format is a comma-separated list of IDs. Input `none` to disable.", config.JoinNotifChannel.IsEmpty() ? "None" : string.Join(",", config.JoinNotifChannel));
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
                                config.JoinNotifChannel = [.. channelText.SplitFast(',').Select(ulong.Parse)];
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Invalid join_notif_channel input, had exception: {ex}");
                                SendErrorMessageReply(command.Message, "Invalid Value", "Argument must be a comma-separated list of channel IDs, or `none`.");
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
                            SendHelpInfo("The channel(s) that should show user voice channel join/leave/move notices. Format is a comma-separated list of IDs. Input `none` to disable.", config.VoiceChannelJoinNotifs.IsEmpty() ? "None" : string.Join(",", config.VoiceChannelJoinNotifs));
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
                                config.VoiceChannelJoinNotifs = [.. channelText.SplitFast(',').Select(ulong.Parse)];
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Invalid voice_channel_join_notif_channel input, had exception: {ex}");
                                SendErrorMessageReply(command.Message, "Invalid Value", "Argument must be a comma-separated list of channel IDs, or `none`.");
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
                            SendHelpInfo("The channel(s) that should show user role change notices. Format is a comma-separated list of IDs. Input `none` to disable.", config.RoleChangeNotifChannel.IsEmpty() ? "None" : string.Join(",", config.RoleChangeNotifChannel));
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
                                config.RoleChangeNotifChannel = [.. channelText.SplitFast(',').Select(ulong.Parse)];
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Invalid role_change_notif_channel input, had exception: {ex}");
                                SendErrorMessageReply(command.Message, "Invalid Value", "Argument must be a comma-separated list of channel IDs, or `none`.");
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
                            SendHelpInfo("The channel(s) that should show username/nickname change notices. Format is a comma-separated list of IDs. Input `none` to disable.", config.NameChangeNotifChannel.IsEmpty() ? "None" : string.Join(",", config.NameChangeNotifChannel));
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
                                config.NameChangeNotifChannel = [.. channelText.SplitFast(',').Select(ulong.Parse)];
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Invalid name_change_notif_channel input, had exception: {ex}");
                                SendErrorMessageReply(command.Message, "Invalid Value", "Argument must be a comma-separated list of channel IDs, or `none`.");
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
                            SendHelpInfo("The channel(s) that should show moderator action logs. Format is a comma-separated list of IDs. Input `none` to disable.", config.ModLogsChannel.IsEmpty() ? "None" : string.Join(",", config.ModLogsChannel));
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
                                config.ModLogsChannel = [.. channelText.SplitFast(',').Select(ulong.Parse)];
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Invalid mod_logs_channel input, had exception: {ex}");
                                SendErrorMessageReply(command.Message, "Invalid Value", "Argument must be a comma-separated list of channel IDs, or `none`.");
                                return;
                            }
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Mod-Logs channel list updated.");
                        }
                        break;
                    }
                case "channel_move_notif_channels":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("The channel(s) that should show logs when a channel moves. Format is a comma-separated list of IDs. Input `none` to disable.", config.ChannelMoveNotifChannel.IsEmpty() ? "None" : string.Join(",", config.ChannelMoveNotifChannel));
                            return;
                        }
                        string channelText = command.RawArguments[1];
                        if (channelText == "none" || channelText == "null")
                        {
                            config.ChannelMoveNotifChannel.Clear();
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Channel-move-notif channel list emptied.");
                        }
                        else
                        {
                            try
                            {
                                config.ChannelMoveNotifChannel = [.. channelText.SplitFast(',').Select(ulong.Parse)];
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Invalid channel_move_notif_channels input, had exception: {ex}");
                                SendErrorMessageReply(command.Message, "Invalid Value", "Argument must be a comma-separated list of channel IDs, or `none`.");
                                return;
                            }
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Channel-move-notif channel list updated.");
                        }
                        break;
                    }
                case "log_channels":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("The mapping of channels, where keys are normal text channels (or categories), and values are a logs channel where message edit/deletes in the first channel get logged to."
                                + " Format is a comma-separated list of colon-separated ID:ID pairs (like `123:456,789:012`). Set key `0` as a catch-all. Input `none` to disable.",
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
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Invalid log_channels input, had exception: {ex}");
                                SendErrorMessageReply(command.Message, "Invalid Value", "Argument must be a comma-separated list of colon-separated ID:ID pairs, or `none`.");
                                return;
                            }
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Logs channel map updated.");
                        }
                        break;
                    }
                case "thread_log_channels":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("The mapping of channels, where keys are normal text channels (or categories), and values are a logs channel where all thread messages in the first channel get logged to."
                                + " Format is a comma-separated list of colon-separated ID:ID pairs (like `123:456,789:012`). Set key `0` as a catch-all. Input `none` to disable.",
                                config.ThreadLogChannels.IsEmpty() ? "None" : string.Join(",", config.ThreadLogChannels.Select(pair => $"{pair.Key}:{pair.Value}")));
                            return;
                        }
                        string channelText = command.RawArguments[1];
                        if (channelText == "none" || channelText == "null")
                        {
                            config.ThreadLogChannels.Clear();
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Thread log channel map emptied.");
                        }
                        else
                        {
                            try
                            {
                                config.ThreadLogChannels = channelText.SplitFast(',').Select(s => new KeyValuePair<ulong, ulong>(ulong.Parse(s.BeforeAndAfter(':', out string after)), ulong.Parse(after))).ToDictionary(p => p.Key, p => p.Value);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Invalid thread_log_channels input, had exception: {ex}");
                                SendErrorMessageReply(command.Message, "Invalid Value", "Argument must be a comma-separated list of colon-separated ID:ID pairs, or `none`.");
                                return;
                            }
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Thread log channel map updated.");
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
                case "allow_warning_unknown_users":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("Whether moderators can warn or ban users that have never been in the Discord before.", config.AllowWarningUnknownUsers ? "true" : "false");
                            return;
                        }
                        if (command.RawArguments[1] == "true")
                        {
                            config.AllowWarningUnknownUsers = true;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"You can now warn users that have never been in the Discord.");
                        }
                        else if (command.RawArguments[1] == "false")
                        {
                            config.AllowWarningUnknownUsers = false;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"You can no longer warn users that have never been in the Discord.");
                        }
                        else
                        {
                            SendErrorMessageReply(command.Message, "Invalid Value", "New value must be `true` or `false` only.");
                            return;
                        }
                        break;
                    }
                case "allow_bot_commands":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("Whether bots can do ModBot commands.", config.AllowBotCommands ? "true" : "false");
                            return;
                        }
                        if (command.RawArguments[1] == "true")
                        {
                            config.AllowBotCommands = true;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Bots can now do ModBot commands.");
                        }
                        else if (command.RawArguments[1] == "false")
                        {
                            config.AllowBotCommands = false;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Bots can no longer do ModBot commands.");
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
                case "ban_implies_mute":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("If true, a ban automatically implies a mute as well (for when they return from a tempban).", config.BanImpliesMute ? "true" : "false");
                            return;
                        }
                        if (command.RawArguments[1] == "true")
                        {
                            config.BanImpliesMute = true;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Bans now imply mutes.");
                        }
                        else if (command.RawArguments[1] == "false")
                        {
                            config.BanImpliesMute = false;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Bans no longer imply mutes.");
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
                        SendGenericPositiveMessageReply(command.Message, "Applied", $"Max ban duration set to {duration.Value.SimpleFormat(false)}.");
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
                case "spambot_automute":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("Whether known automatically detectable spambot behaviors should automatically result in a mute when detected.", config.AutomuteSpambots ? "true" : "false");
                            return;
                        }
                        if (command.RawArguments[1] == "true")
                        {
                            config.AutomuteSpambots = true;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Spambot-automute is now enabled.");
                        }
                        else if (command.RawArguments[1] == "false")
                        {
                            config.AutomuteSpambots = false;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Spambot-automute is now disabled.");
                        }
                        else
                        {
                            SendErrorMessageReply(command.Message, "Invalid Value", "New value must be `true` or `false` only.");
                            return;
                        }
                        break;
                    }
                case "send_warn_list_to_incident_threads":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("Whether to automatically send warning list to incident threads.", config.SendWarnListToIncidentThread ? "true" : "false");
                            return;
                        }
                        if (command.RawArguments[1] == "true")
                        {
                            config.SendWarnListToIncidentThread = true;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Send-warn-list-to-incident-threads is now enabled.");
                        }
                        else if (command.RawArguments[1] == "false")
                        {
                            config.SendWarnListToIncidentThread = false;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Send-warn-list-to-incident-threads is now disabled.");
                        }
                        else
                        {
                            SendErrorMessageReply(command.Message, "Invalid Value", "New value must be `true` or `false` only.");
                            return;
                        }
                        break;
                    }
                case "incident_channel_create_threads":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("Whether to automatically create private threads in the incident channel when a user is muted (only works on nitro level 2 servers).", config.IncidentChannelCreateThreads ? "true" : "false");
                            return;
                        }
                        if (command.RawArguments[1] == "true")
                        {
                            config.IncidentChannelCreateThreads = true;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Incident-channel-create-private-threads is now enabled.");
                        }
                        else if (command.RawArguments[1] == "false")
                        {
                            config.IncidentChannelCreateThreads = false;
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Incident-channel-create-private-threads is now disabled.");
                        }
                        else
                        {
                            SendErrorMessageReply(command.Message, "Invalid Value", "New value must be `true` or `false` only.");
                            return;
                        }
                        break;
                    }
                case "nonspambot_roles":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("The IDs of the roles that indicate a user is definitely not a spam bot. Users with these roles are protected from spambot automute.", config.NonSpambotRoles.IsEmpty() ? "None" : string.Join(",", config.NonSpambotRoles));
                            return;
                        }
                        string roleText = command.RawArguments[1];
                        if (roleText == "none" || roleText == "null")
                        {
                            config.NonSpambotRoles.Clear();
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Non-spambot role list emptied.");
                        }
                        else
                        {
                            try
                            {
                                config.NonSpambotRoles = [.. roleText.SplitFast(',').Select(s => ulong.Parse(s))];
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Invalid nonspambot_roles input, had exception: {ex}");
                                SendErrorMessageReply(command.Message, "Invalid Value", "Argument must be a comma-separated list of Role IDs, or 'none'.");
                                return;
                            }
                            SendGenericPositiveMessageReply(command.Message, "Applied", $"Non-spambot role list updated.");
                        }
                        break;
                    }
                case "add_react_role":
                    {
                        if (command.RawArguments.Length < 4)
                        {
                            StringBuilder roles = new();
                            if (config.ReactRoles.IsEmpty())
                            {
                                roles.Append("None");
                            }
                            else
                            {
                                foreach (KeyValuePair<ulong, GuildConfig.ReactRoleData> pair in config.ReactRoles)
                                {
                                    foreach (KeyValuePair<string, ulong> subPair in pair.Value.ReactToRole)
                                    {
                                        roles.Append($"(post={pair.Key}, role={subPair.Value}, reaction={subPair.Key}) ");
                                    }
                                }
                            }
                            SendHelpInfo("This command can be used to add a post-react role, with the format `add_react_role (post_id) (role_id) (reaction emote name)`", roles.ToString());
                            return;
                        }
                        if (!ulong.TryParse(command.RawArguments[1], out ulong postId))
                        {
                            SendErrorMessageReply(command.Message, "Invalid Value", "That post ID input is not a valid number.");
                            return;
                        }
                        if (!ulong.TryParse(command.RawArguments[2], out ulong roleId))
                        {
                            SendErrorMessageReply(command.Message, "Invalid Value", "That Role ID input is not a valid number.");
                            return;
                        }
                        string emote = command.RawArguments[3].ToLowerFast();
                        GuildConfig.ReactRoleData data = config.ReactRoles.GetOrCreate(postId, () => new GuildConfig.ReactRoleData() { ReactToRole = [] });
                        data.ReactToRole.Add(emote, roleId);
                        SendGenericPositiveMessageReply(command.Message, "Applied", $"Post `{postId}` now applies role `{roleId}` when emote `{emote}` is used.");
                        break;
                    }
                case "remove_react_role":
                    {
                        if (command.RawArguments.Length < 3)
                        {
                            StringBuilder roles = new();
                            if (config.ReactRoles.IsEmpty())
                            {
                                roles.Append("None");
                            }
                            else
                            {
                                foreach (KeyValuePair<ulong, GuildConfig.ReactRoleData> pair in config.ReactRoles)
                                {
                                    foreach (KeyValuePair<string, ulong> subPair in pair.Value.ReactToRole)
                                    {
                                        roles.Append($"(post={pair.Key}, role={subPair.Value}, reaction={subPair.Key}) ");
                                    }
                                }
                            }
                            SendHelpInfo("This command can be used to remove a post-react role, with the format `remove_react_role (post_id) (reaction emote name)`", roles.ToString());
                            return;
                        }
                        if (!ulong.TryParse(command.RawArguments[1], out ulong postId))
                        {
                            SendErrorMessageReply(command.Message, "Invalid Value", "That post ID input is not a valid number.");
                            return;
                        }
                        if (!config.ReactRoles.TryGetValue(postId, out GuildConfig.ReactRoleData data))
                        {
                            SendErrorMessageReply(command.Message, "Invalid Value", "That post ID doesn't correspond to any tracked react-roles.");
                            return;
                        }
                        string emote = command.RawArguments[2].ToLowerFast();
                        if (!data.ReactToRole.Remove(emote))
                        {
                            SendErrorMessageReply(command.Message, "Invalid Value", "That emote name doesn't correspond to any tracked react-roles.");
                            return;
                        }
                        if (data.ReactToRole.IsEmpty())
                        {
                            config.ReactRoles.Remove(postId);
                        }
                        SendGenericPositiveMessageReply(command.Message, "Applied", $"Post `{postId}` no longer applies a role when emote `{emote}` is used.");
                        break;
                    }
                case "add_special_role":
                    {
                        if (command.RawArguments.Length < 5)
                        {
                            SendHelpInfo("This command can be used to add a special role, with the format `add_special_role (name) (RoleID) (AddCommands) (RemoveCommands)` ... "
                                + "if the role should auto-warn, append also newline-separated `(AddLevel) (AddWarnText) (AddExplanation) (RemoveLevel) (RemoveWarnText) (RemoveExplanation)`\n"
                                + "If the notice should redirect to a different channel, append newline-separated `(Channel ID) (Type)` (where for Type 0 = don't, 1 = send message, 2 = public thread, 3 = private thread)\n"
                                + "For example, `add_special_role do-not-support 12345 donotsupport,nosupport,bad allowsupport,removenosupport,good\nNORMAL\nDo-Not-Support status applied\nYou are marked as do-not-support now\nNOTE\nDo-Not-Support status rescinded\nYou are allowed support again`", string.Join(", ", config.SpecialRoles.Keys));
                            return;
                        }
                        string name = command.RawArguments[1].ToLowerFast();
                        if (config.SpecialRoles.ContainsKey(name))
                        {
                            SendErrorMessageReply(command.Message, "Duplicate Role", "That special role already exists.");
                            return;
                        }
                        GuildConfig.SpecialRole role = new() { Name = name };
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
                        role.AddCommands = [.. command.RawArguments[3].SplitFast(',').Select(s => s.ToLowerFast())];
                        role.RemoveCommands = [.. command.RawArguments[4].Before("\n").SplitFast(',').Select(s => s.ToLowerFast())];
                        string[] reSplitArguments = [.. string.Join(" ", command.RawArguments).SplitFast('\n').Skip(1).Select(s => s.Trim().Replace("\\n", "\n")).Where(s => !string.IsNullOrWhiteSpace(s))];
                        if (command.RawArguments.Length > 5)
                        {
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
                        if (command.RawArguments.Length > 7)
                        {
                            if (!ulong.TryParse(reSplitArguments[6], out ulong channelId))
                            {
                                SendErrorMessageReply(command.Message, "Invalid Value", "That Channel ID input is not a valid number.");
                                return;
                            }
                            if (guild.GetChannel(channelId) == null)
                            {
                                SendErrorMessageReply(command.Message, "Invalid Value", "That Channel ID doesn't exist.");
                                return;
                            }
                            if (!int.TryParse(reSplitArguments[7], out int channelType) || channelType < 0 || channelType > 3)
                            {
                                SendErrorMessageReply(command.Message, "Invalid Value", "That Channel Type Code input is not a valid number (0, 1, 2, 3).");
                                return;
                            }
                            role.PutNoticeInChannel = channelId;
                            role.ChannelNoticeType = channelType;
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
                        if (config.SpecialRoles.Remove(name))
                        {
                            SendGenericPositiveMessageReply(command.Message, "Removed", $"Special role `{name}` removed.");
                        }
                        else
                        {
                            SendErrorMessageReply(command.Message, "Missing Role", "Can't remove that special role: it doesn't exist.");
                            return;
                        }
                        break;
                    }
                case "mute_notice_message":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("This command can be used to configure a message to ping users with in the incident channel when they're muted.", config.MuteNoticeMessage ?? GuildConfig.MUTE_NOTICE_DEFAULT);
                            return;
                        }
                        config.MuteNoticeMessage = string.Join(' ', command.RawArguments.Skip(1));
                        SendGenericPositiveMessageReply(command.Message, "Mute Notice Message Set", $"Mute notice message is now: {config.MuteNoticeMessage}");
                        break;
                    }
                case "mute_rejoin_notice_message":
                    {
                        if (command.RawArguments.Length == 1)
                        {
                            SendHelpInfo("This command can be used to configure a message to ping users with in the incident channel when they're muted and rejoin the discord.", config.MuteNoticeMessageRejoin ?? GuildConfig.MUTE_NOTICE_DEFAULT_REJOIN);
                            return;
                        }
                        config.MuteNoticeMessageRejoin = string.Join(' ', command.RawArguments.Skip(1));
                        SendGenericPositiveMessageReply(command.Message, "Mute-then-Rejoin Notice Message Set", $"Mute-then-rejoin notice message is now: {config.MuteNoticeMessageRejoin}");
                        break;
                    }
                default:
                    {
                        EmbedBuilder embed = new EmbedBuilder().WithTitle("Admin-Configure Usage Help").WithColor(255, 128, 0);
                        embed.Description = "ModBot's admin-configure command exists as a temporary trick pending plans to build a web interface to control ModBot more easily."
                            + "\nAny sub-command without further arguments will show more info about current value.\nMost sub-command accept `null` to mean remove/clear any value (except where not possible).";
                        embed.AddField("Available configure sub-commands", "`mute_role`, `moderator_roles`, `mute_notice_message`, `mute_rejoin_notice_message`, `attention_notice`, `incident_channel`, `join_notif_channel`, "
                            + "`voice_channel_join_notif_channel`, `role_change_notif_channel`, `name_change_notif_channel`, `mod_logs_channel`, `log_channels`, `thread_log_channels`, `incident_channel_create_threads`, `incident_thread_auto_add_ids`, "
                            + "`enforce_ascii_name_rule`, `enforce_name_start_rule`, `name_start_rule_lenient`, `warnings_enabled`, `bans_enabled`, `ban_implies_mute`, `max_ban_duration`, `allow_bot_commands`, `send_warn_list_to_incident_threads`, "
                            + "`notify_warns_in_dm`, `spambot_automute`, `nonspambot_roles`, `add_react_role`, `remove_react_role`, `add_special_role`, `remove_special_role`, `allow_warning_unknown_users`, `channel_move_notif_channels`");
                        SendReply(command.Message, embed.Build());
                        return;
                    }
            }
            guildDB.SaveConfig();
        }
    }
}
