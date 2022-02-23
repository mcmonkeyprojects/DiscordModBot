using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace ModBot.WarningHandlers
{
    /// <summary>Possibly warning levels (how serious a warning is).</summary>
    public enum WarningLevel : int
    {
        /// <summary>Not a real warning, just a note.</summary>
        NOTE = -1,
        /// <summary>Automatically given warning.</summary>
        AUTO = 0,
        /// <summary>Not very significant warning.</summary>
        MINOR = 1,
        /// <summary>Standard warning. Counted towards automatic muting.</summary>
        NORMAL = 2,
        /// <summary>More significant than normal warning. Counted extra towardsd automatic muting.</summary>
        SERIOUS = 3,
        /// <summary>Extremely significant warning. Induces an immediate automatic muting.</summary>
        INSTANT_MUTE = 4,
        /// <summary>Generated warning level when a user is banned.</summary>
        BAN = 5
    }
}
