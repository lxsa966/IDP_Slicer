using UnityEngine;

/// <summary>
/// Simple runtime model transform panel for the LayerLab prototype.
/// Unity units are meters; the UI displays millimeters.
/// Attach to its own empty GameObject and assign PrintBedManager.
/// </summary>
public class ModelTransformUI : MonoBehaviour
{
    [Header("Reference")]
    public PrintBedManager bedManager;

    [Header("Position limits (mm)")]
    public float maxHorizontalOffsetMm = 100f;
    public float maxHeightMm = 100f;

    private GameObject selectedModel;
    private float offsetXMm;
    private float offsetYMm;
    private float offsetZMm;
    private float angleXMm; // angles are in degrees (field names kept separate from UI labels)
    private float angleYMm;
    private float angleZMm;

    private GUIStyle panelStyle;
    private GUIStyle headerStyle;
    private GUIStyle labelStyle;
    private GUIStyle valueStyle;
    private Texture2D panelTexture;

    private void Update()
    {
        GameObject current = bedManager != null ? bedManager.currentModel : null;
        if (current == selectedModel) return;

        selectedModel = current;
        ResetValues();
        // New files are already centered by STLImporter. Do not move them here.
    }

    private void ResetValues()
    {
        offsetXMm = offsetYMm = offsetZMm = 0f;
        angleXMm = angleYMm = angleZMm = 0f;
    }

    private void ApplyTransform()
    {
        if (selectedModel == null || bedManager == null) return;

        // Rebuild from a stable rotation, so sliders don't accumulate changes.
        selectedModel.transform.rotation = Quaternion.Euler(
            angleXMm, angleYMm, angleZMm
        );

        // Center the rotated bounding box and set its bottom on the bed.
        bedManager.CenterModel();

        // UI works in millimeters; Unity uses meters.
        selectedModel.transform.position += new Vector3(
            offsetXMm * 0.001f,
            offsetYMm * 0.001f,
            offsetZMm * 0.001f
        );
    }

    private void OnGUI()
    {
        CreateStylesIfNeeded();

        const float width = 310f;
        float x = Mathf.Max(10f, Screen.width - width - 12f);
        Rect panelRect = new Rect(x, 12f, width, 414f);
        GUI.Box(panelRect, GUIContent.none, panelStyle);

        GUILayout.BeginArea(new Rect(x + 14f, 22f, width - 28f, 392f));
        GUILayout.Label("LayerLab | Modellsteuerung", headerStyle);
        GUILayout.Space(6f);

        if (selectedModel == null)
        {
            GUILayout.Label("Bitte zuerst ein STL-/OBJ-Modell laden.", labelStyle);
            GUILayout.EndArea();
            return;
        }

        GUILayout.Label(selectedModel.name, labelStyle);
        GUILayout.Space(8f);
        GUILayout.Label("POSITION (mm)", headerStyle);

        bool changed = false;
        changed |= DrawSlider("X – links/rechts", ref offsetXMm, -maxHorizontalOffsetMm, maxHorizontalOffsetMm);
        changed |= DrawSlider("Y – Hoehe", ref offsetYMm, 0f, maxHeightMm);
        changed |= DrawSlider("Z – vor/zurueck", ref offsetZMm, -maxHorizontalOffsetMm, maxHorizontalOffsetMm);

        GUILayout.Space(8f);
        GUILayout.Label("ROTATION (Grad)", headerStyle);
        changed |= DrawSlider("X – kippen", ref angleXMm, 0f, 360f);
        changed |= DrawSlider("Y – drehen", ref angleYMm, 0f, 360f);
        changed |= DrawSlider("Z – kippen", ref angleZMm, 0f, 360f);

        GUILayout.Space(7f);
        if (GUILayout.Button("Position und Rotation zuruecksetzen", GUILayout.Height(28f)))
        {
            ResetValues();
            changed = true;
        }
        GUILayout.EndArea();

        if (changed) ApplyTransform();
    }

    private bool DrawSlider(string caption, ref float value, float min, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(caption, labelStyle, GUILayout.Width(178f));
        GUILayout.Label(value.ToString("F1"), valueStyle, GUILayout.Width(66f));
        GUILayout.EndHorizontal();

        float next = GUILayout.HorizontalSlider(value, min, max);
        bool changed = !Mathf.Approximately(next, value);
        value = next;
        return changed;
    }

    private void CreateStylesIfNeeded()
    {
        if (panelStyle != null) return;

        panelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        panelTexture.SetPixel(0, 0, new Color(0.06f, 0.09f, 0.14f, 0.95f));
        panelTexture.Apply();

        panelStyle = new GUIStyle(GUI.skin.box);
        panelStyle.normal.background = panelTexture;

        headerStyle = new GUIStyle(GUI.skin.label);
        headerStyle.normal.textColor = new Color(0.45f, 0.8f, 1f);
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.fontSize = 13;

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.normal.textColor = Color.white;
        labelStyle.fontSize = 12;

        valueStyle = new GUIStyle(labelStyle);
        valueStyle.alignment = TextAnchor.MiddleRight;
    }

    private void OnDestroy()
    {
        if (panelTexture != null) Destroy(panelTexture);
    }
}
