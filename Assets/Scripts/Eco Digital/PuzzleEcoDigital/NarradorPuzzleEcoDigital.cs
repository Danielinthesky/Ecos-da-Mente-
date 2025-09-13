using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.Events;

[System.Serializable]
public class TextoPorAnimacao
{
    [Tooltip("Nome exato do estado no Animator (layer definida abaixo)")]
    public string stateName;

    [Tooltip("Linhas a exibir (em ordem). Cada linha é digitada; as mais antigas esmaecem.")]
    [TextArea(2, 6)]
    public string[] linhas;
}

[RequireComponent(typeof(RectTransform))]
public class NarradorPuzzleEcoDigital : MonoBehaviour
{
    [Header("Referências")]
    [SerializeField] private TMP_Text alvoTMP;
    [SerializeField] private Animator animator;
    [SerializeField, Min(0)] private int animatorLayerIndex = 0;

    [Header("Gatilhos")]
    [Tooltip("Se verdadeiro, detecta o estado atual e dispara automaticamente quando entrar em um state mapeado.")]
    [SerializeField] private bool autoStartOnStateEnter = true;
    [SerializeField] private bool reiniciarAoReentrar = true;

    [Header("Tempo")]
    [Tooltip("Tempo usado se não conseguir medir a duração do state (s).")]
    [SerializeField, Min(0.01f)] private float duracaoFallback = 3f;

    [Header("Layout / Apresentação")]
    [Tooltip("Máximo de linhas *anteriores* que permanecem visíveis (sem contar a linha que está sendo digitada).")]
    [SerializeField, Range(0, 5)] private int maxLinhasPreviasVisiveis = 2;

    [Tooltip("Duração do fade-out (s) da linha mais antiga quando passa a ser 'excedente'.")]
    [SerializeField, Min(0f)] private float duracaoFadeOut = 0.6f;

    [Tooltip("Inserir uma linha em branco entre as linhas (apenas visual).")]
    [SerializeField] private bool linhaEmBrancoEntre = false;

    [Header("Mapeamentos")]
    [SerializeField] private List<TextoPorAnimacao> scriptsPorAnimacao = new();

    [Header("Eventos")]
    public UnityEvent OnNarracaoIniciada;
    public UnityEvent OnNarracaoConcluida;

    // -------- internos --------
    private readonly Dictionary<string, string[]> _map = new();
    private Coroutine _rotina;
    private string _estadoAtualNarrado;
    private bool _narrando;

    // estado de linhas exibidas (para controlar alpha por linha)
    private class LinhaEstado
    {
        public string texto;
        public float alpha = 1f;     // 1 = visível ; 0 = invisível
        public bool emFade = false;
        public float fadeTimer = 0f;
    }
    private readonly List<LinhaEstado> _buffer = new(); // mantém as linhas que já apareceram + a atual (parcial)

