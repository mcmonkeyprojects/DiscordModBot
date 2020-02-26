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
    /// Possibly warning levels (how serious a warning is).
    /// </summary>
    public enum WarningLevel : int
    {
        /// <summary>
        /// Not a real warning, just a note.
        /// </summary>
        NOTE = -1,
        /// <summary>
        /// Automatically given warning.
        /// </summary>
        AUTO = 0,
        /// <summary>
        /// Not very significant warning.
        /// </summary>
        MINOR = 1,
        /// <summary>
        /// Standard warning. Counted towards automatic muting.
        /// </summary>
        NORMAL = 2,
        /// <summary>
        /// More significant than normal warning. Counted extra towardsd automatic muting.
        /// </summary>
        SERIOUS = 3,
        /// <summary>
        /// Extremely significant warning. Induces an immediate automatic muting.
        /// </summary>
        INSTANT_MUTE = 4
    }
}
