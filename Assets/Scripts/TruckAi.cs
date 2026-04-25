using UnityEngine;
using UnityEngine.AI;

public class TruckAI : MonoBehaviour
{
    public float myFuel = 100f;
    public Transform myProducer;
    public Transform myFactory;
    private NavMeshAgent myAgent;
    private string myCargo = "None";

    void Start()
    {
        myAgent = GetComponent<NavMeshAgent>();
    }

    void Update()
    {
        myFuel -= Time.deltaTime * 3f;

        if (myCargo == "None")
        {
            myAgent.SetDestination(myProducer.position);
            if (Vector3.Distance(transform.position, myProducer.position) < 2.5f)
            {
                myCargo = "Raw";
            }
        }
        else if (myCargo == "Raw")
        {
            myAgent.SetDestination(myFactory.position);
            if (Vector3.Distance(transform.position, myFactory.position) < 2.5f)
            {
                myCargo = "None";
            }
        }
    }
}