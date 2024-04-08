using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using FreneticUtilities.FreneticDataSyntax;
using FreneticUtilities.FreneticExtensions;
using FreneticUtilities.FreneticToolkit;
using DiscordBotBase;
using DiscordBotBase.CommandHandlers;
using Discord;
using Discord.WebSocket;
using ModBot.Core;

namespace ModBot.Database
{
    /// <summary>Utility class to manage the temporary ban system.</summary>
    public class TempBanManager
    {
        /// <summary>The path to the temp-bans config file.</summary>
        public static string TEMPBAN_FILE_PATH = "config/tempbans.fds"; // TODO: Should be moved to database?

        /// <summary>The FDS file containing the list of temporary bans.</summary>
        public FDSSection TempBansFile;

        /// <summary>Initialize the temp-ban manager.</summary>
        public TempBanManager()
        {
            try
            {
                TempBansFile = FDSUtility.ReadFile(TEMPBAN_FILE_PATH);
            }
            catch (FileNotFoundException)
            {
                TempBansFile = new FDSSection();
                TempBansFile.Set("count", 0);
                Save();
            }
        }

        /// <summary>The time the last auto-scan was ran.</summary>
        public DateTimeOffset LastScan = DateTimeOffset.UtcNow;

        /// <summary>Automatically scans temp-bans on a timer.</summary>
        public void CheckShouldScan()
        {
            if (DateTimeOffset.UtcNow.Subtract(LastScan).TotalMinutes > 30)
            {
                LastScan = DateTimeOffset.UtcNow;
                try
                {
                    Scan();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Auto-scan error: {ex}");
                }
            }
        }

        /// <summary>Disables any existing temp bans for an ID (to add a new one).</summary>
        public void DisableTempBansFor(ulong guildId, ulong userId, bool save = true)
        {
            lock (this)
            {
                FDSSection subSection = TempBansFile.GetSection("temp_ban");
                if (subSection is null)
                {
                    return;
                }
                foreach (string key in subSection.GetRootKeys())
                {
                    FDSSection banSection = subSection.GetSection(key);
                    ulong sectionGuildId = banSection.GetUlong("guild").Value;
                    ulong sectionUserId = banSection.GetUlong("user").Value;
                    if (sectionGuildId == guildId && sectionUserId == userId)
                    {
                        subSection.Remove(key);
                        TempBansFile.Set($"old_bans.{key}", subSection);
                        if (save)
                        {
                            Save();
                        }
                        return;
                    }
                }
            }
        }

        /// <summary>Scan for temp-bans to remove.</summary>
        public void Scan()
        {
            lock (this)
            {
                FDSSection subSection = TempBansFile.GetSection("temp_ban");
                if (subSection is null)
                {
                    return;
                }
                foreach (string key in subSection.GetRootKeys())
                {
                    FDSSection banSection = subSection.GetSection(key);
                    DateTimeOffset endTime = StringConversionHelper.StringToDateTime(banSection.GetString("end")).Value;
                    if (DateTimeOffset.UtcNow.Subtract(endTime).TotalSeconds > 0)
                    {
                        Console.WriteLine("Ban expired!");
                        ulong guildId = banSection.GetUlong("guild").Value;
                        ulong userId = banSection.GetUlong("user").Value;
                        SocketGuild guild = DiscordBotBaseHelper.CurrentBot.Client.GetGuild(guildId);
                        if (guild == null)
                        {
                            Console.WriteLine($"Temp ban expiration failed: invalid guild ID {guildId}!");
                            return;
                        }
                        bool removed = false;
                        try
                        {
                            guild.RemoveBanAsync(userId).Wait();
                            removed = true;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Removal failed: {ex}");
                        }
                        string success = removed ? "" : "\n\nBan removal failed. May already be unbanned.";
                        string name = UserCommands.EscapeUserInput(banSection.GetString("name"));
                        ModBotLoggers.SendEmbedToAllFor(guild, DiscordModBot.GetConfig(guildId).ModLogsChannel, new EmbedBuilder().WithTitle("Temp Ban Expired").WithDescription($"Temp ban for <@{userId}> (`{name}`) expired.{success}").Build());
                        subSection.Remove(key);
                        TempBansFile.Set($"old_bans.{key}", subSection);
                        Save();
                    }
                }
            }
        }

