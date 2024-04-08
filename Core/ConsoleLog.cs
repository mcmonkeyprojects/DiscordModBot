using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ModBot.Core;

public static class ConsoleLog
{
    public static void LogInternal(string message)
    {
        Console.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}");
    }

    public static void Info(string message)
    {
        LogInternal($"[INFO] {message}");
    }

    public static void Warning(string message)
    {
        LogInternal($"[WARNING] {message}");
    }

    public static void Error(string message)
    {
        LogInternal($"[ERROR] {message}");
    }

    public static void Debug(string message)
    {
        LogInternal($"[DEBUG] {message}");
    }
}
