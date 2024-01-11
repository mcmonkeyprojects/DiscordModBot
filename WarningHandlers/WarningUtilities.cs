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
using FreneticUtilities.FreneticToolkit;

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
            public static ConcurrentDictionary<(ulong, ulong), WarnableCache> WarnablesCache = new();

            private static readonly List<(ulong, ulong)> ToClear = [];
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
                WarnableCache cache = new() { User = user, LastUpdated = Environment.TickCount64 };
                WarnableCache.WarnablesCache.TryAdd((guildId, id), cache);
                return user;
            }
        }

        /// <summary>Words that mean "permanent" that a user might try.</summary>
        public static HashSet<string> PermanentWords = new() { "permanent", "permanently", "indefinite", "indefinitely", "forever" };

        /// <summary>Helper to separate digits from letters, for <see cref="ParseDuration(string, bool)"/>.</summary>
        public static AsciiMatcher DigitMatcher = new(AsciiMatcher.Digits + ".");

        /// <summary>Helper to identify timespan suffix keywords, for <see cref="ParseDuration(string, bool)"/>.</summary>
        public static Dictionary<string, string> TimespanSuffixRemapper = new();
        static WarningUtilities()
        {
            static void Add(string realKey, params string[] keys)
            {
                foreach (string key in keys)
                {
                    TimespanSuffixRemapper.Add(key, realKey);
                }
                TimespanSuffixRemapper.Add(realKey, realKey);
            }
            Add("seconds", "s", "sec", "secs", "second");
            Add("minutes", "min", "mins", "minute");
            Add("hours", "h", "hr", "hrs", "hour");
            Add("days", "d", "day");
            Add("weeks", "w", "week");
            Add("months", "mo", "mon", "mons", "month");
            Add("years", "y", "yr", "yrs", "year");
        }

        /// <summary>Parses duration text into a valid TimeSpan, or null.</summary>
        public static TimeSpan? ParseDuration(string durationText, bool mForMinutes = false)
        {
            durationText = durationText.ToLowerFast();
            if (PermanentWords.Contains(durationText))
            {
                return TimeSpan.FromDays(100 * 365);
            }
            int endOfNumber = DigitMatcher.FirstNonMatchingIndex(durationText);
            if (endOfNumber >= durationText.Length)
            {
                return null;
            }
            string numberText = durationText[..endOfNumber];
            string suffixText = durationText[endOfNumber..].ToLowerFast();
            if (suffixText == "m")
            {
                suffixText = mForMinutes ? "minutes" : "months";
            }
            if (TimespanSuffixRemapper.TryGetValue(suffixText, out string formatText) && double.TryParse(numberText, out double numVal))
            {
                switch (formatText)
                {
                    case "seconds": return TimeSpan.FromSeconds(numVal);
                    case "minutes": return TimeSpan.FromMinutes(numVal);
                    case "hours": return TimeSpan.FromHours(numVal);
                    case "days": return TimeSpan.FromDays(numVal);
                    case "weeks": return TimeSpan.FromDays(numVal * 7);
                    case "months": return TimeSpan.FromDays(numVal * 31);
                    case "years": return TimeSpan.FromDays(numVal * 365);
                }
            }
            return null;
        }
    }
}
