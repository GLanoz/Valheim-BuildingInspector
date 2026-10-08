using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace BuildingInspector
{
    public partial class BuildingInspector
    {
        private const int FlagVariantCount = 6;
        private static ConfigEntry<KeyboardShortcut> tiltForwardKey;
        private static ConfigEntry<KeyboardShortcut> tiltBackwardKey;
        private static ConfigEntry<KeyboardShortcut> tiltLeftKey;
        private static ConfigEntry<KeyboardShortcut> tiltRightKey;
        private static ConfigEntry<KeyboardShortcut> resetTiltKey;
        private static ConfigEntry<float> tiltStepDegrees;
        private static ConfigEntry<float> maxTiltDegrees;
        private static ConfigEntry<KeyboardShortcut> editNoteKey;
        private static ConfigEntry<float> flagBlockRadius;
        private static ConfigEntry<bool> testFlagEnabled;
        private static ConfigFile modConfiguration;
        private static readonly List<ConfigEntry<string>> labelNames = new List<ConfigEntry<string>>();
        private static readonly List<ConfigEntry<string>> labelDescriptions = new List<ConfigEntry<string>>();
        private static readonly List<ConfigEntry<string>> labelColors = new List<ConfigEntry<string>>();
        private static FlagVariant[] flagVariants;
        private static FlagVariant testFlagVariant;

        private void BindFlagVariants()
        {
            int[] configSlots = { 1, 2, 3, 4, 5, 6 };
            string[] defaultNames = { "Problem", "Review", "Verified", "Other", "Structural", "Finish" };
            string[] defaultDescriptions =
            {
                "A building issue was found.", "This part needs review.", "This part was inspected.", "Another inspection category.",
                "Structural work needs attention.", "Finishing work needs attention."
            };
            string[] defaultColors = { "#FA2E1F", "#FFE014", "#1FE633", "#1F80FF", "#A64DFF", "#F2F2F2" };
            string[] prefabNames = { "InspectionFlagRed", "InspectionFlagYellow", "InspectionFlagGreen", "InspectionFlagBlue", "InspectionFlagPurple", "InspectionFlagWhite" };
            flagVariants = new FlagVariant[FlagVariantCount];

            for (int index = 0; index < FlagVariantCount; index++)
            {
                int number = configSlots[index];
                labelNames.Add(Config.Bind("Flag Labels", $"Label{number}Name", defaultNames[index], "Display name for this flag category."));
                labelDescriptions.Add(Config.Bind("Flag Labels", $"Label{number}Description", defaultDescriptions[index], "Description shown in the build menu."));
                labelColors.Add(Config.Bind("Flag Labels", $"Label{number}Color", defaultColors[index], "Flag color as a hex value, for example #FF0000."));

                Color color;
                if (!ColorUtility.TryParseHtmlString(labelColors[index].Value, out color))
                {
                    color = Color.white;
                    Logger.LogWarning($"Invalid color for Label{number}Color; using white.");
                }

                string name = string.IsNullOrWhiteSpace(labelNames[index].Value) ? defaultNames[index] : labelNames[index].Value.Trim();
                string description = labelDescriptions[index].Value ?? string.Empty;
                flagVariants[index] = new FlagVariant($"Inspection Flag - {name}", description, prefabNames[index], color);
            }

            string testName = BindTestFlagSetting("Name", "Configuration Test",
                "Display name for the optional test flag.");
            string testDescription = BindTestFlagSetting("Description", "Temporary flag for testing label settings.",
                "Description for the optional test flag.");
            string testColorValue = BindTestFlagSetting("Color", "#FF00FF",
                "Hex color for the optional test flag, for example #FF00FF.");
            Color testColor;
            if (!ColorUtility.TryParseHtmlString(testColorValue, out testColor))
            {
                testColor = Color.magenta;
                Logger.LogWarning($"Invalid Debug TestFlagColor '{testColorValue}'; using #FF00FF.");
            }

            testName = string.IsNullOrWhiteSpace(testName) ? "Configuration Test" : testName.Trim();
            testFlagVariant = new FlagVariant($"[TEST] Inspection Flag - {testName}",
                testDescription ?? string.Empty, "InspectionFlagConfigTest", testColor);
        }

        private string BindTestFlagSetting(string key, string defaultValue, string description)
        {
            // Preserve the old value when the corresponding Debug setting does not exist yet.
            string previousValue = Config.Bind("Test Flag", key, defaultValue, description).Value;
            string value = Config.Bind("Debug", $"TestFlag{key}", previousValue, description).Value;
            Config.Remove(new ConfigDefinition("Test Flag", key));
            return value;
        }

        private void InitializeConfiguration()
        {
            modConfiguration = Config;
            tiltForwardKey = Config.Bind("Flag Rotation", "TiltForwardKey", new KeyboardShortcut(KeyCode.I), "Tilt the flag forward.");
            tiltBackwardKey = Config.Bind("Flag Rotation", "TiltBackwardKey", new KeyboardShortcut(KeyCode.K), "Tilt the flag backward.");
            tiltLeftKey = Config.Bind("Flag Rotation", "TiltLeftKey", new KeyboardShortcut(KeyCode.J), "Tilt the flag to the left.");
            tiltRightKey = Config.Bind("Flag Rotation", "TiltRightKey", new KeyboardShortcut(KeyCode.L), "Tilt the flag to the right.");
            resetTiltKey = Config.Bind("Flag Rotation", "ResetTiltKey", new KeyboardShortcut(KeyCode.Insert), "Reset the flag tilt.");
            tiltStepDegrees = Config.Bind("Flag Rotation", "TiltStepDegrees", 15f, "Tilt change per key press in degrees.");
            maxTiltDegrees = Config.Bind("Flag Rotation", "MaxTiltDegrees", 60f, "Maximum tilt angle in either direction.");
            editNoteKey = Config.Bind("Flag Notes", "EditNoteKey", new KeyboardShortcut(KeyCode.N), "Edit the note on the aimed inspection flag.");
            if (Config.Remove(new ConfigDefinition("Flag Categories", "ChangeCategoryKey")))
            {
                Config.Save();
            }

            flagBlockRadius = Config.Bind("Flag Placement", "BlockRadius", 0.2f,
                new ConfigDescription("Minimum spacing between placed inspection flags in meters.", new AcceptableValueRange<float>(0.05f, 1f)));
            BindFlagVariants();
        }

        private void InitializeDebugConfiguration()
        {
            ConfigEntry<bool> enabled = Config.Bind("Debug", "Enabled", false,
                "Enable inspection editor and RPC diagnostics in LogOutput.log. " +
                "Changes in memory apply immediately; reload the config or restart after editing the file.");
            InspectorDebug.Initialize(enabled, Logger);
            testFlagEnabled = Config.Bind("Debug", "TestFlagEnabled", false,
                "Register an isolated development/test flag for checking label configuration. Restart the game after changing this setting.");

            // Bind the retired key once to remove it from BepInEx's orphaned entries as well.
            Config.Bind("Test Flag", "Enabled", false, "Replaced by Debug.TestFlagEnabled.");
            Config.Remove(new ConfigDefinition("Test Flag", "Enabled"));
            Config.Save();
            MoveDebugSectionToEnd(Config);
        }

        private static void MoveDebugSectionToEnd(ConfigFile configuration)
        {
            try
            {
                string path = configuration.ConfigFilePath;
                string text = File.ReadAllText(path, Encoding.UTF8);
                Match debugSection = Regex.Match(text, @"(?m)^\[Debug\][ \t]*\r?$");
                if (!debugSection.Success)
                    return;

                Match nextSection = Regex.Match(text.Substring(debugSection.Index + debugSection.Length),
                    @"(?m)^\[[^\r\n]+\][ \t]*\r?$");
                if (!nextSection.Success)
                    return;

                int end = debugSection.Index + debugSection.Length + nextSection.Index;
                string debugText = text.Substring(debugSection.Index, end - debugSection.Index);
                string otherSections = text.Remove(debugSection.Index, debugText.Length);
                string newline = text.Contains("\r\n") ? "\r\n" : "\n";
                File.WriteAllText(path, otherSections.TrimEnd('\r', '\n') + newline + newline +
                    debugText.TrimEnd('\r', '\n') + newline, new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                log.LogWarning($"Could not move Debug to the end of the mod configuration: {exception.Message}");
            }
        }

        [HarmonyPatch(typeof(ConfigFile), nameof(ConfigFile.Save))]
        private static class ModConfigurationSavePatch
        {
            [HarmonyPostfix]
            private static void Postfix(ConfigFile __instance)
            {
                // BepInEx sorts sections alphabetically; only reorder this plugin's file.
                if (ReferenceEquals(__instance, modConfiguration))
                    MoveDebugSectionToEnd(__instance);
            }
        }
    }
}
