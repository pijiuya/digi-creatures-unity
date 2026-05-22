using UnityEngine;

namespace DigiCreatures
{
    [DisallowMultipleComponent]
    public class CreatureSpeechBubble : MonoBehaviour
    {
        public void Show(string message, float seconds = 0f)
        {
            CreatureSubtitleHud.Show(name, message, string.Empty, seconds);
        }

        public void SetStatus(string status, Color color, bool visible = true)
        {
            CreatureSubtitleHud.SetStatus(status, string.Empty);
        }

        public void HideImmediate()
        {
        }
    }
}
