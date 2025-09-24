using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public class MecanicaDesenhoNaTela : MonoBehaviour
{
    public enum ProjectionPlane { XY, XZ }
    public enum DrawSpace { World, OverlayCamera }

    // ==== Config geral / espaço ====
    [Header("Espaço de desenho")]
    [SerializeField] private DrawSpace drawSpace = DrawSpace.OverlayCamera;
    [SerializeField] private ProjectionPlane projectionPlane = ProjectionPlane.XY;

    [Header("Câmeras")]
    [Tooltip("World: usa esta câmera para Screen→World (padrão: Camera.main).")]
    [SerializeField] private Camera drawCamera;          // World
    [Tooltip("Overlay: a câmera overlay (URP Overlay / Built-in com Depth maior) que enxerga a layer Gestures.")]
    [SerializeField] private Camera overlayCam;          // Overlay

    [Header("Overlay (fixar na câmera)")]
    [Tooltip("Se ligado, cada traço vira filho da overlayCam (ficando 2D na tela).")]
    [SerializeField] private bool overlayStickToCamera = true;
    [Tooltip("Se ligado, o LineRenderer usa coordenadas LOCAIS (useWorldSpace = false) da overlayCam.")]
    [SerializeField] private bool overlayUseLocalSpace = true;

    [Header("Layer (apenas Overlay)")]
    [SerializeField] private string gesturesLayerName = "Gestures";

    [Header("Linha (px)")]
    [SerializeField, Min(0.5f)] private float lineWidthPixels = 8f;
    [SerializeField] private Gradient lineColor;         // opcional
    [SerializeField] private Material lineMaterial;      // opcional (fallback automático evita rosa)

    [Header("Screen→World")]
    [Tooltip("Distância usada em câmeras de perspectiva para projetar os pontos.")]
    [SerializeField, Min(0.01f)] private float perspectiveDistance = 1.0f;

    [Header("Plano fixo (World)")]
    [SerializeField] private bool useFixedWorldZ = true;   // para XY
    [SerializeField] private float fixedWorldZ = 0f;
    [SerializeField] private bool useFixedWorldY = false;  // para XZ
    [SerializeField] private float fixedWorldY = 0f;

    [Header("Entrada")]
    [SerializeField] private bool enableInput = true;
    [SerializeField] private bool ignoreWhenPointerOverUI = true;
    [SerializeField, Min(0.0001f)] private float minPointDistance = 0.015f;
    [SerializeField, Min(8)] private int maxPointsPerStroke = 4096;

    [Header("Ordenação (World)")]
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 9999;

    [Header("Fade")]
    [SerializeField] private bool autoFade = true;
    [SerializeField, Min(0f)] private float fadeDelay = 0.6f;
    [SerializeField, Min(0.05f)] private float fadeDuration = 0.5f;
    [SerializeField] private bool fadeShrinkWidth = true;

    // ==== API / Events ====
    public delegate void StrokeFinishedHandler(IReadOnlyList<Vector3> pts3D, IReadOnlyList<Vector2> pts2D, float unitsPerPixel);
    /// <summary>Disparado ao soltar o traço. pts3D/pts2D são cópias; unitsPerPixel = mundo por pixel da câmera vigente.</summary>
    public event StrokeFinishedHandler OnStrokeFinished;

    public DrawSpace Mode => drawSpace;
    public ProjectionPlane Plane => projectionPlane;
    public Camera EffectiveCamera => (drawSpace == DrawSpace.World) ? (drawCamera ?? Camera.main) : overlayCam;
    /// <summary>Fator "mundo por pixel" calculado na última atualização da largura.</summary>
    public float CurrentUnitsPerPixel { get; private set; } = 0f;

    // ==== estado ====
    private LineRenderer _current;
    private readonly List<Vector3> _pts3D = new(1024);
    private readonly List<Vector2> _pts2D = new(1024);
    private int _strokeCounter = 0;
    private Material _runtimeMat;
    private int _gesturesLayer = -1;
    private bool _drawing;
    public bool OverlayUseLocalSpace => overlayUseLocalSpace;


    private void Awake()
    {
        if (drawSpace == DrawSpace.World && drawCamera == null) drawCamera = Camera.main;
        if (drawSpace == DrawSpace.OverlayCamera && overlayCam == null)
        {
            overlayCam = Camera.main; // fallback pra não quebrar
            Debug.LogWarning("[MecanicaDesenhoNaTela] overlayCam não atribuída; usando Camera.main como fallback.");
        }

        // Material padrão (Sprites/Default) para fallback encadeado
        var shader = Shader.Find("Sprites/Default");
        if (shader != null) _runtimeMat = new Material(shader);

        _gesturesLayer = LayerMask.NameToLayer(gesturesLayerName);
        if (drawSpace == DrawSpace.OverlayCamera && _gesturesLayer < 0)
            Debug.LogWarning($"[MecanicaDesenhoNaTela] Layer '{gesturesLayerName}' não existe.");
    }

    private void Update()
    {
        if (!enableInput) return;

        if (InputDown())
        {
            if (ignoreWhenPointerOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            StartStroke();
            AddPoint(GetWorldPoint(CurrentPointerPos()));
        }
        else if (_drawing && InputHeld())
        {
            Vector3 wp = GetWorldPoint(CurrentPointerPos());
            if (_pts3D.Count == 0 || (wp - _pts3D[^1]).sqrMagnitude >= (minPointDistance * minPointDistance))
                AddPoint(wp);
        }
        else if (_drawing && InputUp())
        {
            EndStroke();
        }

        if (Input.GetKeyDown(KeyCode.C)) ClearAllStrokes();
    }

    // ==== Entrada ====
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
        if (Input.touchCount > 0 && (Input.GetTouch(0).phase == TouchPhase.Ended || Input.GetTouch(0).phase == TouchPhase.Canceled)) return true;
        return Input.GetMouseButtonUp(0);
#endif
    }
    private Vector2 CurrentPointerPos()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        return Input.mousePosition;
#else
        if (Input.touchCount > 0) return Input.GetTouch(0).position;
        return (Vector2)Input.mousePosition;
#endif
    }

    // ==== Traço ====
    private void StartStroke()
    {
        _drawing = true;
        _pts3D.Clear();
        _pts2D.Clear();

        var go = new GameObject($"Stroke_{_strokeCounter++}");

        // parent padrão: este objeto; em overlay, podemos prender na câmera
        Transform parent = transform;
        if (drawSpace == DrawSpace.OverlayCamera && overlayStickToCamera && overlayCam != null)
            parent = overlayCam.transform;

        go.transform.SetParent(parent, false);

        if (drawSpace == DrawSpace.OverlayCamera && _gesturesLayer >= 0)
            go.layer = _gesturesLayer;

        _current = go.AddComponent<LineRenderer>();

        // Se overlay + local space, o LR usa coordenadas locais da overlayCam
        bool useLocal = (drawSpace == DrawSpace.OverlayCamera) && overlayUseLocalSpace;
        _current.useWorldSpace = !useLocal;

        _current.textureMode = LineTextureMode.Stretch;
        _current.numCapVertices = 8;
        _current.numCornerVertices = 4;
        _current.positionCount = 0;

        if (drawSpace == DrawSpace.World)
        {
            _current.sortingLayerName = sortingLayerName;
            _current.sortingOrder = sortingOrder;
        }

        // material (instância) com fallback anti-rosa
        Material matBase = lineMaterial != null ? lineMaterial : GetFallbackLineMaterial();
        _current.material = new Material(matBase);

        // gradient/cor
        if (lineColor != null && lineColor.colorKeys != null && lineColor.colorKeys.Length > 0)
            _current.colorGradient = lineColor;
        else
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
            );
            _current.colorGradient = g;
        }

        UpdateWidthInPixels(_current);
    }

    private void AddPoint(Vector3 p)
    {
        if (_current == null) return;
        if (_pts3D.Count >= maxPointsPerStroke) return;

        _pts3D.Add(p);
        _current.positionCount = _pts3D.Count;

        bool overlayLocal = (drawSpace == DrawSpace.OverlayCamera) && overlayUseLocalSpace && overlayCam != null;

        if (overlayLocal)
        {
            Vector3 local = overlayCam.transform.InverseTransformPoint(p);
            _current.SetPosition(_pts3D.Count - 1, local);
        }
        else
        {
            _current.SetPosition(_pts3D.Count - 1, p);
        }

        // 2D no plano para o reconhecedor
        if (drawSpace == DrawSpace.World)
        {
            if (projectionPlane == ProjectionPlane.XY) _pts2D.Add(new Vector2(p.x, p.y));
            else _pts2D.Add(new Vector2(p.x, p.z));
        }
        else
        {
            // Overlay: usar XY; se estiver em local space, usar 'local'
            if (overlayLocal && overlayCam != null)
            {
                Vector3 local = overlayCam.transform.InverseTransformPoint(p);
                _pts2D.Add(new Vector2(local.x, local.y));
            }
            else
            {
                _pts2D.Add(new Vector2(p.x, p.y));
            }
        }

        UpdateWidthInPixels(_current);
    }

    private void EndStroke()
    {
        _drawing = false;

        // Notifica o reconhecedor
        var copy3D = _pts3D.ToArray();
        var copy2D = _pts2D.ToArray();
        OnStrokeFinished?.Invoke(copy3D, copy2D, CurrentUnitsPerPixel);

        // Fade
        if (autoFade && _current != null)
        {
            var fader = _current.gameObject.GetComponent<StrokeFader>();
            if (fader == null) fader = _current.gameObject.AddComponent<StrokeFader>();
            fader.Begin(_current, fadeDelay, fadeDuration, fadeShrinkWidth, destroyOnEnd: true);
        }

        _current = null;
        _pts3D.Clear();
        _pts2D.Clear();
    }

    public void ClearAllStrokes()
    {
        var list = new List<GameObject>();
        for (int i = 0; i < transform.childCount; i++)
            list.Add(transform.GetChild(i).gameObject);
        foreach (var go in list) Destroy(go);

        _current = null;
        _pts3D.Clear();
        _pts2D.Clear();
        _drawing = false;
    }

    // ==== Conversão & largura ====
    private Vector3 GetWorldPoint(Vector2 screenPos)
    {
        Camera cam = (drawSpace == DrawSpace.World) ? (drawCamera ?? Camera.main) : overlayCam;
        if (cam == null) return Vector3.zero;

        if (cam.orthographic)
        {
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

    private void UpdateWidthInPixels(LineRenderer lr)
    {
        Camera cam = (drawSpace == DrawSpace.World) ? (drawCamera ?? Camera.main) : overlayCam;
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

        CurrentUnitsPerPixel = worldPerPixel;
        float w = lineWidthPixels * worldPerPixel;
        lr.startWidth = lr.endWidth = w;
    }

    // ==== Fallback de Material/Shader (contra traço rosa) ====
    private static Material _cachedFallback;
    private static Material GetFallbackLineMaterial()
    {
        if (_cachedFallback != null) return _cachedFallback;

        // Tenta URP Unlit → URP 2D Sprite → Sprites/Default → Unlit/Color
        Shader s = Shader.Find("Universal Render Pipeline/Unlit");
        if (s == null) s = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (s == null) s = Shader.Find("Sprites/Default");
        if (s == null) s = Shader.Find("Unlit/Color");

        if (s == null)
        {
            Debug.LogWarning("[MecanicaDesenhoNaTela] Nenhum shader compatível encontrado; usando Unlit/Color.");
            s = Shader.Find("Unlit/Color");
        }

        _cachedFallback = new Material(s);
        // Transparente (URP)
        if (_cachedFallback.HasProperty("_Surface"))
            _cachedFallback.SetFloat("_Surface", 1f); // 0=Opaque, 1=Transparent
        _cachedFallback.renderQueue = 3000; // Transparent
        return _cachedFallback;
    }
}
