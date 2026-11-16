using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Zeigt die Schnittlinien eines Meshes mit einer horizontalen Ebene.
/// Unity Y ist die Hoehenachse, 1 Unity-Einheit = 1 Meter.
/// Keine Druckbahnen, keine geschlossenen Flaechen und kein G-Code.
/// </summary>
public class SlicingManager : MonoBehaviour
{
    [Header("Verknuepfung")]
    public PrintBedManager bedManager;

    [Header("Slicing")]
    [Min(0.05f)] public float layerHeightMm = 1f;
    public Color contourColor = new Color(1f, 0.45f, 0.12f, 1f);
    public bool showContours = true;

    // 0 = Gesamtmodell, 1 = 3D-Aufbau, 2 = 2D-Schnitt
    [Header("Ansichtsmodus")]
    [Range(0, 2)] public int viewMode = 0;
    public Color clippedModelColor = new Color(0.23f, 0.64f, 0.96f, 1f);
    [Tooltip("Die Game-Kamera, fuer die Draufsicht (optional).")]
    public Camera viewCamera;

    private readonly Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();
    private readonly Dictionary<Renderer, bool> originalVisibility = new Dictionary<Renderer, bool>();
    private Material clippingMaterial;
    private int appliedMode = -1;
    private Camera managedCamera;
    private Vector3 savedCamPosition;
    private Quaternion savedCamRotation;
    private bool savedOrthographic;
    private float savedOrthographicSize;
    private bool cameraOverridden;

    private GameObject activeModel;
    private MeshFilter[] sourceFilters = new MeshFilter[0];
    private readonly List<Vector3> segments = new List<Vector3>();
    private readonly List<int> segmentIndices = new List<int>();
    private GameObject contourObject;
    private Mesh contourMesh;
    private Material contourMaterial;
    private int layerCount;
    private int selectedLayer;
    private float lowestY;
    private float modelHeight;
    private Vector3 previousPosition;
    private Quaternion previousRotation;
    private Vector3 previousScale;
    private float previousLayerHeight;
    private GUIStyle panelStyle;
    private GUIStyle titleStyle;
    private GUIStyle labelStyle;
    private string status = "Warte auf Modell ...";

    private void Awake()
    {
        contourObject = new GameObject("Slicing - Schnittkonturen");
        contourObject.transform.SetParent(transform, false);
        contourObject.transform.position = Vector3.zero;
        contourObject.transform.rotation = Quaternion.identity;
        contourObject.transform.localScale = Vector3.one;

        var filter = contourObject.AddComponent<MeshFilter>();
        var renderer = contourObject.AddComponent<MeshRenderer>();
        contourMesh = new Mesh { name = "Horizontale Schnittlinien" };
        contourMesh.indexFormat = IndexFormat.UInt32;
        filter.sharedMesh = contourMesh;

        Shader shader = Shader.Find("LayerLab/SliceLines");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
            Debug.LogWarning("Slicing: Shader 'LayerLab/SliceLines' fehlt. Konturen koennen vom Modell verdeckt werden.");
        }
        Shader clipShader = Shader.Find("LayerLab/SliceClip");
        if (clipShader != null)
        {
            clippingMaterial = new Material(clipShader);
            clippingMaterial.SetColor("_BaseColor", clippedModelColor);
        }
        else Debug.LogWarning("Slicing: SliceClip.shader fehlt. Die 3D-Aufbauansicht ist nicht verfuegbar.");

