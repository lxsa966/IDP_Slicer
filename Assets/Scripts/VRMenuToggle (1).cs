using UnityEngine;
using UnityEngine.XR;

#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

// Muss auf einem dauerhaft aktiven GameObject liegen, nicht auf dem Canvas.
public class VRMenuToggle : MonoBehaviour
{
    [Header("Menue")]
    [SerializeField] private GameObject vrMenuRoot;

    [Header("Controller")]
    [Tooltip("X-Taste am linken Quest-Controller (Primary Button).")]
    [SerializeField] private XRNode controllerHand = XRNode.LeftHand;

    private bool lastButtonPressed;

    private void Update()
    {
        bool pressed = false;

        // Eindeutige XR-Typen verhindern Namenskonflikte mit dem Input System.
        UnityEngine.XR.InputDevice device =
            UnityEngine.XR.InputDevices.GetDeviceAtXRNode(controllerHand);

        if (device.isValid)
        {
            device.TryGetFeatureValue(
                UnityEngine.XR.CommonUsages.primaryButton,
                out pressed
            );
        }

        // Einmal pro Tastendruck umschalten (nicht in jedem Frame).
        bool toggleRequested = pressed && !lastButtonPressed;
        lastButtonPressed = pressed;

#if UNITY_EDITOR
        // Test im Unity-Editor: M-Taste.
        if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
        {
            toggleRequested = true;
        }
#endif

        if (toggleRequested)
        {
            ToggleMenu();
        }
    }

    public void ToggleMenu()
    {
        if (vrMenuRoot == null)
        {
            Debug.LogWarning("VRMenuToggle: VR Menu Root ist nicht zugewiesen.");
            return;
        }

        vrMenuRoot.SetActive(!vrMenuRoot.activeSelf);
    }
}
