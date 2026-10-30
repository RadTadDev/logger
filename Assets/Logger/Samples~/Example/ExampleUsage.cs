using UnityEngine;

namespace RadTadDev.Logger.Samples
{
    /// <summary>
    /// Logs the package name when the scene starts.
    /// </summary>
    public class ExampleUsage : MonoBehaviour
    {
        private void Start()
        {
            Debug.Log($"Using {RadTadDev.Logger.LoggerInfo.PackageName}");
        }
    }
}
