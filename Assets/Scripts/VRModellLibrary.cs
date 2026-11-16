using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class VRModelLibrary : MonoBehaviour
{
    [Header("VR-Oberfläche")]
    [SerializeField] private Button loadModelButton;
    [SerializeField] private GameObject modelListPanel;
    [SerializeField] private GameObject vrMenuRoot; // Gesamtes World-Space-Menue (Canvas)

    [Header("Modellimport")]
    [SerializeField] private STLImporter stlImporter;

    [Header("Verfügbare Modelle")]
    [SerializeField] private string[] modelFiles =
    {
        "cubic V2.STL",
        "cfc cc V2.STL",
        "body_centered_cubic_50mm.obj",
        "diamond_3mm.obj",
        "gyroid_3mm.obj"
    };

    [Header("Modellauswahl")]
    public UnityEvent<string> onModelSelected;

    private void Start()
    {
        if (loadModelButton == null || modelListPanel == null)
        {
            Debug.LogError(
                "VRModelLibrary: UI-Referenzen fehlen!"
            );
            return;
        }

        // Bestehenden Button zum Öffnen verwenden
        loadModelButton.onClick.AddListener(ToggleLibrary);

        // Modellliste automatisch erzeugen
        CreateModelButtons();

        // Die Bibliothek zunächst ausblenden
        modelListPanel.SetActive(false);
    }

    public void ToggleLibrary()
    {
        if (modelListPanel == null)
            return;

        bool isOpen = modelListPanel.activeSelf;
        modelListPanel.SetActive(!isOpen);
    }

    private void CreateModelButtons()
    {
        for (int i = 0; i < modelFiles.Length; i++)
        {
            string fileName = modelFiles[i];

            // Neues UI-Objekt erstellen
            GameObject buttonObject = new GameObject(
                "Model_" + i,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button)
            );

            buttonObject.transform.SetParent(
                modelListPanel.transform,
                false
            );

            // Position und Größe
            RectTransform rect =
                buttonObject.GetComponent<RectTransform>();

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            rect.sizeDelta = new Vector2(520, 60);
            rect.anchoredPosition =
            new Vector2(0, 135 - i * 72);

            // Hintergrund
            Image image = buttonObject.GetComponent<Image>();

            image.color = new Color(
                0.13f, 0.20f, 0.30f, 1f
            );

            // Button konfigurieren
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor =
                new Color(0.30f, 0.65f, 1f);
            colors.pressedColor =
                new Color(0.15f, 0.45f, 0.85f);
            colors.selectedColor =
                new Color(0.30f, 0.65f, 1f);

            button.colors = colors;

            // Text-Objekt erstellen
            GameObject textObject = new GameObject(
                "Text",
                typeof(RectTransform),
                typeof(TextMeshProUGUI)
            );

            textObject.transform.SetParent(
                buttonObject.transform,
                false
            );

            RectTransform textRect =
                textObject.GetComponent<RectTransform>();

            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            TextMeshProUGUI label =
                textObject.GetComponent<TextMeshProUGUI>();

            label.text = fileName;
            label.fontSize = 22;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = true;
            label.fontSizeMin = 12;
            label.fontSizeMax = 22;
            label.raycastTarget = false;

            // Klick-Event
            button.onClick.AddListener(
                () => SelectModel(fileName)
            );
        }
    }

    
     public void SelectModel(string fileName)
{
    Debug.Log("VR-Modell ausgewählt: " + fileName);

    if (stlImporter == null)
    {
        Debug.LogError(
            "VRModelLibrary: STLImporter ist nicht zugewiesen!"
        );
        return;
    }

    // Vorhandenen STL-/OBJ-Importer verwenden.
    stlImporter.LoadModel(fileName);

    // Nur das Menue ausblenden; der Importer und das XR-Rig laufen weiter.
    if (modelListPanel != null)
        modelListPanel.SetActive(false);

    if (vrMenuRoot != null)
        vrMenuRoot.SetActive(false);
    else
        Debug.LogWarning("VRModelLibrary: VR Menu Root fehlt. Nur die Modellliste wurde geschlossen.");
}

}
