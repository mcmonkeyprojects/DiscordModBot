using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;
using Discord;
using Discord.WebSocket;
using ModBot.Database;
using ModBot.Core;

namespace ModBot.WarningHandlers
{
    /// <summary>
    /// Utilities related to warning and mute management.
    /// </summary>
    public class WarningUtilities
    {
        /// <summary>
        /// Returns whether a Discord user is current muted (checks via configuration value for the mute role name).
        /// </summary>
        public static bool IsMuted(IGuildUser user)
        {
            GuildConfig config = DiscordModBot.GetConfig(user.GuildId);
            if (!config.MuteRole.HasValue)
            {
                return false;
            }
            return (user as SocketGuildUser).Roles.Any((role) => role.Id == config.MuteRole.Value);
        }

        /// <summary>Tracker statistic for legacy users that get patched.</summary>
        [Obsolete]
        public static int LegacyUsersPatched = 0;

        /// <summary>
        /// Gets the <see cref="WarnableUser"/> object for a Discord user (by Discord ID).
        /// </summary>
        public static WarnableUser GetWarnableUser(ulong guildId, ulong id)
        {
            ModBotDatabaseHandler.Guild guildData = DiscordModBot.DatabaseHandler.GetDatabase(guildId);
            lock (guildData)
            {
                WarnableUser user = guildData.Users.FindById(unchecked((long)id));
                if (user == null)
                {
                    ModBotDatabaseHandler.LegacyWarnableUser legacyUser = guildData.Users_Outdated.FindById(id);
                    if (legacyUser != null)
                    {
                        Console.WriteLine($"Legacy user data loaded and updated for {id}");
                        user = legacyUser.Convert(id);
                        user.Ensure();
                        user.Save();
                        guildData.Users_Outdated.Delete(id);
                        LegacyUsersPatched++;
                    }
                    if (user == null)
                    {
                        user = new WarnableUser() { DB_ID_Signed = unchecked((long)id), GuildID = guildId };
                        user.Ensure();
                        Console.WriteLine($"New user data generated for {id}");
                    }
                }
                return user;
            }
        }
    }
}
