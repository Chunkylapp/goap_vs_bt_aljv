using UnityEngine;

public class ObstacleSpawner : MonoBehaviour
{
    public GameObject obstaclePrefab;
    public Camera cameraBT;
    public Camera cameraGOAP;

    public float planesOffset = 100f;

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Camera activeCamera = (Input.mousePosition.x < Screen.width / 2f) ? cameraBT : cameraGOAP;

            Ray ray = activeCamera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                Vector3 basePos = hit.point + new Vector3(0, 1.5f, 0);

                Instantiate(obstaclePrefab, basePos, Quaternion.identity);

                if (activeCamera == cameraBT)
                {
                    Instantiate(obstaclePrefab, basePos + new Vector3(planesOffset, 0, 0), Quaternion.identity);
                }
                else
                {
                    Instantiate(obstaclePrefab, basePos - new Vector3(planesOffset, 0, 0), Quaternion.identity);
                }

                Debug.Log("<color=red>Obstacle spawned</color>");
            }
        }
    }
}