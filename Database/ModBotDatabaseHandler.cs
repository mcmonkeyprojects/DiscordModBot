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

#warning TODO: Eventually, remove legacy user updater logic
        public void UpdateLegacyUser(Guild guild, string fileName, FDSSection section)
        {
            if (section == null)
            {
                return;
            }
            ulong id = ulong.Parse(fileName.AfterLast("/").BeforeLast("."));
            if (guild.Users.FindById(id) != null)
            {
                return;
            }
            WarnableUser user = new WarnableUser()
            {
                UserID = id,
                GuildID = guild.ID,
                IsMuted = section.GetBool("is_muted", false).Value,
                LastKnownUsername = section.GetString("last_known_username")
            };
            user.Ensure();
            FDSSection names_section = section.GetSection("seen_names");
            if (names_section != null)
            {
                foreach (string key in names_section.GetRootKeys())
                {
                    FDSSection nameSection = names_section.GetRootData(key).Internal as FDSSection;
                    DateTimeOffset time = StringConversionHelper.StringToDateTime(nameSection.GetString("first_seen_time")).Value;
                    user.SeenNames.Add(new WarnableUser.OldName() { Name = FDSUtility.UnEscapeKey(key), FirstSeen = time });
                }
            }
            long? currentId = section.GetLong("current_id", null);
            if (currentId != null)
            {
                long currentValue = currentId.Value;
                for (long i = currentValue; i > 0; i--)
                {
                    if (section.HasKey("warnings." + i))
                    {
                        FDSSection warnSection = section.GetSection("warnings." + i);
                        Warning warn = new Warning
                        {
                            GivenTo = user.UserID,
                            TimeGiven = StringConversionHelper.StringToDateTime(warnSection.GetString("time_given", "MISSING")).Value,
                            GivenBy = warnSection.GetUlong("given_by").Value,
                            Reason = warnSection.GetString("reason", "MISSING"),
                            Level = EnumHelper<WarningLevel>.ParseIgnoreCase(warnSection.GetString("level", "MISSING")),
                            Link = warnSection.GetString("link")
                        };
                        user.Warnings.Add(warn);
                    }
                }
            }
            if (section.GetBool("is_nosupport", false).Value)
            {
                user.SpecialRoles.Add("nosupport-other");
            }
            guild.Users.Insert(user.UserID, user);
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
                if (Directory.Exists($"./warnings/{id}"))
                {
                    foreach (string file in Directory.GetFiles($"./warnings/{id}", "*.fds"))
                    {
                        try
                        {
                            FDSSection oldUser = FDSUtility.ReadFile(file);
                            UpdateLegacyUser(newGuild, file, oldUser);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error updating legacy user {file}: {ex}");
                        }
                    }
                    if (!Directory.Exists("./warnings/archive_old_data_backup"))
                    {
                        Directory.CreateDirectory("./warnings/archive_old_data_backup");
                    }
                    Directory.Move($"./warnings/{id}", $"./warnings/archive_old_data_backup/{id}");
                }
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