    private void Awake()
    {
        if (alvoTMP == null) alvoTMP = GetComponent<TMP_Text>();
        _map.Clear();
        foreach (var item in scriptsPorAnimacao)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.stateName)) continue;
            _map[item.stateName] = item.linhas ?? System.Array.Empty<string>();
        }
    }

    private void Update()
    {
        if (!autoStartOnStateEnter || animator == null) return;

        var st = animator.GetCurrentAnimatorStateInfo(animatorLayerIndex);
        foreach (var kv in _map)
        {
            if (st.IsName(kv.Key))
            {
                if (reiniciarAoReentrar || _estadoAtualNarrado != kv.Key || !_narrando)
                {
                    StartNarrationForState(kv.Key);
                }
                return;
            }
        }
    }

    // ---------------- API pública ----------------

    public void StartNarrationForCurrentState()
    {
        if (animator == null) return;
        var st = animator.GetCurrentAnimatorStateInfo(animatorLayerIndex);
        foreach (var key in _map.Keys)
        {
            if (st.IsName(key))
            {
                StartNarrationForState(key);
                return;
            }
        }
        Debug.LogWarning("[NarradorPuzzleEcoDigital] Estado atual não está mapeado.");
    }

    public void StartNarrationForState(string stateName)
    {
        if (!_map.ContainsKey(stateName))
        {
            Debug.LogWarning($"[NarradorPuzzleEcoDigital] State '{stateName}' não mapeado.");
            return;
        }

        if (_rotina != null) StopCoroutine(_rotina);
        _rotina = StartCoroutine(RotinaNarrarPorLinhas(_map[stateName], stateName));
        _estadoAtualNarrado = stateName;
    }

    public void StopAndClear()
    {
        if (_rotina != null) StopCoroutine(_rotina);
        _rotina = null;
        _narrando = false;
        _buffer.Clear();
        if (alvoTMP != null)
        {
            alvoTMP.text = string.Empty;
            alvoTMP.maxVisibleCharacters = int.MaxValue; // garante texto inteiro visível quando houver
        }
    }

    // ---------------- núcleo da narração ----------------

    private IEnumerator RotinaNarrarPorLinhas(string[] linhas, string stateName)
    {
        _narrando = true;
        OnNarracaoIniciada?.Invoke();

        _buffer.Clear();
        alvoTMP.text = string.Empty;
        alvoTMP.maxVisibleCharacters = int.MaxValue;

        // tempo efetivo do state
        float duracaoTotal = ObterDuracaoEfetivaDoEstado(stateName);
        if (duracaoTotal <= 0f) duracaoTotal = duracaoFallback;

        // distribuir tempo por linha proporcional ao nº de chars
        int somaChars = 0;
        foreach (var l in linhas) somaChars += (l?.Length ?? 0);
        if (somaChars <= 0) somaChars = Mathf.Max(1, linhas.Length); // evita zero

        // loop por linha
        for (int i = 0; i < linhas.Length; i++)
        {
            string linha = linhas[i] ?? string.Empty;
            int chars = Mathf.Max(1, linha.Length);
            float durLinha = duracaoTotal * (chars / (float)somaChars);

            // antes de digitar a linha i, ver se alguém precisa entrar em fade:
            // manter até 'maxLinhasPreviasVisiveis' anteriores; a (i - (maxPrev + 1)) entra em fade agora
           // AGORA: sempre inicia fade na linha i-2
            int idxExcedente = i - 2;
            if (idxExcedente >= 0 && idxExcedente < _buffer.Count)
            {
                var alvo = _buffer[idxExcedente];
                if (!alvo.emFade) // evita reiniciar fade se já estiver sumindo
                {
                    alvo.emFade = true;
                    alvo.fadeTimer = 0f;
                }
            }


            // adiciona a nova linha no buffer com alpha 1, mas sendo digitada
            var estadoAtual = new LinhaEstado { texto = string.Empty, alpha = 1f, emFade = false };
            _buffer.Add(estadoAtual);

            // digita a linha
            float t = 0f;
            while (t < durLinha)
            {
                t += Time.deltaTime;
                int vis = Mathf.Clamp(Mathf.FloorToInt((t / durLinha) * linha.Length), 0, linha.Length);
                estadoAtual.texto = linha.Substring(0, vis);

                // avança fades das linhas excedentes
                AtualizarFades();

                // recompose
                alvoTMP.text = ComporTexto(_buffer, linhaEmBrancoEntre);
                yield return null;
            }

            // garante linha completa ao finalizar a digitação
            estadoAtual.texto = linha;
            AtualizarFades();
            alvoTMP.text = ComporTexto(_buffer, linhaEmBrancoEntre);

            // pequena folga opcional entre linhas? (aqui não; o pacing está na própria proporção de chars)
        }

        // após última linha, complete eventuais fades restantes (opcional)
        // Se quiser manter as linhas finais sem sumir, basta pular este trecho:
        bool aindaTemFade = ExisteLinhaEmFade();
        while (aindaTemFade)
        {
            AtualizarFades();
            alvoTMP.text = ComporTexto(_buffer, linhaEmBrancoEntre);
            aindaTemFade = ExisteLinhaEmFade();
            yield return null;
        }

        _narrando = false;
        OnNarracaoConcluida?.Invoke();
    }

    // ---------------- utilitários de fade e composição ----------------

    private bool ExisteLinhaEmFade()
    {
        for (int i = 0; i < _buffer.Count; i++)
            if (_buffer[i].emFade && _buffer[i].alpha > 0f) return true;
        return false;
    }

    private void AtualizarFades()
    {
        if (duracaoFadeOut <= 0f) return;

        for (int i = 0; i < _buffer.Count; i++)
        {
            var l = _buffer[i];
            if (!l.emFade) continue;

            l.fadeTimer += Time.deltaTime;
            float k = Mathf.Clamp01(l.fadeTimer / duracaoFadeOut);
            l.alpha = 1f - k;
        }

        // remove linhas que zeraram alpha para liberar espaço
        // (mantém a ordem das restantes)
        for (int i = _buffer.Count - 1; i >= 0; i--)
        {
            if (_buffer[i].emFade && _buffer[i].alpha <= 0f)
                _buffer.RemoveAt(i);
        }
    }

    private static string ColorTag(float alpha01)
    {
        byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha01) * 255f);
        // #FFFFFF + AA
        return $"<color=#FFFFFF{a:X2}>";
    }

    private string ComporTexto(List<LinhaEstado> buffer, bool blankBetween)
    {
        // Monta cada linha com seu alpha usando <color=#FFFFFFAA>linha</color>
        // A linha "em digitação" já está no buffer como última entrada.
        System.Text.StringBuilder sb = new System.Text.StringBuilder(256);

        for (int i = 0; i < buffer.Count; i++)
        {
            var l = buffer[i];
            string open = ColorTag(l.alpha);
            sb.Append(open);
            sb.Append(l.texto);
            sb.Append("</color>");

            if (i < buffer.Count - 1)
            {
                sb.Append('\n');
                if (blankBetween) sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    // ---------------- duração efetiva da animação ----------------

    private float ObterDuracaoEfetivaDoEstado(string stateName)
    {
        if (animator == null) return 0f;

        var st = animator.GetCurrentAnimatorStateInfo(animatorLayerIndex);

        // Se o estado já está ativo, pega a duração/reprodução efetiva
        if (st.IsName(stateName))
        {
            float length = st.length;
            float speedEff = Mathf.Max(1e-6f, animator.speed * st.speedMultiplier);
            return length / speedEff;
        }

        // Caso contrário, tenta a partir do 1º clip da layer atual
        var infos = animator.GetCurrentAnimatorClipInfo(animatorLayerIndex);
        if (infos != null && infos.Length > 0)
        {
            float len = infos[0].clip != null ? infos[0].clip.length : 0f;
            float speed = Mathf.Max(1e-6f, animator.speed * st.speedMultiplier);
            return len / speed;
        }

        return 0f;
    }
}
