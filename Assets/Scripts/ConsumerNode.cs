using UnityEngine;

public class ConsumerNode : BuildingNode
{
    protected override void ProcessEconomy()
    {
        if (inputStock > 0)
        {
            inputStock--;
            if (ScoreManager.Instance != null) ScoreManager.Instance.AddPoint(myTeam);
        }
    }

    protected override void UpdateUI()
    {
        myFloatingText.text = $"{myBuildingName}\nDemand: {inputStock}/{maxCapacity}";
        myFloatingText.color = Color.cyan;
    }
}
