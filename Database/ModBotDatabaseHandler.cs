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
                guild.DB.Dispose();
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
                newGuild.Users = newGuild.DB.GetCollection<WarnableUser>("users");
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

        /// <summary>
        /// inits the database handler, including all loaded guilds.
        /// </summary>
        public void Init(DiscordBot bot)
        {
            foreach (SocketGuild guild in bot.Client.Guilds)
            {
                GetDatabase(guild.Id);
            }
        }
    }
}
