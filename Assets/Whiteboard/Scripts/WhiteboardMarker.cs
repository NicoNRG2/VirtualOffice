using UnityEngine;
using System.Linq;
using Ubiq.Messaging;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable))]
public class WhiteboardMarker : MonoBehaviour
{
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
    private Vector2 _touchPos, _lastTouchPos;
    private bool _touchedLastFrame;
    private Quaternion _lastTouchRot;

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
        if (_isOwner)
        {
            Draw();

            context.SendJson(new MarkerMessage
            {
                position = transform.position,
                rotation = transform.rotation
            });
        }
    }

    // -------------------------------------------------------
    // Grab events
    // -------------------------------------------------------

    private void OnGrab(SelectEnterEventArgs args)
    {
        SetOwner(true);
        ColorPickerUI.Instance?.RegisterMarker(this);
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        SetOwner(false);
        ColorPickerUI.Instance?.UnregisterMarker();
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
        Debug.DrawRay(_tip.position, _tip.up * 0.2f, Color.red);

        if (Physics.Raycast(_tip.position, _tip.up, out _touch, _drawDistance))
        {
            if (_touch.transform.CompareTag("Whiteboard"))
            {
                if (_whiteboard == null)
                    _whiteboard = _touch.transform.GetComponent<Whiteboard>();

                _touchPos = new Vector2(_touch.textureCoord.x, _touch.textureCoord.y);

                var x = (int)(_touchPos.x * _whiteboard.textureSize.x - (_penSize / 2));
                var y = (int)(_touchPos.y * _whiteboard.textureSize.y - (_penSize / 2));

                if (y < 0 || y > _whiteboard.textureSize.y ||
                    x < 0 || x > _whiteboard.textureSize.x)
                    return;

                if (_touchedLastFrame)
                {
                    _whiteboard.texture.SetPixels(x, y, _penSize, _penSize, _colors);

                    for (float f = 0.01f; f < 1.00f; f += 0.01f)
                    {
                        var lerpX = (int)Mathf.Lerp(_lastTouchPos.x, x, f);
                        var lerpY = (int)Mathf.Lerp(_lastTouchPos.y, y, f);
                        _whiteboard.texture.SetPixels(lerpX, lerpY, _penSize, _penSize, _colors);
                    }

                    transform.rotation = _lastTouchRot;
                    _whiteboard.texture.Apply();
                    _whiteboard.UpdateRenderTexture();

                    _whiteboard.SendDraw(
                        x, y,
                        (int)_lastTouchPos.x, (int)_lastTouchPos.y,
                        true,
                        _penSize,
                        _currentColor
                    );
                }
                else
                {
                    _whiteboard.SendDraw(x, y, 0, 0, false, _penSize, _currentColor);
                }

                _lastTouchPos     = new Vector2(x, y);
                _lastTouchRot     = transform.rotation;
                _touchedLastFrame = true;
                return;
            }
        }

        _whiteboard       = null;
        _touchedLastFrame = false;
    }

    private void RebuildColorArray()
    {
        _colors = Enumerable.Repeat(_currentColor, _penSize * _penSize).ToArray();
    }
}