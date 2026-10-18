using UnityEngine.SceneManagement;

namespace Arena.Gameplay.Flow
{
    /// <summary>
    /// Scene flow Menu → Match → results → (again | Menu). Keeps the launch parameters across the scene load; the Match
    /// scene opened directly (editor Play, tests) has none and uses the config's line-up and seed.
    /// </summary>
    public static class SceneFlow
    {
        /// <summary>Parameters of the current / last match, or null.</summary>
        public static MatchLaunchParams Current { get; private set; }

        public static void PlayMatch(MatchLaunchParams launch)
        {
            Current = launch;
            SceneManager.LoadScene(SceneNames.Match);
        }

        /// <summary>"Play again": the same parameters (fighter, bots, seed). An online match cannot be replayed by reloading the
        /// scene — it goes back to the menu (S3.10b; S3.11 queues again instead).</summary>
        public static void Restart()
        {
            if (Current != null && Current.Online)
            {
                ToMenu();
                return;
            }
            SceneManager.LoadScene(SceneNames.Match);
        }

        public static void ToMenu() => SceneManager.LoadScene(SceneNames.Menu);

        /// <summary>Forget the parameters (tests).</summary>
        public static void ClearLaunchParams() => Current = null;
    }
}
