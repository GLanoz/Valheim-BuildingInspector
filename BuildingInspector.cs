using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Managers;

namespace BuildingInspector
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.jotunn.jotunn", BepInDependency.DependencyFlags.HardDependency)]
    public partial class BuildingInspector : BaseUnityPlugin
    {
        public const string PluginGUID = "Lanoz.BuildingInspector";
        public const string PluginName = "Building Inspector";
        public const string PluginVersion = "0.3.1";

        private static ManualLogSource log;

        private void Awake()
        {
            log = Logger;
            Logger.LogInfo("Building Inspector loading...");
            InitializeConfiguration();
            InitializeDebugConfiguration();
            if (InspectorDebug.Enabled)
                InspectorDebug.Write($"Initialization: version={PluginVersion}; registering Harmony patches and prefab event.");
            new Harmony(PluginGUID).PatchAll(typeof(BuildingInspector).Assembly);
            PrefabManager.OnVanillaPrefabsAvailable += RegisterInspectionFlag;
        }
    }
}
