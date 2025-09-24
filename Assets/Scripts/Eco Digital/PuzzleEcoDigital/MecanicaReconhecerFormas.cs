using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class MecanicaReconhecerFormas : MonoBehaviour
{
    public enum AcaoAposReconhecer { Nenhuma, InstanciarPrefab, DestruirAlvos }
    public enum PrioridadeForma { PrimeiroQueBater, PriorizarCirculo, PriorizarV }
    public enum SelectionScope { OnlyInsideGesture, AllOfType }

    [Header("Refs")]
    [SerializeField] private MecanicaDesenhoNaTela desenho;  // se vazio, pega no mesmo GO

    // ============ CÍRCULO ============
    [Header("Círculo (aproximado) - thresholds em PX")]
    [SerializeField, Min(40f)] private float compMinTracoPx = 120f;
    [Tooltip("Fim precisa estar a <= k * RaioMedio do início. 0.8 é tolerante.")]
    [SerializeField, Min(0f)] private float toleranciaFechamentoR = 0.8f;
    [Tooltip("Redondez = std/raio. <= 0.35 é permissivo.")]
    [SerializeField, Range(0.05f, 0.6f)] private float toleranciaRedondezRsd = 0.35f;

    // ============ V ============
    [Header("V (um único canto agudo) - thresholds em PX/Graus")]
    [SerializeField] private bool habilitarV = true;
    [SerializeField, Min(40f)] private float compMinTracoPx_V = 120f;
    [SerializeField, Range(10f, 170f)] private float anguloVMinDeg = 40f;
    [SerializeField, Range(10f, 170f)] private float anguloVMaxDeg = 110f;
    [Tooltip("Retilineidade de cada perna (RMS normalizado). 0.12 tolerante; 0.06 rígido.")]
    [SerializeField, Range(0.02f, 0.3f)] private float toleranciaRetilineidadeRMS = 0.12f;
    [Tooltip("Cada perna deve ter ao menos esta fração do comprimento total.")]
    [SerializeField, Range(0.05f, 0.6f)] private float minFracaoPerna = 0.25f;
    [Tooltip("O vértice deve cair entre estas frações do comprimento total.")]
    [SerializeField, Range(0.0f, 1.0f)] private float posVerticeMinFrac = 0.25f;
    [SerializeField, Range(0.0f, 1.0f)] private float posVerticeMaxFrac = 0.75f;

    // ============ AÇÃO / PRIORIDADE ============
    [Header("Resolução quando várias formas batem")]
    [SerializeField] private PrioridadeForma prioridade = PrioridadeForma.PrimeiroQueBater;

    [Header("Ação ao reconhecer")]
    [SerializeField] private AcaoAposReconhecer acao = AcaoAposReconhecer.DestruirAlvos;

    [Tooltip("Usado apenas se 'InstanciarPrefab' estiver selecionado.")]
    [SerializeField] private GameObject prefabOnRecognized;
    [SerializeField] private bool alignPrefabToCamera = false;

    [Header("Seleção de alvos por forma")]
    [SerializeField] private SelectionScope selectionScopeCircle = SelectionScope.OnlyInsideGesture;
    [SerializeField] private SelectionScope selectionScopeV = SelectionScope.OnlyInsideGesture;

    [Tooltip("Marcadores/Tags do círculo")]
    [SerializeField] private bool circleByComponent = true;   // DestroyOnCircle
    [SerializeField] private bool circleByTag = false;
    [SerializeField] private string circleTag = "DestruivelO";
    [Tooltip("Multiplicador do raio do gesto para seleção (1.0..1.5 recomendado).")]
    [SerializeField, Min(0.5f)] private float circleSelectionRadiusMul = 1.1f;

    [Tooltip("Marcadores/Tags do V")]
    [SerializeField] private bool vByComponent = true;        // DestroyOnV
    [SerializeField] private bool vByTag = false;
    [SerializeField] private string vTag = "DestruivelV";
    [Tooltip("Padding (em unidades do espaço 2D do gesto) para a AABB do traço.")]
    [SerializeField, Min(0f)] private float vAabbPadding = 0.2f;

    // ============ EVENTOS ============
    [System.Serializable] public class CircleEvent : UnityEvent<Vector3, float> {}    // (centro3D, raio)
    [System.Serializable] public class VEvent      : UnityEvent<Vector3, float> {}    // (vertice3D, angDeg)

    [Header("Eventos - Círculo")]
    public CircleEvent OnCircleRecognized;
    public UnityEvent OnCircleRecognizedSimple;

    [Header("Eventos - V")]
    public VEvent OnVRecognized;
    public UnityEvent OnVRecognizedSimple;

    private void OnEnable()
    {
        if (desenho == null) desenho = GetComponent<MecanicaDesenhoNaTela>();
        if (desenho != null) desenho.OnStrokeFinished += HandleStrokeFinished;
        else Debug.LogWarning("[MecanicaReconhecerFormas] Nenhum MecanicaDesenhoNaTela encontrado.");
    }

    private void OnDisable()
    {
        if (desenho != null) desenho.OnStrokeFinished -= HandleStrokeFinished;
    }

    private void HandleStrokeFinished(IReadOnlyList<Vector3> pts3D, IReadOnlyList<Vector2> pts2D, float unitsPerPixel)
    {
        if (pts3D == null || pts3D.Count < 8) return;
        if (pts2D == null || pts2D.Count < 8) return;

        float minLenCircle = compMinTracoPx   * Mathf.Max(0.000001f, unitsPerPixel);
        float minLenV      = compMinTracoPx_V * Mathf.Max(0.000001f, unitsPerPixel);

        bool gotCircle = false, gotV = false;
        Vector3 circleC3 = default; float circleR = 0f;
        int vIdx = -1; float vAng = 0f; Vector3 v3 = default;

        // ---- V ----
        if (habilitarV && PathLength2D(pts2D) >= minLenV)
        {
            if (TryRecognizeV(pts2D, pts3D, out vIdx, out vAng, out v3))
            {
                if (vAng >= Mathf.Min(anguloVMinDeg, anguloVMaxDeg) &&
                    vAng <= Mathf.Max(anguloVMinDeg, anguloVMaxDeg))
                    gotV = true;
            }
        }

        // ---- O ----
        if (PathLength2D(pts2D) >= minLenCircle)
        {
            if (TryRecognizeCircle(pts2D, out circleC3, out circleR, pts3D))
                gotCircle = true;
        }

        // Prioridade
        if (prioridade == PrioridadeForma.PriorizarV && gotV) gotCircle = false;
        else if (prioridade == PrioridadeForma.PriorizarCirculo && gotCircle) gotV = false;

        // Eventos + Ações
        if (gotV)
        {
            OnVRecognized?.Invoke(v3, vAng);
            OnVRecognizedSimple?.Invoke();
            ExecutarAcaoV(pts2D, v3);
        }

        if (gotCircle)
        {
            OnCircleRecognized?.Invoke(circleC3, circleR);
            OnCircleRecognizedSimple?.Invoke();
            ExecutarAcaoCircle(pts2D, circleC3, circleR);
        }
    }

    // ================== AÇÕES ==================
    private void ExecutarAcaoCircle(IReadOnlyList<Vector2> pts2D, Vector3 center3, float radius)
    {
        switch (acao)
        {
            case AcaoAposReconhecer.Nenhuma: return;
            case AcaoAposReconhecer.InstanciarPrefab:
                if (prefabOnRecognized != null)
                {
                    Quaternion rot = Quaternion.identity;
                    if (alignPrefabToCamera && desenho != null && desenho.EffectiveCamera != null)
                        rot = Quaternion.LookRotation(desenho.EffectiveCamera.transform.forward, Vector3.up);
                    Instantiate(prefabOnRecognized, center3, rot);
                }
                return;

            case AcaoAposReconhecer.DestruirAlvos:
                if (selectionScopeCircle == SelectionScope.AllOfType)
                {
                    DestruirPorTipo(circle:true, filtroLocal:null, raioLocal:0f);
                }
                else
                {
                    // Disco em 2D
                    Vector2 center2 = Centroid2D(pts2D); // coerente com o cálculo do raio médio
                    float rSel = radius * circleSelectionRadiusMul;
                    DestruirPorTipo(circle:true, filtroLocal:(p2 => Vector2.Distance(p2, center2) <= rSel), raioLocal:rSel);
                }
                return;
        }
    }

    private void ExecutarAcaoV(IReadOnlyList<Vector2> pts2D, Vector3 v3)
    {
        switch (acao)
        {
            case AcaoAposReconhecer.Nenhuma: return;
            case AcaoAposReconhecer.InstanciarPrefab:
                if (prefabOnRecognized != null)
                {
                    Quaternion rot = Quaternion.identity;
                    if (alignPrefabToCamera && desenho != null && desenho.EffectiveCamera != null)
                        rot = Quaternion.LookRotation(desenho.EffectiveCamera.transform.forward, Vector3.up);
                    Instantiate(prefabOnRecognized, v3, rot);
                }
                return;

            case AcaoAposReconhecer.DestruirAlvos:
                if (selectionScopeV == SelectionScope.AllOfType)
                {
                    DestruirPorTipo(circle:false, filtroLocal:null, raioLocal:0f);
                }
                else
                {
                    // Caixa delimitadora 2D do traço + padding
                    Vector2 min = pts2D[0], max = pts2D[0];
                    for (int i = 1; i < pts2D.Count; i++)
                    {
                        var p = pts2D[i];
                        if (p.x < min.x) min.x = p.x;
                        if (p.y < min.y) min.y = p.y;
                        if (p.x > max.x) max.x = p.x;
                        if (p.y > max.y) max.y = p.y;
                    }
                    min -= Vector2.one * vAabbPadding;
                    max += Vector2.one * vAabbPadding;

                    DestruirPorTipo(circle:false, filtroLocal:(p2 => p2.x >= min.x && p2.y >= min.y && p2.x <= max.x && p2.y <= max.y), raioLocal:0f);
                }
                return;
        }
    }

    /// <summary>
    /// Procura e destrói alvos de um tipo (círculo ou V), opcionalmente filtrando por uma região 2D no espaço do gesto.
    /// </summary>
    private void DestruirPorTipo(bool circle, System.Predicate<Vector2> filtroLocal, float raioLocal)
    {
        int count = 0;

        // Por componente
        if ((circle && circleByComponent) || (!circle && vByComponent))
        {
            if (circle)
            {
                var alvos = FindObjectsOfType<DestroyOnCircle>(false);
                for (int i = 0; i < alvos.Length; i++)
                {
                    if (alvos[i] == null) continue;
                    if (filtroLocal == null || PontoAlvoPassaFiltro(alvos[i].transform.position, filtroLocal))
                    {
                        Destroy(alvos[i].gameObject);
                        count++;
                    }
                }
            }
            else
            {
                var alvos = FindObjectsOfType<DestroyOnV>(false);
                for (int i = 0; i < alvos.Length; i++)
                {
                    if (alvos[i] == null) continue;
                    if (filtroLocal == null || PontoAlvoPassaFiltro(alvos[i].transform.position, filtroLocal))
                    {
                        Destroy(alvos[i].gameObject);
                        count++;
                    }
                }
            }
        }

        // Por tag
        if ((circle && circleByTag && !string.IsNullOrEmpty(circleTag)) ||
            (!circle && vByTag && !string.IsNullOrEmpty(vTag)))
        {
            string useTag = circle ? circleTag : vTag;
            var gos = GameObject.FindGameObjectsWithTag(useTag);
            for (int i = 0; i < gos.Length; i++)
            {
                var go = gos[i];
                if (go == null) continue;
                if (filtroLocal == null || PontoAlvoPassaFiltro(go.transform.position, filtroLocal))
                {
                    Destroy(go);
                    count++;
                }
            }
        }

        if (count == 0)
        {
            string tipo = circle ? "círculo (O)" : "V";
            Debug.Log($"[MecanicaReconhecerFormas] Nenhum alvo {tipo} encontrado no critério de seleção.");
        }
    }

    /// <summary>
    /// Converte a posição de um alvo no mundo para o mesmo espaço 2D usado pelos pts2D do gesto
    /// e aplica o filtroLocal.
    /// </summary>
    private bool PontoAlvoPassaFiltro(Vector3 worldPos, System.Predicate<Vector2> filtroLocal)
    {
        if (filtroLocal == null) return true;

        // Projetar para o espaço 2D do gesto
        Vector2 p2;
        if (!TryProjectWorldToGesture2D(worldPos, out p2))
            return false;

        return filtroLocal(p2);
    }

    /// <summary>
    /// Projeta worldPos para o mesmo espaço 2D usado nos pts2D do gesto.
    /// Para OverlayCamera, assumimos que os pts2D estão no espaço LOCAL da overlayCam (setup recomendado).
    /// Para World: XY ou XZ.
    /// </summary>
    private bool TryProjectWorldToGesture2D(Vector3 worldPos, out Vector2 p2)
{
    p2 = Vector2.zero;
    if (desenho == null) return false;

    var cam = desenho.EffectiveCamera;

    if (desenho.Mode == MecanicaDesenhoNaTela.DrawSpace.World)
    {
        if (desenho.Plane == MecanicaDesenhoNaTela.ProjectionPlane.XY)
            p2 = new Vector2(worldPos.x, worldPos.y);
        else
            p2 = new Vector2(worldPos.x, worldPos.z);
        return true;
    }
    else // OverlayCamera
    {
        if (cam == null) return false;

        // Se o desenho grava pontos em LOCAL SPACE da overlayCam, projetamos pro local.
        // Se grava em WORLD (overlayUseLocalSpace = false), comparamos em WORLD (x,y).
        if (desenho.OverlayUseLocalSpace)
        {
            Vector3 local = cam.transform.InverseTransformPoint(worldPos);
            p2 = new Vector2(local.x, local.y);
        }
        else
        {
            p2 = new Vector2(worldPos.x, worldPos.y);
        }
        return true;
    }
}


    // ================== RECOGNIZERS ==================
    private bool TryRecognizeCircle(IReadOnlyList<Vector2> pts2D, out Vector3 center3, out float radius, IReadOnlyList<Vector3> pts3D)
    {
        center3 = default; radius = 0f;

        Vector2 c2 = Centroid2D(pts2D);
        float meanR = 0f;
        for (int i = 0; i < pts2D.Count; i++) meanR += Vector2.Distance(pts2D[i], c2);
        meanR /= pts2D.Count;
        if (meanR <= Mathf.Epsilon) return false;

        float variance = 0f;
        for (int i = 0; i < pts2D.Count; i++)
        {
            float d = Vector2.Distance(pts2D[i], c2) - meanR;
            variance += d * d;
        }
        variance /= pts2D.Count;
        float std = Mathf.Sqrt(variance);
        float rsd = std / meanR;

        float closure = Vector2.Distance(pts2D[0], pts2D[pts2D.Count - 1]);
        bool closedEnough = closure <= (toleranciaFechamentoR * meanR);
        bool isCircle = closedEnough && (rsd <= toleranciaRedondezRsd);
        if (!isCircle) return false;

        center3 = Centroid3D(pts3D);
        radius = meanR;
        return true;
    }

    private bool TryRecognizeV(IReadOnlyList<Vector2> pts2D, IReadOnlyList<Vector3> pts3D, out int idxVertice, out float anguloDeg, out Vector3 vertice3)
    {
        idxVertice = -1; anguloDeg = 0f; vertice3 = default;
        int n = pts2D.Count;
        if (n < 8) return false;

        int window = Mathf.Clamp(n / 12, 2, 8);
        float minAngle = 181f; int minIdx = -1;

        for (int i = window; i < n - window; i++)
        {
            Vector2 a = pts2D[i - window];
            Vector2 b = pts2D[i];
            Vector2 c = pts2D[i + window];

            Vector2 v1 = (a - b).normalized;
            Vector2 v2 = (c - b).normalized;

            float dot = Mathf.Clamp(Vector2.Dot(v1, v2), -1f, 1f);
            float ang = Mathf.Acos(dot) * Mathf.Rad2Deg;

            if (ang < minAngle)
            {
                minAngle = ang;
                minIdx = i;
            }
        }

        if (minIdx < 0) return false;

        float totalLen = PathLength2D(pts2D);
        float lenA = PathLength2D(pts2D, 0, minIdx);
        float lenB = PathLength2D(pts2D, minIdx, n - 1);

        if (lenA < totalLen * minFracaoPerna) return false;
        if (lenB < totalLen * minFracaoPerna) return false;

        float fracVert = lenA / Mathf.Max(0.000001f, totalLen);
        if (fracVert < posVerticeMinFrac || fracVert > posVerticeMaxFrac) return false;

        if (minAngle < Mathf.Min(anguloVMinDeg, anguloVMaxDeg) || minAngle > Mathf.Max(anguloVMinDeg, anguloVMaxDeg))
            return false;

        float rmsA = NormalizedRMSDistanceToSegment(pts2D, 0, minIdx);
        float rmsB = NormalizedRMSDistanceToSegment(pts2D, minIdx, n - 1);
        if (rmsA > toleranciaRetilineidadeRMS || rmsB > toleranciaRetilineidadeRMS)
            return false;

        float endsDist = Vector2.Distance(pts2D[0], pts2D[n - 1]);
        if (endsDist < 0.30f * totalLen) return false;

        idxVertice = minIdx;
        anguloDeg = minAngle;
        vertice3 = pts3D[Mathf.Clamp(minIdx, 0, pts3D.Count - 1)];
        return true;
    }

    // ================== Utils geométricas ==================
    private static float PathLength2D(IReadOnlyList<Vector2> pts)
    {
        float d = 0f;
        for (int i = 1; i < pts.Count; i++) d += Vector2.Distance(pts[i - 1], pts[i]);
        return d;
    }
    private static float PathLength2D(IReadOnlyList<Vector2> pts, int i0, int i1)
    {
        if (i1 <= i0) return 0f;
        float d = 0f;
        for (int i = i0 + 1; i <= i1; i++) d += Vector2.Distance(pts[i - 1], pts[i]);
        return d;
    }
    private static Vector2 Centroid2D(IReadOnlyList<Vector2> pts)
    {
        Vector2 s = Vector2.zero;
        for (int i = 0; i < pts.Count; i++) s += pts[i];
        return s / Mathf.Max(1, pts.Count);
    }
    private static Vector3 Centroid3D(IReadOnlyList<Vector3> pts)
    {
        Vector3 s = Vector3.zero;
        for (int i = 0; i < pts.Count; i++) s += pts[i];
        return s / Mathf.Max(1, pts.Count);
    }

    private static float NormalizedRMSDistanceToSegment(IReadOnlyList<Vector2> pts, int i0, int i1)
    {
        if (i1 <= i0 + 1) return 0f;
        Vector2 a = pts[i0];
        Vector2 b = pts[i1];
        float segLen = Vector2.Distance(a, b);
        if (segLen <= Mathf.Epsilon) return 0f;

        float sum2 = 0f; int cnt = 0;
        for (int i = i0 + 1; i < i1; i++)
        {
            float dist = DistancePointToSegment(pts[i], a, b);
            sum2 += dist * dist;
            cnt++;
        }
        float rms = Mathf.Sqrt(sum2 / Mathf.Max(1, cnt));
        return rms / segLen;
    }

    private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f);
        t = Mathf.Clamp01(t);
        Vector2 proj = a + t * ab;
        return Vector2.Distance(p, proj);
    }
}
