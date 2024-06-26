﻿using System;
using System.Collections.Generic;
using System.Text;
using Discord;
using Discord.WebSocket;
using FreneticUtilities.FreneticToolkit;
using DiscordBotBase.CommandHandlers;
using ModBot.Database;
using FreneticUtilities.FreneticExtensions;

namespace ModBot.Core
{
    /// <summary>Utilities related to name handling.</summary>
    public class NameUtilities
    {
        /// <summary>Gets the full proper username for a user.</summary>
        public static string Username(IUser user)
        {
            if (user is null || user.Username is null)
            {
                return null;
            }
            return UserCommands.EscapeUserInput(user.Username.Replace("\r", "/r").Replace("\n", "/n"));
        }

        /// <summary>A few common English first names, skimmed from a larger list of most common names, for <see cref="GenerateAsciiName(string)"/>.</summary>
        public static string[] ENGLISH_NAMES = [
            "Bob", "Joe", "Steve", "Fred", "Ron", "Adam", "Lou", "Al", "Andy", "Tony", "Brian", "Calvin", "Chris", "Carl", "Dave", "Dennis", "Doug", "Ed", "George",
            "Harry", "Jack", "Jacob", "John", "Ken", "Max", "Phil", "Robert", "Simon", "Todd", "Tyler", "NewUser", "NewPerson" ];

        /// <summary>Text that can be used for <see cref="GenerateAsciiName(string)"/> to express the name rule.</summary>
        public static readonly string[] ASCII_NAME_PART1 = ["Hey", "Yo", "You"],
            ASCII_NAME_PART2 = ["Please", "Plis", "Plz", "Pls"],
            ASCII_NAME_PART3 = ["Use", "UseA", "Take", "TakeA", "UseAn", "TakeAn"],
            ASCII_NAME_PART4 = ["Ascii", "ASCII", "English", "ENGLISH", "Us-En", "US-EN", "Typable"],
            ASCII_NAME_PART5 = ["Name", "Username", "Nickname", "Nick"];

        /// <summary>Reusable random number generator.</summary>
        public static Random random = new();

        /// <summary>Generates an ASCII placeholder name for users that don't have a usable name.</summary>
        public static string GenerateAsciiName(string currentName)
        {
            StringBuilder preLetters = new();
            for (int i = 0; i < currentName.Length; i++)
            {
                if (IsAsciiSymbol(currentName[i]))
                {
                    preLetters.Append(currentName[i]);
                }
            }
            if (preLetters.Length < 3)
            {
                preLetters.Append(ENGLISH_NAMES[random.Next(ENGLISH_NAMES.Length)]);
            }
            string result = $"NameRule-{preLetters}-"
                + ASCII_NAME_PART1[random.Next(ASCII_NAME_PART1.Length)]
                + ASCII_NAME_PART2[random.Next(ASCII_NAME_PART2.Length)]
                + ASCII_NAME_PART3[random.Next(ASCII_NAME_PART3.Length)]
                + ASCII_NAME_PART4[random.Next(ASCII_NAME_PART4.Length)]
                + ASCII_NAME_PART5[random.Next(ASCII_NAME_PART5.Length)]
                + random.Next(1000, 9999);
            if (result.Length > 30)
            {
                result = result[..30];
            }
            return result;
        }

        public const int MIN_ASCII_LETTERS_ROW = 3;

        /// <summary>Helpful unicode symbol that knocks "! haha my name's on top of the list" idiots to the bottom with minimal name alteration.</summary>
        public const string ANTI_LIST_TOP_SYMBOL = "·";

        /// <summary>Matches letters only, for ASCII-Name-Rule.</summary>
        public static AsciiMatcher LettersOnlyMatcher = new(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'));

        /// <summary>Matches letters and numbers, for ASCII-Name-Rule.</summary>
        public static AsciiMatcher AcceptableSymbolMatcher = new(c => LettersOnlyMatcher.IsMatch(c) || (c >= '0' && c <= '9'));

        /// <summary>Matches any ASCII-range symbol other than A-Z and numbers, for "lenient" first symbol checker.</summary>
        public static AsciiMatcher ForbiddenFirstNameSymbolMatcher = new(c => !AcceptableSymbolMatcher.IsMatch(c) && c < 127);

        /// <summary>Returns true if the character is an acceptable typable ASCII English symbol, A-Z or 0-9.</summary>
        public static bool IsAsciiSymbol(char c)
        {
            return AcceptableSymbolMatcher.IsMatch(c);
        }

        /// <summary>Returns true if the character is an acceptable first character for a name, based on guild config.</summary>
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
            if (config.NameStartRuleLenient)
            {
                return !ForbiddenFirstNameSymbolMatcher.IsMatch(c);
            }
            else
            {
                return LettersOnlyMatcher.IsMatch(c);
            }
        }

