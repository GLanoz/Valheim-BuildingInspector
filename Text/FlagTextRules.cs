using UnityEngine;
using UnityEngine.UI;

namespace BuildingInspector
{
    public partial class BuildingInspector
    {
        private const int MaxNoteLength = 160;
        private const int MaxNoteLines = 5;

        private static string LimitNoteLength(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            int characterCount = 0;
            System.Text.StringBuilder result = new System.Text.StringBuilder(text.Length);

            foreach (char character in text)
            {
                if (character == '\r' || character == '\n')
                {
                    result.Append(character);
                    continue;
                }

                if (characterCount >= MaxNoteLength)
                {
                    continue;
                }

                characterCount++;
                result.Append(character);
            }

            return result.ToString();
        }

        private bool WouldExceedNoteLimits(string text)
        {
            int characterCount = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char character = text[i];

                if (character != '\n' && character != '\r')
                {
                    characterCount++;
                }

                if (characterCount > MaxNoteLength)
                {
                    return true;
                }
            }

            Text textComponent = flagEditorNoteField.textComponent;
            if (!textComponent)
            {
                return false;
            }

            TextGenerator generator = new TextGenerator();

            TextGenerationSettings settings = textComponent.GetGenerationSettings(
                textComponent.rectTransform.rect.size
            );

            settings.generateOutOfBounds = true;
            settings.updateBounds = true;
            settings.horizontalOverflow = HorizontalWrapMode.Wrap;
            settings.verticalOverflow = VerticalWrapMode.Overflow;

            generator.Populate(text, settings);

            int lineCount = generator.lineCount;

            return lineCount > MaxNoteLines;
        }

        private char ValidateNoteInput(string text, int charIndex, char addedChar)
        {
            string newText = text.Insert(charIndex, addedChar.ToString());

            if (WouldExceedNoteLimits(newText))
            {
                return '\0';
            }

            return addedChar;
        }

        private static string DecodeSimpleNoteInput(string text)
        {
            // Match TextInput.OnEnter / OnInput's native escape-sequence conversion.
            return (text ?? string.Empty).Replace("\\n", "\n").Replace("\\t", "\t");
        }

        private static bool WouldExceedSimpleNoteLimits(string text)
        {
            if (LimitNoteLength(text).Length != text.Length || !simpleNoteMeasure)
                return true;

            return simpleNoteMeasure.GetTextInfo(text).lineCount > MaxNoteLines;
        }

        private static char ValidateSimpleNoteInput(string text, int charIndex, char addedChar)
        {
            if (previousSimpleValidation != null)
                addedChar = previousSimpleValidation(text, charIndex, addedChar);
            if (addedChar == '\0')
                return '\0';

            string candidate = text.Insert(charIndex, addedChar.ToString());
            return WouldExceedSimpleNoteLimits(DecodeSimpleNoteInput(candidate)) ? '\0' : addedChar;
        }
    }
}
