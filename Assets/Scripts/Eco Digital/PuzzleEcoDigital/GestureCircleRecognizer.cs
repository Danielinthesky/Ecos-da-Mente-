using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class GestureCircleRecognizer : MonoBehaviour
{
    public enum ProjectionPlane { XY, XZ }
    public enum DrawSpace { World, OverlayCamera }

    [Header("Espaço de desenho")]
    [Tooltip("World = desenha no mundo (como antes). OverlayCamera = desenha só na câmera overlay (UI-like, sem entrar no mundo).")]
    [SerializeField] private DrawSpace drawSpace = DrawSpace.World;

    [Header("Câmeras")]
    [Tooltip("No modo World, esta câmera é usada para Screen->World (padrão: Camera.main).")]
    [SerializeField] private Camera drawCamera; // World
    [Tooltip("No modo OverlayCamera, use a câmera overlay (URP: Render Type=Overlay; Built-in: Depth > Base) que só enxerga a Layer 'Gestures'.")]
    [SerializeField] private Camera overlayCam; // Overlay

    [Header("Layer dos strokes (apenas OverlayCamera)")]
    [SerializeField] private string gesturesLayerName = "Gestures";

    [Header("Aparência da linha")]
    [SerializeField, Min(0.5f)] private float lineWidthPixels = 4f;
    [SerializeField] private Gradient lineColor;      // opcional; se vazio usa branco
    [SerializeField] private Material lineMaterial;   // se vazio, cria Sprites/Default

    [Header("Plano/Profundidade (modo World)")]
    [SerializeField] private ProjectionPlane projectionPlane = ProjectionPlane.XY;
    [SerializeField] private bool useFixedWorldZ = true; // para XY
    [SerializeField] private float fixedWorldZ = 0f;
    [SerializeField] private bool useFixedWorldY = false; // para XZ
    [SerializeField] private float fixedWorldY = 0f;

    [Header("Profundidade (Screen->World)")]
    [Tooltip("World (perspectiva): distância da câmera para projetar os pontos.\nOverlayCamera (perspectiva): distância da OverlayCam para projetar.")]
    [SerializeField, Min(0.01f)] private float perspectiveDistance = 1.0f;

    [Header("Amostragem do traço")]
    [SerializeField, Min(0.0001f)] private float minPointDistance = 0.015f;
    [SerializeField, Min(8)] private int maxPointsPerStroke = 4096;
    [SerializeField] private bool ignoreWhenPointerOverUI = true;

    [Header("Ordenação (modo World)")]
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 9999;

    [Header("Critérios de reconhecimento (círculo aprox.)")]
    [SerializeField, Min(0.01f)] private float minStrokeLength = 1.0f;
    [SerializeField, Min(0f)] private float closureToleranceR = 0.8f;           // fim perto do início (em múltiplos do raio médio)
    [SerializeField, Range(0.05f, 0.6f)] private float roundnessToleranceRsd = 0.35f; // desvio padrão / raio médio

    [Header("Ação ao reconhecer")]
    [SerializeField] private GameObject prefabOnRecognized; // opcional
    [SerializeField] private bool alignPrefabToCamera = false;

    [Header("Dissipar traço (fade)")]
    [SerializeField] private bool autoFade = true;
    [SerializeField, Min(0f)] private float fadeDelay = 0.6f;
    [SerializeField, Min(0.05f)] private float fadeDuration = 0.5f;
    [SerializeField] private bool fadeShrinkWidth = true;

    [Serializable] public class CircleEvent : UnityEvent<Vector3, float> {}
    public CircleEvent OnCircleRecognized;       // (centro, raio médio)
    public UnityEvent OnCircleRecognizedSimple;  // sem parâmetros

    // ===== estado de desenho =====
    private LineRenderer _current;                // LR do traço atual
    private readonly List<Vector3> _points = new(1024);
    private readonly List<Vector2> _points2 = new(1024);
    private bool _drawing;
    private Material _runtimeMat;
    private int _strokeCounter = 0;
    private int _gesturesLayer = -1;

    private void Awake()
    {
        if (drawSpace == DrawSpace.World && drawCamera == null) drawCamera = Camera.main;
        if (drawSpace == DrawSpace.OverlayCamera && overlayCam == null)
        {
            overlayCam = Camera.main; // fallback pra não quebrar; ideal é definir sua GestureCam
            Debug.LogWarning("[GestureCircleRecognizer] overlayCam não atribuída; usando Camera.main como fallback (defina sua GestureCam).");
        }

        if (lineMaterial == null)
        {
            var shader = Shader.Find("Sprites/Default");
            _runtimeMat = new Material(shader);
        }

        _gesturesLayer = LayerMask.NameToLayer(gesturesLayerName);
        if (drawSpace == DrawSpace.OverlayCamera && _gesturesLayer < 0)
            Debug.LogWarning($"[GestureCircleRecognizer] Layer '{gesturesLayerName}' não existe. Crie-a e ajuste o Culling Mask das câmeras.");
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.C))
            ClearAllStrokes();

        if (InputDown())
        {
            if (ignoreWhenPointerOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            StartStroke();
            AddPoint(GetWorldPoint(CurrentPointerPosition()));
        }
        else if (_drawing && InputHeld())
        {
            Vector3 wp = GetWorldPoint(CurrentPointerPosition());
            if (_points.Count == 0 || (wp - _points[_points.Count - 1]).sqrMagnitude >= (minPointDistance * minPointDistance))
                AddPoint(wp);
        }
        else if (_drawing && InputUp())
        {
            EndStrokeAndTryRecognize(); // também aciona fade se autoFade = true
        }
    }

    // ===== entrada =====
    private bool InputDown()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        return Input.GetMouseButtonDown(0);
