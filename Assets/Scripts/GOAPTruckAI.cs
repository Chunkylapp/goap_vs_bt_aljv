using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using TMPro;

public class TruckAction
{
    public string Name;
    public Transform Target;
    public Dictionary<string, bool> Preconditions = new Dictionary<string, bool>();
    public Dictionary<string, bool> Effects = new Dictionary<string, bool>();

    public string ActionKey => $"{Name}_{Target.GetInstanceID()}";

    public TruckAction(string name, Transform target)
    {
        Name = name;
        Target = target;
    }
}

public class GOAPTruckAI : MonoBehaviour
{
    private static Dictionary<string, float> buildingDistanceCache = new Dictionary<string, float>();

    public static Dictionary<int, int> claimedPickups = new Dictionary<int, int>();
    public static Dictionary<int, int> claimedDropoffs = new Dictionary<int, int>();

    public float myFuel;
    public string myCargo = "None";
    public TextMeshPro myFloatingText;

    private NavMeshAgent myAgent;
    private GameObject[] producers, factories, consumers, gasStations;

    private Queue<TruckAction> currentPlan = new Queue<TruckAction>();
    private TruckAction currentAction = null;
    private string goapStatus = "Idle";

    private float fuelDrainRate;

    void Start()
    {
        myAgent = GetComponent<NavMeshAgent>();
        myFuel = Random.Range(70f, 100f);
        myAgent.speed = Random.Range(4f, 5.5f);
        fuelDrainRate = (3f / myAgent.speed) * 1.5f;

        producers = GetLocalObjectsWithTag("Producer");
        factories = GetLocalObjectsWithTag("Factory");
        consumers = GetLocalObjectsWithTag("Consumer");
        gasStations = GetLocalObjectsWithTag("GasStation");
    }

    GameObject[] GetLocalObjectsWithTag(string tag)
    {
        List<GameObject> foundList = new List<GameObject>();
        Transform environmentRoot = transform.root;
        foreach (Transform child in environmentRoot.GetComponentsInChildren<Transform>(true))
        {
            if (child.CompareTag(tag)) foundList.Add(child.gameObject);
        }
        return foundList.ToArray();
    }

    void Update()
    {
        if (myFuel <= 0f)
        {
            myFuel = 0f;
            myAgent.isStopped = true;
            UpdateUI("DEAD (Out of Fuel)");
            GetComponent<Renderer>().material.color = Color.red;
            ReleaseCurrentTarget();
            return;
        }

        myFuel -= Time.deltaTime * 3f;

        if (myFuel < 30f)
        {
            if (currentAction == null || currentAction.Name != "Refuel")
            {
                currentPlan.Clear();
                ReleaseCurrentTarget();

                Transform bestStation = null;
                float minDist = float.MaxValue;
                foreach (var st in gasStations)
                {
                    float d = Vector3.Distance(transform.position, st.transform.position);
                    if (d < minDist) { minDist = d; bestStation = st.transform; }
                }

                if (bestStation != null)
                {
                    currentPlan.Enqueue(new TruckAction("Refuel", bestStation));
                    goapStatus = "Panic Refuel!";
                    currentAction = null;
                }
            }
        }

        if (currentAction != null && !IsActionStillValid(currentAction))
        {
            currentPlan.Clear();
            ReleaseCurrentTarget();
            currentAction = null;
            goapStatus = "Target stolen/dry! Replanning...";
        }
        else if (currentAction != null && myAgent.pathStatus == NavMeshPathStatus.PathPartial)
        {
            currentPlan.Clear();
            ReleaseCurrentTarget();
            currentAction = null;
            goapStatus = "Path blocked! Replanning...";
        }

        if (currentAction == null && myFuel >= 30f)
        {
            if (currentPlan.Count > 0)
            {
                currentAction = currentPlan.Dequeue();
                ClaimTarget(currentAction);
                myAgent.SetDestination(currentAction.Target.position);
                goapStatus = currentAction.Name;
            }
            else
            {
                goapStatus = "Calculating Matrix...";
                BuildPlan();
            }
        }

        if (currentAction != null && !myAgent.pathPending && Vector3.Distance(transform.position, currentAction.Target.position) < 2.5f)
        {
            ExecuteActionLogic();
        }

        UpdateUI(goapStatus);
    }

    void ClaimTarget(TruckAction action)
    {
        if (action.Target == null || action.Name == "Refuel") return;
        int id = action.Target.GetInstanceID();

        if (action.Name.Contains("Pick Up"))
        {
            if (!claimedPickups.ContainsKey(id)) claimedPickups[id] = 0;
            claimedPickups[id]++;
        }
        else if (action.Name.Contains("Drop Off"))
        {
            if (!claimedDropoffs.ContainsKey(id)) claimedDropoffs[id] = 0;
            claimedDropoffs[id]++;
        }
    }

