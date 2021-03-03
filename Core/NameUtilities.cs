using System;
using System.Collections.Generic;
using System.Text;
using Discord;
using Discord.WebSocket;
using FreneticUtilities.FreneticToolkit;
using DiscordBotBase.CommandHandlers;
using ModBot.Database;

namespace ModBot.Core
{
    /// <summary>
    /// utilities related to name handling.
    /// </summary>
    public class NameUtilities
    {
        /// <summary>
        /// Gets the full proper username#disc for a user.
        /// </summary>
        public static string Username(IUser user)
        {
            if (user == null || user.Username == null)
            {
                return null;
            }
            return UserCommands.EscapeUserInput(user.Username.Replace("\r", "/r").Replace("\n", "/n")) + "#" + user.Discriminator;
        }

        public static readonly string[] ASCII_NAME_PART1 = new string[] { "HEY", "hey", "YO", "yo", "YOU", "you", "EY", "ey", "" };
        public static readonly string[] ASCII_NAME_PART2 = new string[] { "PLEASE", "please", "PLIS", "plis", "PLZ", "plz", "" };
        public static readonly string[] ASCII_NAME_PART3 = new string[] { "USE", "use", "useA", "USEa", "TAKE", "take", "TAKEa", "takeA", "" };
        public static readonly string[] ASCII_NAME_PART4 = new string[] { "ASCII", "ascii", "ENGLISH", "english", "us-en", "US-EN", "TYPABLE", "typable", "" };
        public static readonly string[] ASCII_NAME_PART5 = new string[] { "NAME", "name", "USERNAME", "username", "NICKNAME", "nickname", "NICK", "nick", "" };

        public static Random random = new Random();

        public static string GenerateAsciiName(string currentName)
        {
            StringBuilder preLetters = new StringBuilder();
            for (int i = 0; i < currentName.Length; i++)
            {
                if (IsAsciiSymbol(currentName[i]))
                {
                    preLetters.Append(currentName[i]);
                }
            }
            string result = "NameRule" + preLetters
                + ASCII_NAME_PART1[random.Next(ASCII_NAME_PART1.Length)]
                + ASCII_NAME_PART2[random.Next(ASCII_NAME_PART2.Length)]
                + ASCII_NAME_PART3[random.Next(ASCII_NAME_PART3.Length)]
                + ASCII_NAME_PART4[random.Next(ASCII_NAME_PART4.Length)]
                + ASCII_NAME_PART5[random.Next(ASCII_NAME_PART5.Length)]
                + random.Next(1000, 9999);
            if (result.Length > 30)
            {
                result = result.Substring(0, 30);
            }
            return result;
        }

        public const int MIN_ASCII_LETTERS_ROW = 3;

        public const string ANTI_LIST_TOP_SYMBOL = "·";

        public static AsciiMatcher AcceptableSymbolMatcher = new AsciiMatcher(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'));

        public static bool IsAsciiSymbol(char c)
        {
            return AcceptableSymbolMatcher.IsMatch(c);
        }

        public static bool IsValidFirstChar(GuildConfig config, string name)
        {
            if (!config.EnforceNameStartRule)
            {
                return true;
            }
            if (name.StartsWith(ANTI_LIST_TOP_SYMBOL))
            {
                return true;
            }
            char c = name[0];
            return (c >= 'a' && c <= 'z')
                || (c >= 'A' && c <= 'Z');
        }

        public static bool IsValidAsciiName(GuildConfig config, string name)
        {
            if (!config.EnforceAsciiNameRule)
            {
                return true;
            }
            if (name.Length < 2)
            {
                return false;
            }
            if (AcceptableSymbolMatcher.IsOnlyMatches(name))
            {
                return true;
            }
            if (name.Length == 2)
            {
                return IsAsciiSymbol(name[0]) && IsAsciiSymbol(name[1]);
            }
            if (name.Length == 3)
            {
                return IsAsciiSymbol(name[0]) && IsAsciiSymbol(name[1]) && IsAsciiSymbol(name[2]);
            }
            for (int i = 0; i < name.Length; i++)
            {
                if (IsAsciiSymbol(name[i]))
                {
                    int x;
                    for (x = i; x < name.Length; x++)
                    {
                        if (!IsAsciiSymbol(name[x]))
                        {
                            break;
                        }
                    }
                    if (x - i >= MIN_ASCII_LETTERS_ROW)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static bool AsciiNameRuleCheck(IUserMessage message, IGuildUser user)
        {
            GuildConfig config = DiscordModBot.GetConfig((message.Channel as IGuildChannel).GuildId);
            if (!config.EnforceAsciiNameRule && !config.EnforceNameStartRule)
            {
                return false;
            }
            string nick = user.Nickname;
            string username = user.Username;
            if (nick != null)
            {
                if (!IsValidAsciiName(config, nick))
                {
                    if (IsValidAsciiName(config, username))
                    {
                        user.ModifyAsync(u => u.Nickname = "").Wait();
                        UserCommands.SendGenericNegativeMessageReply(message, "ASCII Name Rule Enforcement", $"Non-ASCII nickname for <@{user.Id}> removed. Please only use a readable+typable US-English ASCII nickname.");
                        return true;
                    }
                    else
                    {
                        user.ModifyAsync(u => u.Nickname = GenerateAsciiName(user.Username)).Wait();
                        UserCommands.SendGenericNegativeMessageReply(message, "ASCII Name Rule Enforcement", $"Non-ASCII nickname for <@{user.Id}> change to a placeholder. Please change to a readable+typable US-English ASCII nickname or username.");
                        return true;
                    }
                }
                else if (!IsValidFirstChar(config, nick))
                {
                    if (nick.Length > 30)
                    {
                        nick = nick.Substring(0, 30);
                    }
                    user.ModifyAsync(u => u.Nickname = ANTI_LIST_TOP_SYMBOL + nick).Wait();
                    UserCommands.SendGenericNegativeMessageReply(message, "ASCII Name Rule Enforcement", $"Name patch: <@{user.Id}> had a nickname that started with a symbol or number..."
                        + "applied a special first symbol in place. Please start your name with a letter from A to Z. (This is to prevent users from artificially appearing at the top of the userlist).");
                    return true;
                }
            }
            else
            {
                if (!IsValidAsciiName(config, username))
                {
                    user.ModifyAsync(u => u.Nickname = GenerateAsciiName(user.Username)).Wait();
                    UserCommands.SendGenericNegativeMessageReply(message, "ASCII Name Rule Enforcement", $"Non-ASCII username for <@{user.Id}> has been overriden with a placeholder nickname. Please change to a readable+typable US-English ASCII nickname or username.");
                    return true;
                }
                else if (!IsValidFirstChar(config, username))
                {
                    if (username.Length > 30)
                    {
                        username = username.Substring(0, 30);
                    }
                    user.ModifyAsync(u => u.Nickname = ANTI_LIST_TOP_SYMBOL + username).Wait();
                    UserCommands.SendGenericNegativeMessageReply(message, "ASCII Name Rule Enforcement", $"Name patch: <@{user.Id}> had a nickname that started with a symbol or number..."
                        + "applied a special first symbol in place. Please start your name with a letter from A to Z. (This is to prevent users from artificially appearing at the top of the userlist).");
                    return true;
                }
            }
            return false;
        }
    }
}
