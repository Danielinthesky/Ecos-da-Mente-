using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class GestureCircleRecognizer : MonoBehaviour
{
    public enum ProjectionPlane { XY, XZ }

    [Header("Câmera de desenho")]
    [SerializeField] private Camera drawCamera; // se vazio, usa Camera.main

    [Header("Aparência da linha")]
    [SerializeField, Min(0.5f)] private float lineWidthPixels = 4f;
    [SerializeField] private Gradient lineColor;      // opcional; se vazio usa branco
    [SerializeField] private Material lineMaterial;   // se vazio, cria Sprites/Default

    [Header("Plano/Profundidade")]
    [SerializeField] private ProjectionPlane projectionPlane = ProjectionPlane.XY;
    [SerializeField] private bool useFixedWorldZ = true; // para XY
    [SerializeField] private float fixedWorldZ = 0f;
    [SerializeField] private bool useFixedWorldY = false; // para XZ
    [SerializeField] private float fixedWorldY = 0f;
    [SerializeField, Min(0.01f)] private float perspectiveDistance = 2f; // para câmeras perspectiva

    [Header("Amostragem do traço")]
    [SerializeField, Min(0.0001f)] private float minPointDistance = 0.015f;
    [SerializeField, Min(8)] private int maxPointsPerStroke = 4096;
    [SerializeField] private bool ignoreWhenPointerOverUI = true;

    [Header("Ordenação (por cima)")]
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

    private void Awake()
    {
        if (drawCamera == null) drawCamera = Camera.main;

        if (lineMaterial == null)
        {
            var shader = Shader.Find("Sprites/Default");
            _runtimeMat = new Material(shader);
        }
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

        _current = go.AddComponent<LineRenderer>();
        _current.useWorldSpace = true;
        _current.textureMode = LineTextureMode.Stretch;
        _current.numCapVertices = 8;
        _current.numCornerVertices = 4;
        _current.sortingLayerName = sortingLayerName;
        _current.sortingOrder = sortingOrder;
        _current.positionCount = 0;

                Material matBase = null;
        if (lineMaterial != null)
            matBase = lineMaterial;
        else if (_runtimeMat != null)
            matBase = _runtimeMat;

        if (matBase != null)
            _current.material = new Material(matBase);
        else
            _current.material = new Material(Shader.Find("Sprites/Default"));

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
            // ainda assim dispara fade/cleanup se quiser
            TryAttachFadeAndForget(_current);
            _current = null;
            _points.Clear(); _points2.Clear();
            return;
        }

        // 2D para análise
        _points2.Clear();
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
                    if (projectionPlane == ProjectionPlane.XY)
                        center3 = new Vector3(c.x, c.y, useFixedWorldZ ? fixedWorldZ : (drawCamera != null ? drawCamera.transform.position.z + perspectiveDistance : 0f));
                    else
                        center3 = new Vector3(c.x, useFixedWorldY ? fixedWorldY : 0f, c.y);

                    OnCircleRecognized?.Invoke(center3, meanR);
                    OnCircleRecognizedSimple?.Invoke();

                    if (prefabOnRecognized != null)
                    {
                        Quaternion rot = alignPrefabToCamera && drawCamera != null
                            ? Quaternion.LookRotation(drawCamera.transform.forward, Vector3.up)
                            : Quaternion.identity;

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
        if (drawCamera == null) drawCamera = Camera.main;
        if (drawCamera == null) return Vector3.zero;

        if (drawCamera.orthographic)
        {
            Vector3 wp = drawCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, Mathf.Abs(drawCamera.nearClipPlane + 0.01f)));
            if (projectionPlane == ProjectionPlane.XY && useFixedWorldZ) wp.z = fixedWorldZ;
            if (projectionPlane == ProjectionPlane.XZ && useFixedWorldY) wp.y = fixedWorldY;
            return wp;
        }
        else
        {
            Vector3 sp = new Vector3(screenPos.x, screenPos.y, perspectiveDistance);
            var wp = drawCamera.ScreenToWorldPoint(sp);

            if (projectionPlane == ProjectionPlane.XY && useFixedWorldZ) wp.z = fixedWorldZ;
            if (projectionPlane == ProjectionPlane.XZ && useFixedWorldY) wp.y = fixedWorldY;

            return wp;
        }
    }

    private void AtualizarLarguraEmPixels(LineRenderer lr)
    {
        if (lr == null || drawCamera == null) return;

        float worldPerPixel;
        if (drawCamera.orthographic)
        {
            worldPerPixel = (drawCamera.orthographicSize * 2f) / Screen.height;
        }
        else
        {
            float h = 2f * perspectiveDistance * Mathf.Tan(drawCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
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
