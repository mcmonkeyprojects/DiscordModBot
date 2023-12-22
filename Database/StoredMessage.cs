using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using FreneticUtilities.FreneticExtensions;
using FreneticUtilities.FreneticToolkit;
using LiteDB;
using Newtonsoft.Json;

namespace ModBot.Database
{
    /// <summary>Represent a database-storable copy of a message.</summary>
    public class StoredMessage
    {
        /// <summary>
        /// The message's Discord ID, for database storage.
        /// This is an unchecked cast from ulong to long (last bit becomes a sign) to force the database to store it properly.
        /// </summary>
        [BsonId]
        public long DB_ID_Signed { get; set; }

        /// <summary>Gets the Discord message ID.</summary>
        public ulong MessageID()
        {
            return unchecked((ulong)DB_ID_Signed);
        }

        /// <summary>The raw message content.</summary>
        public string Content { get; set; }

        /// <summary>The ID of the channel this message was in.</summary>
        public ulong ChannelID { get; set; }

        /// <summary>The ID of the message replied to, or 0 if none.</summary>
        public ulong RepliesToID { get; set; }

        /// <summary>The ID of the user that wrote the message.</summary>
        public ulong AuthorID { get; set; }

        /// <summary>A list of attached file links, if any.</summary>
        public List<string> Attachments { get; set; }

        /// <summary>The raw JSON encoded embeds, if any.</summary>
        public List<string> Embeds { get; set; }

        /// <summary>A list of alterations to the message over time, if any.</summary>
        public List<MessageAlteration> MessageEdits { get; set; }

        /// <summary>Represents a single change to a message.</summary>
        public class MessageAlteration
        {
            /// <summary>If true, the message was deleted.</summary>
            public bool IsDeleted { get; set; }

            /// <summary>Standard encoded date/time string of when the message alteration happened.</summary>
            public string Time { get; set; }

            /// <summary>The new content of the message.</summary>
            public string Content { get; set; }
        }

        /// <summary>Gets the most-current content of this message (after any editing).</summary>
        public string CurrentContent()
        {
            if (MessageEdits is null || MessageEdits.IsEmpty())
            {
                return Content;
            }
            return MessageEdits.LastOrDefault(e => !string.IsNullOrWhiteSpace(e.Content))?.Content ?? Content;
        }

        public StoredMessage()
        {
        }

        public StoredMessage(ulong id)
        {
            DB_ID_Signed = unchecked((long)id);
        }

        public StoredMessage(IMessage message) : this(message.Id)
        {
            Content = message.Content;
            ChannelID = message.Channel.Id;
            RepliesToID = message.Reference?.MessageId.GetValueOrDefault(0) ?? 0;
            AuthorID = message.Author.Id;
            Attachments = message.Attachments.Any() ? message.Attachments.Select(a => a.Url).ToList() : null;
            Embeds = message.Embeds.Any() ? message.Embeds.Select(e => JsonConvert.SerializeObject(e)).ToList() : null;
            if (message.EditedTimestamp.HasValue)
            {
                MessageEdits = [new MessageAlteration() { Time = StringConversionHelper.DateTimeToString(message.EditedTimestamp.Value, true), Content = "(Edited Before ModBot Logs)" }];
            }
        }
    }
}
