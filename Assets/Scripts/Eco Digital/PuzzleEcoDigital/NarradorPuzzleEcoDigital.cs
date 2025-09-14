using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.Events;

[System.Serializable]
public class TextoPorAnimacao
{
    [Tooltip("Nome exato do state no Animator (só o nome do state, sem 'Base Layer.')")]
    public string stateName;

    [Tooltip("Linhas a exibir (em ordem). Uma por vez, no mesmo lugar.")]
    [TextArea(2, 6)]
    public string[] linhas;

    [Tooltip("Se > 0, usa este tempo total para TODAS as linhas deste state (ignora o padrão global).")]
    [Min(0f)] public float duracaoTotalOverride = 0f;
}

[RequireComponent(typeof(RectTransform))]
public class NarradorPuzzleEcoDigital : MonoBehaviour
{
    public enum ModoTemporizacao
    {
        DuracaoTotalDistribuida,   // distribui o tempo total entre as linhas proporcional ao nº de caracteres
        VelocidadePorCaractere     // usa caracteresPorSegundo
    }

    [Header("Referências")]
    [SerializeField] private TMP_Text alvoTMP;
    [SerializeField] private Animator animator;
    [SerializeField, Min(0)] private int animatorLayerIndex = 0;

    [Header("Auto-início")]
    [Tooltip("Se verdadeiro, tenta iniciar a narração no primeiro frame útil (após o Animator avaliar state).")]
    [SerializeField] private bool iniciarNoInicio = true;

    [Tooltip("Se definido, força iniciar por este state no começo (ignora detecção automática no 1º frame).")]
    [SerializeField] private string stateInicialOverride = "";

    [Header("Gatilhos em runtime")]
    [Tooltip("Se verdadeiro, detecta continuamente o state atual e inicia quando entrar em um state mapeado.")]
    [SerializeField] private bool autoStartOnStateEnter = true;

    [Tooltip("Se verdadeiro, ao SAIR e voltar para o mesmo state, a narração pode reiniciar (limpa concluídos).")]
    [SerializeField] private bool reiniciarAoReentrar = true;

    [Header("Temporização")]
    [SerializeField] private ModoTemporizacao modoTempo = ModoTemporizacao.DuracaoTotalDistribuida;
    [SerializeField, Min(0.01f)] private float duracaoTotalPadrao = 6f;
    [SerializeField, Min(1f)] private float caracteresPorSegundo = 18f;

    [Header("Apresentação")]
    [SerializeField, Min(0f)] private float duracaoFadeEntreLinhas = 0.35f;
    [SerializeField, Min(0f)] private float pausaAposDigitar = 0.0f;
    [SerializeField] private bool fazerFadeNaUltimaLinha = false;

    [Header("Mapeamentos")]
    [SerializeField] private List<TextoPorAnimacao> scriptsPorAnimacao = new();

    [Header("Eventos")]
    public UnityEvent OnNarracaoIniciada;
    public UnityEvent OnNarracaoConcluida;

    [Header("Integração com Game Manager")]
    [Tooltip("GameManager que deve ser notificado quando um state específico terminar suas linhas.")]
    [SerializeField] private EcoDigitalGameManager ecoGameManager;

    [Tooltip("Nome do state que, ao concluir todas as linhas, deve abrir o painel de início.")]
    [SerializeField] private string stateParaAbrirPainel = "CameraJogo3";

    // -------- internos --------
    private readonly Dictionary<string, TextoPorAnimacao> _map = new();
    private readonly HashSet<string> _statesConcluidos = new();
    private Coroutine _rotina;
    private string _stateAtual;
    private string _stateAnterior;
    private string _stateNarrado;
    private bool _narrando;
    private Color _corOriginal;

