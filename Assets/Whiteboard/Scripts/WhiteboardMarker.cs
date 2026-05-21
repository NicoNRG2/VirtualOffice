using UnityEngine;
using System.Linq;
using Ubiq.Messaging;

public class WhiteboardMarker : MonoBehaviour
{
    [SerializeField] private Transform _tip;
    [SerializeField] private int _penSize = 5;
    [SerializeField] private float _drawDistance = 0.005f;

    // -------------------------------------------------------
    // Networking
    // -------------------------------------------------------
    private NetworkContext context;
    private bool _isOwner = false;   // true solo per il giocatore locale

    private struct MarkerMessage
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    // -------------------------------------------------------
    // Drawing
    // -------------------------------------------------------
    private Renderer _renderer;
    private Color[] _colors;

    private RaycastHit _touch;
    private Whiteboard _whiteboard;
    private Vector2 _touchPos, _lastTouchPos;
    private bool _touchedLastFrame;
    private Quaternion _lastTouchRot;

    // -------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------

    void Start()
    {
        _renderer = _tip.GetComponent<Renderer>();
        _colors   = Enumerable.Repeat(_renderer.material.color, _penSize * _penSize).ToArray();

        context = NetworkScene.Register(this);
    }

    void Update()
    {
        if (_isOwner)
        {
            Draw();

            // Invia posizione/rotazione del marker agli altri peer ogni frame
            context.SendJson(new MarkerMessage
            {
                position = transform.position,
                rotation = transform.rotation
            });
        }
    }

    // -------------------------------------------------------
    // Chiamate dall'esterno per assegnare/revocare la proprietà
    // (collega questi metodi agli eventi XRGrabInteractable nel tuo Pen controller,
    //  oppure chiama SetOwner(true/false) dove gestisci il grab)
    // -------------------------------------------------------

    public void SetOwner(bool owner)
    {
        _isOwner = owner;
    }

    // -------------------------------------------------------
    // Ricezione messaggi dalla rete (peer remoti)
    // -------------------------------------------------------

    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        if (_isOwner) return; // ignora se siamo già owner

        var data = message.FromJson<MarkerMessage>();
        transform.position = data.position;
        transform.rotation = data.rotation;
        // Il disegno remoto è gestito da Whiteboard.ProcessMessage
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
                    // --- Applica localmente ---
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

                    // --- Invia via rete il punto corrente ---
                    // (inviamo il punto "canonico" x,y; l'interpolazione è locale)
                    _whiteboard.SendDraw(x, y, _penSize, _renderer.material.color);
                }

                _lastTouchPos = new Vector2(x, y);
                _lastTouchRot = transform.rotation;
                _touchedLastFrame = true;
                return;
            }
        }

        _whiteboard = null;
        _touchedLastFrame = false;
    }
}