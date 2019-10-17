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

namespace ModBot
{
    /// <summary>
    /// General program entry and handler.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// The current bot object (the instance will change if the bot is restarted).
        /// </summary>
        public static DiscordModBot CurrentBot = null;

        /// <summary>
        /// Software entry point - starts the bot.
        /// </summary>
        static void Main(string[] args)
        {
            CurrentBot = new DiscordModBot();
            LaunchBotThread(args);
        }

        /// <summary>
        /// Launches a bot thread.
        /// </summary>
        public static void LaunchBotThread(string[] args)
        {
            Thread thr = new Thread(new ParameterizedThreadStart(BotThread))
            {
                Name = "discordmodbot" + new Random().Next(5000)
            };
            thr.Start(args);
        }

        /// <summary>
        /// The bot thread rootmost method, takes a string array object as input.
        /// </summary>
        public static void BotThread(Object obj)
        {
            try
            {
                CurrentBot.InitAndRun(obj as string[]);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Discord crash: " + ex.ToString());
                Thread.Sleep(10 * 1000);
                Thread.CurrentThread.Name = "discordbotthread_dead" + new Random().Next(5000);
                LaunchBotThread(new string[0]);
            }
        }
    }
}
