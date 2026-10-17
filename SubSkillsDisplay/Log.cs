using BepInEx.Logging;

namespace SubSkillsDisplay
{
    internal static class Log
    {
        private static ManualLogSource _source;

        internal static void Init(ManualLogSource source) => _source = source;

        internal static void Debug(object data) => _source.LogDebug(data);
        internal static void Info(object data) => _source.LogInfo(data);
        internal static void Message(object data) => _source.LogMessage(data);
        internal static void Warning(object data) => _source.LogWarning(data);
        internal static void Error(object data) => _source.LogError(data);
        internal static void Fatal(object data) => _source.LogFatal(data);
    }
}
