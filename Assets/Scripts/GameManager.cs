using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [SerializeField] private GameObject loginButton;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void BeenClicked()
    {
        loginButton.SetActive(false);
        FamilySearchAuth.Instance.InitAuth();
    }
}
