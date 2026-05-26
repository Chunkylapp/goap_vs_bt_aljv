using UnityEngine;
using Unity.AI.Navigation;
using System.Collections.Generic;
using UnityEngine.AI;
using System.Linq;
using System.IO;

public class CompetitionManager : MonoBehaviour
{
    public static CompetitionManager Instance;

    public NavMeshSurface surfaceBT;
    public NavMeshSurface surfaceGOAP;

    public Transform rootBT;
    public Transform rootGOAP;

    public List<GameObject> btBuildings;
    public List<GameObject> goapBuildings;

    public bool isAutoRunning = true;
    public float roundDuration = 100f;
    private float roundTimer = 0f;
    private int roundCount = 1;

    public float btFuelBurnt = 0f;
    public float goapFuelBurnt = 0f;

    public float goapTotalPlanTime = 0f;
    public int goapPlanCount = 0;
    public float btIdleTime = 0f;
    public float goapIdleTime = 0f;
    public int btAborts = 0;
    public int goapAborts = 0;

    public int btDeaths = 0;
    public int goapDeaths = 0;
    public float avgDeliveryDist = 0f;

    private string telemetryPath;

    private List<Vector3> validBTOffsets = new List<Vector3>();
    private Dictionary<GameObject, Vector3> initialTruckPositions = new Dictionary<GameObject, Vector3>();
    private Dictionary<GameObject, Quaternion> initialTruckRotations = new Dictionary<GameObject, Quaternion>();

    void Awake()
    {
        Instance = this;
        telemetryPath = Path.Combine(Application.dataPath, "..", "SimulationTelemetry.csv");
        if (!File.Exists(telemetryPath))
        {
            File.WriteAllText(telemetryPath, "Round,BT_Score,GOAP_Score,BT_Fuel,GOAP_Fuel,GOAP_AvgPlan_ms,BT_Idle_s,GOAP_Idle_s,BT_Aborts,GOAP_Aborts,BT_Deaths,GOAP_Deaths,Avg_Delivery_Dist\n");
        }
    }
    void Start()
    {
        AutoPopulateAndInitialize();
        roundTimer = roundDuration;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            if (btBuildings == null || btBuildings.Count == 0) AutoPopulateAndInitialize();
            RandomizeAndReset();
        }

