using UnityEngine;

public class ProducerNode : BuildingNode
{
    protected override void ProcessEconomy()
    {
        if (outputStock < maxCapacity) outputStock++;
    }

    protected override void UpdateUI()
    {
        myFloatingText.text = $"{myBuildingName}\nStock: {outputStock}/{maxCapacity}";
        myFloatingText.color = Color.green;
    }
}