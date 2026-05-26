using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using TMPro;
using System.Threading.Tasks;
using System.Linq;

public class TruckAction
{
    public string Name;
    public Transform Target;
    public int TargetID;
    public Vector3 TargetPosition;
    public Dictionary<string, bool> Preconditions = new Dictionary<string, bool>();
    public Dictionary<string, bool> Effects = new Dictionary<string, bool>();

    public TruckAction(string name, Transform target)
    {
        Name = name;
        Target = target;
        if (target != null)
        {
            TargetID = target.GetInstanceID();
            TargetPosition = target.position;
        }
    }
}

public class GOAPTruckAI : MonoBehaviour
{
    private static Dictionary<string, float> buildingDistanceCache = new Dictionary<string, float>();
    private static bool distancesPrecalculated = false;

    public static Dictionary<int, int> claimedPickups = new Dictionary<int, int>();
    public static Dictionary<int, int> claimedDropoffs = new Dictionary<int, int>();

    public Team myTeam;
    public float myFuel;
    public string myCargo = "None";
    public TextMeshPro myFloatingText;
    public bool isDead = false;

    private NavMeshAgent myAgent;
    private GameObject[] producers, factories, consumers, gasStations;

    private Queue<TruckAction> currentPlan = new Queue<TruckAction>();
    private TruckAction currentAction = null;
    private string goapStatus = "Idle";

    private float fuelDrainRate;
    private bool isPlanning = false;

    void Start()
    {
        myAgent = GetComponent<NavMeshAgent>();
        myFuel = Random.Range(70f, 100f);
        myAgent.speed = Random.Range(4f, 5.5f);
        fuelDrainRate = 3f / myAgent.speed;

        FindLocalBuildings();

        if (!distancesPrecalculated) PrecalculateDistances();
    }

    void FindLocalBuildings()
    {
        producers = GetLocalObjectsWithTag("Producer");
        factories = GetLocalObjectsWithTag("Factory");
        consumers = GetLocalObjectsWithTag("Consumer");
        gasStations = GetLocalObjectsWithTag("GasStation");
    }

    public static void GlobalClearDistanceCache()
    {
        buildingDistanceCache.Clear();
        claimedPickups.Clear();
        claimedDropoffs.Clear();
        distancesPrecalculated = false;
    }

    public void ForceRecalculateDistances()
    {
        AbortPlan();
        isPlanning = false;
        if (!distancesPrecalculated) PrecalculateDistances();
    }

    void AbortPlan()
    {
        if (currentAction != null || currentPlan.Count > 0)
        {
            if (CompetitionManager.Instance != null) CompetitionManager.Instance.goapAborts++;
        }
        if (currentAction != null) ReleaseTarget(currentAction);
        foreach (var act in currentPlan) ReleaseTarget(act);
        currentPlan.Clear();
        currentAction = null;
    }

