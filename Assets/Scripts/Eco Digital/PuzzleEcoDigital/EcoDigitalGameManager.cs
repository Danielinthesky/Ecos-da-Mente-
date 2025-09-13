using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class EcoDigitalManager : MonoBehaviour
{
    public enum EstadoFase
    {
        Inativo,
        TransicaoCamera,
        Jogando,
        Concluida
    }

    [System.Serializable]
    public class EtapaConfig
    {
        [Header("Identificação")]
        [Tooltip("Nome amigável da etapa (apenas organizacional).")]
        public string nome = "Etapa";

        [Tooltip("Descrição/objetivo mostrado em UI (opcional).")]
        [TextArea] public string descricao;

        [Header("Câmera / Animação")]
        [Tooltip("Trigger no Animator da CÂMERA que inicia a movimentação para esta etapa.")]
        public string triggerCamera;

        [Tooltip("Nome do estado da ANIMAÇÃO de câmera (opcional, usado como fallback para detectar final).")]
        public string nomeEstadoCameraContem = "";

        [Tooltip("Tempo máximo para considerar a transição concluída caso não use Animation Event.")]
        [Min(0.1f)] public float timeoutTransicao = 2.0f;

        [Tooltip("Bloquear jogador durante a transição de câmera?")]
        public bool bloquearJogadorNaTransicao = true;

        [Header("Eventos")]
        public UnityEvent OnEtapaEntrou;     // Dispara quando a câmera terminou e a etapa fica “jogável”
        public UnityEvent OnEtapaConcluida;  // Dispara quando você chamar ConcluirEtapaAtual()

        [Header("Depuração")]
        [Tooltip("Mostrar gizmo/label desta etapa (puramente visual/editor).")]
        public bool debugMostrarNoEditor = false;
        [Tooltip("Ponto de interesse da etapa (apenas para referência visual opcional).")]
        public Transform referenciaVisual;
    }

    [Header("Referências")]
    [Tooltip("Animator responsável pelas animações de MOVIMENTO DA CÂMERA.")]
    [SerializeField] private Animator cameraAnimator;

    [Tooltip("Componentes de movimento a serem desativados durante transições de câmera.")]
    [SerializeField] private List<MonoBehaviour> componentesMovimentoParaDesativar = new List<MonoBehaviour>();

    [Tooltip("Opcional: PlayerInput do Eco para bloquear inputs durante transição.")]
    [SerializeField] private PlayerInput playerInput;

    [Tooltip("Opcional: UI de objetivo (exibir descricao).")]
    [SerializeField] private UnityEngine.UI.Text uiObjetivo; // pode ser TextMeshProUGUI, troque o tipo se usar TMP

    [Header("Etapas")]
    [Tooltip("Lista ordenada das etapas do puzzle (configuração principal).")]
    [SerializeField] private List<EtapaConfig> etapas = new List<EtapaConfig>();

    [Header("Hotkeys (Debug)")]
    [SerializeField] private bool debugHotkeys = true;
    [SerializeField] private KeyCode keyProxima = KeyCode.F5;
    [SerializeField] private KeyCode keyRepetir = KeyCode.F6;
    [SerializeField] private KeyCode keyAnterior = KeyCode.F7;

    // ===== Estado Interno =====
    public int EtapaAtualIndex { get; private set; } = -1;
    public EstadoFase EstadoAtual { get; private set; } = EstadoFase.Inativo;
    public bool EmTransicaoCamera => EstadoAtual == EstadoFase.TransicaoCamera;

    // Controle de corrotina da transição
    private Coroutine rotinaTransicao;

    private void Reset()
    {
        if (!cameraAnimator)
            cameraAnimator = FindObjectOfType<Animator>();
        if (!playerInput)
            playerInput = FindObjectOfType<PlayerInput>();
    }

    private void Update()
    {
        if (!debugHotkeys) return;
        if (Input.GetKeyDown(keyProxima)) ProximaEtapa();
        if (Input.GetKeyDown(keyRepetir)) RepetirEtapaAtual();
        if (Input.GetKeyDown(keyAnterior)) EtapaAnterior();
    }

    #region API Pública (chame de outros scripts)

    /// <summary>Inicia no índice informado (0, 1, 2...). Se já havia uma etapa ativa, troca para a nova.</summary>
    public void IniciarOuIrParaEtapa(int index)
    {
        if (!IndiceValido(index))
        {
            Debug.LogWarning($"[EcoDigitalManager] Índice inválido: {index}");
            return;
        }
        TrocarParaEtapa(index);
    }

    /// <summary>Vai para a próxima etapa na lista.</summary>
    public void ProximaEtapa()
    {
        int alvo = (EtapaAtualIndex < 0) ? 0 : EtapaAtualIndex + 1;
        if (!IndiceValido(alvo))
        {
            Debug.Log("[EcoDigitalManager] Não há próxima etapa. Fase concluída.");
            EstadoAtual = EstadoFase.Concluida;
            return;
        }
        TrocarParaEtapa(alvo);
    }

    /// <summary>Volta para a etapa anterior (debug).</summary>
    public void EtapaAnterior()
    {
        int alvo = Mathf.Max(0, EtapaAtualIndex - 1);
        if (!IndiceValido(alvo))
        {
            Debug.LogWarning("[EcoDigitalManager] Não há etapa anterior.");
            return;
        }
        TrocarParaEtapa(alvo);
    }

    /// <summary>Reexecuta a etapa atual (replay da câmera).</summary>
    public void RepetirEtapaAtual()
    {
        if (!IndiceValido(EtapaAtualIndex))
        {
            Debug.LogWarning("[EcoDigitalManager] Sem etapa atual para repetir.");
            return;
        }
        TrocarParaEtapa(EtapaAtualIndex);
    }

    /// <summary>Chame quando o objetivo da etapa atual foi concluído (puzzle resolvido).</summary>
    public void ConcluirEtapaAtual()
    {
        if (!IndiceValido(EtapaAtualIndex)) return;
        var etapa = etapas[EtapaAtualIndex];

        etapa.OnEtapaConcluida?.Invoke();
        // Aqui você pode já ir para a próxima etapa automaticamente, se quiser:
        // ProximaEtapa();
    }

    /// <summary>Animation Event no último frame da animação de CÂMERA deve chamar este método.</summary>
    public void AnimEvent_FimTransicaoCamera()
    {
        if (EstadoAtual != EstadoFase.TransicaoCamera) return;
        FinalizarTransicaoCamera();
    }

    #endregion

    #region Núcleo

    private void TrocarParaEtapa(int index)
    {
        // Cancela transições anteriores
        if (rotinaTransicao != null)
        {
            StopCoroutine(rotinaTransicao);
            rotinaTransicao = null;
        }

        EtapaAtualIndex = index;
        EstadoAtual = EstadoFase.TransicaoCamera;

        var etapa = etapas[EtapaAtualIndex];

        // Atualiza UI de objetivo (se houver)
        if (uiObjetivo)
            uiObjetivo.text = etapa.descricao;

        // Bloqueia movimento se configurado
        if (etapa.bloquearJogadorNaTransicao)
            ToggleControleJogador(false);

        // Dispara trigger da CÂMERA
        if (cameraAnimator && !string.IsNullOrEmpty(etapa.triggerCamera))
        {
            cameraAnimator.ResetTrigger(etapa.triggerCamera); // limpeza defensiva
            cameraAnimator.SetTrigger(etapa.triggerCamera);
        }

        // Se não tiver Animation Event, usamos fallback com timeout + (opcional) nomeEstado
        rotinaTransicao = StartCoroutine(RoutineAguardarTransicaoCamera(etapa));
    }

    private IEnumerator RoutineAguardarTransicaoCamera(EtapaConfig etapa)
    {
        float t0 = Time.time;
        bool entrouNoEstado = string.IsNullOrEmpty(etapa.nomeEstadoCameraContem); // se não informar, ignora detecção por estado

        while (Time.time - t0 < etapa.timeoutTransicao)
        {
            if (cameraAnimator && !string.IsNullOrEmpty(etapa.nomeEstadoCameraContem))
            {
                var st = cameraAnimator.GetCurrentAnimatorStateInfo(0); // layer 0 por padrão
                if (!entrouNoEstado)
                {
                    if (st.IsName(etapa.nomeEstadoCameraContem) ||
                        st.fullPathHash.ToString().Contains(etapa.nomeEstadoCameraContem))
                    {
                        entrouNoEstado = true;
                    }
                }
                else
                {
                    if (!cameraAnimator.IsInTransition(0) && st.normalizedTime >= 0.99f)
                        break; // terminou
                }
            }
            yield return null;
        }

        FinalizarTransicaoCamera();
    }

    private void FinalizarTransicaoCamera()
    {
        rotinaTransicao = null;
        EstadoAtual = EstadoFase.Jogando;

        // Libera movimento se havia bloqueado
        var etapa = etapas[EtapaAtualIndex];
        if (etapa.bloquearJogadorNaTransicao)
            ToggleControleJogador(true);

        // Evento de entrada na etapa (agora jogável)
        etapa.OnEtapaEntrou?.Invoke();
    }

    private bool IndiceValido(int idx) => idx >= 0 && idx < etapas.Count;

    #endregion

    #region Controle do Jogador

    private void ToggleControleJogador(bool habilitar)
    {
        if (componentesMovimentoParaDesativar != null)
        {
            foreach (var comp in componentesMovimentoParaDesativar)
                if (comp) comp.enabled = habilitar;
        }
        if (playerInput)
            playerInput.enabled = habilitar;
    }

    #endregion

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (etapas == null) return;
        GUIStyle style = new GUIStyle();
        style.fontSize = 12;
        style.normal.textColor = Color.white;

        foreach (var e in etapas)
        {
            if (!e.debugMostrarNoEditor || !e.referenciaVisual) continue;
            Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.4f);
            Gizmos.DrawSphere(e.referenciaVisual.position, 0.25f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(e.referenciaVisual.position + Vector3.up * 0.35f, e.nome, style);
#endif
        }
    }
#endif
}