    private void Awake()
    {
        if (alvoTMP == null) alvoTMP = GetComponent<TMP_Text>();
        _corOriginal = alvoTMP != null ? alvoTMP.color : Color.white;

        _map.Clear();
        foreach (var item in scriptsPorAnimacao)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.stateName)) continue;
            _map[item.stateName] = item;
        }

        if (ecoGameManager == null)
            ecoGameManager = FindFirstObjectByType<EcoDigitalGameManager>();
    }

    private void OnEnable()
    {
        // Arranque inicial após 1 frame, para o Animator já ter avaliado o primeiro state.
        if (iniciarNoInicio)
            StartCoroutine(_ArranqueInicial());
    }

    private IEnumerator _ArranqueInicial()
    {
        // aguarda um frame
        yield return null;

        if (!string.IsNullOrEmpty(stateInicialOverride))
        {
            StartNarrationForState(stateInicialOverride);
            yield break;
        }

        // tenta iniciar com o state atual detectado
        var det = DetectarStateMapeado();
        if (!string.IsNullOrEmpty(det))
        {
            StartNarrationForState(det);
        }
        else if (autoStartOnStateEnter)
        {
            // não achou no primeiro frame; o Update seguirá tentando quando o Animator entrar em um state mapeado
        }
    }

    private void Update()
    {
        if (!autoStartOnStateEnter || animator == null) return;

        _stateAnterior = _stateAtual;
        _stateAtual = DetectarStateMapeado();

        // Se mudou de state
        if (_stateAtual != _stateAnterior)
        {
            if (reiniciarAoReentrar)
                _statesConcluidos.Clear();
        }

        if (!string.IsNullOrEmpty(_stateAtual))
        {
            if (!_narrando && !_statesConcluidos.Contains(_stateAtual))
                StartNarrationForState(_stateAtual);
        }
    }

    // ---------------- detecção robusta de state ----------------

    private bool StateInfoMatches(AnimatorStateInfo info, string stateName)
    {
        // 1) compara por shortNameHash (nome do state)
        int shortHash = Animator.StringToHash(stateName);
        if (info.shortNameHash == shortHash) return true;

        // 2) compara por caminho completo "LayerName.StateName" no fullPathHash
        string layerName = (animator != null && animatorLayerIndex >= 0 && animatorLayerIndex < animator.layerCount)
            ? animator.GetLayerName(animatorLayerIndex)
            : "Base Layer";

        string fullPath = $"{layerName}.{stateName}";
        int fullHash = Animator.StringToHash(fullPath);
        if (info.fullPathHash == fullHash) return true;

        // 3) fallback: API IsName (cobre variações internas)
        return info.IsName(stateName) || info.IsName(fullPath);
    }

    private string DetectarStateMapeado()
    {
        if (animator == null) return null;
        var st = animator.GetCurrentAnimatorStateInfo(animatorLayerIndex);
        foreach (var kv in _map)
        {
            if (StateInfoMatches(st, kv.Key))
                return kv.Key;
        }
        return null;
    }

    // ---------------- API pública ----------------

    public void StartNarrationForCurrentState()
    {
        var st = animator.GetCurrentAnimatorStateInfo(animatorLayerIndex);
        foreach (var key in _map.Keys)
        {
            if (StateInfoMatches(st, key))
            {
                StartNarrationForState(key);
                return;
            }
        }
        Debug.LogWarning("[NarradorPuzzleEcoDigital] Estado atual não está mapeado.");
    }

    public void StartNarrationForState(string stateName)
    {
        if (!_map.TryGetValue(stateName, out var bloco))
        {
            Debug.LogWarning($"[NarradorPuzzleEcoDigital] State '{stateName}' não mapeado.");
            return;
        }

        if (_statesConcluidos.Contains(stateName))
            return;

        if (_rotina != null) StopCoroutine(_rotina);
        _rotina = StartCoroutine(RotinaBloco(bloco, stateName));
        _stateNarrado = stateName;
    }

    public void StopAndClear()
    {
        if (_rotina != null) StopCoroutine(_rotina);
        _rotina = null;
        _narrando = false;
        if (alvoTMP != null)
        {
            alvoTMP.text = string.Empty;
            alvoTMP.color = _corOriginal;
        }
    }

    // ---------------- núcleo ----------------

    private IEnumerator RotinaBloco(TextoPorAnimacao bloco, string stateName)
    {
        _narrando = true;
        OnNarracaoIniciada?.Invoke();

        if (alvoTMP != null)
        {
            alvoTMP.text = string.Empty;
            alvoTMP.color = _corOriginal;
        }

        float duracaoTotal = (bloco.duracaoTotalOverride > 0f) ? bloco.duracaoTotalOverride : duracaoTotalPadrao;

        int somaChars = 0;
        if (bloco.linhas != null)
            foreach (var l in bloco.linhas) somaChars += (l?.Length ?? 0);
        if (somaChars <= 0) somaChars = Mathf.Max(1, bloco.linhas?.Length ?? 1);

        for (int i = 0; i < (bloco.linhas?.Length ?? 0); i++)
        {
            string linha = bloco.linhas[i] ?? string.Empty;

            float durLinha;
            if (modoTempo == ModoTemporizacao.DuracaoTotalDistribuida)
            {
                int chars = Mathf.Max(1, linha.Length);
                durLinha = duracaoTotal * (chars / (float)somaChars);
            }
            else
            {
                durLinha = Mathf.Max(1, linha.Length) / Mathf.Max(1f, caracteresPorSegundo);
            }

            // Digita
            yield return StartCoroutine(DigitarLinha(linha, durLinha));

            // Pausa opcional
            if (pausaAposDigitar > 0f) yield return new WaitForSeconds(pausaAposDigitar);

            // Fade-out
            bool ehUltima = (i == (bloco.linhas.Length - 1));
            if (!ehUltima || fazerFadeNaUltimaLinha)
            {
                if (duracaoFadeEntreLinhas > 0f)
                    yield return StartCoroutine(FadeOutAtual(duracaoFadeEntreLinhas));
                else if (alvoTMP != null)
                    alvoTMP.text = string.Empty;
            }
        }

        if (!string.IsNullOrEmpty(stateName))
            _statesConcluidos.Add(stateName);

        // Aviso ao Game Manager quando o state desejado concluir
        if (!string.IsNullOrEmpty(stateName) && stateName == stateParaAbrirPainel)
        {
            if (ecoGameManager == null)
                ecoGameManager = FindFirstObjectByType<EcoDigitalGameManager>();
            ecoGameManager?.MostrarPainelInicio();
        }

        _narrando = false;
        OnNarracaoConcluida?.Invoke();
    }

    private IEnumerator DigitarLinha(string linhaCompleta, float duracao)
    {
        if (alvoTMP == null) yield break;

        alvoTMP.text = string.Empty;
        alvoTMP.color = _corOriginal;
        duracao = Mathf.Max(0.0001f, duracao);

        float t = 0f;
        int len = linhaCompleta.Length;

        while (t < duracao)
        {
            t += Time.deltaTime;
            int vis = Mathf.Clamp(Mathf.FloorToInt((t / duracao) * len), 0, len);
            alvoTMP.text = (vis <= 0) ? string.Empty : linhaCompleta.Substring(0, vis);
            yield return null;
        }

        alvoTMP.text = linhaCompleta;
    }

    private IEnumerator FadeOutAtual(float dur)
    {
        if (alvoTMP == null) yield break;
        if (dur <= 0f) { alvoTMP.text = string.Empty; yield break; }

        Color c0 = alvoTMP.color;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            var c = c0; c.a = Mathf.Lerp(c0.a, 0f, k);
            alvoTMP.color = c;
            yield return null;
        }
        alvoTMP.text = string.Empty;
        var cReset = _corOriginal; cReset.a = 1f;
        alvoTMP.color = cReset;
    }
}