        if (isAutoRunning)
        {
            roundTimer -= Time.deltaTime;
            if (roundTimer <= 0f)
            {
                EndRoundAndRestart();
            }
        }
    }

    void AutoPopulateAndInitialize()
    {
        btBuildings = new List<GameObject>();
        goapBuildings = new List<GameObject>();
        validBTOffsets.Clear();

        GameObject leftRoot = GameObject.Find("GOAP AI - LEFT");
        GameObject rightRoot = GameObject.Find("Normal BT AI - RIGHT");

        if (leftRoot != null) rootGOAP = leftRoot.transform;
        if (rightRoot != null) rootBT = rightRoot.transform;

        BuildingNode[] allNodes = FindObjectsByType<BuildingNode>(FindObjectsSortMode.None);

        var btNodes = allNodes.Where(n => n.myTeam == Team.BT).OrderBy(n => n.name).ToList();
        var goapNodes = allNodes.Where(n => n.myTeam == Team.GOAP).OrderBy(n => n.name).ToList();

        foreach (var node in btNodes)
        {
            btBuildings.Add(node.gameObject);
            if (rootBT != null) validBTOffsets.Add(node.transform.position - rootBT.position);
        }

        foreach (var node in goapNodes)
        {
            goapBuildings.Add(node.gameObject);
        }

        foreach (var truck in FindObjectsByType<SmartTruckAI>(FindObjectsSortMode.None))
        {
            initialTruckPositions[truck.gameObject] = truck.transform.position;
            initialTruckRotations[truck.gameObject] = truck.transform.rotation;
        }
        foreach (var truck in FindObjectsByType<GOAPTruckAI>(FindObjectsSortMode.None))
        {
            initialTruckPositions[truck.gameObject] = truck.transform.position;
            initialTruckRotations[truck.gameObject] = truck.transform.rotation;
        }
    }

    void EndRoundAndRestart()
    {
        if (ScoreManager.Instance != null)
        {
            Debug.Log($"<color=magenta>--- ROUND {roundCount} ENDED ---</color>");
            Debug.Log($"<color=cyan>FINAL SCORE -> BT: {ScoreManager.Instance.scoreBT} | GOAP: {ScoreManager.Instance.scoreGOAP}</color>");

            float avgPlan = goapPlanCount > 0 ? goapTotalPlanTime / goapPlanCount : 0f;
            string csvLine = $"{roundCount},{ScoreManager.Instance.scoreBT},{ScoreManager.Instance.scoreGOAP},{Mathf.Round(btFuelBurnt)},{Mathf.Round(goapFuelBurnt)},{Mathf.Round(avgPlan)},{Mathf.Round(btIdleTime)},{Mathf.Round(goapIdleTime)},{btAborts},{goapAborts},{btDeaths},{goapDeaths},{Mathf.Round(avgDeliveryDist)}\n";
            File.AppendAllText(telemetryPath, csvLine);
            Debug.Log($"<color=green>Telemetry saved to {telemetryPath}</color>");
        }

        btFuelBurnt = 0f;
        goapFuelBurnt = 0f;
        goapTotalPlanTime = 0f;
        goapPlanCount = 0;
        btIdleTime = 0f;
        goapIdleTime = 0f;
        btAborts = 0;
        goapAborts = 0;
        btDeaths = 0;
        goapDeaths = 0;

        roundCount++;
        roundTimer = roundDuration;
        RandomizeAndReset();
    }
    public void RandomizeAndReset()
    {
        Debug.Log($"<color=orange>RANDOMIZING WORLD FOR ROUND {roundCount}...</color>");

        System.Random rng = new System.Random();
        var shuffledOffsets = validBTOffsets.OrderBy(a => rng.Next()).ToList();

        for (int i = 0; i < btBuildings.Count; i++)
        {
            if (rootBT != null)
            {
                btBuildings[i].transform.position = rootBT.position + shuffledOffsets[i];
            }

            if (i < goapBuildings.Count && rootGOAP != null)
            {
                goapBuildings[i].transform.position = rootGOAP.position + shuffledOffsets[i];
            }
        }

        if (surfaceBT != null) surfaceBT.BuildNavMesh();
        if (surfaceGOAP != null) surfaceGOAP.BuildNavMesh();

        if (ScoreManager.Instance != null) ScoreManager.Instance.ResetScores();

        GOAPTruckAI.GlobalClearDistanceCache();
        ResetAllEntities();

        CalculateLayoutComplexity();
    }

    void CalculateLayoutComplexity()
    {
        var prods = btBuildings.Where(b => b.GetComponent<ProducerNode>() != null).ToList();
        var facts = btBuildings.Where(b => b.GetComponent<FactoryNode>() != null).ToList();
        var cons = btBuildings.Where(b => b.GetComponent<ConsumerNode>() != null).ToList();

        float p2f = 0f;
        foreach (var p in prods) foreach (var f in facts) p2f += Vector3.Distance(p.transform.position, f.transform.position);
        float avgP2F = prods.Count > 0 && facts.Count > 0 ? p2f / (prods.Count * facts.Count) : 0f;

        float f2c = 0f;
        foreach (var f in facts) foreach (var c in cons) f2c += Vector3.Distance(f.transform.position, c.transform.position);
        float avgF2C = facts.Count > 0 && cons.Count > 0 ? f2c / (facts.Count * cons.Count) : 0f;

        avgDeliveryDist = avgP2F + avgF2C;
    }

    void ResetAllEntities()
    {
        foreach (var b in FindObjectsByType<BuildingNode>(FindObjectsSortMode.None))
        {
            b.inputStock = 0;
            b.outputStock = 0;
            if (b is ProducerNode) b.outputStock = 2;
        }

        foreach (var truck in FindObjectsByType<SmartTruckAI>(FindObjectsSortMode.None))
        {
            truck.myFuel = 100f;
            truck.myCargo = "None";
            truck.isDead = false;
            if (initialTruckPositions.ContainsKey(truck.gameObject))
            {
                NavMeshAgent agent = truck.GetComponent<NavMeshAgent>();
                if (agent != null)
                {
                    agent.enabled = true;
                    agent.isStopped = false;
                    agent.Warp(initialTruckPositions[truck.gameObject]);
                }
                truck.transform.rotation = initialTruckRotations[truck.gameObject];
                truck.GetComponent<Renderer>().material.color = Color.white;
            }
        }

        foreach (var truck in FindObjectsByType<GOAPTruckAI>(FindObjectsSortMode.None))
        {
            truck.myFuel = 100f;
            truck.myCargo = "None";
            truck.isDead = false;
            if (initialTruckPositions.ContainsKey(truck.gameObject))
            {
                NavMeshAgent agent = truck.GetComponent<NavMeshAgent>();
                if (agent != null)
                {
                    agent.enabled = true;
                    agent.isStopped = false;
                    agent.Warp(initialTruckPositions[truck.gameObject]);
                }
                truck.transform.rotation = initialTruckRotations[truck.gameObject];
                truck.GetComponent<Renderer>().material.color = Color.white;
            }
            truck.ForceRecalculateDistances();
        }
    }
}
