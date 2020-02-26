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

namespace DiscordModBot
{
    /// <summary>
    /// Represents a single warning given to a user.
    /// </summary>
    public class Warning
    {
        /// <summary>
        /// The time the warning was given.
        /// </summary>
        public DateTimeOffset TimeGiven;

        /// <summary>
        /// Who the warning was given by (Discord ID).
        /// </summary>
        public ulong GivenBy;

        /// <summary>
        /// Who the warning was given to (Discord ID).
        /// </summary>
        public ulong GivenTo;

        /// <summary>
        /// The warning reason (as input by a helper).
        /// </summary>
        public string Reason;

        /// <summary>
        /// The warning level (as input by a helper).
        /// </summary>
        public WarningLevel Level;

        /// <summary>
        /// A generated link to where the warning was given.
        /// </summary>
        public string Link;

        /// <summary>
        /// Creates a warning object from a save file section.
        /// </summary>
        public static Warning FromSection(FDSSection section, ulong userId)
        {
            Warning warn = new Warning
            {
                GivenTo = userId,
                TimeGiven = StringConversionHelper.StringToDateTime(section.GetString("time_given", "MISSING")).Value,
                GivenBy = section.GetUlong("given_by").Value,
                Reason = section.GetString("reason", "MISSING"),
                Level = EnumHelper<WarningLevel>.ParseIgnoreCase(section.GetString("level", "MISSING")),
                Link = section.GetString("link")
            };
            return warn;
        }

        /// <summary>
        /// Saves the warning object into a save file section.
        /// </summary>
        public void SaveToSection(FDSSection section)
        {
            section.Set("time_given", StringConversionHelper.DateTimeToString(TimeGiven, false));
            section.Set("given_by", GivenBy);
            section.Set("reason", Reason);
            section.Set("level", Level.ToString());
            section.Set("link", Link);
        }
    }
}
