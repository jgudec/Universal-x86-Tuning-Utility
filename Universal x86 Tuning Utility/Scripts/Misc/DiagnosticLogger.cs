using Serilog;
using Serilog.Core;
using Serilog.Events;
using System;
using System.Runtime.CompilerServices;

namespace Universal_x86_Tuning_Utility.Scripts.Misc
{
    internal static class DiagnosticLogger
    {
        public static readonly LoggingLevelSwitch LevelSwitch = new(LogEventLevel.Warning);

        public static void ApplySettingsLevel()
        {
            LevelSwitch.MinimumLevel = Properties.Settings.Default.DiagnosticLogLevel switch
            {
                0 => LogEventLevel.Fatal + 1,
                1 => LogEventLevel.Warning,
                2 => LogEventLevel.Information,
                3 => LogEventLevel.Debug,
                _ => LogEventLevel.Warning
            };
        }

        public static void LogError(
            Exception ex,
            string context,
            [CallerMemberName] string caller = "",
            [CallerFilePath] string file = "")
        {
            Log.Warning(ex, "[{Source}.{Caller}] {Context}",
                System.IO.Path.GetFileNameWithoutExtension(file), caller, context);
        }

        public static void LogInfo(
            string message,
            [CallerMemberName] string caller = "",
            [CallerFilePath] string file = "")
        {
            Log.Information("[{Source}.{Caller}] {Message}",
                System.IO.Path.GetFileNameWithoutExtension(file), caller, message);
        }

        public static void LogDebug(
            string message,
            [CallerMemberName] string caller = "",
            [CallerFilePath] string file = "")
        {
            Log.Debug("[{Source}.{Caller}] {Message}",
                System.IO.Path.GetFileNameWithoutExtension(file), caller, message);
        }
    }
}
