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
using FreneticUtilities.FreneticToolkit;
using DiscordBotBase;
using DiscordBotBase.CommandHandlers;
using ModBot.Database;
using ModBot.WarningHandlers;
using ModBot.CommandHandlers;
using LiteDB;

namespace ModBot.Core
{
    /// <summary>Helper class for logging channels.</summary>
    public class ModBotLoggers
    {
        /// <summary>The relevant bot instance.</summary>
        public DiscordBot Bot;

        /// <summary>initialize all logger events on a Discord bot.</summary>
        public void InitLoggers(DiscordBot _bot)
        {
            Bot = _bot;
            Bot.Client.UserJoined += (user) =>
            {
                if (Bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                try
                {
                    if (user.Id == Bot.Client.CurrentUser.Id)
                    {
                        return Task.CompletedTask;
                    }
                    DiscordModBot.TempBanHandler.CheckShouldScan();
                    WarnableUser warnable = WarningUtilities.GetWarnableUser(user.Guild.Id, user.Id);
                    GuildConfig config = DiscordModBot.GetConfig(user.Guild.Id);
                    if (config.JoinNotifChannel.Any() || config.ModLogsChannel.Any())
                    {
                        int nameCount = warnable.SeenNames.Count;
                        string seenNameText = nameCount < 1 ? "" : $" User has {nameCount} previously seen name(s).";
                        string createdDateText = $"`{StringConversionHelper.DateTimeToString(user.CreatedAt, false)}` ({user.CreatedAt.Subtract(DateTimeOffset.Now).SimpleFormat(true)})";
                        string message = $"User <@{user.Id}> (name: `{NameUtilities.Username(user)}`, ID: `{user.Id}`) joined. User account first created {createdDateText}.{seenNameText}";
                        SendEmbedToAllFor(user.Guild, config.JoinNotifChannel, new EmbedBuilder().WithColor(32, 255, 128).WithTitle("User Join").WithDescription(message).Build());
                        if (DateTimeOffset.Now.Subtract(user.CreatedAt).TotalDays < 31 * 6)
                        {
                            SendEmbedToAllFor(user.Guild, config.ModLogsChannel, new EmbedBuilder().WithTitle("New Account Join").WithDescription($"User <@{user.Id}> (`{NameUtilities.Username(user)}`) joined the Discord as an account first created {createdDateText}.").Build(), text: $"<@{user.Id}>");
                        }
                    }
                    DiscordModBot.TrackUsernameFor(user, user.Guild);
                    if (config.MuteRole.HasValue)
                    {
                        if (warnable.IsMuted)
                        {
                            SocketRole role = user.Guild.GetRole(config.MuteRole.Value);
                            if (role != null)
                            {
                                user.AddRoleAsync(role).Wait();
                                Task.Delay(6000).Wait(); // Backup in case another bot conflicts with this bot (due to the weird way AddRole works inside)
                                user.AddRoleAsync(role).Wait();
                            }
                        }
                    }
                    foreach (string specialRoleName in warnable.SpecialRoles)
                    {
                        if (config.SpecialRoles.TryGetValue(specialRoleName, out GuildConfig.SpecialRole specialRole))
                        {
                            SocketRole role = user.Guild.GetRole(specialRole.RoleID);
                            if (role != null)
                            {
                                user.AddRoleAsync(role).Wait();
                                Task.Delay(6000).Wait(); // Backup in case another bot conflicts with this bot (due to the weird way AddRole works inside)
                                user.AddRoleAsync(role).Wait();
                            }
                        }
                    }
                    if (!warnable.Warnings.Any())
                    {
                        Console.WriteLine($"Pay no mind to user-join: {user.Id} to {user.Guild.Id} ({user.Guild.Name})");
                        return Task.CompletedTask;
                    }
                    if (config.ModLogsChannel.Any() || config.IncidentChannel.Any())
                    {
                        IReadOnlyCollection<SocketTextChannel> channels = user.Guild.TextChannels;
                        foreach (ulong chan in config.ModLogsChannel)
                        {
                            IEnumerable<SocketTextChannel> possibles = channels.Where(schan => schan.Id == chan);
                            if (possibles.Any())
                            {
                                string description = $"User <@{user.Id}> (`{NameUtilities.Username(user)}`) just joined, and has prior warnings.";
                                SendEmbedToAllFor(user.Guild, config.ModLogsChannel, new EmbedBuilder().WithTitle("Warned User Join").WithDescription(description).Build(), text: $"<@{user.Id}>");
                                WarningCommands.SendWarningList(warnable, 0, possibles.First(), null);
                            }
                        }
                        foreach (ulong chan in config.IncidentChannel)
                        {
                            SocketGuildChannel incidentChan = user.Guild.GetChannel(chan);
                            if (incidentChan != null && incidentChan is ISocketMessageChannel incidentChanText)
                            {
                                if (warnable.IsMuted)
                                {
                                    SocketThreadChannel thread = WarningCommands.GenerateThreadFor(config, user.Guild, user, warnable);
                                    if (thread is not null)
                                    {
                                        incidentChanText = thread;
                                    }
                                    incidentChanText.SendMessageAsync($"<@{user.Id}>", embed: new EmbedBuilder().WithTitle("Automatic Mute Applied").WithColor(255, 0, 0).WithDescription(config.MuteNoticeMessageRejoin ?? GuildConfig.MUTE_NOTICE_DEFAULT_REJOIN).Build()).Wait();
                                }
                                return Task.CompletedTask;
                            }
                        }
                    }
                    Console.WriteLine("Failed to warn of dangerous user-join: " + user.Id + " to " + user.Guild.Id + "(" + user.Guild.Name + ")");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error handling user join: {ex}");
                }
                return Task.CompletedTask;
            };
            Bot.Client.UserLeft += (guild, user) =>
            {
                if (Bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                try
                {
                    if (user.Id == Bot.Client.CurrentUser.Id)
                    {
                        return Task.CompletedTask;
                    }
                    DiscordModBot.TempBanHandler.CheckShouldScan();
                    GuildConfig config = DiscordModBot.GetConfig(guild.Id);
                    if (config.JoinNotifChannel.Any())
                    {
                        string message = $"User <@{user.Id}> (name: `{NameUtilities.Username(user)}`, ID: `{user.Id}`) left.";
                        SendEmbedToAllFor(guild, config.JoinNotifChannel, new EmbedBuilder().WithTitle("User Left").WithColor(64, 64, 0).WithDescription(message).Build());
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error handling user leave: {ex}");
                }
                return Task.CompletedTask;
            };
            Bot.Client.UserBanned += (user, guild) =>
            {
                if (Bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                if (user.Id == Bot.Client.CurrentUser.Id)
                {
                    return Task.CompletedTask;
                }
                GuildConfig config = DiscordModBot.GetConfig(guild.Id);
                if (config.ModLogsChannel.Any())
                {
                    string message = $"User <@{user.Id}> (name: `{NameUtilities.Username(user)}`, ID: `{user.Id}`) was banned.";
                    SendEmbedToAllFor(guild, config.ModLogsChannel, new EmbedBuilder().WithTitle("User Banned").WithColor(128, 0, 0).WithDescription(message).Build());
                }
                return Task.CompletedTask;
            };
            Bot.Client.MessageUpdated += (cache, message, channel) =>
            {
                if (Bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                try
                {
                    if (message.Author.Id == Bot.Client.CurrentUser.Id)
                    {
                        return Task.CompletedTask;
                    }
                    if (message.Author.IsBot || message.Author.IsWebhook)
                    {
                        return Task.CompletedTask;
                    }
                    if (channel is not SocketGuildChannel socketChannel)
                    {
                        return Task.CompletedTask;
                    }
                    bool hasCache = TryGetCached(socketChannel, cache.Id, out StoredMessage oldMessage);
                    if (hasCache && oldMessage.CurrentContent() == message.Content)
                    {
                        // Its a reaction/embed-load/similar, ignore it.
                        return Task.CompletedTask;
                    }
                    if (message.Author.Id == 0) // inexplicably possible in relation to threads
                    {
                        return Task.CompletedTask;
                    }
                    LogMessageChange(socketChannel, cache.Id, message, message.Content);
                    GuildConfig config = DiscordModBot.GetConfig(socketChannel.Guild.Id);
                    if (config.LogChannels.Any())
                    {
                        string originalText = hasCache ? oldMessage.CurrentContent() + (oldMessage.Attachments is null ? "" : string.Join(", ", oldMessage.Attachments)) : $"(not cached)";
                        string newText = message.Content + string.Join(", ", message.Attachments.Select(a => a.Url));
                        int longerLength = Math.Max(originalText.Length, newText.Length);
                        int firstDifference = StringConversionHelper.FindFirstDifference(originalText, newText);
                        int lastDifference = longerLength - StringConversionHelper.FindFirstDifference(originalText.ReverseFast(), newText.ReverseFast());
                        if (firstDifference == -1 || lastDifference == -1)
                        {
                            // Shouldn't be possible.
                            return Task.CompletedTask;
                        }
                        originalText = TrimForDifferencing(originalText, firstDifference, lastDifference, longerLength);
                        newText = TrimForDifferencing(newText, firstDifference, lastDifference, longerLength);
                        LogChannelActivity(socketChannel, $"+> Message from `{NameUtilities.Username(message.Author)}` (`{message.Author.Id}`) **edited** in {ReferenceChannelSource(socketChannel)}:\n**Old:** \"*{originalText}*\"\n**New:** \"*{newText}*\"");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error while processing message delete {ex}");
                }
                return Task.CompletedTask;
            };
            Bot.Client.MessageDeleted += (cache, channel) =>
            {
                if (Bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                try
                {
                    Console.WriteLine($"Parsing deletion of message id {cache.Id} in channel {channel.Id}");
                    if (channel.GetOrDownloadAsync().Result is not SocketGuildChannel socketChannel)
                    {
                        return Task.CompletedTask;
                    }
                    LogMessageChange(socketChannel, cache.Id, cache.HasValue ? cache.Value : null, "");
                    bool hasCache = TryGetCached(socketChannel, cache.Id, out StoredMessage message);
                    if (hasCache)
                    {
                        if (message.AuthorID == Bot.Client.CurrentUser.Id)
                        {
                            return Task.CompletedTask;
                        }
                        SocketUser author = Bot.Client.GetUser(message.AuthorID);
                        if (author is not null && (author.IsBot || author.IsWebhook))
                        {
                            return Task.CompletedTask;
                        }
                    }
                    GuildConfig config = DiscordModBot.GetConfig(socketChannel.Guild.Id);
                    if (config.LogChannels.Any())
                    {
                        SocketUser user = hasCache ? Bot.Client.GetUser(message.AuthorID) : null;
                        string originalText = hasCache ? message.CurrentContent() + (message.Attachments is null ? "" : string.Join(", ", message.Attachments)) : null;
                        if (originalText.Length > 1850)
                        {
                            originalText = originalText[..1800] + "...";
                        }
                        if (originalText is null)
                        {
                            originalText = $"(not cached post ID `{cache.Id}`)";
                        }
                        else
                        {
                            originalText = UserCommands.EscapeForPlainText(originalText);
                        }
                        string author;
                        if (user is not null)
                        {
                            author = $"`{NameUtilities.Username(user)}` (`{user.Id}`)";
                        }
                        string replyNote = "";
                        if (hasCache)
                        {
                            WarnableUser warnUser = WarningUtilities.GetWarnableUser(socketChannel.Guild.Id, message.AuthorID);
                            if (warnUser is not null && !string.IsNullOrWhiteSpace(warnUser.LastKnownUsername))
                            {
                                author = $"`{warnUser.LastKnownUsername}` (`{warnUser.UserID()}`)";
                            }
                            else
                            {
                                author = $"(broken/unknown user: `{message.AuthorID}`)";
                            }
                            if (message.RepliesToID != 0)
                            {
                                if (TryGetCached(socketChannel, message.RepliesToID, out StoredMessage repliedMessage))
                                {
                                    WarnableUser repliedAuthor = WarningUtilities.GetWarnableUser(socketChannel.Guild.Id, repliedMessage.AuthorID);
                                    if (repliedAuthor is not null && !string.IsNullOrWhiteSpace(repliedAuthor.LastKnownUsername))
                                    {
                                        replyNote = $" (was in **reply** to message `{message.RepliesToID}` by author `{repliedAuthor.LastKnownUsername}` (`{repliedMessage.AuthorID}`))";
                                    }
                                    else
                                    {
                                        replyNote = $" (was in **reply** to message `{message.RepliesToID}` by unknown author `{repliedMessage.AuthorID}`)";
                                    }
                                }
                                else
                                {
                                    replyNote = $" (was in **reply** to unknown message `{message.RepliesToID}`)";
                                }
                            }
                        }
                        else
                        {
                            author = $"(unknown)";
                        }
                        LogChannelActivity(socketChannel, $"+> Message from {author} **deleted** in {ReferenceChannelSource(socketChannel)}{replyNote}: {originalText}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error while processing message delete {ex}");
                }
                return Task.CompletedTask;
            };
            Bot.Client.UserVoiceStateUpdated += (user, oldState, newState) =>
            {
                if (Bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                if (user.Id == Bot.Client.CurrentUser.Id)
                {
                    return Task.CompletedTask;
                }
                if (user is not SocketGuildUser socketUser)
                {
                    return Task.CompletedTask;
                }
                GuildConfig config = DiscordModBot.GetConfig(socketUser.Guild.Id);
                if (config.VoiceChannelJoinNotifs.Any())
                {
                    if (oldState.VoiceChannel?.Id != newState.VoiceChannel?.Id)
                    {
                        EmbedBuilder embed = new EmbedBuilder().WithTitle("User Move In Voice Channels").WithColor(0, 64, 255);
                        if (oldState.VoiceChannel is not null)
                        {
                            embed.AddField("Old Channel", $"<#{oldState.VoiceChannel.Id}>");
                        }
                        if (newState.VoiceChannel is not null)
                        {
                            embed.AddField("New Channel", $"<#{newState.VoiceChannel.Id}>");
                        }
                        string changeType = newState.VoiceChannel is null ? "left a" : (oldState.VoiceChannel is null ? "entered a" : "moved to a different");
                        embed.Description = $"User <@{user.Id}> {changeType} voice channel.";
                        SendEmbedToAllFor((newState.VoiceChannel ?? oldState.VoiceChannel).Guild, config.VoiceChannelJoinNotifs, embed.Build());
                    }
                }
                return Task.CompletedTask;
            };
            Bot.Client.GuildMemberUpdated += (oldUser, newUser) =>
            {
                if (Bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                try
                {
                    if (newUser.Id == Bot.Client.CurrentUser.Id)
                    {
                        return Task.CompletedTask;
                    }
                    GuildConfig config = DiscordModBot.GetConfig(newUser.Guild.Id);
                    if (config.RoleChangeNotifChannel.Any() && oldUser.HasValue)
                    {
                        bool lostRoles = oldUser.Value.Roles.Any(r => !newUser.Roles.Contains(r));
                        bool gainedRoles = newUser.Roles.Any(r => !oldUser.Value.Roles.Contains(r));
                        if (lostRoles || gainedRoles)
                        {
                            EmbedBuilder roleChangeEmbed = new EmbedBuilder().WithTitle("User Role Change").WithDescription($"User <@{newUser.Id}> had roles updated.");
                            if (lostRoles)
                            {
                                roleChangeEmbed.AddField("Roles Removed", string.Join(", ", oldUser.Value.Roles.Where(r => !newUser.Roles.Contains(r)).Select(r => $"<@&{r.Id}>")));
                            }
                            if (gainedRoles)
                            {
                                roleChangeEmbed.AddField("Roles Added", string.Join(", ", newUser.Roles.Where(r => !oldUser.Value.Roles.Contains(r)).Select(r => $"<@&{r.Id}>")));
                            }
                            SendEmbedToAllFor(newUser.Guild, config.RoleChangeNotifChannel, roleChangeEmbed.Build());
                        }
                    }
                    if (config.NameChangeNotifChannel.Any() && oldUser.HasValue)
                    {
                        if (oldUser.Value.Nickname != newUser.Nickname)
                        {
                            EmbedBuilder embed = new EmbedBuilder().WithTitle("User Nickname Changed").WithColor(0, 255, 255);
                            if (oldUser.Value.Nickname != null)
                            {
                                embed.AddField("Old Nickname", $"`{UserCommands.EscapeUserInput(oldUser.Value.Nickname)}`");
                            }
                            if (newUser.Nickname != null)
                            {
                                embed.AddField("New Nickname", $"`{UserCommands.EscapeUserInput(newUser.Nickname)}`");
                            }
                            string changeType = newUser.Nickname == null ? "removed their" : (oldUser.Value.Nickname == null ? "added a" : "changed their");
                            embed.Description = $"User <@{newUser.Id}> {changeType} nickname.";
                            SendEmbedToAllFor(newUser.Guild, config.NameChangeNotifChannel, embed.Build());
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Write($"Error handling guild user update {ex}");
                }
                return Task.CompletedTask;
            };
            Bot.Client.ThreadCreated += (thread) =>
            {
                if (Bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                LogThreadActivity(thread, $"**New thread created**"); //  by user `{NameUtilities.Username(thread.Owner)}` (`{thread.Owner?.Id}`)
                return Task.CompletedTask;
            };
            Bot.Client.ThreadDeleted += (thread) =>
            {
                if (Bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                if (!thread.HasValue)
                {
                    return Task.CompletedTask;
                }
                LogThreadActivity(thread.Value, $"**Thread deleted**");
                return Task.CompletedTask;
            };
            Bot.Client.ThreadMemberJoined += (user) =>
            {
                if (Bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                LogThreadJoin(user.Thread, $"`{NameUtilities.Username(user)}` (`{user.Id}`)");
                return Task.CompletedTask;
            };
            Bot.Client.ThreadMemberLeft += (user) =>
            {
                if (Bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                LogThreadActivity(user.Thread, $"**User left thread:** `{NameUtilities.Username(user)}` (`{user.Id}`)");
                return Task.CompletedTask;
            };
            Bot.Client.ThreadUpdated += (oldThread, newThread) =>
            {
                if (Bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                if (oldThread.HasValue)
                {
                    if (newThread.Name != oldThread.Value.Name)
                    {
                        LogThreadActivity(newThread, $"**Thread name changed:** was `{UserCommands.EscapeUserInput(oldThread.Value.Name)}`");
                    }
                    if (newThread.IsArchived && !oldThread.Value.IsArchived)
                    {
                        LogThreadActivity(newThread, $"**Thread archived**");
                    }
                    if (!newThread.IsArchived && oldThread.Value.IsArchived)
                    {
                        LogThreadActivity(newThread, $"**Thread pulled out from archive**");
                    }
                }
                return Task.CompletedTask;
            };
            Bot.Client.MessageReceived += (socketMessage) =>
            {
                if (socketMessage is not IUserMessage message)
                {
                    return Task.CompletedTask;
                }
                if (Bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                LogNewMessage(socketMessage);
                if (message.Channel is not SocketThreadChannel threadChannel)
                {
                    return Task.CompletedTask;
                }
                if (message.Author.IsBot || message.Author.IsWebhook || message.Author.Id == Bot.Client.CurrentUser.Id)
                {
                    return Task.CompletedTask;
                }
                string messageText = socketMessage.Content;
                if (messageText.Length > 1000)
                {
                    messageText = messageText[0..900] + "...";
                }
                if (socketMessage.Attachments.Any())
                {
                    messageText += " " + string.Join(' ', socketMessage.Attachments.Select(s => s.Url));
                }
                if (string.IsNullOrWhiteSpace(messageText))
                {
                    messageText = "(Empty message)";
                }
                string output = $"User `{NameUtilities.Username(message.Author)}` (`{message.Author.Id}`) said: {UserCommands.EscapeForPlainText(messageText)}";
                LogThreadActivity(threadChannel, output);
                return Task.CompletedTask;
            };
        }

        public bool TryGetCached(SocketChannel channel, ulong id, out StoredMessage message)
        {
            message = GetCachedMessage(channel, id);
            return message is not null;
        }

        public StoredMessage GetCachedMessage(SocketChannel channel, ulong id)
        {
            if (channel is not SocketGuildChannel guildChannel)
            {
                return null;
            }
            ulong parentId = channel is SocketThreadChannel threadChannel ? threadChannel.ParentChannel.Id : channel.Id;
            return DiscordModBot.DatabaseHandler.GetDatabase(guildChannel.Guild.Id).GetMessageHistory(parentId).FindById(unchecked((long)id));
        }

        public void LogNewMessage(SocketMessage message)
        {
            if (message.Channel is not SocketGuildChannel guildChannel)
            {
                return;
            }
            ulong parentId = message.Channel is SocketThreadChannel threadChannel ? threadChannel.ParentChannel.Id : message.Channel.Id;
            DiscordModBot.DatabaseHandler.GetDatabase(guildChannel.Guild.Id).GetMessageHistory(parentId).Upsert(new StoredMessage(message));
        }

        public void LogMessageChange(SocketChannel channel, ulong messageId, IMessage cached, string newContent)
        {
            if (channel is not SocketGuildChannel guildChannel)
            {
                return;
            }
            ulong parentId = channel is SocketThreadChannel threadChannel ? threadChannel.ParentChannel.Id : channel.Id;
            ILiteCollection<StoredMessage> history = DiscordModBot.DatabaseHandler.GetDatabase(guildChannel.Guild.Id).GetMessageHistory(parentId);
            StoredMessage stored = history.FindById(unchecked((long)messageId));
            if (stored is null)
            {
                if (cached is null)
                {
                    return;
                }
                stored = new StoredMessage(cached);
            }
            if (stored.MessageEdits is null)
            {
                stored.MessageEdits = new List<StoredMessage.MessageAlteration>();
            }
            if (stored.MessageEdits.Count > 50) // Some bots will spam message edits, so just stop bothering to log after there's too many to avoid wasting database space.
            {
                return;
            }
            stored.MessageEdits.Add(new StoredMessage.MessageAlteration() { Time = StringConversionHelper.DateTimeToString(DateTimeOffset.UtcNow, true), Content = newContent, IsDeleted = string.IsNullOrEmpty(newContent) });
            history.Upsert(stored);
        }

        public class ThreadJoinsBulker
        {
            public SocketThreadChannel ThreadChannel;

            public List<string> NewUsers = new();

            public DateTimeOffset LastAddedTo = DateTimeOffset.UtcNow;

            public ModBotLoggers Logger;

            public ThreadJoinsBulker(ModBotLoggers logger, SocketThreadChannel thread)
            {
                Logger = logger;
                ThreadChannel = thread;
                Task.Factory.StartNew(() =>
                {
                    while (true)
                    {
                        Thread.Sleep(500);
                        lock (Logger.ThreadJoinsLock)
                        {
                            if (DateTimeOffset.UtcNow.Subtract(LastAddedTo).TotalSeconds > 1.5)
                            {
                                Logger.LogThreadActivity(ThreadChannel, $"**User(s) joined thread:** {string.Join(", ", NewUsers)}");
                                Logger.ThreadJoins.Remove(ThreadChannel.Id, out _);
                                return;
                            }
                        }
                    }
                });
            }
        }

        public ConcurrentDictionary<ulong, ThreadJoinsBulker> ThreadJoins = new();

        public LockObject ThreadJoinsLock = new();

        public void LogThreadJoin(SocketThreadChannel threadChannel, string user)
        {
            GuildConfig config = DiscordModBot.GetConfig(threadChannel.Guild.Id);
            if (config.ThreadLogChannels.IsEmpty())
            {
                return;
            }
            lock (ThreadJoinsLock)
            {
                ThreadJoinsBulker bulker = ThreadJoins.GetOrAdd(threadChannel.Id, id => new ThreadJoinsBulker(this, threadChannel));
                bulker.NewUsers.Add(user);
                bulker.LastAddedTo = DateTimeOffset.UtcNow;
            }
        }

        public ConcurrentDictionary<ulong, (string, DateTimeOffset)> LastThreadLogHeader = new();

        public void LogThreadActivity(SocketThreadChannel threadChannel, string activity)
        {
            GuildConfig config = DiscordModBot.GetConfig(threadChannel.Guild.Id);
            if (config.ThreadLogChannels.IsEmpty())
            {
                return;
            }
            ulong targetChannelId = 0;
            if (config.ThreadLogChannels.TryGetValue(threadChannel.ParentChannel.Id, out ulong directLogChannelId))
            {
                targetChannelId = directLogChannelId;
            }
            else if (config.ThreadLogChannels.TryGetValue(0, out ulong defaultLogChannelId))
            {
                targetChannelId = defaultLogChannelId;
            }
            if (targetChannelId == 0)
            {
                return;
            }
            SocketTextChannel target = threadChannel.Guild.GetTextChannel(targetChannelId);
            if (target is null)
            {
                return;
            }
            try
            {
                string header = $"[**Thread Log**] <#{threadChannel.Id}> (`{threadChannel.Id}: {UserCommands.EscapeUserInput(threadChannel.Name)}` in channel <#{threadChannel.ParentChannel.Id}>): ";
                if (LastThreadLogHeader.TryGetValue(target.Id, out (string, DateTimeOffset) lastHeader) && header == lastHeader.Item1 && DateTimeOffset.UtcNow.Subtract(lastHeader.Item2).TotalMinutes < 15)
                {
                    header = "[Cont'd] ";
                }
                else
                {
                    LastThreadLogHeader[target.Id] = (header, DateTimeOffset.UtcNow);
                }
                Bot.GetBulker(target).Send(header + activity);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to output thread logs to channel {targetChannelId}: {ex}");
            }
        }

        /// <summary>Utility to send an embed to all channels in a list of IDs for a specific guild.</summary>
        public static void SendEmbedToAllFor(SocketGuild guild, List<ulong> notifChannels, Embed embed, string text = null)
        {
            IReadOnlyCollection<SocketTextChannel> channels = guild.TextChannels;
            foreach (ulong chan in notifChannels)
            {
                IEnumerable<SocketTextChannel> possibles = channels.Where(schan => schan.Id == chan);
                if (possibles.Any())
                {
                    possibles.First().SendMessageAsync(text: text, embed: embed, allowedMentions: AllowedMentions.None).Wait();
                }
            }
        }

        /// <summary>Utility for edit notification processing.</summary>
        public string TrimForDifferencing(string text, int firstDiff, int lastDiff, int longerLength)
        {
            if (lastDiff == firstDiff)
            {
                if (text.Length > 1700)
                {
                    text = text[..1600] + "...";
                }
                return UserCommands.EscapeForPlainText(text);
            }
            if (lastDiff < firstDiff)
            {
                (lastDiff, firstDiff) = (firstDiff, lastDiff);
            }
            if ((firstDiff > 10 || lastDiff < longerLength - 10) && lastDiff - firstDiff < 1500)
            {
                string preText = firstDiff == 0 ? "" : text[..firstDiff];
                if (preText.Length > 800)
                {
                    preText = $"... {preText[^600..]}";
                }
                string lastText = lastDiff >= text.Length ? "" : text[lastDiff..];
                if (lastText.Length > 800)
                {
                    lastText = $"{lastText[..600]} ...";
                }
                string middleText = text[firstDiff..Math.Min(lastDiff, text.Length)];
                if (!string.IsNullOrWhiteSpace(middleText))
                {
                    return $"{UserCommands.EscapeForPlainText(preText)}**__{UserCommands.EscapeForPlainText(middleText)}__**{UserCommands.EscapeForPlainText(lastText)}";
                }
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                return "(blank)";
            }
            else
            {
                if (text.Length > 1700)
                {
                    text = text[..1600] + "...";
                }
                return UserCommands.EscapeForPlainText(text);
            }
        }

        /// <summary>Tries to get the channel ID that the given channel should log into. Returns false if no such log channel exists.</summary>
        public bool TryGetLogChannel(SocketGuildChannel channel, out ulong logChannel)
        {
            GuildConfig config = DiscordModBot.GetConfig(channel.Guild.Id);
            if (config.LogChannels.TryGetValue(channel.Id, out logChannel))
            {
                return true;
            }
            if (config.LogChannels.TryGetValue(0, out logChannel))
            {
                return true;
            }
            ICategoryChannel category = channel.Guild.CategoryChannels.FirstOrDefault(c => c.Channels.Any(chan => chan.Id == channel.Id));
            if (category != null && config.LogChannels.TryGetValue(category.Id, out logChannel))
            {
                return true;
            }
            return false;
        }

        /// <summary>Creates a reference string to a text channel, or a thread channel.</summary>
        public static string ReferenceChannelSource(SocketGuildChannel channel)
        {
            if (channel is SocketThreadChannel threadChannel)
            {
                return $"<#{channel.Id}> in <#{threadChannel.ParentChannel.Id}>";
            }
            return $"<#{channel.Id}>";
        }

        /// <summary>Sends a log message to a log channel (if applicable).</summary>
        /// <param name="channel">The channel where a loggable action happened.</param>
        /// <param name="message">A message to log.</param>
        public void LogChannelActivity(SocketGuildChannel channel, string message)
        {
            if (channel is SocketThreadChannel threadChannel)
            {
                channel = threadChannel.ParentChannel;
            }
            if (!TryGetLogChannel(channel, out ulong logChannel))
            {
                return;
            }
            channel = channel.Guild.GetChannel(logChannel);
            if (channel is not SocketTextChannel textChannel)
            {
                Console.WriteLine($"Bad channel log output ID: {logChannel}");
                return;
            }
            Bot.GetBulker(textChannel).Send(message);
        }
    }
}
