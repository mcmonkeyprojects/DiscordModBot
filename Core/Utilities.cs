using Discord;
using FreneticUtilities.FreneticExtensions;
using ModBot.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ModBot.Core
{
    public static class Utilities
    {
        public static string CleanAttachUrl(string url) => url.StartsWith("https://cdn.discordapp.com/attachments/") ? url.Before('?') : url;

        public static string AttachmentString(this IMessage message)
        {
            return message.Attachments is null ? "" : message.Attachments.Select(a => CleanAttachUrl(a.Url)).JoinString(", ");
        }

        public static string AttachmentString(this StoredMessage message)
        {
            return message.Attachments is null ? "" : message.Attachments.Select(CleanAttachUrl).JoinString(", ");
        }
    }
}
