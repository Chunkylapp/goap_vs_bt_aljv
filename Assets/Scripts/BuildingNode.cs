using UnityEngine;
using TMPro;

public abstract class BuildingNode : MonoBehaviour
{
    public Team myTeam;
    public TextMeshPro myFloatingText;
    public string myBuildingName = "Cladire";

    public int inputStock = 0;
    public int outputStock = 0;
    public int maxCapacity = 10;

    public float timeToProcess = 3f;
    protected float timer = 0f;

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= timeToProcess)
        {
            timer = 0f;
            ProcessEconomy();
        }
        if (myFloatingText != null) UpdateUI();
    }

    protected abstract void ProcessEconomy();
    protected abstract void UpdateUI();

    public bool TryDropOff() { if (inputStock < maxCapacity) { inputStock++; return true; } return false; }
    public bool TryPickUp() { if (outputStock > 0) { outputStock--; return true; } return false; }
    public int GetUtilityScore(bool isDroppingOff) { return isDroppingOff ? maxCapacity - inputStock : outputStock; }
}
