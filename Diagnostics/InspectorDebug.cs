using BepInEx.Configuration;
using BepInEx.Logging;

namespace BuildingInspector
{
    internal static class InspectorDebug
    {
        private static ConfigEntry<bool> enabled;
        private static ManualLogSource logger;

        internal static bool Enabled => enabled != null && enabled.Value;

        internal static string Role => !ZNet.instance ? "local" :
            !ZNet.instance.IsServer() ? "client" :
            ZNet.IsOpenServer() || !Player.m_localPlayer ? "server" : "local";

        internal static void Initialize(ConfigEntry<bool> setting, ManualLogSource source)
        {
            logger = source;
            enabled = setting;
        }

        // Callers check Enabled before constructing diagnostic messages.
        internal static void Write(string message)
        {
            if (Enabled)
                logger.LogInfo("[Debug] " + message);
        }
    }
}