        if (shader != null)
        {
            contourMaterial = new Material(shader);
            contourMaterial.color = contourColor;
            renderer.sharedMaterial = contourMaterial;
        }
        else Debug.LogError("Slicing: Kein geeigneter Shader gefunden.");
    }

    private void Update()
    {
        GameObject next = bedManager != null ? bedManager.currentModel : null;
        if (next != activeModel)
        {
            RestoreRendererState();
            RestoreCamera();
            activeModel = next;
            appliedMode = -1;
            LoadModel();
        }
        if (activeModel == null) return;

        float clampedHeight = Mathf.Max(0.05f, layerHeightMm);
        bool moved = activeModel.transform.position != previousPosition ||
                     activeModel.transform.rotation != previousRotation ||
                     activeModel.transform.lossyScale != previousScale;
        if (moved || !Mathf.Approximately(previousLayerHeight, clampedHeight))
        {
            RefreshBounds();
            UpdateContour();
            if (viewMode == 2) UpdateTopCamera();
        }
        if (appliedMode != viewMode)
            ApplyViewMode();
    }

    /// <summary>
    /// Wird vom STLImporter aufgerufen, nachdem ein neues Modell auf dem
    /// Druckbett platziert wurde. Aktualisiert die Schichten und wendet
    /// den aktuell ausgewaehlten Ansichtsmodus erneut an.
    /// </summary>
    public void ReloadCurrentModel()
    {
        // Zustand des vorherigen Modells wiederherstellen.
        RestoreRendererState();
        RestoreCamera();

        activeModel = bedManager != null ? bedManager.currentModel : null;
        appliedMode = -1;

        // LoadModel setzt den Schichtregler zurueck, berechnet die
        // Geometrie neu und aktiviert den aktuellen Ansichtsmodus.
        LoadModel();

        Debug.Log("Slicing: Modellwechsel verarbeitet: " +
            (activeModel != null ? activeModel.name : "Kein Modell"));
    }

    private void LoadModel()
    {
        contourMesh.Clear();
        if (activeModel == null)
        {
            sourceFilters = new MeshFilter[0];
            layerCount = 0;
            status = "Warte auf Modell ...";
            return;
        }
        sourceFilters = activeModel.GetComponentsInChildren<MeshFilter>();
        selectedLayer = 0;
        RefreshBounds();
        UpdateContour();
        ApplyViewMode();
    }

    private void RefreshBounds()
    {
        previousLayerHeight = Mathf.Max(0.05f, layerHeightMm);
        previousPosition = activeModel.transform.position;
        previousRotation = activeModel.transform.rotation;
        previousScale = activeModel.transform.lossyScale;

        Renderer[] renderers = activeModel.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            layerCount = 0;
            status = "Modell hat keine sichtbare Geometrie";
            return;
        }
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        lowestY = bounds.min.y;
        modelHeight = bounds.size.y;
        float step = previousLayerHeight * 0.001f;
        layerCount = Mathf.Clamp(Mathf.CeilToInt(modelHeight / step), 1, 100000);
        selectedLayer = Mathf.Clamp(selectedLayer, 0, layerCount - 1);
    }

    private void UpdateContour()
    {
        contourMesh.Clear();
        if (clippingMaterial != null && layerCount > 0)
            clippingMaterial.SetFloat("_CutoffY", GetPlaneY());
        if (!showContours || layerCount == 0 || sourceFilters.Length == 0) return;

        float heightMeters = previousLayerHeight * 0.001f;
        float planeY = lowestY + Mathf.Min(modelHeight - 0.000001f,
            (selectedLayer + 0.5f) * heightMeters);
        segments.Clear();
        segmentIndices.Clear();

        foreach (MeshFilter filter in sourceFilters)
        {
            if (filter == null || filter.sharedMesh == null || !filter.gameObject.activeInHierarchy) continue;
            Mesh mesh = filter.sharedMesh;
            if (!mesh.isReadable)
            {
                Debug.LogWarning("Slicing: Mesh nicht lesbar: " + mesh.name);
                continue;
            }
            Vector3[] vertices = mesh.vertices;
            Matrix4x4 localToWorld = filter.transform.localToWorldMatrix;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                // STL/OBJ-Importer erzeugen Dreiecksmeshes.
                if (mesh.GetTopology(submesh) != MeshTopology.Triangles) continue;
                int[] triangles = mesh.GetTriangles(submesh);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    Vector3 a = localToWorld.MultiplyPoint3x4(vertices[triangles[i]]);
                    Vector3 b = localToWorld.MultiplyPoint3x4(vertices[triangles[i + 1]]);
                    Vector3 c = localToWorld.MultiplyPoint3x4(vertices[triangles[i + 2]]);
                    AddTriangleSection(a, b, c, planeY);
                }
            }
        }

        if (segments.Count > 0)
        {
            contourMesh.SetVertices(segments);
            contourMesh.SetIndices(segmentIndices, MeshTopology.Lines, 0);
            // Linien sollen auch bei ungünstigem Blickwinkel nicht ausgeblendet werden.
            contourMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
        }
        status = string.Format("Schicht {0}/{1} | Hoehe: {2:F2} mm | Linien: {3}",
            selectedLayer + 1, layerCount, (planeY - lowestY) * 1000f, segments.Count / 2);
    }

    private float GetPlaneY()
    {
        float step = Mathf.Max(0.05f, layerHeightMm) * 0.001f;
        return lowestY + Mathf.Min(Mathf.Max(0f, modelHeight - 0.000001f),
            (selectedLayer + 0.5f) * step);
    }

    private void RestoreRendererState()
    {
        foreach (var pair in originalMaterials)
        {
            if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
        }
        foreach (var pair in originalVisibility)
        {
            if (pair.Key != null) pair.Key.enabled = pair.Value;
        }
        originalMaterials.Clear();
        originalVisibility.Clear();
        appliedMode = -1;
    }

    private void ApplyViewMode()
    {
        if (activeModel == null) return;
        RestoreRendererState();
        viewMode = Mathf.Clamp(viewMode, 0, 2);
        Renderer[] renderers = activeModel.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            originalMaterials[renderer] = renderer.sharedMaterials;
            originalVisibility[renderer] = renderer.enabled;
            if (viewMode == 1 && clippingMaterial != null)
            {
                int count = Mathf.Max(1, renderer.sharedMaterials.Length);
                Material[] materials = new Material[count];
                for (int i = 0; i < materials.Length; i++) materials[i] = clippingMaterial;
                renderer.sharedMaterials = materials;
            }
            else if (viewMode == 2)
            {
                renderer.enabled = false;
            }
        }
        if (clippingMaterial != null)
        {
            clippingMaterial.SetFloat("_CutoffY", GetPlaneY());
            clippingMaterial.SetColor("_BaseColor", clippedModelColor);
        }
        if (viewMode == 2) UpdateTopCamera();
        else RestoreCamera();
        appliedMode = viewMode;
    }

    private void UpdateTopCamera()
    {
        Camera cameraToUse = viewCamera != null ? viewCamera : Camera.main;
        if (cameraToUse == null) return;
        if (!cameraOverridden)
        {
            managedCamera = cameraToUse;
            savedCamPosition = cameraToUse.transform.position;
            savedCamRotation = cameraToUse.transform.rotation;
            savedOrthographic = cameraToUse.orthographic;
            savedOrthographicSize = cameraToUse.orthographicSize;
            cameraOverridden = true;
        }
        Bounds bounds = GetCurrentBounds();
        float aspect = Mathf.Max(0.1f, cameraToUse.aspect);
        float extent = Mathf.Max(bounds.extents.z, bounds.extents.x / aspect);
        cameraToUse.orthographic = true;
        cameraToUse.orthographicSize = Mathf.Max(0.055f, extent * 1.3f);
        cameraToUse.transform.position = new Vector3(bounds.center.x,
            bounds.max.y + Mathf.Max(0.8f, bounds.size.y * 2f), bounds.center.z);
        cameraToUse.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    private Bounds GetCurrentBounds()
    {
        Renderer[] renderers = activeModel.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(activeModel.transform.position, Vector3.one * 0.1f);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private void RestoreCamera()
    {
        if (!cameraOverridden || managedCamera == null) return;
        managedCamera.transform.position = savedCamPosition;
        managedCamera.transform.rotation = savedCamRotation;
        managedCamera.orthographic = savedOrthographic;
        managedCamera.orthographicSize = savedOrthographicSize;
        cameraOverridden = false;
        managedCamera = null;
    }

    private void AddTriangleSection(Vector3 a, Vector3 b, Vector3 c, float y)
    {
        Vector3 p = default;
        Vector3 q = default;
        int found = 0;
        Cross(a, b, y, ref p, ref q, ref found);
        Cross(b, c, y, ref p, ref q, ref found);
        Cross(c, a, y, ref p, ref q, ref found);
        if (found != 2 || (p - q).sqrMagnitude < 1e-14f) return;
        int first = segments.Count;
        segments.Add(p);
        segments.Add(q);
        segmentIndices.Add(first);
        segmentIndices.Add(first + 1);
    }

    private static void Cross(Vector3 a, Vector3 b, float y,
        ref Vector3 p, ref Vector3 q, ref int found)
    {
        // Halboffenes Intervall verhindert doppelte Eckpunkt-Treffer.
        bool aBelow = a.y < y;
        bool bBelow = b.y < y;
        if (aBelow == bBelow || found == 2) return;
        float t = (y - a.y) / (b.y - a.y);
        Vector3 point = Vector3.LerpUnclamped(a, b, t);
        if (found == 0) p = point;
        else q = point;
        found++;
    }

    private void OnGUI()
    {
        if (panelStyle == null)
        {
            panelStyle = new GUIStyle(GUI.skin.box);
            panelStyle.normal.background = Texture2D.whiteTexture;
            panelStyle.normal.textColor = Color.white;
            panelStyle.padding = new RectOffset(12, 12, 10, 10);
            titleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = new Color(0.45f, 0.8f, 1f);
            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.normal.textColor = Color.white;
        }
        // Unter dem 3D-Fenster; linke Bibliothek und rechter Transform-Dialog bleiben frei.
        Rect r = new Rect(Mathf.Max(5f, Screen.width / 2f - 215f), Screen.height - 188f, 430f, 178f);
        Color was = GUI.color;
        GUI.color = new Color(0.09f, 0.13f, 0.21f, 0.97f);
        GUI.Box(r, GUIContent.none, panelStyle);
        GUI.color = was;
        GUILayout.BeginArea(new Rect(r.x + 10f, r.y + 6f, r.width - 20f, r.height - 12f));
        GUILayout.Label("LayerLab | Schichtvorschau", titleStyle);
        GUILayout.Label(status, labelStyle);
        int nextMode = GUILayout.SelectionGrid(viewMode,
            new [] { "Gesamt + Schnitt", "3D-Aufbau", "2D-Schnitt" }, 3);
        if (nextMode != viewMode) { viewMode = nextMode; ApplyViewMode(); }
        bool show = GUILayout.Toggle(showContours, "Schnittlinien anzeigen");
        if (show != showContours) { showContours = show; UpdateContour(); }

        GUILayout.BeginHorizontal();
        GUILayout.Label("Schichthoehe (mm)", labelStyle, GUILayout.Width(132));
        string[] choices = { "0.2", "0.5", "1.0", "2.0" };
        int chosen = Mathf.Approximately(layerHeightMm, 0.2f) ? 0 :
                     Mathf.Approximately(layerHeightMm, 0.5f) ? 1 :
                     Mathf.Approximately(layerHeightMm, 2.0f) ? 3 : 2;
        int newChoice = GUILayout.SelectionGrid(chosen, choices, 4);
        if (newChoice != chosen)
        {
            layerHeightMm = new float[] { 0.2f, 0.5f, 1f, 2f }[newChoice];
            if (activeModel != null) { RefreshBounds(); UpdateContour(); }
        }
        GUILayout.EndHorizontal();

        if (layerCount > 0)
        {
            int next = Mathf.RoundToInt(GUILayout.HorizontalSlider(selectedLayer, 0, layerCount - 1));
            if (next != selectedLayer) { selectedLayer = next; UpdateContour(); }
        }
        GUILayout.EndArea();
    }

    private void OnDestroy()
    {
        RestoreRendererState();
        RestoreCamera();
        if (contourMesh != null) Destroy(contourMesh);
        if (contourMaterial != null) Destroy(contourMaterial);
        if (clippingMaterial != null) Destroy(clippingMaterial);
    }
}
