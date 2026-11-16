using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Zeichnet ein flaches, maßhaltiges Raster auf dem 200x200-mm-Druckbett.
/// Statt kamerazugewandter LineRenderer werden flache Mesh-Streifen verwendet.
/// Das Raster bleibt dadurch auch bei skaliertem PrintBed gut sichtbar.
/// Das Objekt PrintBedGrid bleibt ein Kind von PrintBed (lokal 0/0/0, Scale 1).
/// </summary>
public class PrintBedGrid : MonoBehaviour
{
    [Header("Bed size")]
    [Min(1f)] public float bedSizeMm = 200f;
    [Min(1f)] public float gridStepMm = 10f;
    [Min(1)] public int majorEvery = 5;

    [Header("Appearance")]
    public Material lineMaterial; // PrintBedGrid_Unlit (URP/Unlit, Base Color Weiß)
    public Color minorColor = new Color(0.69f, 0.73f, 0.77f, 1f);
    public Color majorColor = new Color(0.40f, 0.47f, 0.53f, 1f);

    [Tooltip("Breite der feinen Linien in Millimetern")]
    [Min(0.05f)] public float minorWidthMm = 0.5f;
    [Tooltip("Breite der Hauptlinien in Millimetern")]
    [Min(0.05f)] public float majorWidthMm = 1.0f;
    [Tooltip("Abstand über der Oberfläche in METERN (z.B. 0.001 = 1 mm)")]
    [Min(0f)] public float yOffset = 0.001f;

    private readonly List<GameObject> generatedObjects = new List<GameObject>();
    private readonly List<Mesh> generatedMeshes = new List<Mesh>();
    private readonly List<Material> generatedMaterials = new List<Material>();

    private void Start()
    {
        BuildGrid();
    }

    [ContextMenu("Build Grid")]
    public void BuildGrid()
    {
        ClearGrid();

        // Unity-Plane: 10 x 10 lokale Einheiten; bei PrintBed-Skalierung
        // 0.02/1/0.02 entspricht dies 200 x 200 mm.
        // Alle Streifen liegen flach in der XZ-Ebene, nicht zur Kamera gedreht.
        const float halfLocalPlane = 5f;
        float size = Mathf.Max(1f, bedSizeMm);
        float step = Mathf.Max(1f, gridStepMm);
        int intervals = Mathf.Max(1, Mathf.RoundToInt(size / step));
        int majorStep = Mathf.Max(1, majorEvery);

        // In lokale Koordinaten umrechnen: Linienbreiten bleiben in der Welt
        // in Millimetern korrekt, obwohl PrintBed z.B. X/Z=0.02 skaliert ist.
        Vector3 worldScale = transform.lossyScale;
        float scaleX = Mathf.Max(0.000001f, Mathf.Abs(worldScale.x));
        float scaleY = Mathf.Max(0.000001f, Mathf.Abs(worldScale.y));
        float scaleZ = Mathf.Max(0.000001f, Mathf.Abs(worldScale.z));
        float minorWidthX = Mathf.Max(0.05f, minorWidthMm) * 0.001f / scaleX;
        float minorWidthZ = Mathf.Max(0.05f, minorWidthMm) * 0.001f / scaleZ;
        float majorWidthX = Mathf.Max(0.05f, majorWidthMm) * 0.001f / scaleX;
        float majorWidthZ = Mathf.Max(0.05f, majorWidthMm) * 0.001f / scaleZ;
        float minorY = yOffset / scaleY;
        float majorY = (yOffset + 0.0001f) / scaleY;

        List<Vector3> minorVertices = new List<Vector3>();
        List<int> minorTriangles = new List<int>();
        List<Vector3> majorVertices = new List<Vector3>();
        List<int> majorTriangles = new List<int>();

        for (int i = 0; i <= intervals; i++)
        {
            float pos = Mathf.Lerp(-halfLocalPlane, halfLocalPlane, i / (float)intervals);
            bool major = (i % majorStep == 0) || i == intervals;
            List<Vector3> vertices = major ? majorVertices : minorVertices;
            List<int> triangles = major ? majorTriangles : minorTriangles;
            float halfWidthX = (major ? majorWidthX : minorWidthX) * 0.5f;
            float halfWidthZ = (major ? majorWidthZ : minorWidthZ) * 0.5f;
            float y = major ? majorY : minorY;

            // Konstantes X: senkrechter Streifen über die Z-Richtung.
            AddQuad(vertices, triangles,
                Mathf.Max(-halfLocalPlane, pos - halfWidthX),
                Mathf.Min(halfLocalPlane, pos + halfWidthX),
                -halfLocalPlane, halfLocalPlane, y);

            // Konstantes Z: waagerechter Streifen über die X-Richtung.
            AddQuad(vertices, triangles,
                -halfLocalPlane, halfLocalPlane,
                Mathf.Max(-halfLocalPlane, pos - halfWidthZ),
                Mathf.Min(halfLocalPlane, pos + halfWidthZ), y);
        }

        CreateGridMesh("Grid - Minor", minorVertices, minorTriangles, minorColor);
        CreateGridMesh("Grid - Major", majorVertices, majorTriangles, majorColor);
    }