        /// <summary>Save the temp-bans file.</summary>
        public void Save()
        {
            lock (this)
            {
                FDSUtility.SaveToFile(TempBansFile, TEMPBAN_FILE_PATH);
            }
        }

        /// <summary>Temporarily bans a user from a guild for a set duration.</summary>
        public void TempBan(ulong guildId, ulong userId, DateTimeOffset until, ulong sourceId, string reason)
        {
            ConsoleLog.Debug($"Will temp-ban user {userId} from guild {guildId} until {until} due to source {sourceId} for reason: {reason}.");
            SocketGuild guild = DiscordBotBaseHelper.CurrentBot.Client.GetGuild(guildId);
            if (guild is null)
            {
                ConsoleLog.Warning($"Ban failed: invalid guild ID {guildId}!");
                return;
            }
            until = until.ToUniversalTime();
            SocketGuildUser user = guild.GetUser(userId);
            string name = user is null ? "(unknown)" :  $"{user.Username}";
            bool isForever = until.Year > DateTimeOffset.Now.Year + 50;
            string path = isForever ? "permanent_bans" : "temp_ban";
            lock (this)
            {
                ConsoleLog.Debug($"Temp-ban: lock acquired");
                DisableTempBansFor(guildId, userId, false);
                int count = TempBansFile.GetInt("count").Value + 1;
                TempBansFile.Set("count", count);
                TempBansFile.Set($"{path}.{count}.guild", guildId);
                TempBansFile.Set($"{path}.{count}.user", userId);
                TempBansFile.Set($"{path}.{count}.name", name);
                TempBansFile.Set($"{path}.{count}.reason", reason);
                if (!isForever)
                {
                    TempBansFile.Set($"{path}.{count}.end", StringConversionHelper.DateTimeToString(until, false));
                }
                Save();
                ConsoleLog.Debug($"Temp-ban: config save complete");
            }
            string banReason = (isForever ? "Indefinite ban" : $"Temporary ban for {(until - DateTimeOffset.UtcNow).SimpleFormat(false)} (ends <t:{until.ToUnixTimeSeconds()}>)") + $" by moderator <@{sourceId}>";
            if (!string.IsNullOrWhiteSpace(reason))
            {
                reason = $" Reason: `{reason}`";
                banReason += reason;
            }
            if (user is not null)
            {
                try
                {
                    ConsoleLog.Debug($"Temp-ban: will DM");
                    IDMChannel channel = user.CreateDMChannelAsync().Result;
                    string durationMessage = isForever ? "This ban lasts until manually removed by staff." : $"This ban expires <t:{until.ToUnixTimeSeconds()}:R>.";
                    channel.SendMessageAsync(embed: new EmbedBuilder().WithDescription("Discord Mod Bot").WithDescription($"You have been banned from **{guild.Name}**. {durationMessage}{reason}").WithThumbnailUrl(guild.IconUrl).Build()).Wait(new TimeSpan(0, 0, 20));
                    ConsoleLog.Debug($"Temp-ban: DM sent");
                }
                catch (Exception ex)
                {
                    ConsoleLog.Error($"Excepting sending tempban notice: {ex}");
                }
                user.BanAsync(0, banReason.Length > 400 ? (banReason[0..400] + "..") : banReason).Wait();
            }
            else
            {
                guild.AddBanAsync(userId, 0, banReason).Wait();
            }
            ConsoleLog.Debug($"Temp-ban: banning completed");
        }
    }
}
