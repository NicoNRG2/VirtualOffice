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
    // Networking — only the owner (the player holding the marker)
    // sends position updates; all other clients just receive them.
    // -------------------------------------------------------
    private NetworkContext context;
    private bool _isOwner = false;

    // Lightweight struct sent every frame to synchronise marker position/rotation.
    private struct MarkerMessage
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    // -------------------------------------------------------
    // Drawing state
    // -------------------------------------------------------
    private Renderer _renderer;
    private Color _currentColor;
    private Color[] _colors;      // pre-built pen-size² array reused each stroke segment

    private RaycastHit _touch;
    private Whiteboard _whiteboard;
    private Vector2 _touchPos;

    // Texture coordinates of the last drawn point, used for stroke interpolation.
    private int _lastTexX, _lastTexY;
    private bool _touchedLastFrame;

    // -------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------

    void Awake()
    {
        // Subscribe to XR grab events to track ownership (who is holding the marker).
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

    // Each frame the owner draws locally and broadcasts its transform.
    // Non-owners skip this entirely — their marker is moved by ProcessMessage.
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
    // Grab / release events
    // -------------------------------------------------------

    private void OnGrab(SelectEnterEventArgs args)
    {
        SetOwner(true);

        // Auto-discover the nearest ColorPickerUI if none was set in the Inspector.
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

    // Finds the closest ColorPickerUI in the scene by Euclidean distance.
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
    // Public API
    // -------------------------------------------------------

    public void SetOwner(bool owner)
    {
        _isOwner = owner;
    }

    // Called by ColorPickerUI when the user picks a new color.
    public void SetColor(Color color)
    {
        _currentColor = color;
        _renderer.material.color = color;
        RebuildColorArray();
    }

    public Color GetColor() => _currentColor;

    // -------------------------------------------------------
    // Network message reception (non-owner side)
    // -------------------------------------------------------

    // Non-owner clients receive the marker's transform and update it directly,
    // so remote players can see the marker moving in real time.
    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        if (_isOwner) return;

        var data = message.FromJson<MarkerMessage>();
        transform.position = data.position;
        transform.rotation = data.rotation;
    }

    // -------------------------------------------------------
    // Drawing logic — only executed by the owner
    // -------------------------------------------------------

    private void Draw()
    {
        if (_tip == null) return;

        Debug.DrawRay(_tip.position, _tip.up * 0.2f, Color.red);

        // Raycast forward from the tip; if it hits a Whiteboard surface, draw on it.
        if (Physics.Raycast(_tip.position, _tip.up, out _touch, _drawDistance))
        {
            if (_touch.transform.CompareTag("Whiteboard"))
            {
                if (_whiteboard == null)
                    _whiteboard = _touch.transform.GetComponent<Whiteboard>();

                _touchPos = new Vector2(_touch.textureCoord.x, _touch.textureCoord.y);

                // Convert UV hit coordinates to pixel coordinates on the texture.
                int x = (int)(_touchPos.x * _whiteboard.textureSize.x - (_penSize / 2));
                int y = (int)(_touchPos.y * _whiteboard.textureSize.y - (_penSize / 2));

                x = Mathf.Clamp(x, 0, (int)_whiteboard.textureSize.x - _penSize);
                y = Mathf.Clamp(y, 0, (int)_whiteboard.textureSize.y - _penSize);

                if (_touchedLastFrame)
                {
                    // Interpolate between the last and current pixel position to fill
                    // gaps caused by fast movement (same lerp logic replicated on remote clients).
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

                    // Send the segment to remote peers (hasLast=true triggers lerp on their side too).
                    _whiteboard.SendDraw(x, y, _lastTexX, _lastTexY, true, _penSize, _currentColor);
                }
                else
                {
                    // First contact point of a new stroke — draw a single square immediately.
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

        // Marker lifted off the board — reset stroke continuity.
        _whiteboard       = null;
        _touchedLastFrame = false;
    }

    // Pre-builds a flat Color array of pen-size identical colors so SetPixels
    // can be called without allocating a new array on every draw call.
    private void RebuildColorArray()
    {
        _colors = Enumerable.Repeat(_currentColor, _penSize * _penSize).ToArray();
    }
}