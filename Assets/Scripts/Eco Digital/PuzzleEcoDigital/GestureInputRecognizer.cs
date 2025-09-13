// GestureInputRecognizer.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

[Serializable] public class GestureEvent : UnityEvent<GestureSymbol, float> {}

[RequireComponent(typeof(LineRenderer))]
public class GestureInputRecognizer : MonoBehaviour
{
    [Header("Captura")]
    [Tooltip("Se true, ignora toques quando o ponteiro estiver sobre UI.")]
    public bool ignorarSobreUI = true;

    [Header("Desenho")]
    public float minComprimentoParaReconhecer = 50f;
    public float larguraLinha = 0.03f;
    public Gradient corLinha;

    [Header("Reconhecedor")]
    public int pontosAmostra = 64;
    public float tamanhoNormalizacao = 1f;
    public float anguloPasso = Mathf.Deg2Rad * 2f;
    public float anguloFaixa = Mathf.Deg2Rad * 30f;
    public float scoreAceitacao = 0.75f; // 0..1

    public GestureEvent OnGestureRecognized;

    private LineRenderer lr;
    private readonly List<Vector2> strokeScreen = new();
    private Dictionary<GestureSymbol, List<Vector2>> templates;
    private Camera uiCam;
    private bool desenhando;

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        lr.positionCount = 0;
        lr.widthMultiplier = larguraLinha;
        if (corLinha != null) lr.colorGradient = corLinha;