        /// <summary>Returns true if the name is acceptable based on guild config.</summary>
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

        /// <summary>Performs a full name-rule check on a user, based on guild config. Returns true if a name rule change was applied, or false if no action was taken.</summary>
        public static bool AsciiNameRuleCheck(IUserMessage message, IGuildUser user)
        {
            GuildConfig config = DiscordModBot.GetConfig((message.Channel as IGuildChannel).GuildId);
            if (!config.EnforceAsciiNameRule && !config.EnforceNameStartRule)
            {
                return false;
            }
            string nick = user.Nickname;
            string globalname = user.GlobalName;
            string username = user.Username;
            if (string.IsNullOrWhiteSpace(globalname))
            {
                globalname = username;
            }
            if (nick is not null)
            {
                if (!IsValidAsciiName(config, nick))
                {
                    if (IsValidAsciiName(config, globalname))
                    {
                        user.ModifyAsync(u => u.Nickname = "").Wait();
                        UserCommands.SendGenericNegativeMessageReply(message, "ASCII Name Rule Enforcement", $"Non-ASCII nickname for <@{user.Id}> removed. Please only use a readable+typable US-English ASCII nickname.");
                        return true;
                    }
                    else if (IsValidAsciiName(config, username))
                    {
                        user.ModifyAsync(u => u.Nickname = username).Wait();
                        UserCommands.SendGenericNegativeMessageReply(message, "ASCII Name Rule Enforcement", $"Non-ASCII nickname for <@{user.Id}> changed to base username. Please only use a readable+typable US-English ASCII nickname.");
                        return true;
                    }
                    else
                    {
                        user.ModifyAsync(u => u.Nickname = GenerateAsciiName(username)).Wait();
                        UserCommands.SendGenericNegativeMessageReply(message, "ASCII Name Rule Enforcement", $"Non-ASCII nickname for <@{user.Id}> change to a placeholder. Please change to a readable+typable US-English ASCII nickname or username.");
                        return true;
                    }
                }
                else if (!IsValidFirstChar(config, nick))
                {
                    if (nick.Length > 30)
                    {
                        nick = nick[..30];
                    }
                    user.ModifyAsync(u => u.Nickname = ANTI_LIST_TOP_SYMBOL + nick).Wait();
                    UserCommands.SendGenericNegativeMessageReply(message, "ASCII Name Rule Enforcement", $"Name patch: <@{user.Id}> had a nickname that started with a symbol or number..."
                        + "applied a special first symbol in place. Please start your name with a letter from A to Z. (This is to prevent users from artificially appearing at the top of the userlist).");
                    return true;
                }
            }
            else
            {
                if (!IsValidAsciiName(config, globalname))
                {
                    if (IsValidAsciiName(config, username))
                    {
                        user.ModifyAsync(u => u.Nickname = username).Wait();
                        UserCommands.SendGenericNegativeMessageReply(message, "ASCII Name Rule Enforcement", $"Non-ASCII nickname for <@{user.Id}> changed to base username. Please only use a readable+typable US-English ASCII nickname.");
                        return true;
                    }
                    else
                    {
                        user.ModifyAsync(u => u.Nickname = GenerateAsciiName(username)).Wait();
                        UserCommands.SendGenericNegativeMessageReply(message, "ASCII Name Rule Enforcement", $"Non-ASCII username for <@{user.Id}> has been overriden with a placeholder nickname. Please change to a readable+typable US-English ASCII nickname or username.");
                        return true;
                    }
                }
                else if (!IsValidFirstChar(config, globalname))
                {
                    if (globalname.Length > 30)
                    {
                        globalname = globalname[..30];
                    }
                    user.ModifyAsync(u => u.Nickname = ANTI_LIST_TOP_SYMBOL + globalname).Wait();
                    UserCommands.SendGenericNegativeMessageReply(message, "ASCII Name Rule Enforcement", $"Name patch: <@{user.Id}> had a nickname that started with a symbol or number..."
                        + "applied a special first symbol in place. Please start your name with a letter from A to Z. (This is to prevent users from artificially appearing at the top of the userlist).");
                    return true;
                }
            }
            return false;
        }

        /// <summary>Gets a sorting-usage estimate number of how similar two username strings are.</summary>
        public static int GetSimilarityEstimate(string name1, string name2)
        {
            name1 = name1.BeforeLast('#').ToLowerFast();
            name2 = name2.BeforeLast('#').ToLowerFast();
            if (name1 == name2)
            {
                return -2;
            }
            if (name1.Length < name2.Length)
            {
                (name1, name2) = (name2, name1);
            }
            if (name1.Contains(name2))
            {
                return -1;
            }
            return StringConversionHelper.GetLevenshteinDistance(name1, name2);
        }
    }
}
