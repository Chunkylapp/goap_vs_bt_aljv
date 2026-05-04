using UnityEngine;
using TMPro;

public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance;

    public int myTotalDelvieredProducts = 0;
    public float myElapsedTime = 0f;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Update()
    {
        myElapsedTime += Time.deltaTime;
    }

    public void AddPoint()
    {
        myTotalDelvieredProducts++;
        Debug.Log($"<color=cyan>NEW SCORE:</color> {myTotalDelvieredProducts} products delivered in {Mathf.Round(myElapsedTime)} seconds.");
    }
}