        templates = GestureTemplates.Load();
        uiCam = Camera.main;
    }

    void Update()
    {
        bool down = Input.GetMouseButtonDown(0);
        bool hold = Input.GetMouseButton(0);
        bool up   = Input.GetMouseButtonUp(0);

        if (down)
        {
            if (ignorarSobreUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            ComecarDesenho();
            AdicionarPonto(Input.mousePosition);
        }
        else if (hold && desenhando)
        {
            AdicionarPonto(Input.mousePosition);
        }
        else if (up && desenhando)
        {
            FinalizarDesenho();
        }
    }

    void ComecarDesenho()
    {
        desenhando = true;
        strokeScreen.Clear();
        lr.positionCount = 0;
    }

    void AdicionarPonto(Vector3 screen)
    {
        Vector2 p = new(screen.x, screen.y);
        if (strokeScreen.Count == 0 || (p - strokeScreen[^1]).sqrMagnitude > 4f) // 2px
        {
            strokeScreen.Add(p);
            // desenha em world no plano da câmera (z=1 para passar do near plane)
            Vector3 w = uiCam != null ? uiCam.ScreenToWorldPoint(new Vector3(p.x, p.y, 1f)) : new Vector3(p.x, p.y, 0f);
            lr.positionCount++;
            lr.SetPosition(lr.positionCount - 1, w);
        }
    }

    void FinalizarDesenho()
    {
        desenhando = false;
        if (lr != null) lr.positionCount = 0;

        if (Comprimento(strokeScreen) < minComprimentoParaReconhecer || strokeScreen.Count < 8)
            return;

        var (simbolo, score) = Reconhecer(strokeScreen);
        if (score >= scoreAceitacao)
            OnGestureRecognized?.Invoke(simbolo, score);
    }

    // ===================== $1 RECOGNIZER (simplificado) =====================
    (GestureSymbol, float) Reconhecer(List<Vector2> pontosTela)
    {
        var pts = Preprocess(pontosTela);

        GestureSymbol melhor = GestureSymbol.Circulo;
        float melhorDist = float.MaxValue;

        foreach (var kv in templates)
        {
            var temp = Preprocess(ConverterTemplateParaTela(kv.Value, pontosTela));
            float d = DistanciaComRotacao(pts, temp);
            if (d < melhorDist)
            {
                melhorDist = d;
                melhor = kv.Key;
            }
        }

        float maxDist = tamanhoNormalizacao * 0.5f;
        float score = 1f - Mathf.Clamp01(melhorDist / maxDist);
        return (melhor, score);
    }

    List<Vector2> Preprocess(List<Vector2> pts)
    {
        var r = Resample(pts, pontosAmostra);
        float ang = IndicativeAngle(r);
        r = RotateBy(r, -ang);
        r = ScaleTo(r, tamanhoNormalizacao);
        r = TranslateTo(r, Vector2.zero);
        return r;
    }

    List<Vector2> ConverterTemplateParaTela(List<Vector2> temp01, List<Vector2> stroke)
    {
        var (min, max) = AABB(stroke);
        Vector2 size = max - min;
        var list = new List<Vector2>(temp01.Count);
        foreach (var p in temp01)
            list.Add(new Vector2(min.x + p.x * size.x, min.y + p.y * size.y));
        return list;
    }

    float DistanciaComRotacao(List<Vector2> a, List<Vector2> b)
    {
        float best = float.MaxValue;
        for (float t = -anguloFaixa; t <= anguloFaixa; t += anguloPasso)
        {
            var rb = RotateBy(b, t);
            float d = PathDistance(a, rb);
            if (d < best) best = d;
        }
        return best;
    }

    // -------- utilitários do $1 --------
    static List<Vector2> Resample(List<Vector2> pts, int n)
    {
        float pathLen = PathLength(pts);
        if (pathLen <= 0f || n <= 2) return new List<Vector2>(pts);

        float I = pathLen / (n - 1);
        float D = 0f;

        var newPts = new List<Vector2>(n) { pts[0] };
        for (int i = 1; i < pts.Count; i++)
        {
            float d = Vector2.Distance(pts[i - 1], pts[i]);
            if (d == 0f) continue;

            while (D + d >= I)
            {
                float t = (I - D) / d;
                Vector2 q = Vector2.Lerp(pts[i - 1], pts[i], t);
                newPts.Add(q);
                // “avança” segmento a partir de q
                pts[i - 1] = q;
                d = Vector2.Distance(pts[i - 1], pts[i]);
                D = 0f;
            }
            D += d;
        }
        if (newPts.Count < n) newPts.Add(pts[^1]);
        return newPts;
    }

    static float PathLength(List<Vector2> pts)
    {
        float L = 0f;
        for (int i = 1; i < pts.Count; i++) L += Vector2.Distance(pts[i - 1], pts[i]);
        return L;
    }

    static float IndicativeAngle(List<Vector2> pts)
    {
        Vector2 c = Centroid(pts);
        return Mathf.Atan2(c.y - pts[0].y, c.x - pts[0].x);
    }

    static List<Vector2> RotateBy(List<Vector2> pts, float angle)
    {
        Vector2 c = Centroid(pts);
        float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
        var r = new List<Vector2>(pts.Count);
        for (int i = 0; i < pts.Count; i++)
        {
            Vector2 d = pts[i] - c;
            r.Add(new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos) + c);
        }
        return r;
    }

    static List<Vector2> ScaleTo(List<Vector2> pts, float size)
    {
        var (min, max) = AABB(pts);
        Vector2 s = max - min;
        float scale = (s.x > s.y) ? size / Mathf.Max(s.x, 1e-6f) : size / Mathf.Max(s.y, 1e-6f);
        var r = new List<Vector2>(pts.Count);
        foreach (var p in pts) r.Add((p - min) * scale);
        return r;
    }

    static List<Vector2> TranslateTo(List<Vector2> pts, Vector2 to)
    {
        Vector2 c = Centroid(pts);
        var r = new List<Vector2>(pts.Count);
        foreach (var p in pts) r.Add(p + (to - c));
        return r;
    }

    static (Vector2 min, Vector2 max) AABB(List<Vector2> pts)
    {
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var p in pts)
        {
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        return (min, max);
    }

    static float PathDistance(List<Vector2> a, List<Vector2> b)
    {
        float d = 0f;
        for (int i = 0; i < a.Count; i++)
            d += Vector2.Distance(a[i], b[i]);
        return d / a.Count;
    }

    static float Comprimento(List<Vector2> pts) => PathLength(pts);

    // ✔️ Faltava esta função:
    static Vector2 Centroid(List<Vector2> pts)
    {
        if (pts == null || pts.Count == 0) return Vector2.zero;
        Vector2 s = Vector2.zero;
        for (int i = 0; i < pts.Count; i++) s += pts[i];
        return s / pts.Count;
    }
}
