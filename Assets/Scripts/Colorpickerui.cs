using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ColorPickerUI : MonoBehaviour
{
    // Niente più Singleton — ogni istanza è indipendente

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

    private WhiteboardMarker _marker;
    private float _h, _s, _v;
    private bool  _suppressCallbacks;

    private void Start()
    {
        hueSlider.onValueChanged.AddListener(OnHSVSliderChanged);
        satSlider.onValueChanged.AddListener(OnHSVSliderChanged);
        valSlider.onValueChanged.AddListener(OnHSVSliderChanged);

        if (hexInput     != null) hexInput.onEndEdit.AddListener(OnHexInputChanged);
        if (eraserButton != null) eraserButton.onClick.AddListener(OnEraserClicked);
        if (toggleButton != null) toggleButton.onClick.AddListener(TogglePanel);

        Color.RGBToHSV(defaultColor, out _h, out _s, out _v);
        ApplyHSVToSliders();
        RefreshUI();

        if (pickerPanel != null) pickerPanel.SetActive(false);
    }

    // -------------------------------------------------------
    // API pubblica — ora chiamata da WhiteboardMarker direttamente
    // -------------------------------------------------------

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
    // Toggle
    // -------------------------------------------------------

    public void TogglePanel()
    {
        if (pickerPanel == null) return;
        pickerPanel.SetActive(!pickerPanel.activeSelf);
    }

    public void ShowPanel() { if (pickerPanel != null) pickerPanel.SetActive(true);  }
    public void HidePanel() { if (pickerPanel != null) pickerPanel.SetActive(false); }

    // -------------------------------------------------------
    // Callbacks UI
    // -------------------------------------------------------

    private void OnHSVSliderChanged(float _)
    {
        if (_suppressCallbacks) return;
        _h = hueSlider.value;
        _s = satSlider.value;
        _v = valSlider.value;
        RefreshUI();
        ApplyToMarker();
    }

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

    private void OnEraserClicked()
    {
        _h = 0f; _s = 0f; _v = 1f;
        _suppressCallbacks = true;
        ApplyHSVToSliders();
        _suppressCallbacks = false;
        RefreshUI();
        ApplyToMarker();
    }

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

    private void ApplyHSVToSliders()
    {
        hueSlider.value = _h;
        satSlider.value = _s;
        valSlider.value = _v;
    }

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

    private void ApplyToMarker()
    {
        if (_marker == null) return;
        _marker.SetColor(Color.HSVToRGB(_h, _s, _v));
    }
}