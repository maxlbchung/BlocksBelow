using UnityEngine;
using UnityEngine.SceneManagement;

public class gameController : MonoBehaviour
{
    [SerializeField] Scene[] levels;
    int currentLevel = 0;
    
    public void LoadLevel(int levelIndex)
    {
        if (levelIndex < 0 || levelIndex >= levels.Length)
        {
            Debug.LogError($"Invalid level index: {levelIndex}");
            return;
        }
        currentLevel = levelIndex;
        SceneManager.LoadScene(levels[levelIndex].name);
    }

    public void LoadNextLevel()
    {

        currentLevel++;
        if (currentLevel < levels.Length)
        {
            SceneManager.LoadScene(levels[currentLevel].name);
        }
        else
        {
            Debug.Log("No more levels to load.");
        }
    }
}
