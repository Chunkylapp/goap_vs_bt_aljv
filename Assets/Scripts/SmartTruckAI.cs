using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

public class SmartTruckAI : MonoBehaviour
{
    public float myFuel;
    public string myCargo = "None";
    public TextMeshPro myFloatingText;

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
        Transform environmentRoot = transform.root;

        foreach (Transform child in environmentRoot.GetComponentsInChildren<Transform>(true))
        {
            if (child.CompareTag(tag))
            {
                foundList.Add(child.gameObject);
            }
        }
        return foundList.ToArray();
    }

    void Update()
    {
        if (myFuel <= 0f)
        {
            myFuel = 0f;
            myAgent.isStopped = true;
            SetState(transform, "Out of gas");
            GetComponent<Renderer>().material.color = Color.red;
        }
        else
        {
            myFuel -= Time.deltaTime * 3f;

            if (myFuel < 30f)
            {
                myCurrentTarget = GetBestTarget(myGasStations, false, false);
                if (myCurrentTarget != null)
                {
                    SetState(myCurrentTarget, "Rushing to the gas station");
                    if (Vector3.Distance(transform.position, myCurrentTarget.position) < 2.5f) myFuel = 100f;
                }
            }
            else if (myCargo == "Product")
            {
                myCurrentTarget = GetBestTarget(myConsumers, true, true);
                if (myCurrentTarget != null)
                {
                    SetState(myCurrentTarget, "Taking to consumer");
                    TryDropOff(myCurrentTarget, "None");
                }
            }
            else if (myCargo == "Raw")
            {
                myCurrentTarget = GetBestTarget(myFactories, true, true);
                if (myCurrentTarget != null)
                {
                    SetState(myCurrentTarget, "Taking to factory");
                    TryDropOff(myCurrentTarget, "None");
                }
            }
            else
            {
                Transform bestFactory = GetBestTarget(myFactories, true, false);
                Transform bestProducer = GetBestTarget(myProducers, true, false);

                int factoryScore = GetNodeScore(bestFactory, false);
                int producerScore = GetNodeScore(bestProducer, false);

                if (factoryScore > producerScore && factoryScore > 0)
                {
                    myCurrentTarget = bestFactory;
                    SetState(myCurrentTarget, "Preiau product");
                    TryPickUp(myCurrentTarget, "Product");
                }
                else
                {
                    myCurrentTarget = bestProducer;
                    if (myCurrentTarget != null)
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
        if (Vector3.Distance(transform.position, target.position) < 2.5f)
        {
            BuildingNode node = target.GetComponent<BuildingNode>();
            if (node != null && node.TryDropOff())
                myCargo = nextCargo;
        }
    }

    void TryPickUp(Transform target, string nextCargo)
    {
        if (Vector3.Distance(transform.position, target.position) < 2.5f)
        {
            BuildingNode node = target.GetComponent<BuildingNode>();
            if (node != null && node.TryPickUp())
                myCargo = nextCargo;
        }
    }

    void SetState(Transform target, string stateName)
    {
        if (target == null)
            return;

        myAgent.SetDestination(target.position)

        if (myState != stateName)
            myState = stateName;
    }
}