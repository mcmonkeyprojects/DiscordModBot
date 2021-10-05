using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;
using Discord;
using Discord.WebSocket;
using ModBot.Database;
using ModBot.Core;
using FreneticUtilities.FreneticExtensions;
using System.Collections.Concurrent;

namespace ModBot.WarningHandlers
{
    /// <summary>Utilities related to warning and mute management.</summary>
    public class WarningUtilities
    {
        /// <summary>Returns whether a Discord user is current muted (checks via configuration value for the mute role name).</summary>
        public static bool IsMuted(IGuildUser user)
        {
            GuildConfig config = DiscordModBot.GetConfig(user.GuildId);
            if (!config.MuteRole.HasValue)
            {
                return false;
            }
            return (user as SocketGuildUser).Roles.Any((role) => role.Id == config.MuteRole.Value);
        }

        /// <summary>Helper for caching <see cref="WarnableUser"/> instances.</summary>
        public class WarnableCache
        {
            /// <summary>The actual user instance.</summary>
            public WarnableUser User;

            /// <summary>When this instance was last used.</summary>
            public long LastUpdated;

            /// <summary>The cache itself.</summary>
            public static ConcurrentDictionary<(ulong, ulong), WarnableCache> WarnablesCache = new ConcurrentDictionary<(ulong, ulong), WarnableCache>();

            private static readonly List<(ulong, ulong)> ToClear = new List<(ulong, ulong)>();
            private static long LastCleared;

            public static void Clean()
            {
                lock (ToClear)
                {
                    long now = Environment.TickCount64;
                    if (now - LastCleared < 2500)
                    {
                        return;
                    }
                    LastCleared = now;
                    ToClear.Clear();
                    foreach (KeyValuePair<(ulong, ulong), WarnableCache> pair in WarnablesCache)
                    {
                        if ((now - pair.Value.LastUpdated) > 5000)
                        {
                            ToClear.Add(pair.Key);
                        }
                    }
                    foreach ((ulong, ulong) id in ToClear)
                    {
                        WarnablesCache.Remove(id, out _);
                    }
                }
            }
        }

        /// <summary>Gets the <see cref="WarnableUser"/> object for a Discord user (by Discord ID).</summary>
        public static WarnableUser GetWarnableUser(ulong guildId, ulong id)
        {
            ModBotDatabaseHandler.Guild guildData = DiscordModBot.DatabaseHandler.GetDatabase(guildId);
            lock (guildData)
            {
                if (WarnableCache.WarnablesCache.TryGetValue((guildId, id), out WarnableCache cached))
                {
                    cached.LastUpdated = Environment.TickCount64;
                    return cached.User;
                }
                WarnableUser user = guildData.Users.FindById(unchecked((long)id));
                if (user == null)
                {
                    user = new WarnableUser() { DB_ID_Signed = unchecked((long)id), GuildID = guildId };
                    user.Ensure();
                    Console.WriteLine($"New user data generated for {id}");
                }
                WarnableCache cache = new WarnableCache() { User = user, LastUpdated = Environment.TickCount64 };
                WarnableCache.WarnablesCache.TryAdd((guildId, id), cache);
                return user;
            }
        }

        /// <summary>Words that mean "permanent" that a user might try.</summary>
        public static HashSet<string> PermanentWords = new HashSet<string>() { "permanent", "permanently", "indefinite", "indefinitely", "forever" };

        /// <summary>Parses duration text into a valid TimeSpan, or null.</summary>
        public static TimeSpan? ParseDuration(string durationText)
        {
            durationText = durationText.ToLowerFast();
            if (PermanentWords.Contains(durationText))
            {
                return TimeSpan.FromDays(100 * 365);
            }
            if (durationText.EndsWith("h") && double.TryParse(durationText.Before('h'), out double hours))
            {
                return TimeSpan.FromHours(hours);
            }
            else if (durationText.EndsWith("d") && double.TryParse(durationText.Before('d'), out double days))
            {
                return TimeSpan.FromDays(days);
            }
            else if (durationText.EndsWith("w") && double.TryParse(durationText.Before('w'), out double weeks))
            {
                return TimeSpan.FromDays(weeks * 7);
            }
            else if (durationText.EndsWith("m") && double.TryParse(durationText.Before('m'), out double months))
            {
                return TimeSpan.FromDays(months * 31);
            }
            else if (durationText.EndsWith("y") && double.TryParse(durationText.Before('y'), out double years))
            {
                return TimeSpan.FromDays(years * 365);
            }
            return null;
        }
    }
}
