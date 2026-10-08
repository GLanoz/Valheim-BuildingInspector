using System;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BuildingInspector
{
    public partial class BuildingInspector
    {
        private static bool flagEditorOpen;
        private static bool flagEditorInputBlocked;
        private static bool flagEditorInputUnblockPending;
        private static int flagEditorUnblockFrame;
        private static FlagNoteReceiver flagEditorTarget;
        private static GameObject flagEditorCanvas;
        private static GameObject flagEditorPanel;
        private static InputField flagEditorNoteField;
        private static Button[] flagEditorCategoryButtons;
        private static Image[] flagEditorCategoryButtonImages;
        private static string originalNote;
        private static int originalCategory;
        private static int draftCategory;
        private static CursorLockMode editorPreviousCursorLock;
        private static bool editorPreviousCursorVisible;

        private void Update()
        {
            if (flagEditorOpen)
            {
                if (!flagEditorTarget || !flagEditorTarget.CanEditCategory() || Input.GetKeyDown(KeyCode.Escape))
                {
                    CloseFlagEditor();
                }
                return;
            }

            if (flagEditorInputUnblockPending)
            {
                if (Time.frameCount <= flagEditorUnblockFrame || Input.GetMouseButton(0) || Input.GetMouseButton(1))
                {
                    return;
                }

                GUIManager.BlockInput(false);
                flagEditorInputBlocked = false;
                flagEditorInputUnblockPending = false;
            }

            if (!editNoteKey.Value.IsDown() || TextInput.IsVisible() || InventoryGui.IsVisible() ||
                Menu.IsVisible() || Console.IsVisible() || StoreGui.IsVisible() || Minimap.IsOpen())
            {
                return;
            }

            Camera camera = Camera.main;
            if (!camera)
            {
                return;
            }

            Ray ray = camera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f));
            float cameraOffset = Player.m_localPlayer
                ? Vector3.Distance(camera.transform.position, Player.m_localPlayer.transform.position)
                : 0f;
            float maxDistance = 6f + cameraOffset;
            RaycastHit[] hits = Physics.RaycastAll(ray, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (first, second) => first.distance.CompareTo(second.distance));
            foreach (RaycastHit hit in hits)
            {
                FlagNoteReceiver receiver = hit.collider.GetComponentInParent<FlagNoteReceiver>();
                if (receiver)
                {
                    if (!receiver.CanEditCategory())
                    {
                        return;
                    }

                    OpenFlagEditor(receiver);
                    return;
                }

                if (BlocksNoteRay(hit.collider))
                {
                    return;
                }
            }
        }

        private void LateUpdate()
        {
            if (!flagEditorInputBlocked)
            {
                return;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            Player localPlayer = Player.m_localPlayer;
            if (localPlayer)
            {
                localPlayer.SetControls(Vector3.zero, false, false, false, false, false, false,
                    false, false, false, false, false);
                localPlayer.SetMoveDir(Vector3.zero);
                localPlayer.StopMovement();
            }
        }

        private void OpenFlagEditor(FlagNoteReceiver receiver)
        {
            if (!receiver || !receiver.CanEditCategory() || !EnsureFlagEditorUi())
            {
                return;
            }

            flagEditorTarget = receiver;
            originalNote = receiver.GetText();
            originalCategory = receiver.GetEditorCategoryIndex();
            draftCategory = originalCategory;
            flagEditorNoteField.text = originalNote;
            UpdateFlagEditorCategorySelection();
            flagEditorCanvas.SetActive(true);
            flagEditorOpen = true;
            flagEditorInputBlocked = true;
            editorPreviousCursorLock = Cursor.lockState;
            editorPreviousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            GUIManager.BlockInput(true);

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem)
            {
                eventSystem.SetSelectedGameObject(flagEditorNoteField.gameObject);
            }

            flagEditorNoteField.ActivateInputField();
        }

        private bool EnsureFlagEditorUi()
        {
            if (flagEditorCanvas)
            {
                return true;
            }

            GUIManager gui = GUIManager.Instance;
            GameObject customGui = GUIManager.CustomGUIFront;
            if (gui == null || !customGui)
            {
                return false;
            }

            flagEditorCanvas = new GameObject("BuildingInspectorFlagEditor", typeof(RectTransform));
            flagEditorCanvas.transform.SetParent(customGui.transform, false);
            RectTransform rootRect = flagEditorCanvas.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            flagEditorCanvas.SetActive(false);

            GameObject backdropObject = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            backdropObject.transform.SetParent(flagEditorCanvas.transform, false);
            RectTransform backdropRect = backdropObject.GetComponent<RectTransform>();
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = Vector2.zero;
            backdropRect.offsetMax = Vector2.zero;
            Image backdrop = backdropObject.GetComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0.58f);
            backdrop.raycastTarget = true;

            flagEditorPanel = gui.CreateWoodpanel(flagEditorCanvas.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, 740f, 560f, false);
            RectTransform panelRect = flagEditorPanel.GetComponent<RectTransform>();
            panelRect.SetAsLastSibling();

            CreateEditorText(gui, "INSPECTION FLAG", flagEditorPanel.transform, new Vector2(0f, 238f),
                new Vector2(650f, 40f), 30, TextAnchor.MiddleCenter, new Color(1f, 0.55f, 0.15f));
            CreateEditorText(gui, "Note", flagEditorPanel.transform, new Vector2(0, 185f),
                new Vector2(620f, 28f), 22, TextAnchor.MiddleLeft, Color.white);

            GameObject inputObject = gui.CreateInputField(flagEditorPanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 98f),
                InputField.ContentType.Standard, "Add an inspection note...", 18, 630f, 125f);
            flagEditorNoteField = inputObject.GetComponent<InputField>();
            flagEditorNoteField.onValidateInput += ValidateNoteInput;
            flagEditorNoteField.lineType = InputField.LineType.MultiLineNewline;
            flagEditorNoteField.characterLimit = 0;
            flagEditorNoteField.text = string.Empty;
            flagEditorNoteField.textComponent.alignment = TextAnchor.UpperLeft;
            flagEditorNoteField.textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            flagEditorNoteField.textComponent.verticalOverflow = VerticalWrapMode.Overflow;

            CreateEditorText(gui, "Category", flagEditorPanel.transform, new Vector2(0, 10f),
                new Vector2(620f, 28f), 22, TextAnchor.MiddleLeft, Color.white);
            flagEditorCategoryButtons = new Button[FlagVariantCount];
            flagEditorCategoryButtonImages = new Image[FlagVariantCount];
            for (int index = 0; index < FlagVariantCount; index++)
            {
                int row = index / 3;
                int column = index % 3;
                Vector2 position = new Vector2((column - 1) * 222f, -55f - row * 75f);
                GameObject buttonObject = gui.CreateButton(string.Empty, flagEditorPanel.transform,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, 210f, 62f);
                Button button = buttonObject.GetComponent<Button>();
                Image buttonImage = buttonObject.GetComponent<Image>();
                button.transition = Selectable.Transition.None;
                flagEditorCategoryButtons[index] = button;
                flagEditorCategoryButtonImages[index] = buttonImage;
                int categoryIndex = index;
                button.onClick.AddListener(() => SetDraftCategory(categoryIndex));

                GameObject iconObject = new GameObject("FlagIcon", typeof(RectTransform), typeof(Image));
                iconObject.transform.SetParent(buttonObject.transform, false);
                RectTransform iconRect = iconObject.GetComponent<RectTransform>();
                iconRect.anchorMin = new Vector2(0f, 0.5f);
                iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0f, 0.5f);
                iconRect.anchoredPosition = new Vector2(8f, 0f);
                iconRect.sizeDelta = new Vector2(45f, 45f);
                Image icon = iconObject.GetComponent<Image>();
                icon.sprite = flagVariants[index].Icon;
                icon.preserveAspect = true;
                icon.raycastTarget = false;

                string categoryName = string.IsNullOrWhiteSpace(labelNames[index].Value)
                    ? $"Category {index + 1}"
                    : labelNames[index].Value.Trim();
                CreateEditorText(gui, categoryName, buttonObject.transform, new Vector2(25f, 0f),
                    new Vector2(135f, 52f), 16, TextAnchor.MiddleCenter, new Color(1f, 0.55f, 0.15f));
            }

            GameObject saveObject = gui.CreateButton("Save", flagEditorPanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(100f, -230f), 180f, 48f);
            saveObject.GetComponent<Button>().onClick.AddListener(SaveFlagEditor);
            GameObject cancelObject = gui.CreateButton("Cancel", flagEditorPanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-100f, -230f), 180f, 48f);
            cancelObject.GetComponent<Button>().onClick.AddListener(CloseFlagEditor);
            UpdateFlagEditorCategorySelection();
            return true;
        }

        private static void CreateEditorText(GUIManager gui, string text, Transform parent, Vector2 position,
            Vector2 size, int fontSize, TextAnchor alignment, Color color)
        {
            GameObject textObject = gui.CreateText(text, parent, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), position, gui.AveriaSerif, fontSize, Color.white,
                true, Color.black, size.x, size.y, false);
            Text label = textObject.GetComponent<Text>();
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
        }

        private static void SetDraftCategory(int categoryIndex)
        {
            if (categoryIndex < 0 || categoryIndex >= FlagVariantCount)
            {
                return;
            }

            draftCategory = categoryIndex;
            UpdateFlagEditorCategorySelection();
        }

        private static void UpdateFlagEditorCategorySelection()
        {
            if (flagEditorCategoryButtons == null)
            {
                return;
            }

            for (int index = 0; index < flagEditorCategoryButtons.Length; index++)
            {
                bool selected = index == draftCategory;
                Image image = flagEditorCategoryButtonImages[index];
                if (image)
                {
                    image.color = selected ? new Color(0.62f, 0.43f, 0.2f, 1f) : Color.white;
                }

                flagEditorCategoryButtons[index].interactable = true;
            }
        }

        private static void SaveFlagEditor()
        {
            if (!flagEditorTarget || !flagEditorTarget.CanEditCategory())
            {
                CloseFlagEditor();
                return;
            }

            string note = LimitNoteLength((flagEditorNoteField.text ?? string.Empty).Trim());
            if (!string.Equals(note, originalNote, StringComparison.Ordinal))
            {
                flagEditorTarget.SetText(note);
            }

            if (draftCategory != originalCategory)
            {
                flagEditorTarget.RequestCategoryChange(draftCategory);
            }

            CloseFlagEditor();
        }

        private static void CloseFlagEditor()
        {
            if (!flagEditorOpen)
            {
                return;
            }

            flagEditorOpen = false;
            flagEditorTarget = null;
            if (flagEditorCanvas)
            {
                flagEditorCanvas.SetActive(false);
            }

            flagEditorInputUnblockPending = true;
            flagEditorUnblockFrame = Time.frameCount;
            Cursor.lockState = editorPreviousCursorLock;
            Cursor.visible = editorPreviousCursorVisible;
            originalNote = string.Empty;
            originalCategory = -1;
            draftCategory = -1;
        }

        private static bool BlocksNoteRay(Collider collider)
        {
            if (!collider)
            {
                return false;
            }

            if (collider.GetComponentInParent<Piece>() || collider.GetComponentInParent<WearNTear>() ||
                collider.GetComponentInParent<Heightmap>() || collider.GetComponentInParent<Character>() ||
                collider.GetComponentInParent<TreeLog>())
            {
                return true;
            }

            TreeBase tree = collider.GetComponentInParent<TreeBase>();
            return tree && collider.gameObject == tree.gameObject;
        }

        [HarmonyPatch(typeof(Player), "TakeInput")]
        private static class FlagEditorTakeInputPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(Player __instance, ref bool __result)
            {
                if (!flagEditorInputBlocked || __instance != Player.m_localPlayer)
                {
                    return true;
                }

                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(Player), "PlayerAttackInput")]
        private static class FlagEditorAttackInputPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(Player __instance)
            {
                if (!flagEditorInputBlocked || __instance != Player.m_localPlayer)
                {
                    return true;
                }

                return false;
            }
        }
    }
}
