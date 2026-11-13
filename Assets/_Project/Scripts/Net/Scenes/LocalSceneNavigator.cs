using Game.Application.Ports;
using UnityEngine.SceneManagement;

namespace Game.Net.Scenes
{
    /// <summary>Loads non-networked scenes. Only used while no session is running.</summary>
    public sealed class LocalSceneNavigator : ISceneNavigator
    {
        public void LoadMenu()
        {
            if (SceneManager.GetActiveScene().name != SceneNames.Menu)
            {
                SceneManager.LoadScene(SceneNames.Menu, LoadSceneMode.Single);
            }
        }
    }
}
