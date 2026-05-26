using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

public class SmartTruckAI : MonoBehaviour
{
    public Team myTeam;
    public float myFuel;
    public string myCargo = "None";
    public TextMeshPro myFloatingText;
    public bool isDead = false;

    private NavMeshAgent myAgent;
    private string myState = "";
    private Transform myCurrentTarget;

    private GameObject[] myProducers, myFactories, myConsumers, myGasStations;

    void Start()
    {
        myAgent = GetComponent<NavMeshAgent>();
        myFuel = Random.Range(70f, 100f);
        myAgent.speed = Random.Range(4f, 5.5f);

        myProducers = GetLocalObjectsWithTag("Producer");
        myFactories = GetLocalObjectsWithTag("Factory");
        myConsumers = GetLocalObjectsWithTag("Consumer");
        myGasStations = GetLocalObjectsWithTag("GasStation");
    }

    GameObject[] GetLocalObjectsWithTag(string tag)
    {
        List<GameObject> foundList = new List<GameObject>();
        GameObject[] allWithTag = GameObject.FindGameObjectsWithTag(tag);

        foreach (GameObject obj in allWithTag)
        {
            BuildingNode node = obj.GetComponent<BuildingNode>();
            if (node != null)
            {
                if (node.myTeam == myTeam) foundList.Add(obj);
                continue;
            }

            TeamMember tm = obj.GetComponent<TeamMember>();
            if (tm != null)
            {
                if (tm.myTeam == myTeam) foundList.Add(obj);
                continue;
            }

            Transform parent = obj.transform.parent;
            if (parent != null)
            {
                BuildingNode pNode = parent.GetComponent<BuildingNode>();
                TeamMember pTm = parent.GetComponent<TeamMember>();
                if ((pNode != null && pNode.myTeam == myTeam) || (pTm != null && pTm.myTeam == myTeam))
                {
                    foundList.Add(obj);
                }
            }
        }
        return foundList.ToArray();
    }

    bool IsTargetValid(Transform target, bool isDroppingOff)
    {
        if (target == null) return false;
        BuildingNode node = target.GetComponent<BuildingNode>();
        return node != null && node.GetUtilityScore(isDroppingOff) > 0;
    }

    void Update()
    {
        if (myFuel <= 0f)
        {
            if (!isDead)
            {
                isDead = true;
                if (CompetitionManager.Instance != null) CompetitionManager.Instance.btDeaths++;
            }
            myFuel = 0f;
            myAgent.isStopped = true;
            SetState(transform, "Out of gas");
            GetComponent<Renderer>().material.color = Color.red;
        }
        else
        {
            float fuelConsumed = Time.deltaTime * 3f;
            myFuel -= fuelConsumed;
            if (CompetitionManager.Instance != null) CompetitionManager.Instance.btFuelBurnt += fuelConsumed;

            if (myCurrentTarget == null)
            {
                if (CompetitionManager.Instance != null) CompetitionManager.Instance.btIdleTime += Time.deltaTime;
            }

            if (myFuel < 30f)
            {
                if (myCurrentTarget == null || !myCurrentTarget.CompareTag("GasStation"))
                {
                    myCurrentTarget = GetBestTarget(myGasStations, false, false);
                }

                if (myCurrentTarget != null)
                {
                    SetState(myCurrentTarget, "Rushing to the gas station");
                    if (Vector3.Distance(transform.position, myCurrentTarget.position) < 3.5f)
                    {
                        myFuel = 100f;
                        myCurrentTarget = null;
                    }
                }
            }
            else if (myCargo == "Product")
            {
                if (myCurrentTarget != null && myCurrentTarget.CompareTag("Consumer") && !IsTargetValid(myCurrentTarget, true))
                {
                    if (CompetitionManager.Instance != null) CompetitionManager.Instance.btAborts++;
                }

                if (myCurrentTarget == null || !myCurrentTarget.CompareTag("Consumer") || !IsTargetValid(myCurrentTarget, true))
                {
                    myCurrentTarget = GetBestTarget(myConsumers, true, true);
                }

                if (myCurrentTarget != null)
                {
                    SetState(myCurrentTarget, "Taking to consumer");
                    TryDropOff(myCurrentTarget, "None");
                }
            }
            else if (myCargo == "Raw")
            {
                if (myCurrentTarget != null && myCurrentTarget.CompareTag("Factory") && !IsTargetValid(myCurrentTarget, true))
                {
                    if (CompetitionManager.Instance != null) CompetitionManager.Instance.btAborts++;
                }

                if (myCurrentTarget == null || !myCurrentTarget.CompareTag("Factory") || !IsTargetValid(myCurrentTarget, true))
                {
                    myCurrentTarget = GetBestTarget(myFactories, true, true);
                }

                if (myCurrentTarget != null)
                {
                    SetState(myCurrentTarget, "Taking to factory");
                    TryDropOff(myCurrentTarget, "None");
                }
            }
            else
            {
                bool isValidFactory = myCurrentTarget != null && myCurrentTarget.CompareTag("Factory") && IsTargetValid(myCurrentTarget, false);
                bool isValidProducer = myCurrentTarget != null && myCurrentTarget.CompareTag("Producer") && IsTargetValid(myCurrentTarget, false);

                if (myCurrentTarget != null && (myCurrentTarget.CompareTag("Factory") || myCurrentTarget.CompareTag("Producer")) && !isValidFactory && !isValidProducer)
                {
                    if (CompetitionManager.Instance != null) CompetitionManager.Instance.btAborts++;
                }

                if (!isValidFactory && !isValidProducer)
                {
                    Transform bestFactory = GetBestTarget(myFactories, true, false);
                    Transform bestProducer = GetBestTarget(myProducers, true, false);

                    int factoryScore = GetNodeScore(bestFactory, false) * 2;
                    int producerScore = GetNodeScore(bestProducer, false);

                    if (factoryScore >= producerScore && factoryScore > 0)
                    {
                        myCurrentTarget = bestFactory;
                    }
                    else
                    {
                        myCurrentTarget = bestProducer;
                    }
                }

                if (myCurrentTarget != null)
                {
                    if (myCurrentTarget.CompareTag("Factory"))
                    {
                        SetState(myCurrentTarget, "Picking up product");
                        TryPickUp(myCurrentTarget, "Product");
                    }
                    else
                    {
                        SetState(myCurrentTarget, "Picking raw");
                        TryPickUp(myCurrentTarget, "Raw");
                    }
                }
            }
        }

        if (myFloatingText != null)
        {
            myFloatingText.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            string targetName = (myCurrentTarget != null) ? myCurrentTarget.name : "-";
            string cargoUI = myCargo == "Raw" ? "Raw" : (myCargo == "Product" ? "Product" : "Empty");
            myFloatingText.text = $"<color=#FFD700>Fuel: {Mathf.Round(myFuel)}%</color> | <color=#00FFFF>{cargoUI}</color>\n<color=#00FF00>{targetName}</color>";
        }
    }

