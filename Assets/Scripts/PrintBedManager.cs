
using UnityEngine;

public class PrintBedManager : MonoBehaviour
{
    [Header("Druckbett")]
    public Transform printBed;

    [Header("Druckbettgroesse in mm")]
    public float bedWidth = 200f;
    public float bedDepth = 200f;

    [Header("Aktuelles Modell")]
    public GameObject currentModel;

    private const float MmToMeters = 0.001f;

    void Start()
    {
        UpdatePrintBed();
        CenterModel();
    }

    public void UpdatePrintBed()
    {
        if (printBed == null)
            return;

        // Unity-Plane hat standardmaessig 10 x 10 Einheiten.
        printBed.localScale = new Vector3(
            bedWidth * MmToMeters / 10f,
            1f,
            bedDepth * MmToMeters / 10f
        );
    }

    public void CenterModel()
    {
        if (currentModel == null || printBed == null)
            return;

        Renderer[] renderers =
            currentModel.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
            return;

        Bounds bounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        // Modell horizontal mittig positionieren.
        Vector3 offset = new Vector3(
            printBed.position.x - bounds.center.x,
            printBed.position.y - bounds.min.y,
            printBed.position.z - bounds.center.z
        );

        currentModel.transform.position += offset;
    }
}
