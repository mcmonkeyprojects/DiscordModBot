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
using DiscordModBot.CommandHandlers;
using DiscordBotBase;
using DiscordBotBase.CommandHandlers;

namespace DiscordModBot
{
    /// <summary>
    /// Helper class for logging channels.
    /// </summary>
    public class ModBotLoggers
    {
        /// <summary>
        /// initialize all logger events on a Discord bot.
        /// </summary>
        public void InitLoggers(DiscordBot bot)
        {
            bot.Client.UserJoined += (user) =>
            {
                if (bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                if (user.Id == bot.Client.CurrentUser.Id)
                {
                    return Task.CompletedTask;
                }
                WarnableUser warnable = WarningUtilities.GetWarnableUser(user.Guild.Id, user.Id);
                IReadOnlyCollection<SocketTextChannel> channels = user.Guild.TextChannels;
                foreach (ulong chan in DiscordModBot.JoinNotifChannel)
                {
                    IEnumerable<SocketTextChannel> possibles = channels.Where(schan => schan.Id == chan);
                    if (possibles.Any())
                    {
                        int nameCount = warnable.OldNames().Count();
                        string seenNameText = nameCount < 1 ? "" : $" User has {nameCount} previously seen name(s).";
                        string createdDateText = $"`{StringConversionHelper.DateTimeToString(user.CreatedAt, false)}` ({user.CreatedAt.Subtract(DateTimeOffset.Now).SimpleFormat(true)})";
                        string message = $"User <@{user.Id}> (name: `{NameUtilities.Username(user)}`, ID: `{user.Id}`) joined. User account first created {createdDateText}.{seenNameText}";
                        possibles.First().SendMessageAsync(embed: new EmbedBuilder().WithTitle("User Join").WithDescription(message).Build()).Wait();
                    }
                }
                if (!warnable.GetWarnings().Any())
                {
                    Console.WriteLine($"Pay no mind to user-join: {user.Id} to {user.Guild.Id} ({user.Guild.Name})");
                    return Task.CompletedTask;
                }
                if (warnable.IsMuted)
                {
                    SocketRole role = user.Guild.Roles.FirstOrDefault((r) => r.Name.ToLowerInvariant() == DiscordModBot.MuteRoleName);
                    if (role == null)
                    {
                        Console.WriteLine("Cannot apply mute: no muted role found.");
                    }
                    else
                    {
                        user.AddRoleAsync(role).Wait();
                    }
                }
                foreach (ulong chan in DiscordModBot.IncidentChannel)
                {
                    IEnumerable<SocketTextChannel> possibles = channels.Where(schan => schan.Id == chan);
                    if (possibles.Any())
                    {
                        string message = $"User <@{ user.Id}> (`{NameUtilities.Username(user)}`) just joined, and has prior warnings. Use the `listwarnings` command to see details.";
                        possibles.First().SendMessageAsync(embed: new EmbedBuilder().WithTitle("Warned User Join").WithDescription(message).Build()).Wait();
                        if (warnable.IsMuted)
                        {
                            possibles.First().SendMessageAsync($"<@{user.Id}>", embed: new EmbedBuilder().WithTitle("Automatic Mute Applied").WithDescription("You have been automatically muted by the system due to being muted and then rejoining the Discord."
                                + " You may discuss the situation in this channel only, until a moderator unmutes you.").Build()).Wait();
                        }
                        return Task.CompletedTask;
                    }
                }
                Console.WriteLine("Failed to warn of dangerous user-join: " + user.Id + " to " + user.Guild.Id + "(" + user.Guild.Name + ")");
                return Task.CompletedTask;
            };
            bot.Client.UserLeft += (user) =>
            {
                if (bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                if (user.Id == bot.Client.CurrentUser.Id)
                {
                    return Task.CompletedTask;
                }
                IReadOnlyCollection<SocketTextChannel> channels = user.Guild.TextChannels;
                foreach (ulong chan in DiscordModBot.JoinNotifChannel)
                {
                    IEnumerable<SocketTextChannel> possibles = channels.Where(schan => schan.Id == chan);
                    if (possibles.Any())
                    {
                        string message = $"User <@{user.Id}> (name: `{NameUtilities.Username(user)}`, ID: `{user.Id}`) left.";
                        possibles.First().SendMessageAsync(embed: new EmbedBuilder().WithTitle("User Left").WithDescription(message).Build()).Wait();
                    }
                }
                return Task.CompletedTask;
            };
            bot.Client.MessageUpdated += (cache, message, channel) =>
            {
                if (bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                if (message.Author.Id == bot.Client.CurrentUser.Id)
                {
                    return Task.CompletedTask;
                }
                if (cache.HasValue && cache.Value.Content == message.Content)
                {
                    // Its a reaction/embed-load/similar, ignore it.
                    return Task.CompletedTask;
                }
                string originalText = cache.HasValue ? UserCommands.EscapeUserInput(cache.Value.Content) : "(not cached)";
                string newText = UserCommands.EscapeUserInput(message.Content);
                int longerLength = Math.Max(originalText.Length, newText.Length);
                int firstDifference = StringConversionHelper.FindFirstDifference(originalText, newText);
                int lastDifference = longerLength - StringConversionHelper.FindFirstDifference(originalText.ReverseFast(), newText.ReverseFast());
                if (firstDifference == -1 || lastDifference == -1)
                {
                    // Shouldn't be possible.
                    return Task.CompletedTask;
                }
                originalText = TrimForDifferencing(originalText, 700, firstDifference, lastDifference, longerLength);
                newText = TrimForDifferencing(newText, 900, firstDifference, lastDifference, longerLength);
                LogChannelActivity(channel.Id, $"+> Message from `{NameUtilities.Username(message.Author)}` (`{message.Author.Id}`) **edited** in <#{channel.Id}>:\n{originalText} Became:\n{newText}");
                return Task.CompletedTask;
            };
            bot.Client.MessageDeleted += (cache, channel) =>
            {
                if (bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                if (cache.HasValue && cache.Value.Author.Id == bot.Client.CurrentUser.Id)
                {
                    return Task.CompletedTask;
                }
                string originalText = cache.HasValue ? UserCommands.EscapeUserInput(cache.Value.Content) : "(not cached)";
                string author = cache.HasValue ? $"`{NameUtilities.Username(cache.Value.Author)}` (`{cache.Value.Author.Id}`)" : "(unknown)";
                LogChannelActivity(channel.Id, $"+> Message from {author} **deleted** in <#{channel.Id}>: `{originalText}`");
                return Task.CompletedTask;
            };
            bot.Client.GuildMemberUpdated += (oldUser, newUser) =>
            {
                if (bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                if (newUser.Id == bot.Client.CurrentUser.Id)
                {
                    return Task.CompletedTask;
                }
                bool lostRoles = oldUser.Roles.Any(r => !newUser.Roles.Contains(r));
                bool gainedRoles = newUser.Roles.Any(r => !oldUser.Roles.Contains(r));
                if (lostRoles || gainedRoles)
                {
                    EmbedBuilder roleChangeEmbed = new EmbedBuilder().WithTitle("User Role Change").WithDescription($"User <@{newUser.Id}> had roles updated.");
                    if (lostRoles)
                    {
                        roleChangeEmbed.AddField("Roles Removed", string.Join(", ", oldUser.Roles.Where(r => !newUser.Roles.Contains(r)).Select(r => $"`{r.Name}`")));
                    }
                    if (gainedRoles)
                    {
                        roleChangeEmbed.AddField("Roles Added", string.Join(", ", newUser.Roles.Where(r => !oldUser.Roles.Contains(r)).Select(r => $"`{r.Name}`")));
                    }
                    IReadOnlyCollection<SocketTextChannel> channels = newUser.Guild.TextChannels;
                    foreach (ulong chan in DiscordModBot.RoleChangeNotifChannel)
                    {
                        IEnumerable<SocketTextChannel> possibles = channels.Where(schan => schan.Id == chan);
                        if (possibles.Any())
                        {
                            possibles.First().SendMessageAsync(embed: roleChangeEmbed.Build()).Wait();
                        }
                    }
                }
                if (oldUser.Nickname != newUser.Nickname)
                {
                    EmbedBuilder embed = new EmbedBuilder().WithTitle("User Nickname Changed");
                    if (oldUser.Nickname != null)
                    {
                        embed.AddField("Old Nickname", $"`{UserCommands.EscapeUserInput(oldUser.Nickname)}`");
                    }
                    if (newUser.Nickname != null)
                    {
                        embed.AddField("New Nickname", $"`{UserCommands.EscapeUserInput(newUser.Nickname)}`");
                    }
                    string changeType = newUser.Nickname == null ? "removed their" : (oldUser.Nickname == null ? "added a" : "changed their");
                    embed.Description = $"User <@{newUser.Id}> {changeType} nickname.";
                    IReadOnlyCollection<SocketTextChannel> channels = newUser.Guild.TextChannels;
                    foreach (ulong chan in DiscordModBot.RoleChangeNotifChannel)
                    {
                        IEnumerable<SocketTextChannel> possibles = channels.Where(schan => schan.Id == chan);
                        if (possibles.Any())
                        {
                            possibles.First().SendMessageAsync(embed: embed.Build()).Wait();
                        }
                    }
                }
                return Task.CompletedTask;
            };
        }

        /// <summary>
        /// Utility for edit notifications.
        /// </summary>
        public string TrimForDifferencing(string text, int cap, int firstDiff, int lastDiff, int longerLength)
        {
            int initialFirstDiff = firstDiff;
            int initialLastDiff = lastDiff;
            if (text.Length > cap)
            {
                if (firstDiff > 100)
                {
                    text = "..." + text.Substring(firstDiff - 50);
                    lastDiff -= (firstDiff - 50 - "...".Length);
                    firstDiff = 50 + "...".Length;
                }
                if (text.Length > cap)
                {
                    text = text.Substring(0, Math.Min(lastDiff + 50, (cap - 50))) + "...";
                    lastDiff = Math.Min(lastDiff, (cap - 50));
                }
            }
            if (initialFirstDiff > 10 || initialLastDiff < longerLength - 10)
            {
                string preText = firstDiff == 0 ? "" : $"`{text.Substring(0, firstDiff)}`";
                string lastText = lastDiff >= text.Length ? "" : $"`{text.Substring(lastDiff)}`";
                text = $"{preText} **__`{text.Substring(firstDiff, Math.Min(lastDiff, text.Length) - firstDiff)}`__** {lastText}";
            }
            else
            {
                text = $"`{text}`";
            }
            return text;
        }

        /// <summary>
        /// Sends a log message to a log channel (if applicable).
        /// </summary>
        /// <param name="channelId">The channel where a loggable action happened.</param>
        /// <param name="message">A message to log.</param>
        public void LogChannelActivity(ulong channelId, string message)
        {
            if (!DiscordModBot.LogChannels.TryGetValue(channelId, out ulong logChannel) && !DiscordModBot.LogChannels.TryGetValue(0, out logChannel))
            {
                return;
            }
            if (!(DiscordBotBaseHelper.CurrentBot.Client.GetChannel(logChannel) is SocketTextChannel channel))
            {
                Console.WriteLine($"Bad channel log output ID: {logChannel}");
                return;
            }
            channel.SendMessageAsync(message).Wait();
        }
    }
}