    private static void AddQuad(List<Vector3> vertices, List<int> triangles,
        float xMin, float xMax, float zMin, float zMax, float y)
    {
        int index = vertices.Count;
        vertices.Add(new Vector3(xMin, y, zMin));
        vertices.Add(new Vector3(xMax, y, zMin));
        vertices.Add(new Vector3(xMax, y, zMax));
        vertices.Add(new Vector3(xMin, y, zMax));

        // Oberseite zeigt nach +Y (sichtbar von oben).
        triangles.Add(index);
        triangles.Add(index + 2);
        triangles.Add(index + 1);
        triangles.Add(index);
        triangles.Add(index + 3);
        triangles.Add(index + 2);
    }

    private void CreateGridMesh(string objectName, List<Vector3> vertices,
        List<int> triangles, Color color)
    {
        if (vertices.Count == 0) return;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (lineMaterial == null && shader == null)
        {
            Debug.LogError("PrintBedGrid: Bitte PrintBedGrid_Unlit als Line Material zuweisen.");
            return;
        }

        GameObject child = new GameObject(objectName);
        child.transform.SetParent(transform, false);
        generatedObjects.Add(child);

        Mesh mesh = new Mesh { name = objectName + " Mesh" };
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        generatedMeshes.Add(mesh);
        child.AddComponent<MeshFilter>().sharedMesh = mesh;

        // Jede Linienart hat ihr eigenes Material, sodass Minor/Major-Farben
        // auch mit URP/Unlit (ohne Vertex-Color-Unterstützung) funktionieren.
        Material material = lineMaterial != null ? new Material(lineMaterial) : new Material(shader);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        else material.color = color;
        generatedMaterials.Add(material);

        MeshRenderer renderer = child.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    [ContextMenu("Clear Grid")]
    public void ClearGrid()
    {
        // Auch alte LineRenderer-Kinder entfernen, falls das Skript im laufenden
        // Play-Modus ersetzt oder das Raster neu aufgebaut wurde.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (child.name == "MajorLine" || child.name == "MinorLine" ||
                child.name == "Grid - Major" || child.name == "Grid - Minor")
            {
                child.SetActive(false);
                SafeDestroy(child);
            }
        }
        foreach (Mesh mesh in generatedMeshes) if (mesh != null) SafeDestroy(mesh);
        foreach (Material material in generatedMaterials) if (material != null) SafeDestroy(material);
        generatedObjects.Clear();
        generatedMeshes.Clear();
        generatedMaterials.Clear();
    }

    private static void SafeDestroy(Object obj)
    {
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    private void OnDestroy()
    {
        ClearGrid();
    }
}
