using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Color picker HSV a schermo per cambiare il colore di WhiteboardMarker.
///
/// Setup nel Inspector:
///   - Crea un Canvas (Screen Space - Overlay) con un pannello figlio.
///   - Assegna i riferimenti nel Inspector come indicato sotto.
///   - Collega il tuo WhiteboardMarker al campo "marker".
///
/// Gerarchia Canvas consigliata:
///   Canvas
///   └── ColorPickerPanel          ← assegna a "pickerPanel"
///       ├── PreviewImage          ← assegna a "colorPreview"  (Image)
///       ├── HueSlider             ← assegna a "hueSlider"     (Slider, min=0 max=1)
///       ├── SaturationSlider      ← assegna a "satSlider"     (Slider, min=0 max=1)
///       ├── ValueSlider           ← assegna a "valSlider"     (Slider, min=0 max=1)
///       ├── HexInputField         ← assegna a "hexInput"      (TMP_InputField, opzionale)
///       └── EraserButton          ← assegna a "eraserButton"  (Button, opzionale)
///   ToggleButton                  ← assegna a "toggleButton"  (Button, fuori dal panel)
/// </summary>
public class ColorPickerUI : MonoBehaviour
{
    [Header("Riferimenti UI")]
    [SerializeField] private GameObject      pickerPanel;
    [SerializeField] private Image           colorPreview;
    [SerializeField] private Slider          hueSlider;
    [SerializeField] private Slider          satSlider;
    [SerializeField] private Slider          valSlider;
    [SerializeField] private TMP_InputField  hexInput;      // opzionale
    [SerializeField] private Button          eraserButton;  // opzionale
    [SerializeField] private Button          toggleButton;

    [Header("Marker da controllare")]
    [SerializeField] private WhiteboardMarker marker;

    [Header("Colore iniziale")]
    [SerializeField] private Color startColor = Color.black;

    // -------------------------------------------------------
    // Stato interno
    // -------------------------------------------------------
    private float _h, _s, _v;
    private bool _suppressCallbacks; // evita loop slider→hex→slider

    // -------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------

    private void Start()
    {
        // Inizializza con il colore del marker se disponibile, altrimenti startColor
        Color initial = (marker != null) ? marker.GetColor() : startColor;
        Color.RGBToHSV(initial, out _h, out _s, out _v);

        // Collega i listener
        hueSlider.onValueChanged.AddListener(OnHSVSliderChanged);
        satSlider.onValueChanged.AddListener(OnHSVSliderChanged);
        valSlider.onValueChanged.AddListener(OnHSVSliderChanged);

        if (hexInput != null)
            hexInput.onEndEdit.AddListener(OnHexInputChanged);

        if (eraserButton != null)
            eraserButton.onClick.AddListener(OnEraserClicked);

        if (toggleButton != null)
            toggleButton.onClick.AddListener(TogglePanel);

        // Applica stato iniziale senza triggerare OnHSVSliderChanged in loop
        ApplyHSVToSliders();
        RefreshUI();

        // Pannello chiuso di default
        if (pickerPanel != null) pickerPanel.SetActive(false);
    }

    // -------------------------------------------------------
    // Toggle visibilità pannello
    // -------------------------------------------------------

    public void TogglePanel()
    {
        if (pickerPanel == null) return;
        pickerPanel.SetActive(!pickerPanel.activeSelf);
    }

    public void ShowPanel()  { if (pickerPanel != null) pickerPanel.SetActive(true);  }
    public void HidePanel()  { if (pickerPanel != null) pickerPanel.SetActive(false); }

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
        // Gomma = bianco puro
        _h = 0f; _s = 0f; _v = 1f;

        _suppressCallbacks = true;
        ApplyHSVToSliders();
        _suppressCallbacks = false;

        RefreshUI();
        ApplyToMarker();
    }

    // -------------------------------------------------------
    // Helpers
    // -------------------------------------------------------

    /// <summary>
    /// Imposta un colore dall'esterno (es. da un preset button).
    /// </summary>
    public void SetColor(Color color)
    {
        Color.RGBToHSV(color, out _h, out _s, out _v);

        _suppressCallbacks = true;
        ApplyHSVToSliders();
        _suppressCallbacks = false;

        RefreshUI();
        ApplyToMarker();
    }

    private void ApplyHSVToSliders()
    {
        hueSlider.value = _h;
        satSlider.value = _s;
        valSlider.value = _v;
    }

    private void RefreshUI()
    {
        Color current = Color.HSVToRGB(_h, _s, _v);

        if (colorPreview != null)
            colorPreview.color = current;

        if (hexInput != null)
        {
            _suppressCallbacks = true;
            hexInput.text = ColorUtility.ToHtmlStringRGB(current);
            _suppressCallbacks = false;
        }
    }

    private void ApplyToMarker()
    {
        if (marker == null) return;
        marker.SetColor(Color.HSVToRGB(_h, _s, _v));
    }
}