    int GetNodeScore(Transform target, bool isDroppingOff)
    {
        if (target == null)
            return -1;
        BuildingNode node = target.GetComponent<BuildingNode>();

        return node != null ? node.GetUtilityScore(isDroppingOff) : -1;
    }

    Transform GetBestTarget(GameObject[] targets, bool isBuilding, bool isDroppingOff)
    {
        if (targets == null || targets.Length == 0)
            return null;

        Transform bestTarget = null;
        float bestScore = -Mathf.Infinity;
        Vector3 currentPosition = transform.position;

        foreach (GameObject potentialTarget in targets)
        {
            if (potentialTarget == null)
                continue;
            float distance = Vector3.Distance(currentPosition, potentialTarget.transform.position);
            float score = -distance;

            if (isBuilding)
            {
                BuildingNode node = potentialTarget.GetComponent<BuildingNode>();
                if (node == null)
                    continue;
                int utility = node.GetUtilityScore(isDroppingOff);
                if (utility <= 0)
                    continue;
                score += (utility * 15f) + Random.Range(-10f, 20f);
            }

            if (score > bestScore) { bestScore = score; bestTarget = potentialTarget.transform; }
        }
        return bestTarget;
    }

    void TryDropOff(Transform target, string nextCargo)
    {
        if (Vector3.Distance(transform.position, target.position) < 3.5f)
        {
            BuildingNode node = target.GetComponent<BuildingNode>();
            if (node != null && node.TryDropOff())
            {
                myCargo = nextCargo;
                myCurrentTarget = null;
            }
        }
    }

    void TryPickUp(Transform target, string nextCargo)
    {
        if (Vector3.Distance(transform.position, target.position) < 3.5f)
        {
            BuildingNode node = target.GetComponent<BuildingNode>();
            if (node != null && node.TryPickUp())
            {
                myCargo = nextCargo;
                myCurrentTarget = null;
            }
        }
    }

    void SetState(Transform target, string stateName)
    {
        if (target == null)
            return;

        if (myAgent != null && myAgent.isActiveAndEnabled && myAgent.isOnNavMesh)
        {
            myAgent.SetDestination(target.position);
        }

        if (myState != stateName)
            myState = stateName;
    }
}
