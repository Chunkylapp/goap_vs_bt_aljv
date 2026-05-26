using UnityEngine;
using TMPro;

public enum Team { BT, GOAP }

public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance;

    public int scoreBT = 0;
    public int scoreGOAP = 0;
    public float myElapsedTime = 0f;

    public TextMeshProUGUI scoreText;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Update()
    {
        myElapsedTime += Time.deltaTime;
        if (scoreText != null)
        {
            scoreText.text = $"Time: {Mathf.Round(myElapsedTime)}s\n<color=#00FFFF>BT: {scoreBT}</color> vs <color=#FFD700>GOAP: {scoreGOAP}</color>";
        }
    }

    public void AddPoint(Team team)
    {
        if (team == Team.BT) scoreBT++;
        else scoreGOAP++;

        Debug.Log($"<color=cyan>NEW SCORE:</color> BT: {scoreBT} | GOAP: {scoreGOAP} at {Mathf.Round(myElapsedTime)}s");
    }

    public void ResetScores()
    {
        scoreBT = 0;
        scoreGOAP = 0;
        myElapsedTime = 0f;
    }
}
