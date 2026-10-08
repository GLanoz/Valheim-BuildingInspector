using System;
using System.Collections.Generic;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace BuildingInspector
{
    public partial class BuildingInspector
    {
        private static readonly List<Texture2D> IconTextures = new List<Texture2D>();
        private static Texture2D clothTexture;

        private void RegisterInspectionFlag()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= RegisterInspectionFlag;
            try
            {
                int registeredCount = AddInspectionFlags();
                Logger.LogInfo($"Inspection Flags registered: {registeredCount}.");
                Logger.LogInfo("Building Inspector loaded!");
            }
            catch (Exception exception)
            {
                Logger.LogError($"Failed to register Inspection Flag: {exception}");
            }
        }

        private int AddInspectionFlags()
        {
            GameObject materialSource = PrefabManager.Cache.GetPrefab<GameObject>("wood_pole");
            Renderer sourceRenderer = materialSource ? materialSource.GetComponentInChildren<Renderer>() : null;
            if (!sourceRenderer || !sourceRenderer.sharedMaterial)
            {
                throw new InvalidOperationException("Could not find the vanilla wood_pole material.");
            }

            Piece sourcePiece = materialSource.GetComponent<Piece>();
            if (!sourcePiece)
            {
                throw new InvalidOperationException("Could not find the Piece component on vanilla wood_pole.");
            }

            WearNTear sourceWearNTear = materialSource.GetComponent<WearNTear>();
            if (!sourceWearNTear)
            {
                throw new InvalidOperationException("Could not find the WearNTear component on vanilla wood_pole.");
            }

            Material woodMaterial = sourceRenderer.sharedMaterial;
            int pieceLayer = LayerMask.NameToLayer("piece");
            int registeredCount = 0;

            foreach (FlagVariant variant in flagVariants)
            {
                if (AddInspectionFlag(variant, woodMaterial, sourcePiece.m_placeEffect, sourceWearNTear, pieceLayer))
                {
                    registeredCount++;
                }
            }

            if (registeredCount != flagVariants.Length)
            {
                throw new InvalidOperationException($"Only {registeredCount} of {flagVariants.Length} Inspection Flags were registered.");
            }

            if (testFlagEnabled.Value)
            {
                if (AddInspectionFlag(testFlagVariant, woodMaterial, sourcePiece.m_placeEffect, sourceWearNTear, pieceLayer))
                {
                    Logger.LogInfo("Optional configuration test flag registered.");
                }
            }

            var blockingFlags = new List<Piece>();
            foreach (FlagVariant variant in flagVariants)
            {
                GameObject flagPrefab = PrefabManager.Cache.GetPrefab<GameObject>(variant.PrefabName);
                Piece flagPiece = flagPrefab ? flagPrefab.GetComponent<Piece>() : null;
                if (flagPiece)
                {
                    blockingFlags.Add(flagPiece);
                }
            }

            foreach (Piece flagPiece in blockingFlags)
            {
                flagPiece.m_blockingPieces = blockingFlags;
                flagPiece.m_blockRadius = Mathf.Clamp(flagBlockRadius.Value, 0.05f, 1f);
            }

            return registeredCount;
        }

        private bool AddInspectionFlag(FlagVariant variant, Material woodMaterial,
            EffectList placeEffect, WearNTear sourceWearNTear, int pieceLayer)
        {
            GameObject prefab = PrefabManager.Instance.CreateEmptyPrefab(variant.PrefabName, true);
            if (!prefab)
            {
                throw new InvalidOperationException($"Jotunn could not create the {variant.PrefabName} prefab.");
            }

            prefab.transform.localScale = Vector3.one * 0.5f;
            prefab.AddComponent<Piece>();
            prefab.AddComponent<FlagNoteReceiver>();

            BoxCollider collider = prefab.GetComponent<BoxCollider>();
            if (!collider)
            {
                throw new InvalidOperationException("The InspectionFlag prefab has no root BoxCollider.");
            }

            MeshRenderer rootRenderer = prefab.GetComponent<MeshRenderer>();
            if (rootRenderer)
            {
                rootRenderer.enabled = false;
            }

            if (pieceLayer >= 0)
            {
                prefab.layer = pieceLayer;
            }

            collider.center = new Vector3(0f, 0.68f, 0f);
            collider.size = new Vector3(0.55f, 1.4f, 0.16f);
            collider.isTrigger = false;

            // Root origin is the surface attachment point; the pole starts at local Y = 0.
            GameObject pole = AddPrimitive(prefab, PrimitiveType.Cylinder, "InspectionFlagPole",
                new Vector3(0f, 0.34f, 0f), new Vector3(0.027f, 0.68f, 0.027f),
                woodMaterial, pieceLayer);
            AddClothSprite(pole, woodMaterial, variant, pieceLayer);

            WearNTear wearNTear = prefab.AddComponent<WearNTear>();
            wearNTear.m_new = pole;
            wearNTear.m_worn = pole;
            wearNTear.m_broken = pole;
            wearNTear.m_noRoofWear = true;
            wearNTear.m_noSupportWear = sourceWearNTear.m_noSupportWear;
            wearNTear.m_supports = false;
            wearNTear.m_staticPosition = sourceWearNTear.m_staticPosition;
            wearNTear.m_materialType = sourceWearNTear.m_materialType;
            wearNTear.m_health = sourceWearNTear.m_health;
            wearNTear.m_autoCreateFragments = false;
            wearNTear.m_fragmentRoots = Array.Empty<GameObject>();

            var config = new PieceConfig
            {
                Name = variant.DisplayName,
                Description = variant.Description,
                PieceTable = "Hammer",
                Category = "Inspection"
            };

            config.Icon = CreateFlagIcon(variant.Color, variant.PrefabName);
            if (!config.Icon)
            {
                throw new InvalidOperationException($"Could not create an icon for {variant.PrefabName}.");
            }
            variant.Icon = config.Icon;

            var customPiece = new CustomPiece(prefab, false, config);
            customPiece.Piece.m_resources = Array.Empty<Piece.Requirement>();
            customPiece.Piece.m_placeEffect = placeEffect;
            // Keep native rotation enabled so Valheim does not treat the wheel as camera zoom.
            // AlignFlagToSurface replaces its world-axis rotation with a surface-relative one.
            customPiece.Piece.m_canRotate = true;

            if (!PieceManager.Instance.AddPiece(customPiece))
            {
                throw new InvalidOperationException($"Jotunn's PieceManager rejected {variant.PrefabName}.");
            }

            return true;
        }

        private static GameObject AddPrimitive(GameObject parent, PrimitiveType type, string name,
            Vector3 localPosition, Vector3 localScale, Material material, int pieceLayer)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent.transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;

            if (pieceLayer >= 0)
            {
                part.layer = pieceLayer;
            }

            Collider partCollider = part.GetComponent<Collider>();
            if (partCollider)
            {
                partCollider.enabled = false;
            }

            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer)
            {
                renderer.sharedMaterial = material;
            }

            return part;
        }

        private static void AddClothSprite(GameObject pole, Material woodMaterial, FlagVariant variant, int pieceLayer)
        {
            if (!clothTexture)
            {
                const int textureSize = 16;
                clothTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
                Color32[] pixels = new Color32[textureSize * textureSize];
                for (int y = 0; y < textureSize; y++)
                {
                    for (int x = 0; x < textureSize; x++)
                    {
                        bool warpThread = x % 4 == 0;
                        bool weftThread = y % 4 == 0;
                        byte shade = warpThread && weftThread ? (byte)236 :
                            warpThread || weftThread ? (byte)246 : (byte)255;
                        pixels[y * textureSize + x] = new Color32(shade, shade, shade, 255);
                    }
                }

                clothTexture.SetPixels32(pixels);
                clothTexture.Apply(false, true);
                clothTexture.filterMode = FilterMode.Bilinear;
            }

            Material clothMaterial = new Material(woodMaterial);
            clothMaterial.name = $"{variant.PrefabName}_ClothMaterial";
            clothMaterial.mainTexture = clothTexture;
            Color clothColor = variant.Color;
            clothColor.a = Mathf.Min(clothColor.a, 0.85f);
            clothMaterial.color = clothColor;
            if (clothMaterial.HasProperty("_Mode"))
            {
                clothMaterial.SetFloat("_Mode", 2f);
            }
            if (clothMaterial.HasProperty("_SrcBlend"))
            {
                clothMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            }
            if (clothMaterial.HasProperty("_DstBlend"))
            {
                clothMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }
            if (clothMaterial.HasProperty("_ZWrite"))
            {
                clothMaterial.SetInt("_ZWrite", 0);
            }
            if (clothMaterial.HasProperty("_Cull"))
            {
                clothMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            }
            if (clothMaterial.HasProperty("_Metallic"))
            {
                clothMaterial.SetFloat("_Metallic", 0f);
            }
            if (clothMaterial.HasProperty("_Glossiness"))
            {
                clothMaterial.SetFloat("_Glossiness", 0f);
            }
            if (clothMaterial.HasProperty("_EmissionColor"))
            {
                clothMaterial.SetColor("_EmissionColor", Color.black);
            }
            clothMaterial.DisableKeyword("_EMISSION");
            clothMaterial.DisableKeyword("_ALPHATEST_ON");
            clothMaterial.EnableKeyword("_ALPHABLEND_ON");
            clothMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            clothMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            GameObject cloth = new GameObject("InspectionFlagCloth");
            // Keep the cloth geometry in pole-local space so every pole rotation carries it rigidly.
            cloth.transform.SetParent(pole.transform, false);
            const float clothWidth = 0.39f;
            const float clothHeight = 0.24f;
            MeshFilter poleMesh = pole ? pole.GetComponent<MeshFilter>() : null;
            Vector3 poleScale = pole.transform.localScale;
            float poleScaleX = Mathf.Max(Mathf.Abs(poleScale.x), 0.0001f);
            float poleScaleY = Mathf.Max(Mathf.Abs(poleScale.y), 0.0001f);
            Bounds poleBounds = poleMesh && poleMesh.sharedMesh
                ? poleMesh.sharedMesh.bounds
                : new Bounds(Vector3.zero, Vector3.one);
            float clothWidthInPoleSpace = clothWidth / poleScaleX;
            float clothHeightInPoleSpace = clothHeight / poleScaleY;
            float halfClothHeightInPoleSpace = clothHeightInPoleSpace * 0.5f;
            const float clothThickness = 0.003f;
            float poleWorldScaleZ = Mathf.Max(Mathf.Abs(pole.transform.lossyScale.z), 0.0001f);
            float halfClothDepthInPoleSpace = clothThickness / poleWorldScaleZ * 0.5f;
            cloth.transform.localPosition = new Vector3(
                -poleBounds.extents.x,
                poleBounds.max.y - halfClothHeightInPoleSpace,
                0f);
            if (pieceLayer >= 0)
            {
                cloth.layer = pieceLayer;
            }

            Mesh clothMesh = new Mesh();
            clothMesh.name = "InspectionFlagClothMesh";
            clothMesh.vertices = new[]
            {
                new Vector3(0f, -halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, -halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(0f, halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(0f, -halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, -halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace),
                new Vector3(0f, halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace),
                new Vector3(0f, -halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace),
                new Vector3(0f, -halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(0f, halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(0f, halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, -halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, -halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(0f, halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace),
                new Vector3(0f, halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace),
                new Vector3(0f, -halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace),
                new Vector3(0f, -halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, -halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, -halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace)
            };
            clothMesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f)
            };
            clothMesh.triangles = new[]
            {
                0, 1, 2, 0, 2, 3,
                4, 6, 5, 4, 7, 6,
                8, 9, 10, 8, 10, 11,
                12, 13, 14, 12, 14, 15,
                16, 17, 18, 16, 18, 19,
                20, 21, 22, 20, 22, 23
            };
            clothMesh.RecalculateNormals();
            clothMesh.RecalculateBounds();
            cloth.AddComponent<MeshFilter>().sharedMesh = clothMesh;
            MeshRenderer clothRenderer = cloth.AddComponent<MeshRenderer>();
            clothRenderer.sharedMaterial = clothMaterial;
        }

        private static Sprite CreateFlagIcon(Color flagColor, string iconName)
        {
            const int size = 64;
            Color32[] pixels = new Color32[size * size];
            Color32 wood = new Color32(112, 75, 42, 255);
            Color32 cloth = flagColor;

            DrawRectangle(pixels, size, 29, 13, 34, 54, wood);
            DrawRectangle(pixels, size, 19, 8, 44, 13, wood);
            DrawRectangle(pixels, size, 34, 43, 53, 55, cloth);
            DrawRectangle(pixels, size, 34, 39, 49, 43, cloth);

            Texture2D iconTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            iconTexture.SetPixels32(pixels);
            iconTexture.Apply(false, true);
            IconTextures.Add(iconTexture);
            Sprite iconSprite = Sprite.Create(iconTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            iconSprite.name = $"{iconName}Icon";
            return iconSprite;
        }

        private static void DrawRectangle(Color32[] pixels, int textureSize,
            int xMin, int yMin, int xMax, int yMax, Color32 color)
        {
            for (int y = yMin; y < yMax; y++)
            {
                for (int x = xMin; x < xMax; x++)
                {
                    pixels[y * textureSize + x] = color;
                }
            }
        }
    }
}
