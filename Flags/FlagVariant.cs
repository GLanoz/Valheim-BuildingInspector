using UnityEngine;

namespace BuildingInspector
{
    public partial class BuildingInspector
    {
        private sealed class FlagVariant
        {
            public string DisplayName { get; }
            public string Description { get; }
            public string PrefabName { get; }
            public Color Color { get; }
            public Sprite Icon { get; set; }

            public FlagVariant(string displayName, string description, string prefabName, Color color)
            {
                DisplayName = displayName;
                Description = description;
                PrefabName = prefabName;
                Color = color;
            }
        }
    }
}
