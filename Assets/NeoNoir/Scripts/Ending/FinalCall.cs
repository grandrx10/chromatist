using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The last level, after Luna's finale: in the dark, the player calls out
    /// to one of their blades, has a last conversation with it, and the game
    /// ends. With no blade high enough, the only choice is "Nobody", which has
    /// a conversation of its own.
    /// </summary>
    /// <remarks>
    /// Put this on any object in the final scene, and set that scene as
    /// Luna's finale's Next Scene so it loads under the white.
    ///
    /// In order:
    ///   1. <see cref="startDelay"/> seconds after the scene loads (long
    ///      enough for the finale's white to fade), <see cref="question"/> is
    ///      asked with one choice per entry of <see cref="companions"/> whose
    ///      blade is at <see cref="requiredLevel"/> or higher, in list order.
    ///      If none are, the only choice is <see cref="nobodyLabel"/>.
    ///   2. The chosen blade's dialogue plays, or <see cref="nobodyDialogue"/>.
    ///   3. The screen fades to black over <see cref="fadeTime"/>, stays black
    ///      for <see cref="blackHold"/>, and the game quits (play mode stops
    ///      in the editor).
    ///
    /// The player is never seen here. The scene needn't have one; if it does,
    /// every renderer on it is hidden and its controls locked.
    ///
    /// Luna is not a choice: leave her out of the list. The dialogue box shows
    /// at most <see cref="DialogueBox.MaxChoices"/> choices.
    /// </remarks>
    public sealed class FinalCall : MonoBehaviour
    {
        [Serializable]
        private sealed class Companion
        {
            [Tooltip("The blade. Its Display Name is the choice's label.")]
            public BladeDefinition blade;

            [Tooltip("The last conversation, played after this blade is chosen. Rich text allowed.")]
            public List<DialogueLine> dialogue = new List<DialogueLine>();
        }

        [Header("The call")]
        [SerializeField]
        [Tooltip("Seconds after the scene loads before the question is asked.")]
        [Min(0f)]
        private float startDelay = 2f;

        [SerializeField]
        [Tooltip("Asked with the choices under it.")]
        private DialogueLine question = new DialogueLine { text = "Who do you call out to?" };

        [SerializeField]
        [Tooltip("Level a blade must have reached to be a choice.")]
        [Min(1)]
        private int requiredLevel = 3;

        [Header("Blades (not Luna)")]
        [SerializeField]
        private List<Companion> companions = new List<Companion>();

        [Header("Nobody")]
        [SerializeField]
        [Tooltip("The only choice when no blade has reached the required level.")]
        private string nobodyLabel = "Nobody";

        [SerializeField]
        [Tooltip("Played after choosing Nobody. Rich text allowed.")]
        private List<DialogueLine> nobodyDialogue = new List<DialogueLine>();

        [Header("The end")]
        [SerializeField]
        [Tooltip("Seconds the screen takes to fade to black after the last line.")]
        [Min(0f)]
        private float fadeTime = 3f;

        [SerializeField]
        [Tooltip("Seconds the screen stays black before the game quits.")]
        [Min(0f)]
        private float blackHold = 2f;

        private readonly List<Companion> offered = new List<Companion>();

        private void Start()
        {
            HidePlayer();
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            for (float clock = 0f; clock < startDelay; clock += Time.unscaledDeltaTime)
            {
                yield return null;
            }

            offered.Clear();
            List<string> labels = new List<string>();

            foreach (Companion companion in companions)
            {
                if (companion == null || companion.blade == null || BladeUnlocks.LevelOf(companion.blade) < requiredLevel)
                {
                    continue;
                }

                if (labels.Count == DialogueBox.MaxChoices)
                {
                    Debug.LogWarning($"[FinalCall] More than {DialogueBox.MaxChoices} blades qualify; the rest are left off.", this);
                    break;
                }

                offered.Add(companion);
                labels.Add(companion.blade.DisplayName);
            }

            if (labels.Count == 0)
            {
                labels.Add(nobodyLabel);
            }

            DialogueBox.Instance.Ask(question, labels, OnChosen);
        }

        private void OnChosen(int choice)
        {
            List<DialogueLine> lines = choice < offered.Count ? offered[choice].dialogue : nobodyDialogue;
            Play(lines, 0);
        }

        private void Play(List<DialogueLine> lines, int index)
        {
            if (lines == null || index >= lines.Count)
            {
                DialogueBox.Instance.Close();
                StartCoroutine(End());
                return;
            }

            DialogueBox.Instance.Say(lines[index], () => Play(lines, index + 1));
        }

        private IEnumerator End()
        {
            ScreenFade fade = ScreenFade.Create(Color.black, 0f);
            yield return fade.FadeTo(1f, fadeTime);

            for (float clock = 0f; clock < blackHold; clock += Time.unscaledDeltaTime)
            {
                yield return null;
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static void HidePlayer()
        {
            PlayerMotor motor = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);

            if (motor == null)
            {
                return;
            }

            foreach (Renderer renderer in motor.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = false;
            }

            PlayerInputReader input = motor.GetComponent<PlayerInputReader>();

            if (input != null)
            {
                input.Locked = true;
            }
        }
    }
}
