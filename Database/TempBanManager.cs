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
    /// <summary>
    /// Utility class to manage the temporary ban system.
    /// </summary>
    public class TempBanManager
    {
        /// <summary>
        /// The path to the temp-bans config file.
        /// </summary>
        public static string TEMPBAN_FILE_PATH = "config/tempbans.fds";

        /// <summary>
        /// The FDS file containing the list of temporary bans.
        /// </summary>
        public FDSSection TempBansFile;

        /// <summary>
        /// Initialize the temp-ban manager.
        /// </summary>
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

        /// <summary>
        /// The time the last auto-scan was ran.
        /// </summary>
        public DateTimeOffset LastScan = DateTimeOffset.UtcNow;

        /// <summary>
        /// Automatically scans temp-bans on a timer.
        /// </summary>
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

        /// <summary>
        /// Disables any existing temp bans for an ID (to add a new one).
        /// </summary>
        public void DisableTempBansFor(ulong guildId, ulong userId)
        {
            lock (this)
            {
                FDSSection subSection = TempBansFile.GetSection("temp_ban");
                if (subSection == null)
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
                        Save();
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Scan for temp-bans to remove.
        /// </summary>
        public void Scan()
        {
            lock (this)
            {
                FDSSection subSection = TempBansFile.GetSection("temp_ban");
                if (subSection == null)
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

        /// <summary>
        /// Save the temp-bans file.
        /// </summary>
        public void Save()
        {
            lock (this)
            {
                FDSUtility.SaveToFile(TempBansFile, TEMPBAN_FILE_PATH);
            }
        }

        /// <summary>
        /// Temporarily bans a user from a guild for a set duration.
        /// </summary>
        public void TempBan(ulong guildId, ulong userId, TimeSpan duration)
        {
            SocketGuild guild = DiscordBotBaseHelper.CurrentBot.Client.GetGuild(guildId);
            if (guild == null)
            {
                Console.WriteLine($"Ban failed: invalid guild ID {guildId}!");
                return;
            }
            SocketGuildUser user = guild.GetUser(userId);
            string name = user == null ? "(unknown)" :  $"{user.Username}#{user.Discriminator}";
            bool isForever = duration.TotalDays > (365 * 50);
            string path = isForever ? "permanent_bans" : "temp_ban";
            lock (this)
            {
                DisableTempBansFor(guildId, userId);
                int count = TempBansFile.GetInt("count").Value + 1;
                TempBansFile.Set("count", count);
                TempBansFile.Set($"{path}.{count}.guild", guildId);
                TempBansFile.Set($"{path}.{count}.user", userId);
                TempBansFile.Set($"{path}.{count}.name", name);
                if (!isForever)
                {
                    TempBansFile.Set($"{path}.{count}.end", StringConversionHelper.DateTimeToString(DateTimeOffset.UtcNow.Add(duration), false));
                }
                Save();
            }
            string banReason = isForever ? "Indefinite ban" : $"Temporary ban for {duration.SimpleFormat(false)}";
            if (user != null)
            {
                try
                {
                    IDMChannel channel = user.GetOrCreateDMChannelAsync().Result;
                    string durationMessage = isForever ? "This ban lasts until manually removed by staff." : $"This ban expires **{duration.SimpleFormat(true)}**.";
                    channel.SendMessageAsync(embed: new EmbedBuilder().WithDescription("Discord Mod Bot").WithDescription($"You have been banned from **{guild.Name}**. {durationMessage}").Build()).Wait(new TimeSpan(0, 1, 0));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Excepting sending tempban notice: {ex}");
                }
                user.BanAsync(0, banReason).Wait();
            }
            else
            {
                guild.AddBanAsync(userId, 0, banReason).Wait();
            }
        }
    }
}
