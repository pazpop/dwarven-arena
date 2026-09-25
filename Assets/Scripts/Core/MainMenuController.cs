// Menu (titre/pause/crédits) : un seul panneau réutilisé pour le lancement, la
// pause (Échap) et le game over — voir GAMEPLAY.md#menu pour le détail.
using UnityEngine;

public class MainMenuController : MonoBehaviour
{
    public static MainMenuController Instance { get; private set; }

    [Header("Panneaux")]
    public GameObject menuPanel;
    public GameObject creditsPanel;

    private void Awake()
    {
        Instance = this;

        if (GameManager.IsTraining)
        {
            // Entraînement ML-Agents (mlagents-learn) : pas de menu, jeu actif immédiatement
            gameObject.SetActive(false);
            return;
        }

        Time.timeScale = 0f; // Vagues/physique en pause tant que le menu est affiché
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (menuPanel.activeSelf) ResumeGame();
            else OpenMenu();
        }
    }

    // Échap depuis une pause en cours de partie : reprend sans rien réinitialiser
    private void ResumeGame()
    {
        menuPanel.SetActive(false);
        Time.timeScale = 1f;
    }

    // Appelé par Échap et par GameManager.GameOver() (mort du joueur)
    public void OpenMenu()
    {
        if (GameManager.IsTraining) return; // jamais de pause pendant l'entraînement ML-Agents
        creditsPanel.SetActive(false);
        menuPanel.SetActive(true);
        Time.timeScale = 0f;
    }

    // Appelé par le bouton "Start" (premier lancement ET relance après une mort)
    public void OnStart()
    {
        menuPanel.SetActive(false);
        Time.timeScale = 1f;
        GameManager.Instance.ResetGame();
    }

    // Appelé par le bouton "Credits"
    public void OnCredits()
    {
        menuPanel.SetActive(false);
        creditsPanel.SetActive(true);
    }

    // Appelé par le bouton "Retour" du panneau crédits
    public void OnCreditsBack()
    {
        creditsPanel.SetActive(false);
        menuPanel.SetActive(true);
    }

    // Appelé par le bouton "Quit"
    public void OnQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
