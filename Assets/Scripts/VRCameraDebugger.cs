
using UnityEngine;

public class VRCameraDebug : MonoBehaviour
{
    public Transform vrCamera;
    public Transform printBed;

    private void Update()
    {
        if (vrCamera == null || printBed == null)
            return;

        if (Time.frameCount % 120 == 0)
        {
            Debug.Log(
                "VR-Kamera Weltposition: " + vrCamera.position +
                " | Blickrichtung: " + vrCamera.forward +
                " | Druckbett Weltposition: " + printBed.position +
                " | Abstand: " +
                Vector3.Distance(vrCamera.position, printBed.position)
            );
        }
    }
}

