using UnityEngine;

public class FactoryNode : BuildingNode
{
    protected override void ProcessEconomy()
    {
        if (inputStock > 0 && outputStock < maxCapacity)
        {
            inputStock--;
            outputStock++;
        }
    }

    protected override void UpdateUI()
    {
        myFloatingText.text = $"{myBuildingName}\nMaterial: {inputStock}/{maxCapacity}\nProduce: {outputStock}/{maxCapacity}";
        myFloatingText.color = Color.yellow;
    }
}