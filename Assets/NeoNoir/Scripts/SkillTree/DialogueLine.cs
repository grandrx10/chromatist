using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>One box of dialogue: who is speaking and what they say.</summary>
    [Serializable]
    public sealed class DialogueLine
    {
        [Tooltip("Name shown above the text. Leave empty for narration.")]
        public string speaker;

        [TextArea(3, 6)]
        [Tooltip("Rich text tags <b>, <i>, <size=40> and <color=#ff3040> are allowed.")]
        public string text;

        [Tooltip("Characters per second for this line only. 0 uses the dialogue box's speed.")]
        [Min(0f)]
        public float charactersPerSecond;
    }
}
