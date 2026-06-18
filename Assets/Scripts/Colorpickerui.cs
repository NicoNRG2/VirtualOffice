using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Each workstation has its own independent ColorPickerUI instance.
// There is no singleton — multiple pickers can coexist in the scene simultaneously.
public class ColorPickerUI : MonoBehaviour
{
    [Header("Riferimenti UI")]
    [SerializeField] private GameObject     pickerPanel;
    [SerializeField] private Image          colorPreview;
    [SerializeField] private Slider         hueSlider;
    [SerializeField] private Slider         satSlider;
    [SerializeField] private Slider         valSlider;
    [SerializeField] private TMP_InputField hexInput;
    [SerializeField] private Button         eraserButton;
    [SerializeField] private Button         toggleButton;

    [Header("Colore di default")]
    [SerializeField] private Color defaultColor = Color.black;

    // The marker currently controlled by this picker. Null when no marker is held.
    private WhiteboardMarker _marker;

    // Internal HSV representation — converted to RGB only when applying to the marker or UI.
    private float _h, _s, _v;

    // Guard flag: prevents UI callbacks from firing while the code itself updates sliders/fields,
    // which would otherwise cause infinite feedback loops.
    private bool  _suppressCallbacks;

    private void Start()
    {
        // Wire up all UI controls to their respective callbacks.
        hueSlider.onValueChanged.AddListener(OnHSVSliderChanged);
        satSlider.onValueChanged.AddListener(OnHSVSliderChanged);
        valSlider.onValueChanged.AddListener(OnHSVSliderChanged);

        if (hexInput     != null) hexInput.onEndEdit.AddListener(OnHexInputChanged);
        if (eraserButton != null) eraserButton.onClick.AddListener(OnEraserClicked);
        if (toggleButton != null) toggleButton.onClick.AddListener(TogglePanel);

        // Initialise UI to the default color without triggering callbacks.
        Color.RGBToHSV(defaultColor, out _h, out _s, out _v);
        ApplyHSVToSliders();
        RefreshUI();

        if (pickerPanel != null) pickerPanel.SetActive(false);
    }

    // -------------------------------------------------------
    // Public API — called by WhiteboardMarker on grab/release
    // -------------------------------------------------------

    // Associates a marker with this picker and syncs the UI to the marker's current color.
    public void RegisterMarker(WhiteboardMarker marker)
    {
        _marker = marker;
        if (_marker == null) return;

        Color.RGBToHSV(_marker.GetColor(), out _h, out _s, out _v);
        _suppressCallbacks = true;
        ApplyHSVToSliders();
        _suppressCallbacks = false;
        RefreshUI();

        Debug.Log($"[ColorPickerUI] ({name}) Marker registrato: {marker.name}");
    }

    public void UnregisterMarker()
    {
        _marker = null;
        Debug.Log($"[ColorPickerUI] ({name}) Marker deregistrato.");
    }

    // -------------------------------------------------------
    // Panel visibility
    // -------------------------------------------------------

    public void TogglePanel()
    {
        if (pickerPanel == null) return;
        pickerPanel.SetActive(!pickerPanel.activeSelf);
    }

    public void ShowPanel() { if (pickerPanel != null) pickerPanel.SetActive(true);  }
    public void HidePanel() { if (pickerPanel != null) pickerPanel.SetActive(false); }

    // -------------------------------------------------------
    // UI callbacks
    // -------------------------------------------------------

    // Fired whenever any HSV slider changes value; reads all three sliders together
    // to rebuild the color and push it to the marker.
    private void OnHSVSliderChanged(float _)
    {
        if (_suppressCallbacks) return;
        _h = hueSlider.value;
        _s = satSlider.value;
        _v = valSlider.value;
        RefreshUI();
        ApplyToMarker();
    }

    // Parses a 6- or 8-digit hex string entered by the user and converts it to HSV.
    private void OnHexInputChanged(string hex)
    {
        if (_suppressCallbacks) return;
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6 && hex.Length != 8) return;

        if (ColorUtility.TryParseHtmlString("#" + hex, out Color parsed))
        {
            Color.RGBToHSV(parsed, out _h, out _s, out _v);
            _suppressCallbacks = true;
            ApplyHSVToSliders();
            _suppressCallbacks = false;
            RefreshUI();
            ApplyToMarker();
        }
    }

    // The eraser sets the color to pure white (H=0, S=0, V=1),
    // effectively painting over existing strokes with the background color.
    private void OnEraserClicked()
    {
        _h = 0f; _s = 0f; _v = 1f;
        _suppressCallbacks = true;
        ApplyHSVToSliders();
        _suppressCallbacks = false;
        RefreshUI();
        ApplyToMarker();
    }

    // External entry point — allows other scripts to drive the picker programmatically.
    public void SetColor(Color color)
    {
        Color.RGBToHSV(color, out _h, out _s, out _v);
        _suppressCallbacks = true;
        ApplyHSVToSliders();
        _suppressCallbacks = false;
        RefreshUI();
        ApplyToMarker();
    }

    // -------------------------------------------------------
    // Helpers
    // -------------------------------------------------------

    // Pushes the internal HSV values back to the three sliders (suppression must be
    // managed by the caller to avoid callback loops).
    private void ApplyHSVToSliders()
    {
        hueSlider.value = _h;
        satSlider.value = _s;
        valSlider.value = _v;
    }

    // Updates the color preview image and the hex input field to match current HSV.
    private void RefreshUI()
    {
        Color current = Color.HSVToRGB(_h, _s, _v);
        if (colorPreview != null) colorPreview.color = current;
        if (hexInput != null)
        {
            _suppressCallbacks = true;
            hexInput.text = ColorUtility.ToHtmlStringRGB(current);
            _suppressCallbacks = false;
        }
    }

    // Converts the current HSV state to RGB and sends it to the registered marker.
    private void ApplyToMarker()
    {
        if (_marker == null) return;
        _marker.SetColor(Color.HSVToRGB(_h, _s, _v));
    }
}