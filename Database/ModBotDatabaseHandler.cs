using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text;
using System.IO;
using LiteDB;
using DiscordBotBase;
using Discord.WebSocket;
using ModBot.WarningHandlers;
using ModBot.Core;
using FreneticUtilities.FreneticDataSyntax;
using FreneticUtilities.FreneticToolkit;
using FreneticUtilities.FreneticExtensions;
using System.Linq;

namespace ModBot.Database
{
    /// <summary>
    /// The primary internal database handler for DiscordModBot.
    /// </summary>
    public class ModBotDatabaseHandler
    {
        /// <summary>
        /// Represents a single guild in the database.
        /// </summary>
        public class Guild
        {
            /// <summary>
            /// The guild ID.
            /// </summary>
            public ulong ID;

            /// <summary>
            /// The database instance.
            /// </summary>
            public LiteDatabase DB;

            /// <summary>
            /// The user collection.
            /// </summary>
            [Obsolete]
            public ILiteCollection<LegacyWarnableUser> Users_Outdated;

            /// <summary>
            /// The user collection.
            /// </summary>
            public ILiteCollection<WarnableUser> Users;

            /// <summary>
            /// The config collection.
            /// </summary>
            public ILiteCollection<GuildConfig> ConfigCollection;

            /// <summary>
            /// The config for the guild.
            /// </summary>
            public GuildConfig Config;

            /// <summary>
            /// Re-saves the guild config.
            /// </summary>
            public void SaveConfig()
            {
                ConfigCollection.Update(0, Config);
            }
        }

        /// <summary>
        /// A map of all tracked guilds by ID to their database.
        /// Generally, use <see cref="GetDatabase(SocketGuild)"/> instead of this.
        /// </summary>
        public ConcurrentDictionary<ulong, Guild> Guilds = new ConcurrentDictionary<ulong, Guild>();

        /// <summary>
        /// Shuts down the database handler cleanly.
        /// </summary>
        public void Shutdown()
        {
            foreach (Guild guild in Guilds.Values)
            {
                try
                {
                    guild.DB.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error shutting down database for guild {guild.ID}: {ex}");
                }
            }
            Guilds.Clear();
        }

        /// <summary>
        /// Gets the database for a guild, initializing it if needed.
        /// </summary>
        public Guild GetDatabase(ulong guildId)
        {
            return Guilds.GetOrAdd(guildId, (id) =>
            {
                Guild newGuild = new Guild
                {
                    ID = id,
                    DB = new LiteDatabase($"./saves/server_{id}.ldb", null)
                };
                newGuild.Users_Outdated = newGuild.DB.GetCollection<LegacyWarnableUser>("users");
                newGuild.Users = newGuild.DB.GetCollection<WarnableUser>("users_vtwo");
                newGuild.ConfigCollection = newGuild.DB.GetCollection<GuildConfig>("guild_configs");
                newGuild.Config = newGuild.ConfigCollection.FindById(0);
                if (newGuild.Config == null)
                {
                    newGuild.Config = DiscordModBot.DefaultGuildConfig.Duplicate();
                    newGuild.Config.Ensure();
                    newGuild.ConfigCollection.Insert(0, newGuild.Config);
                }
                newGuild.Config.Ensure();
                return newGuild;
            });
        }

        public ModBotDatabaseHandler()
        {
            if (!Directory.Exists("./saves"))
            {
                Directory.CreateDirectory("./saves");
            }
        }

        [Obsolete]
        public static void LegacyPatchGuild(Guild guild)
        {
            foreach (LegacyWarnableUser user in guild.Users_Outdated.FindAll().ToList())
            {
                if (user.RawUserID != 0)
                {
                    Console.WriteLine($"Patch legacy raw user {user.RawUserID}");
                    user.Convert(user.RawUserID).Save();
                    guild.Users_Outdated.Delete(user.Legacy_DatabaseID);
                    WarningUtilities.LegacyUsersPatched++;
                }
            }
        }

        [Obsolete]
        public static void LegacyPatchGeneral()
        {
#warning TODO: Extremely Temporary user ID patch helper
            if (Directory.Exists("./warnings/archive_old_data_backup"))
            {
                foreach (string guildFolder in Directory.EnumerateDirectories("./warnings/archive_old_data_backup/"))
                {
                    if (ulong.TryParse(guildFolder.AfterLast('/'), out ulong guildId))
                    {
                        Console.WriteLine($"Checking legacy save data {guildId}");
                        string folder = $"./warnings/archive_old_data_backup/{guildId}/";
                        foreach (string userFile in Directory.EnumerateFiles(folder))
                        {
                            if (userFile.EndsWith(".fds") && ulong.TryParse(userFile.AfterLast('/').Before('.'), out ulong userID))
                            {
                                WarningUtilities.GetWarnableUser(guildId, userID);
                            }
                        }
                    }
                }
                Console.WriteLine($"Re-updated {WarningUtilities.LegacyUsersPatched} users.");
                Directory.Move("./warnings/archive_old_data_backup", "./warnings/archive_old_data_backup_v2");
            }
        }

        [Obsolete]
        public class LegacyWarnableUser
        {
            [Obsolete]
            [BsonId]
            public ulong Legacy_DatabaseID { get; set; }
            public ulong GuildID { get; set; }
            public ulong RawUserID { get; set; }
            public List<OldName> SeenNames { get; set; }
            public class OldName
            {
                public string Name { get; set; }
                public DateTimeOffset FirstSeen { get; set; }
            }
            public bool IsMuted { get; set; }
            public List<string> SpecialRoles { get; set; }
            public List<Warning> Warnings { get; set; }
            public string LastKnownUsername { get; set; }
            public WarnableUser Convert(ulong id)
            {
                return new WarnableUser()
                {
                    DB_ID_Signed = unchecked((long)id),
                    GuildID = GuildID,
                    SeenNames = SeenNames == null ? new List<WarnableUser.OldName>() : SeenNames.Select(o => new WarnableUser.OldName() { FirstSeen = o.FirstSeen, Name = o.Name }).ToList(),
                    IsMuted = IsMuted,
                    SpecialRoles = SpecialRoles,
                    Warnings = Warnings,
                    LastKnownUsername = LastKnownUsername
                };
            }
        }


        /// <summary>
        /// inits the database handler, including all loaded guilds.
        /// </summary>
        public void Init(DiscordBot bot)
        {
            foreach (SocketGuild guild in bot.Client.Guilds)
            {
                Guild guildData = GetDatabase(guild.Id);
                LegacyPatchGuild(guildData);
            }
            LegacyPatchGeneral();
        }
    }
}