    void ReleaseCurrentTarget()
    {
        if (currentAction == null || currentAction.Target == null || currentAction.Name == "Refuel") return;
        int id = currentAction.Target.GetInstanceID();

        if (currentAction.Name.Contains("Pick Up") && claimedPickups.ContainsKey(id))
        {
            claimedPickups[id]--;
        }
        else if (currentAction.Name.Contains("Drop Off") && claimedDropoffs.ContainsKey(id))
        {
            claimedDropoffs[id]--;
        }
    }

    bool IsActionStillValid(TruckAction action)
    {
        if (action.Name == "Refuel") return true;
        BuildingNode node = action.Target.GetComponent<BuildingNode>();
        if (node == null) return false;

        if (action.Name.Contains("Pick Up") && node.GetUtilityScore(false) <= 0) return false;
        if (action.Name.Contains("Drop Off") && node.GetUtilityScore(true) <= 0) return false;
        return true;
    }

    void ExecuteActionLogic()
    {
        bool success = true;
        BuildingNode node = currentAction.Target.GetComponent<BuildingNode>();

        if (currentAction.Name == "Refuel") myFuel = 100f;
        else if (currentAction.Name == "Pick Up Raw") { if (node.TryPickUp()) myCargo = "Raw"; else success = false; }
        else if (currentAction.Name == "Drop Off Raw") { if (node.TryDropOff()) myCargo = "None"; else success = false; }
        else if (currentAction.Name == "Pick Up Product") { if (node.TryPickUp()) myCargo = "Product"; else success = false; }
        else if (currentAction.Name == "Drop Off Product") { if (node.TryDropOff()) myCargo = "None"; else success = false; }

        if (!success) currentPlan.Clear();
        ReleaseCurrentTarget();
        currentAction = null;
    }

    void BuildPlan()
    {
        Dictionary<string, bool> currentState = new Dictionary<string, bool>
        {
            { "has_raw", myCargo == "Raw" },
            { "has_product", myCargo == "Product" },
            { "is_empty", myCargo == "None" },
            { "task_complete", false }
        };

        List<TruckAction> availableActions = GenerateAvailableActions();
        List<TruckAction> bestPlan = null;
        float bestScore = -float.MaxValue;
        Dictionary<Transform, float> truckDistances = new Dictionary<Transform, float>();

        void SearchGraph(Dictionary<string, bool> state, float simFuel, Vector3 simPosition, Transform lastTarget, List<TruckAction> path, float currentScore)
        {
            if (path.Count > 3) return;

            if (state.ContainsKey("task_complete") && state["task_complete"] == true)
            {
                if (currentScore > bestScore)
                {
                    bestScore = currentScore;
                    bestPlan = new List<TruckAction>(path);
                }
            }

            foreach (var action in availableActions)
            {
                if (path.Contains(action)) continue;

                bool isValid = true;
                foreach (var pre in action.Preconditions)
                {
                    if (!state.ContainsKey(pre.Key) || state[pre.Key] != pre.Value) { isValid = false; break; }
                }

                if (isValid)
                {
                    float realDist = GetActionDistance(simPosition, lastTarget, action.Target, truckDistances);
                    if (realDist >= 10000f) continue;

                    float fuelNeeded = realDist * fuelDrainRate;
                    if (simFuel < fuelNeeded) continue;

                    float nextSimFuel = (action.Name == "Refuel") ? 100f : (simFuel - fuelNeeded);

                    if (action.Name != "Refuel")
                    {
                        float distToNearestGas = GetDistanceToNearestGasStationBTStyle(action.Target);
                        if (nextSimFuel < (distToNearestGas * fuelDrainRate)) continue;
                    }
                    else if (simFuel > 65f) continue;

                    float actionScore = -realDist;

                    if (action.Name != "Refuel")
                    {
                        BuildingNode node = action.Target.GetComponent<BuildingNode>();
                        if (node != null)
                        {
                            bool isDroppingOff = action.Name.Contains("Drop");

                            int utility = node.GetUtilityScore(isDroppingOff);
                            actionScore += utility * 15f;
                        }
                    }

                    var newState = new Dictionary<string, bool>(state);
                    foreach (var eff in action.Effects) newState[eff.Key] = eff.Value;

                    path.Add(action);
                    SearchGraph(newState, nextSimFuel, action.Target.position, action.Target, path, currentScore + actionScore);
                    path.RemoveAt(path.Count - 1);
                }
            }
        }

        SearchGraph(currentState, myFuel, transform.position, null, new List<TruckAction>(), 0f);

        if (bestPlan != null)
        {
            foreach (var act in bestPlan)
                currentPlan.Enqueue(act);
        }
        else
        {
            if (myFuel < 100f)
            {
                Transform nearestStation = null;
                float minD = float.MaxValue;
                foreach (var st in gasStations)
                {
                    float d = Vector3.Distance(transform.position, st.transform.position);
                    if (d < minD && (d * fuelDrainRate) <= myFuel) { minD = d; nearestStation = st.transform; }
                }

                if (nearestStation != null && minD > 2f)
                {
                    currentPlan.Enqueue(new TruckAction("Refuel", nearestStation));
                    goapStatus = "Idle -> Refueling";
                    return;
                }
            }
            goapStatus = "Waiting for tasks...";
        }
    }

