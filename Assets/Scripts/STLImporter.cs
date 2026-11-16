using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

// Keinen weiteren STLImporter anlegen: vorhandene Datei vollständig ersetzen.
public class STLImporter : MonoBehaviour
{
    [Header("Startmodell (Name inklusive Dateiendung)")]
    public string fileName = "cubic V2.STL";

    [Header("Verknuepfungen")]
    public PrintBedManager bedManager;
    public SlicingManager slicingManager;
    public GameObject testModel;

    [Header("Modellbibliothek im Ordner StreamingAssets")]
    public string[] modelFiles =
    {
        "cubic V2.STL",
        "cfc cc V2.STL",
        "body_centered_cubic_50mm.obj",
        "diamond_3mm.obj",
        "gyroid_3mm.obj"
    };

    [Header("Darstellung")]
    public Color modelColor = new Color(0.23f, 0.64f, 0.96f);

    private GameObject importedModel;
    // Speichert das Material, das diesem Importer gehoert.
    // Nicht das aktuell angezeigte Material des Renderers zerstoeren:
    // im 3D-Aufbau ist das eventuell das SliceClip-Material!
    private Material importedMaterial;
    private bool loading;
    private string status = "Bereit";
    private const float MmToMeters = 0.001f;

    private void Start()
    {
        LoadModel(fileName);
    }

    public void LoadModel(string selectedFile)
    {
        if (loading || string.IsNullOrWhiteSpace(selectedFile))
            return;

        // Nur reine Dateinamen aus der eigenen Modellbibliothek erlauben.
        if (Path.GetFileName(selectedFile) != selectedFile)
        {
            Debug.LogError("Ungueltiger Dateiname: " + selectedFile);
            return;
        }
        StartCoroutine(LoadModelRoutine(selectedFile));
    }

