using UnityEditor;
using UnityEngine;

namespace RadTadDev.Logger.Editor
{
    /// <summary>
    /// Editor menu items for Logger.
    /// </summary>
    public static class LoggerMenu
    {
        [MenuItem("Tools/Logger/About")]
        private static void About()
        {
            Debug.Log($"{LoggerInfo.PackageName}");
        }
    }
}
