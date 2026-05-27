using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Color picker HSV a schermo per cambiare il colore di WhiteboardMarker.
/// </summary>
public class ColorPickerUI : MonoBehaviour
{
    // Singleton leggero: accessibile da PenController senza riferimento diretto
    public static ColorPickerUI Instance { get; private set; }

    [Header("Riferimenti UI")]
    [SerializeField] private GameObject     pickerPanel;
    [SerializeField] private Image          colorPreview;
    [SerializeField] private Slider         hueSlider;
    [SerializeField] private Slider         satSlider;
    [SerializeField] private Slider         valSlider;
    [SerializeField] private TMP_InputField hexInput;     // opzionale
    [SerializeField] private Button         eraserButton; // opzionale
    [SerializeField] private Button         toggleButton;

    [Header("Colore di default (nessun marker registrato)")]
    [SerializeField] private Color defaultColor = Color.black;

    // -------------------------------------------------------
    // Stato interno
    // -------------------------------------------------------
    private WhiteboardMarker _marker;   // marker attualmente in mano
    private float _h, _s, _v;
    private bool  _suppressCallbacks;

    // -------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        hueSlider.onValueChanged.AddListener(OnHSVSliderChanged);
        satSlider.onValueChanged.AddListener(OnHSVSliderChanged);
        valSlider.onValueChanged.AddListener(OnHSVSliderChanged);

        if (hexInput     != null) hexInput.onEndEdit.AddListener(OnHexInputChanged);
        if (eraserButton != null) eraserButton.onClick.AddListener(OnEraserClicked);
        if (toggleButton != null) toggleButton.onClick.AddListener(TogglePanel);

        // Stato iniziale con il colore di default
        Color.RGBToHSV(defaultColor, out _h, out _s, out _v);
        ApplyHSVToSliders();
        RefreshUI();

        if (pickerPanel != null) pickerPanel.SetActive(false);
    }

    // -------------------------------------------------------
    // API pubblica — chiamata da PenController / XRGrabInteractable
    // -------------------------------------------------------

    /// <summary>
    /// Registra il marker appena afferrato e aggiorna la UI
    /// con il suo colore corrente.
    /// </summary>
    public void RegisterMarker(WhiteboardMarker marker)
    {
        _marker = marker;

        if (_marker != null)
        {
            // Sincronizza la UI con il colore già presente sul marker
            Color.RGBToHSV(_marker.GetColor(), out _h, out _s, out _v);
            _suppressCallbacks = true;
            ApplyHSVToSliders();
            _suppressCallbacks = false;
            RefreshUI();
        }

        Debug.Log($"[ColorPickerUI] Marker registrato: {marker?.name ?? "null"}");
    }

    /// <summary>
    /// Deregistra il marker quando la penna viene rilasciata.
    /// La UI rimane visibile ma non applica colori finché non si
    /// afferra un altro marker.
    /// </summary>
    public void UnregisterMarker()
    {
        _marker = null;
        Debug.Log("[ColorPickerUI] Marker deregistrato.");
    }

    // -------------------------------------------------------
    // Toggle visibilità pannello
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
        _h = 0f; _s = 0f; _v = 1f; // bianco = gomma
        _suppressCallbacks = true;
        ApplyHSVToSliders();
        _suppressCallbacks = false;
        RefreshUI();
        ApplyToMarker();
    }

    /// <summary>
    /// Imposta un colore da bottoni preset esterni (onClick nel Inspector).
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