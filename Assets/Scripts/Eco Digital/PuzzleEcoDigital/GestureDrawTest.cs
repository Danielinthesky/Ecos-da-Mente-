using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public class GestureDrawTest : MonoBehaviour
{
    [Header("Câmera de desenho")]
    [SerializeField] private Camera drawCamera;          // se vazio, usa Camera.main

    [Header("Aparência da linha")]
    [SerializeField, Min(0.001f)] private float lineWidth = 0.035f;
    [SerializeField] private Gradient lineColor;         // opcional; se vazio usa branco
    [SerializeField] private Material lineMaterial;      // se vazio, cria um material Sprites/Default

    [Header("Plano/Profundidade")]
    [Tooltip("Se verdadeiro e a câmera for ortográfica, fixa o Z mundial das linhas (bom para 2D/isométrico).")]
    [SerializeField] private bool useFixedWorldZ = true;
    [SerializeField] private float fixedWorldZ = 0f;

    [Tooltip("Se a câmera for perspectiva, distância da câmera para posicionar os pontos (em unidades).")]
    [SerializeField, Min(0.01f)] private float perspectiveDistance = 2.0f;

    [Header("Amostragem do traço")]
    [SerializeField, Min(0.0001f)] private float minPointDistance = 0.015f; // distância mínima entre pontos
    [SerializeField, Min(8)] private int maxPointsPerStroke = 4096;
    [SerializeField] private bool ignoreWhenPointerOverUI = true;

    [Header("Ordenação (para aparecer por cima)")]
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 9999;

    // ===== estado interno =====
    private LineRenderer _current;
    private readonly List<Vector3> _points = new List<Vector3>(1024);
    private bool _drawing;
    private Material _runtimeMat;

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
        // Limpar tudo (atalho)
        if (Input.GetKeyDown(KeyCode.C))
            ClearAllStrokes();

        // Início do desenho
        if (InputDown())
        {
            if (ignoreWhenPointerOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            StartStroke();
            AddPoint(GetWorldPoint(CurrentPointerPosition()));
        }
        // Mantendo o desenho
        else if (_drawing && InputHeld())
        {
            Vector3 wp = GetWorldPoint(CurrentPointerPosition());
            if (_points.Count == 0 || (wp - _points[_points.Count - 1]).sqrMagnitude >= (minPointDistance * minPointDistance))
                AddPoint(wp);
        }
        // Fim do desenho
        else if (_drawing && InputUp())
        {
            EndStroke();
        }
    }

    // ===== entrada (mouse/touch) =====
    private bool InputDown()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        return Input.GetMouseButtonDown(0);
#else
        return Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began
               || Input.GetMouseButtonDown(0);
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

        var go = new GameObject("Stroke");
        go.transform.SetParent(transform, false);

        _current = go.AddComponent<LineRenderer>();
        _current.useWorldSpace = true;
        _current.textureMode = LineTextureMode.Stretch;
        _current.numCapVertices = 8;
        _current.numCornerVertices = 4;

        _current.sortingLayerName = sortingLayerName;
        _current.sortingOrder = sortingOrder;

        _current.widthMultiplier = 1f;
        _current.startWidth = lineWidth;
        _current.endWidth = lineWidth;

        if (lineMaterial != null) _current.material = lineMaterial;
        else _current.material = _runtimeMat;

        if (lineColor != null && lineColor.colorKeys.Length > 0)
            _current.colorGradient = lineColor;
        else
        {
            Gradient g = new Gradient();
            g.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
            );
            _current.colorGradient = g;
        }
        _current.positionCount = 0;
    }

    private void AddPoint(Vector3 p)
    {
        if (_current == null) return;
        if (_points.Count >= maxPointsPerStroke) return;

        _points.Add(p);
        _current.positionCount = _points.Count;
        _current.SetPosition(_points.Count - 1, p);
    }

    private void EndStroke()
    {
        _drawing = false;
        _points.Clear();
        _current = null;
    }

    public void ClearAllStrokes()
    {
        // Apaga todos os filhos "Stroke"
        var toDestroy = new List<GameObject>();
        for (int i = 0; i < transform.childCount; i++)
            toDestroy.Add(transform.GetChild(i).gameObject);

        foreach (var go in toDestroy)
            Destroy(go);
    }

    // ===== projeção de tela → mundo =====
    private Vector3 GetWorldPoint(Vector2 screenPos)
    {
        if (drawCamera == null) drawCamera = Camera.main;
        if (drawCamera == null) return Vector3.zero;

        if (drawCamera.orthographic)
        {
            Vector3 wp = drawCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, Mathf.Abs(drawCamera.nearClipPlane + 0.01f)));
            if (useFixedWorldZ) wp.z = fixedWorldZ;
            return wp;
        }
        else
        {
            // Perspectiva: projeta a uma distância fixa à frente da câmera
            Vector3 sp = new Vector3(screenPos.x, screenPos.y, perspectiveDistance);
            return drawCamera.ScreenToWorldPoint(sp);
        }
    }
}
