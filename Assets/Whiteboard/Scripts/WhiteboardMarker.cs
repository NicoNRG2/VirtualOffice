using UnityEngine;
using System.Linq;
using Ubiq.Messaging;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable))]
public class WhiteboardMarker : MonoBehaviour
{
    [SerializeField] private ColorPickerUI _colorPickerUI;
    [SerializeField] private Transform _tip;
    [SerializeField] private int _penSize = 5;
    [SerializeField] private float _drawDistance = 0.005f;

    // -------------------------------------------------------
    // Networking
    // -------------------------------------------------------
    private NetworkContext context;
    private bool _isOwner = false;

    private struct MarkerMessage
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    // -------------------------------------------------------
    // Drawing
    // -------------------------------------------------------
    private Renderer _renderer;
    private Color _currentColor;
    private Color[] _colors;

    private RaycastHit _touch;
    private Whiteboard _whiteboard;
    private Vector2 _touchPos;

    // Coordinate texture dell'ultimo punto disegnato (già clampate)
    private int _lastTexX, _lastTexY;
    private bool _touchedLastFrame;

    // -------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------

    void Awake()
    {
        var grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        grab.selectEntered.AddListener(OnGrab);
        grab.selectExited.AddListener(OnRelease);
    }

    void Start()
    {
        if (_tip == null)
        {
            Debug.LogError("[WhiteboardMarker] _tip non assegnato nell'Inspector!");
            return;
        }

        _renderer     = _tip.GetComponent<Renderer>();
        _currentColor = _renderer.material.color;
        RebuildColorArray();

        context = NetworkScene.Register(this);
    }

    void OnDestroy()
    {
        var grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        if (grab == null) return;
        grab.selectEntered.RemoveListener(OnGrab);
        grab.selectExited.RemoveListener(OnRelease);
    }

    void Update()
    {
        if (!_isOwner) return;

        Draw();

        context.SendJson(new MarkerMessage
        {
            position = transform.position,
            rotation = transform.rotation
        });
    }

    // -------------------------------------------------------
    // Grab events
    // -------------------------------------------------------

    private void OnGrab(SelectEnterEventArgs args)
    {
        SetOwner(true);

        // Se non è assegnato manualmente, cerca il canvas più vicino
        if (_colorPickerUI == null)
            _colorPickerUI = FindNearestColorPicker();

        _colorPickerUI?.RegisterMarker(this);
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        SetOwner(false);
        _touchedLastFrame = false;
        _whiteboard       = null;
        _colorPickerUI?.UnregisterMarker();
    }

    private ColorPickerUI FindNearestColorPicker()
    {
        ColorPickerUI[] all = FindObjectsByType<ColorPickerUI>(FindObjectsSortMode.None);
        ColorPickerUI nearest = null;
        float minDist = float.MaxValue;

        foreach (var ui in all)
        {
            float d = Vector3.Distance(transform.position, ui.transform.position);
            if (d < minDist) { minDist = d; nearest = ui; }
        }
        return nearest;
    }

    // -------------------------------------------------------
    // API pubblica
    // -------------------------------------------------------

    public void SetOwner(bool owner)
    {
        _isOwner = owner;
    }

    public void SetColor(Color color)
    {
        _currentColor = color;
        _renderer.material.color = color;
        RebuildColorArray();
    }

    public Color GetColor() => _currentColor;

    // -------------------------------------------------------
    // Ricezione messaggi dalla rete
    // -------------------------------------------------------

    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        if (_isOwner) return;

        var data = message.FromJson<MarkerMessage>();
        transform.position = data.position;
        transform.rotation = data.rotation;
    }

    // -------------------------------------------------------
    // Drawing logic (solo owner)
    // -------------------------------------------------------

    private void Draw()
    {
        if (_tip == null) return;

        Debug.DrawRay(_tip.position, _tip.up * 0.2f, Color.red);

        if (Physics.Raycast(_tip.position, _tip.up, out _touch, _drawDistance))
        {
            if (_touch.transform.CompareTag("Whiteboard"))
            {
                if (_whiteboard == null)
                    _whiteboard = _touch.transform.GetComponent<Whiteboard>();

                _touchPos = new Vector2(_touch.textureCoord.x, _touch.textureCoord.y);

                int x = (int)(_touchPos.x * _whiteboard.textureSize.x - (_penSize / 2));
                int y = (int)(_touchPos.y * _whiteboard.textureSize.y - (_penSize / 2));

                // Clamp per evitare out-of-bounds
                x = Mathf.Clamp(x, 0, (int)_whiteboard.textureSize.x - _penSize);
                y = Mathf.Clamp(y, 0, (int)_whiteboard.textureSize.y - _penSize);

                if (_touchedLastFrame)
                {
                    // --- Disegno locale con lerp ---
                    for (float f = 0f; f <= 1.00f; f += 0.01f)
                    {
                        int lerpX = Mathf.Clamp((int)Mathf.Lerp(_lastTexX, x, f),
                                                0, (int)_whiteboard.textureSize.x - _penSize);
                        int lerpY = Mathf.Clamp((int)Mathf.Lerp(_lastTexY, y, f),
                                                0, (int)_whiteboard.textureSize.y - _penSize);
                        _whiteboard.texture.SetPixels(lerpX, lerpY, _penSize, _penSize, _colors);
                    }
                    _whiteboard.texture.Apply();
                    _whiteboard.UpdateRenderTexture();

                    // --- Invio rete: lastX/lastY sono le stesse coordinate già clampate ---
                    _whiteboard.SendDraw(x, y, _lastTexX, _lastTexY, true, _penSize, _currentColor);
                }
                else
                {
                    // Primo punto del tratto: disegna subito anche localmente
                    _whiteboard.texture.SetPixels(x, y, _penSize, _penSize, _colors);
                    _whiteboard.texture.Apply();
                    _whiteboard.UpdateRenderTexture();

                    _whiteboard.SendDraw(x, y, 0, 0, false, _penSize, _currentColor);
                }

                _lastTexX         = x;
                _lastTexY         = y;
                _touchedLastFrame = true;
                return;
            }
        }

        // Non sta toccando la whiteboard
        _whiteboard       = null;
        _touchedLastFrame = false;
    }

    private void RebuildColorArray()
    {
        _colors = Enumerable.Repeat(_currentColor, _penSize * _penSize).ToArray();
    }
}