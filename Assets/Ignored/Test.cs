using System;
using System.Threading.Tasks;
using RadTadDev.Logger;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ignored
{
    public class Test : MonoBehaviour
    {
        private void Awake()
        {
            Log.Debug(
                "Logger smoke test controls:\n" +
                "  1 - Trace\n" +
                "  2 - Info\n" +
                "  3 - Warn\n" +
                "  4 - Error\n" +
                "  5 - Exception (thrown + caught, so it has a stack trace)\n" +
                "  6 - Debug\n" +
                "  7 - Cycle MinimumLevel, then log one of each level\n" +
                "  8 - Spam 200 Info messages from many threads\n" +
                "  9 - Info with a non-string object (Vector3)");
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return; // no keyboard connected

            // 1-5: one key per public API
            if (kb.digit1Key.wasPressedThisFrame) Log.Trace("Info trace");
            if (kb.digit1Key.wasPressedThisFrame) Log.Info("Info test");
            if (kb.digit2Key.wasPressedThisFrame) Log.Warn("Warn test");
            if (kb.digit3Key.wasPressedThisFrame) Log.Error("Error test");
            if (kb.digit4Key.wasPressedThisFrame) Log.Exception(MakeThrownException());
            if (kb.digit5Key.wasPressedThisFrame) Log.Debug("Debug test");

            // 6: cycle MinimumLevel, then fire one of each level to see what gets filtered
            if (kb.digit6Key.wasPressedThisFrame)
            {
                CycleMinimumLevel();
                Log.Trace("Filter check: Trace");
                Log.Info("Filter check: Info");
                Log.Warn("Filter check: Warn");
                Log.Error("Filter check: Error");
            }

            // 7: hammer Dispatch from many threads at once (thread-safety check)
            if (kb.digit7Key.wasPressedThisFrame) Parallel.For(0, 200, i => Log.Info($"Parallel message {i}"));

            // 8: Info with a non-string object, to check the object -> string path
            if (kb.digit8Key.wasPressedThisFrame) Log.Info(new Vector3(1, 2, 3));
        }

        private static void CycleMinimumLevel()
        {
            var levels = (LogLevel[])Enum.GetValues(typeof(LogLevel));
            var next = (Array.IndexOf(levels, Log.MinimumLevel) + 1) % levels.Length;
            Log.MinimumLevel = levels[next];
            Log.Debug($"MinimumLevel is now {Log.MinimumLevel}");
        }

        // Throw and catch so the exception has a real stack trace to log.
        private static Exception MakeThrownException()
        {
            try
            {
                throw new InvalidOperationException("Test exception");
            }
            catch (Exception e)
            {
                return e;
            }
        }
    }
}