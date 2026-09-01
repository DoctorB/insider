using System.Collections;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Insider.UnityMonoSmoke
{
    public sealed class SmokePlayer : MonoBehaviour
    {
        private const float ExitDelaySeconds = 8.0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            var gameObject = new GameObject("Insider Unity Mono Smoke");
            DontDestroyOnLoad(gameObject);
            gameObject.AddComponent<SmokePlayer>();
            Debug.Log("INSIDER_UNITY_MONO_SMOKE_PLAYER_STARTED");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int CalculateHookValue(int value)
        {
            return value + 5;
        }

        private IEnumerator Start()
        {
            var gameHookedValue = CalculateHookValue(2);
            Debug.Log($"INSIDER_UNITY_MONO_SMOKE_GAME_HOOKED_VALUE={gameHookedValue}");

            yield return new WaitForSecondsRealtime(ExitDelaySeconds);
            Debug.Log("INSIDER_UNITY_MONO_SMOKE_PLAYER_EXITING");
            Application.Quit(0);
        }
    }
}