#else
        return (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began) || Input.GetMouseButtonDown(0);
#endif
    }
    private bool InputHeld()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        return Input.GetMouseButton(0);
#else
        if (Input.touchCount > 0)
        {
            var ph = Input.GetTouch(0).phase;
            if (ph == TouchPhase.Moved || ph == TouchPhase.Stationary) return true;
        }
        return Input.GetMouseButton(0);
#endif
    }
    private bool InputUp()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        return Input.GetMouseButtonUp(0);
#else
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Ended) return true;
        return Input.GetMouseButtonUp(0);
#endif
    }
    private Vector2 CurrentPointerPosition()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        return Input.mousePosition;
#else
        if (Input.touchCount > 0) return Input.GetTouch(0).position;
        return (Vector2)Input.mousePosition;
#endif
    }

    // ===== ciclo do traço =====
    private void StartStroke()
    {
        _drawing = true;
        _points.Clear();
        _points2.Clear();

        // cria um GO próprio para ESTE traço
        var go = new GameObject($"Stroke_{_strokeCounter++}");
        go.transform.SetParent(transform, false);

        // No modo Overlay, garanta que o stroke esteja na Layer certa
        if (drawSpace == DrawSpace.OverlayCamera && _gesturesLayer >= 0)
            go.layer = _gesturesLayer;

        _current = go.AddComponent<LineRenderer>();
        _current.useWorldSpace = true;
        _current.textureMode = LineTextureMode.Stretch;
        _current.numCapVertices = 8;
        _current.numCornerVertices = 4;
        _current.positionCount = 0;

        // Ordenação só importa no modo World (opacos/transparentes). Em Overlay, a GestureCam já desenha por cima.
        if (drawSpace == DrawSpace.World)
        {
            _current.sortingLayerName = sortingLayerName;
            _current.sortingOrder = sortingOrder;
        }

        // material (instância)
        Material matBase = null;
        if (lineMaterial != null) matBase = lineMaterial;
        else if (_runtimeMat != null) matBase = _runtimeMat;

        if (matBase != null) _current.material = new Material(matBase);
        else _current.material = new Material(Shader.Find("Sprites/Default"));

        // cor
        if (lineColor != null && lineColor.colorKeys != null && lineColor.colorKeys.Length > 0)
            _current.colorGradient = lineColor;
        else
        {
            var g = new Gradient();
            g.SetKeys(
                new [] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new [] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
            );
            _current.colorGradient = g;
        }

        AtualizarLarguraEmPixels(_current);
    }

    private void AddPoint(Vector3 p)
    {
        if (_current == null) return;
        if (_points.Count >= maxPointsPerStroke) return;

        _points.Add(p);
        _current.positionCount = _points.Count;
        _current.SetPosition(_points.Count - 1, p);

        AtualizarLarguraEmPixels(_current);
    }

    private void EndStrokeAndTryRecognize()
    {
        _drawing = false;
        if (_current == null || _points.Count < 8)
        {
            TryAttachFadeAndForget(_current);
            _current = null;
            _points.Clear(); _points2.Clear();
            return;
        }

        // ===== Preparar pontos 2D para análise =====
        _points2.Clear();

        if (drawSpace == DrawSpace.World)
        {
            if (projectionPlane == ProjectionPlane.XY)
            {
                for (int i = 0; i < _points.Count; i++)
                {
                    var v = _points[i];
                    _points2.Add(new Vector2(v.x, v.y));
                }
            }
            else // XZ
            {
                for (int i = 0; i < _points.Count; i++)
                {
                    var v = _points[i];
                    _points2.Add(new Vector2(v.x, v.z));
                }
            }
        }
        else // OverlayCamera: usamos XY da overlayCam (os pontos já estão no espaço da câmera overlay)
        {
            for (int i = 0; i < _points.Count; i++)
            {
                var v = _points[i];
                _points2.Add(new Vector2(v.x, v.y));
            }
        }

        float strokeLen = PathLength(_points);
        if (strokeLen >= minStrokeLength)
        {
            Vector2 c = Centroid(_points2);

            // raio médio
            float meanR = 0f;
            for (int i = 0; i < _points2.Count; i++)
                meanR += Vector2.Distance(_points2[i], c);
            meanR /= _points2.Count;

            if (meanR > Mathf.Epsilon)
            {
                float variance = 0f;
                for (int i = 0; i < _points2.Count; i++)
                {
                    float d = Vector2.Distance(_points2[i], c) - meanR;
                    variance += d * d;
                }
                variance /= _points2.Count;
                float std = Mathf.Sqrt(variance);
                float rsd = std / meanR;

                float closure = Vector2.Distance(_points2[0], _points2[_points2.Count - 1]);
                bool closedEnough = closure <= (closureToleranceR * meanR);
                bool isCircle = closedEnough && (rsd <= roundnessToleranceRsd);

                if (isCircle)
                {
                    Vector3 center3;
                    if (drawSpace == DrawSpace.World)
                    {
                        if (projectionPlane == ProjectionPlane.XY)
                            center3 = new Vector3(c.x, c.y, useFixedWorldZ ? fixedWorldZ : (drawCamera != null ? drawCamera.transform.position.z + perspectiveDistance : 0f));
                        else
                            center3 = new Vector3(c.x, useFixedWorldY ? fixedWorldY : 0f, c.y);
                    }
                    else
                    {
                        // Overlay: o z é próximo do near da overlayCam (ortho) ou na distance fixa (perspectiva)
                        if (overlayCam != null && overlayCam.orthographic)
                            center3 = new Vector3(c.x, c.y, overlayCam.transform.position.z + Mathf.Abs(overlayCam.nearClipPlane) + 0.01f);
                        else
                            center3 = new Vector3(c.x, c.y, overlayCam != null ? overlayCam.transform.position.z + perspectiveDistance : 0f);
                    }

                    OnCircleRecognized?.Invoke(center3, meanR);
                    OnCircleRecognizedSimple?.Invoke();

                    if (prefabOnRecognized != null)
                    {
                        Quaternion rot;
                        if (alignPrefabToCamera)
                        {
                            var cam = (drawSpace == DrawSpace.World) ? drawCamera : overlayCam;
                            rot = cam != null ? Quaternion.LookRotation(cam.transform.forward, Vector3.up) : Quaternion.identity;
                        }
                        else rot = Quaternion.identity;

                        Instantiate(prefabOnRecognized, center3, rot);
                    }
                }
            }
        }

        // aplica fade nesse traço e libera para o próximo
        TryAttachFadeAndForget(_current);
        _current = null;
        _points.Clear();
        _points2.Clear();
    }

    private void TryAttachFadeAndForget(LineRenderer lr)
    {
        if (lr == null) return;

        if (autoFade)
        {
            var fader = lr.gameObject.GetComponent<StrokeFader>();
            if (fader == null) fader = lr.gameObject.AddComponent<StrokeFader>();
            fader.Begin(lr, fadeDelay, fadeDuration, fadeShrinkWidth, destroyOnEnd: true);
        }
    }

    public void ClearAllStrokes()
    {
        var toDestroy = new List<GameObject>();
        for (int i = 0; i < transform.childCount; i++)
            toDestroy.Add(transform.GetChild(i).gameObject);

        foreach (var go in toDestroy) Destroy(go);

        _current = null;
        _points.Clear();
        _points2.Clear();
    }

    // ===== util =====
    private Vector3 GetWorldPoint(Vector2 screenPos)
    {
        // Decide a câmera e a projeção conforme o modo
        Camera cam = (drawSpace == DrawSpace.World) ? drawCamera : overlayCam;
        if (cam == null) cam = Camera.main;
        if (cam == null) return Vector3.zero;

        if (cam.orthographic)
        {
            // z próximo do near (Overlay) OU respeitando fixedZ/fixedY (World)
            Vector3 wp = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, Mathf.Abs(cam.nearClipPlane) + 0.01f));
            if (drawSpace == DrawSpace.World)
            {
                if (projectionPlane == ProjectionPlane.XY && useFixedWorldZ) wp.z = fixedWorldZ;
                if (projectionPlane == ProjectionPlane.XZ && useFixedWorldY) wp.y = fixedWorldY;
            }
            return wp;
        }
        else
        {
            // distância fixa à frente da câmera
            Vector3 sp = new Vector3(screenPos.x, screenPos.y, perspectiveDistance);
            var wp = cam.ScreenToWorldPoint(sp);

            if (drawSpace == DrawSpace.World)
            {
                if (projectionPlane == ProjectionPlane.XY && useFixedWorldZ) wp.z = fixedWorldZ;
                if (projectionPlane == ProjectionPlane.XZ && useFixedWorldY) wp.y = fixedWorldY;
            }
            return wp;
        }
    }

    private void AtualizarLarguraEmPixels(LineRenderer lr)
    {
        Camera cam = (drawSpace == DrawSpace.World) ? drawCamera : overlayCam;
        if (lr == null || cam == null) return;

        float worldPerPixel;
        if (cam.orthographic)
        {
            worldPerPixel = (cam.orthographicSize * 2f) / Screen.height;
        }
        else
        {
            float h = 2f * perspectiveDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            worldPerPixel = h / Screen.height;
        }

        float w = lineWidthPixels * worldPerPixel;
        lr.startWidth = lr.endWidth = w;
    }

    private static float PathLength(List<Vector3> pts)
    {
        float d = 0f;
        for (int i = 1; i < pts.Count; i++)
            d += Vector3.Distance(pts[i - 1], pts[i]);
        return d;
    }

    private static Vector2 Centroid(List<Vector2> pts)
    {
        if (pts == null || pts.Count == 0) return Vector2.zero;
        Vector2 s = Vector2.zero;
        for (int i = 0; i < pts.Count; i++) s += pts[i];
        return s / pts.Count;
    }
}