    void PrecalculateDistances()
    {
        List<GameObject> allBuildings = new List<GameObject>();
        allBuildings.AddRange(producers);
        allBuildings.AddRange(factories);
        allBuildings.AddRange(consumers);
        allBuildings.AddRange(gasStations);

        foreach (var b1 in allBuildings)
        {
            foreach (var b2 in allBuildings)
            {
                if (b1 == b2) continue;
                string key = $"{b1.GetInstanceID()}_{b2.GetInstanceID()}";
                if (!buildingDistanceCache.ContainsKey(key))
                {
                    buildingDistanceCache[key] = GetNavMeshDistance(b1.transform.position, b2.transform.position);
                }
            }
        }
        distancesPrecalculated = true;
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

            // Check for TeamMember (Gas Stations)
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

    void Update()
    {
        if (myFuel <= 0f)
        {
            if (myAgent.enabled)
            {
                if (!isDead)
                {
                    isDead = true;
                    if (CompetitionManager.Instance != null) CompetitionManager.Instance.goapDeaths++;
                }
                myFuel = 0f;
                myAgent.isStopped = true;
                myAgent.enabled = false;
                UpdateUI("DEAD (Out of Fuel)");
                GetComponent<Renderer>().material.color = Color.red;
                AbortPlan();
            }
            return;
        }
        float fuelConsumed = Time.deltaTime * 3f;
        myFuel -= fuelConsumed;
        if (CompetitionManager.Instance != null) CompetitionManager.Instance.goapFuelBurnt += fuelConsumed;

        if (currentAction == null)
        {
            if (CompetitionManager.Instance != null) CompetitionManager.Instance.goapIdleTime += Time.deltaTime;
        }

        if (myFuel < 30f && (currentAction == null || currentAction.Name != "Refuel"))
        {
            AbortPlan();


            Transform bestStation = null;
            float minDist = float.MaxValue;
            foreach (var st in gasStations)
            {
                float d = Vector3.Distance(transform.position, st.transform.position);
                if (d < minDist) { minDist = d; bestStation = st.transform; }
            }

            if (bestStation != null)
            {
                currentAction = new TruckAction("Refuel", bestStation);
                if (myAgent != null && myAgent.isActiveAndEnabled && myAgent.isOnNavMesh) myAgent.SetDestination(currentAction.TargetPosition);
                goapStatus = "Panic Refuel!";
            }
        }

        if (currentAction != null && currentAction.Name != "Refuel")
        {
            if (!IsActionStillValid(currentAction) || (myAgent != null && myAgent.pathStatus == NavMeshPathStatus.PathPartial))
            {
                AbortPlan();
                goapStatus = "Replanning...";
            }
        }

        if (currentAction == null && !isPlanning)
        {
            if (currentPlan.Count > 0)
            {
                currentAction = currentPlan.Dequeue();
                if (myAgent != null && myAgent.isActiveAndEnabled && myAgent.isOnNavMesh) myAgent.SetDestination(currentAction.TargetPosition);
                goapStatus = currentAction.Name;
            }
            else if (myFuel >= 30f)
            {
                StartAsyncPlan();
            }
        }

        if (currentAction != null && !myAgent.pathPending && Vector3.Distance(transform.position, currentAction.TargetPosition) < 3.5f)
        {
            ExecuteActionLogic();
        }

        UpdateUI(goapStatus);
    }

    async void StartAsyncPlan()
    {
        if (isPlanning) return;

        if (producers == null || producers.Length == 0) FindLocalBuildings();
        if (producers == null || producers.Length == 0) return;

        isPlanning = true;
        goapStatus = "Calculating Matrix...";

        var state = new Dictionary<string, bool>
        {
            { "has_raw", myCargo == "Raw" },
            { "has_product", myCargo == "Product" },
            { "is_empty", myCargo == "None" },
            { "task_complete", false }
        };

        var pickupsSnapshot = new Dictionary<int, int>(claimedPickups);
        var dropoffsSnapshot = new Dictionary<int, int>(claimedDropoffs);

        Vector3 pos = transform.position;
        float fuel = myFuel;

        var nodeSnapshots = new List<NodeSnapshot>();
        void AddSnapshots(GameObject[] objs, string type)
        {
            if (objs == null) return;
            foreach (var o in objs)
            {
                if (o == null) continue;
                var node = o.GetComponent<BuildingNode>();
                if (node == null) continue;

                nodeSnapshots.Add(new NodeSnapshot
                {
                    ID = o.GetInstanceID(),
                    Position = o.transform.position,
                    Type = type,
                    InputUtility = node.GetUtilityScore(true),
                    OutputUtility = node.GetUtilityScore(false)
                });
            }
        }
        AddSnapshots(producers, "Producer");
        AddSnapshots(factories, "Factory");
        AddSnapshots(consumers, "Consumer");
        AddSnapshots(gasStations, "GasStation");

        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        var plan = await Task.Run(() => DoPlanSearch(state, pos, fuel, nodeSnapshots, pickupsSnapshot, dropoffsSnapshot));
        sw.Stop();

        if (CompetitionManager.Instance != null)
        {
            CompetitionManager.Instance.goapTotalPlanTime += sw.ElapsedMilliseconds;
            CompetitionManager.Instance.goapPlanCount++;
        }

        if (plan != null)
        {
            AbortPlan();
            foreach (var act in plan)
            {
                Transform target = FindTransformByID(act.TargetID);
                if (target != null)
                {
                    act.Target = target;
                    currentPlan.Enqueue(act);
                    ClaimTarget(act);
                }
            }
        }
        isPlanning = false;
    }

    struct NodeSnapshot
    {
        public int ID;
        public Vector3 Position;
        public string Type;
        public int InputUtility;
        public int OutputUtility;
    }

    List<TruckAction> DoPlanSearch(Dictionary<string, bool> startState, Vector3 startPos, float startFuel, List<NodeSnapshot> nodes, Dictionary<int, int> pickupsSnapshot, Dictionary<int, int> dropoffsSnapshot)
    {
        List<TruckAction> bestPlan = null;
        float bestScore = -float.MaxValue;

        List<TruckAction> availableActions = new List<TruckAction>();
        foreach (var n in nodes)
        {
            if (n.Type == "GasStation") availableActions.Add(new TruckAction("Refuel", null) { TargetID = n.ID, TargetPosition = n.Position });
            else if (n.Type == "Producer")
            {
                int claimed = pickupsSnapshot.ContainsKey(n.ID) ? pickupsSnapshot[n.ID] : 0;
                if (n.OutputUtility - claimed > 0)
                {
                    var a = new TruckAction("Pick Up Raw", null) { TargetID = n.ID, TargetPosition = n.Position };
                    a.Preconditions.Add("is_empty", true);
                    a.Effects.Add("is_empty", false); a.Effects.Add("has_raw", true);
                    availableActions.Add(a);
                }
            }
            else if (n.Type == "Factory")
            {
                int claimedD = dropoffsSnapshot.ContainsKey(n.ID) ? dropoffsSnapshot[n.ID] : 0;
                int claimedP = pickupsSnapshot.ContainsKey(n.ID) ? pickupsSnapshot[n.ID] : 0;
                if (n.InputUtility - claimedD > 0)
                {
                    var a = new TruckAction("Drop Off Raw", null) { TargetID = n.ID, TargetPosition = n.Position };
                    a.Preconditions.Add("has_raw", true);
                    a.Effects.Add("has_raw", false); a.Effects.Add("is_empty", true); a.Effects.Add("task_complete", true);
                    availableActions.Add(a);
                }
                if (n.OutputUtility - claimedP > 0)
                {
                    var a = new TruckAction("Pick Up Product", null) { TargetID = n.ID, TargetPosition = n.Position };
                    a.Preconditions.Add("is_empty", true);
                    a.Effects.Add("is_empty", false); a.Effects.Add("has_product", true);
                    availableActions.Add(a);
                }
            }
            else if (n.Type == "Consumer")
            {
                int claimedD = dropoffsSnapshot.ContainsKey(n.ID) ? dropoffsSnapshot[n.ID] : 0;
                if (n.InputUtility - claimedD > 0)
                {
                    var a = new TruckAction("Drop Off Product", null) { TargetID = n.ID, TargetPosition = n.Position };
                    a.Preconditions.Add("has_product", true);
                    a.Effects.Add("has_product", false); a.Effects.Add("is_empty", true); a.Effects.Add("task_complete", true);
                    availableActions.Add(a);
                }
            }
        }

        void Search(Dictionary<string, bool> state, float fuel, Vector3 pos, int lastID, List<TruckAction> path, float score)
        {
            if (path.Count > 4) return;

            float finalScore = state.ContainsKey("task_complete") && state["task_complete"] ? score + 1000f : score;

            if (path.Count > 0 && finalScore > bestScore)
            {
                bestScore = finalScore;
                bestPlan = new List<TruckAction>(path);
            }

            foreach (var action in availableActions)
            {
                if (path.Any(a => a.TargetID == action.TargetID && a.Name == action.Name)) continue;

                bool valid = true;
                foreach (var pre in action.Preconditions) if (!state.ContainsKey(pre.Key) || state[pre.Key] != pre.Value) { valid = false; break; }
                if (!valid) continue;

                float dist = 0;
                if (lastID == -1) dist = Vector3.Distance(pos, action.TargetPosition);
                else
                {
                    string key = $"{lastID}_{action.TargetID}";
                    dist = buildingDistanceCache.ContainsKey(key) ? buildingDistanceCache[key] : Vector3.Distance(pos, action.TargetPosition);
                }

                if (action.Name != "Refuel")
                {
                    int claimedPick = pickupsSnapshot.ContainsKey(action.TargetID) ? pickupsSnapshot[action.TargetID] : 0;
                    int claimedDrop = dropoffsSnapshot.ContainsKey(action.TargetID) ? dropoffsSnapshot[action.TargetID] : 0;
                    dist += (claimedPick + claimedDrop) * 40f;
                }

                float fuelNeeded = dist * fuelDrainRate;
                if (fuel < fuelNeeded) continue;

                float nextFuel = (action.Name == "Refuel") ? 100f : (fuel - fuelNeeded);
                if (action.Name == "Refuel" && fuel > 70f) continue;

                float actionScore = -dist;
                var node = nodes.Find(n => n.ID == action.TargetID);

                if (action.Name == "Refuel" && fuel <= 70f)
                {
                    actionScore += (70f - fuel) * 2f;
                }

                if (action.Name == "Pick Up Product") actionScore += node.OutputUtility * 40f;
                else if (action.Name == "Drop Off Product") actionScore += node.InputUtility * 50f;
                else if (action.Name == "Pick Up Raw") actionScore += node.OutputUtility * 10f;
                else if (action.Name == "Drop Off Raw") actionScore += node.InputUtility * 15f;

                var newState = new Dictionary<string, bool>(state);
                foreach (var eff in action.Effects) newState[eff.Key] = eff.Value;

                path.Add(action);
                Search(newState, nextFuel, action.TargetPosition, action.TargetID, path, score + actionScore);
                path.RemoveAt(path.Count - 1);
            }
        }

        Search(startState, startFuel, startPos, -1, new List<TruckAction>(), 0f);
        return bestPlan;
    }

    Transform FindTransformByID(int id)
    {
        foreach (var g in producers) if (g.GetInstanceID() == id) return g.transform;
        foreach (var g in factories) if (g.GetInstanceID() == id) return g.transform;
        foreach (var g in consumers) if (g.GetInstanceID() == id) return g.transform;
        foreach (var g in gasStations) if (g.GetInstanceID() == id) return g.transform;
        return null;
    }

    void ClaimTarget(TruckAction action)
    {
        if (action.Target == null || action.Name == "Refuel") return;
        int id = action.TargetID;
        if (action.Name.Contains("Pick Up")) { if (!claimedPickups.ContainsKey(id)) claimedPickups[id] = 0; claimedPickups[id]++; }
        else if (action.Name.Contains("Drop Off")) { if (!claimedDropoffs.ContainsKey(id)) claimedDropoffs[id] = 0; claimedDropoffs[id]++; }
    }

    void ReleaseTarget(TruckAction action)
    {
        if (action == null || action.Name == "Refuel") return;
        int id = action.TargetID;
        if (action.Name.Contains("Pick Up") && claimedPickups.ContainsKey(id)) claimedPickups[id]--;
        else if (action.Name.Contains("Drop Off") && claimedDropoffs.ContainsKey(id)) claimedDropoffs[id]--;
    }

    bool IsActionStillValid(TruckAction action)
    {
        if (action.Target == null) return false;
        BuildingNode node = action.Target.GetComponent<BuildingNode>();
        if (node == null) return false;
        if (action.Name.Contains("Pick Up") && node.GetUtilityScore(false) <= 0) return false;
        if (action.Name.Contains("Drop Off") && node.GetUtilityScore(true) <= 0) return false;
        return true;
    }

    void ExecuteActionLogic()
    {
        bool success = true;
        BuildingNode node = currentAction.Target != null ? currentAction.Target.GetComponent<BuildingNode>() : null;

        if (currentAction.Name == "Refuel") myFuel = 100f;
        else if (currentAction.Name == "Pick Up Raw") { if (node != null && node.TryPickUp()) myCargo = "Raw"; else success = false; }
        else if (currentAction.Name == "Drop Off Raw") { if (node != null && node.TryDropOff()) myCargo = "None"; else success = false; }
        else if (currentAction.Name == "Pick Up Product") { if (node != null && node.TryPickUp()) myCargo = "Product"; else success = false; }
        else if (currentAction.Name == "Drop Off Product") { if (node != null && node.TryDropOff()) myCargo = "None"; else success = false; }

        if (!success) AbortPlan();
        else
        {
            ReleaseTarget(currentAction);
            currentAction = null;
        }
    }

    float GetNavMeshDistance(Vector3 start, Vector3 end)
    {
        NavMeshPath path = new NavMeshPath();
        if (NavMesh.CalculatePath(start, end, NavMesh.AllAreas, path))
        {
            if (path.status != NavMeshPathStatus.PathComplete) return 100000f;
            float length = 0f;
            for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            return length;
        }
        return 100000f;
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