    List<TruckAction> GenerateAvailableActions()
    {
        List<TruckAction> actions = new List<TruckAction>();
        foreach (var station in gasStations) { actions.Add(new TruckAction("Refuel", station.transform)); }

        foreach (var prod in producers)
        {
            BuildingNode node = prod.GetComponent<BuildingNode>();
            if (node != null)
            {
                int id = prod.transform.GetInstanceID();
                int claimed = claimedPickups.ContainsKey(id) ? claimedPickups[id] : 0;

                if (node.GetUtilityScore(false) - claimed > 0)
                {
                    var a = new TruckAction("Pick Up Raw", prod.transform);
                    a.Preconditions.Add("is_empty", true);
                    a.Effects.Add("is_empty", false); a.Effects.Add("has_raw", true);
                    actions.Add(a);
                }
            }
        }

        foreach (var fact in factories)
        {
            BuildingNode node = fact.GetComponent<BuildingNode>();
            if (node != null)
            {
                int id = fact.transform.GetInstanceID();
                int claimedDrops = claimedDropoffs.ContainsKey(id) ? claimedDropoffs[id] : 0;
                int claimedPicks = claimedPickups.ContainsKey(id) ? claimedPickups[id] : 0;

                if (node.GetUtilityScore(true) - claimedDrops > 0)
                {
                    var dropRaw = new TruckAction("Drop Off Raw", fact.transform);
                    dropRaw.Preconditions.Add("has_raw", true);
                    dropRaw.Effects.Add("has_raw", false); dropRaw.Effects.Add("is_empty", true);
                    dropRaw.Effects.Add("task_complete", true);
                    actions.Add(dropRaw);
                }

                if (node.GetUtilityScore(false) - claimedPicks > 0)
                {
                    var pickProduct = new TruckAction("Pick Up Product", fact.transform);
                    pickProduct.Preconditions.Add("is_empty", true);
                    pickProduct.Effects.Add("is_empty", false); pickProduct.Effects.Add("has_product", true);
                    actions.Add(pickProduct);
                }
            }
        }

        foreach (var cons in consumers)
        {
            BuildingNode node = cons.GetComponent<BuildingNode>();
            if (node != null)
            {
                int id = cons.transform.GetInstanceID();
                int claimedDrops = claimedDropoffs.ContainsKey(id) ? claimedDropoffs[id] : 0;

                if (node.GetUtilityScore(true) - claimedDrops > 0)
                {
                    var dropProduct = new TruckAction("Drop Off Product", cons.transform);
                    dropProduct.Preconditions.Add("has_product", true);
                    dropProduct.Effects.Add("has_product", false); dropProduct.Effects.Add("is_empty", true);
                    dropProduct.Effects.Add("task_complete", true);
                    actions.Add(dropProduct);
                }
            }
        }
        return actions;
    }

    float GetActionDistance(Vector3 simPosition, Transform lastTarget, Transform currentTarget, Dictionary<Transform, float> truckDistances)
    {
        if (lastTarget == null)
        {
            if (!truckDistances.ContainsKey(currentTarget))
                truckDistances[currentTarget] = GetNavMeshDistance(simPosition, currentTarget.position);
            return truckDistances[currentTarget];
        }
        else
        {
            if (lastTarget == currentTarget) return 0f;

            string distKey = $"{lastTarget.GetInstanceID()}_{currentTarget.GetInstanceID()}";
            if (!buildingDistanceCache.ContainsKey(distKey))
                buildingDistanceCache[distKey] = GetNavMeshDistance(lastTarget.position, currentTarget.position);
            return buildingDistanceCache[distKey];
        }
    }

    float GetDistanceToNearestGasStationBTStyle(Transform fromBuilding)
    {
        float minStationDist = float.MaxValue;
        foreach (var station in gasStations)
        {
            float d = Vector3.Distance(fromBuilding.position, station.transform.position);
            if (d < minStationDist) minStationDist = d;
        }
        return minStationDist;
    }

    float GetNavMeshDistance(Vector3 start, Vector3 end)
    {
        NavMeshPath path = new NavMeshPath();
        if (NavMesh.CalculatePath(start, end, NavMesh.AllAreas, path))
        {
            if (path.status != NavMeshPathStatus.PathComplete) return float.MaxValue;
            float length = 0f;
            for (int i = 1; i < path.corners.Length; i++)
                length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            return length;
        }
        return float.MaxValue;
    }

    void UpdateUI(string state)
    {
        if (myFloatingText != null)
        {
            myFloatingText.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            string cargo = myCargo == "Raw" ? "Raw" : (myCargo == "Product" ? "Product" : "Empty");
            myFloatingText.text = $"<color=#FFD700>Fuel: {Mathf.Round(myFuel)}%</color> | Cargo: {cargo}\n<color=#00FF00>Plan: {state}</color>";
        }
    }
}