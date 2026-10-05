using UnityEngine;
using UnityEngine.InputSystem;

namespace PokeMemories.Preview
{
    /// <summary>
    /// Preview-only: cycles the skater animations so the imported frames can be checked
    /// in Play mode. Tap / click / Space for the next clip, or press 1-4.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class SkaterAnimationPreview : MonoBehaviour
    {
        static readonly string[] States = { "idle", "push", "jump", "grind" };

        Animator animator;
        int current;

        void Awake() => animator = GetComponent<Animator>();

        void Update()
        {
            var keyboard = Keyboard.current;
            var pointer = Pointer.current;

            if ((pointer != null && pointer.press.wasPressedThisFrame) ||
                (keyboard != null && keyboard.spaceKey.wasPressedThisFrame))
                Play((current + 1) % States.Length);

            if (keyboard == null) return;
            if (keyboard.digit1Key.wasPressedThisFrame) Play(0);
            if (keyboard.digit2Key.wasPressedThisFrame) Play(1);
            if (keyboard.digit3Key.wasPressedThisFrame) Play(2);
            if (keyboard.digit4Key.wasPressedThisFrame) Play(3);
        }

        void Play(int index)
        {
            current = index;
            animator.Play(States[index], 0, 0f);
        }

        void OnGUI()
        {
            GUI.Label(new Rect(16, 16, 600, 30),
                $"Animation: {States[current]}   (tap / Space = next, 1-4 = pick)");
        }
    }
}
