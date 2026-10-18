using UnityEngine;
using UnityEngine.SceneManagement;

namespace Arena.Gameplay.Flow
{
    /// <summary>First scene entry point: loads the menu.</summary>
    public sealed class BootLoader : MonoBehaviour
    {
        private void Start()
        {
            SceneManager.LoadScene(SceneNames.Menu);
        }
    }
}