    private IEnumerator LoadModelRoutine(string selectedFile)
    {
        loading = true;
        status = "Lade: " + selectedFile;

        string path;
#if UNITY_EDITOR || UNITY_STANDALONE
        string localPath = Path.Combine(Application.streamingAssetsPath, selectedFile);
        path = new Uri(Path.GetFullPath(localPath)).AbsoluteUri;
#else
        path = Application.streamingAssetsPath.TrimEnd('/') + "/" +
               UnityWebRequest.EscapeURL(selectedFile);
#endif
        Debug.Log("Modelldatei: " + path);

        using (UnityWebRequest request = UnityWebRequest.Get(path))
        {
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                status = "Laden fehlgeschlagen: " + selectedFile;
                Debug.LogError(status + " | " + request.error);
                loading = false;
                yield break;
            }

            Mesh mesh = null;
            try
            {
                byte[] bytes = request.downloadHandler.data;
                string extension = Path.GetExtension(selectedFile).ToLowerInvariant();
                if (extension == ".stl") mesh = ParseBinarySTL(bytes);
                else if (extension == ".obj") mesh = ParseOBJ(bytes);
                else throw new Exception("Nur binaere STL und OBJ werden unterstuetzt.");

                ReplaceModel(mesh, selectedFile);
                fileName = selectedFile;
                status = "Geladen: " + selectedFile;
                Debug.Log(status + " | Dreiecke: " + mesh.triangles.Length / 3);
            }
            catch (Exception ex)
            {
                if (mesh != null) Destroy(mesh);
                status = "Importfehler: " + selectedFile;
                Debug.LogError(status + " | " + ex.Message);
            }
        }
        loading = false;
    }

    private void ReplaceModel(Mesh mesh, string selectedFile)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            throw new Exception("URP Unlit Shader nicht gefunden.");

        GameObject newObject = new GameObject("Import: " + selectedFile);
        newObject.AddComponent<MeshFilter>().sharedMesh = mesh;

        Material newMaterial = new Material(shader);
        newMaterial.color = modelColor;
        newObject.AddComponent<MeshRenderer>().sharedMaterial = newMaterial;

        GameObject oldObject = importedModel;
        Material oldMaterial = importedMaterial;
        importedModel = newObject;
        importedMaterial = newMaterial;

        if (testModel != null) testModel.SetActive(false);

        if (bedManager != null)
        {
            bedManager.currentModel = newObject;
            bedManager.CenterModel();

            // Erst jetzt ist das Modell korrekt positioniert.
            // Der SlicingManager soll seine alten Renderer-Zustaende
            // zuruecksetzen und den 3D-Aufbau neu initialisieren.
            if (slicingManager != null && slicingManager.isActiveAndEnabled)
                slicingManager.ReloadCurrentModel();
            else
                Debug.LogWarning("STLImporter: Kein aktiver SlicingManager zugewiesen. " +
                                 "Bitte im Inspector das aktive Objekt verknuepfen.");
        }
        else Debug.LogWarning("PrintBedManager nicht zugewiesen.");

        if (oldObject != null)
        {
            MeshFilter oldFilter = oldObject.GetComponent<MeshFilter>();
            if (oldFilter != null && oldFilter.sharedMesh != null)
                Destroy(oldFilter.sharedMesh);

            // WICHTIG: Nicht oldRenderer.sharedMaterial zerstoeren!
            // Das kann im 3D-Aufbau das gemeinsame SliceClip-Material sein.
            if (oldMaterial != null)
                Destroy(oldMaterial);

            Destroy(oldObject);
        }

        Debug.Log("STLImporter: Modellwechsel abgeschlossen: " + selectedFile);
    }

    private Mesh ParseBinarySTL(byte[] data)
    {
        if (data == null || data.Length < 84) throw new Exception("STL zu klein.");
        using (MemoryStream stream = new MemoryStream(data))
        using (BinaryReader reader = new BinaryReader(stream))
        {
            reader.ReadBytes(80);
            uint count = reader.ReadUInt32();
            if (84L + 50L * count != data.Length)
                throw new Exception("Keine binaere STL-Datei oder unvollstaendig.");
            if (count > 1000000) throw new Exception("Mehr als 1 Mio. Dreiecke.");
            Vector3[] vertices = new Vector3[(int)count * 3];
            int[] triangles = new int[(int)count * 3];
            for (int i = 0; i < count; i++)
            {
                reader.ReadBytes(12); // Normale
                for (int k = 0; k < 3; k++)
                {
                    float x = reader.ReadSingle();
                    float y = reader.ReadSingle();
                    float z = reader.ReadSingle();
                    int n = i * 3 + k;
                    vertices[n] = ConvertToUnity(x, y, z);
                    triangles[n] = n;
                }
                reader.ReadUInt16();
            }
            return BuildMesh(vertices, triangles);
        }
    }

    private Mesh ParseOBJ(byte[] bytes)
    {
        // Dieser Parser unterstuetzt v/f, v/vt, v//vn, v/vt/vn,
        // positive/negative Indizes und Polygone (Fan-Triangulierung).
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        CultureInfo culture = CultureInfo.InvariantCulture;
        using (StringReader reader = new StringReader(Encoding.UTF8.GetString(bytes)))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.StartsWith("v ", StringComparison.Ordinal))
                {
                    string[] tokens = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length < 4) throw new Exception("OBJ-Vertex ungueltig.");
                    vertices.Add(ConvertToUnity(
                        float.Parse(tokens[1], culture),
                        float.Parse(tokens[2], culture),
                        float.Parse(tokens[3], culture)));
                }
                else if (line.StartsWith("f ", StringComparison.Ordinal))
                {
                    string[] tokens = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length < 4) continue;
                    int first = ParseVertexIndex(tokens[1], vertices.Count);
                    int previous = ParseVertexIndex(tokens[2], vertices.Count);
                    for (int i = 3; i < tokens.Length; i++)
                    {
                        int next = ParseVertexIndex(tokens[i], vertices.Count);
                        triangles.Add(first);
                        triangles.Add(previous);
                        triangles.Add(next);
                        previous = next;
                    }
                }
            }
        }
        if (vertices.Count == 0 || triangles.Count == 0)
            throw new Exception("OBJ enthaelt keine verwertbaren Vertices/Faces.");
        if (triangles.Count / 3 > 1000000)
            throw new Exception("Mehr als 1 Mio. Dreiecke.");
        return BuildMesh(vertices.ToArray(), triangles.ToArray());
    }

    private static int ParseVertexIndex(string token, int vertexCount)
    {
        string[] parts = token.Split('/');
        int index = int.Parse(parts[0], CultureInfo.InvariantCulture);
        int result = index > 0 ? index - 1 : vertexCount + index;
        if (index == 0 || result < 0 || result >= vertexCount)
            throw new Exception("Ungueltiger OBJ-Face-Index: " + token);
        return result;
    }

    private static Vector3 ConvertToUnity(float x, float y, float z)
    {
        // Modelle werden als Millimeter und Z-up interpretiert.
        return new Vector3(x, z, -y) * MmToMeters;
    }

    private static Mesh BuildMesh(Vector3[] vertices, int[] triangles)
    {
        Mesh mesh = new Mesh();
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

#if UNITY_EDITOR || UNITY_STANDALONE
    // Einfache Testoberflaeche fuer PC/Editor.
    // Spaeter durch ein World-Space-Canvas fuer Quest 3 ersetzen.
    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(12, 12, 270, 58 + 32 * modelFiles.Length), GUI.skin.box);
        GUILayout.Label("LayerLab | Modellbibliothek");
        GUILayout.Label(status);
        GUI.enabled = !loading;
        foreach (string name in modelFiles)
        {
            if (GUILayout.Button(name, GUILayout.Height(26))) LoadModel(name);
        }
        GUI.enabled = true;
        GUILayout.EndArea();
    }
#endif
}
