using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem; // opcional, apenas se usar PlayerInput

[DisallowMultipleComponent]
public class GerenciadorAnimacoesEcoDigital : MonoBehaviour
{
    [Header("Referências")]
    [Tooltip("Animator do personagem (deve conter os parâmetros below).")]
    [SerializeField] private Animator animator;

    [Tooltip("Sistema de mensagens simples que dispara evento C# ao receber notificação.")]
    [SerializeField] private SistemaMensagens sistemaMensagens;

    [Tooltip("Opcional: componentes de movimento a desabilitar enquanto empurrado (ex.: seu controller).")]
    [SerializeField] private List<MonoBehaviour> componentesMovimentoParaDesativar = new List<MonoBehaviour>();

    [Tooltip("Opcional: PlayerInput para bloquear input enquanto empurrado.")]
    [SerializeField] private PlayerInput playerInput;

    [Header("Parâmetros do Animator")]
    [Tooltip("Nome do parâmetro float que representa a velocidade (ex.: 'Speed').")]
    [SerializeField] private string nomeParamVelocidade = "Speed";

    [Tooltip("Trigger para entrar na animação 'ParadoDigitando'.")]
    [SerializeField] private string triggerParadoDigitando = "ParadoDigitando";

    [Tooltip("Trigger para entrar na animação 'AndandoDigitando'.")]
    [SerializeField] private string triggerAndandoDigitando = "AndandoDigitando";

    [Tooltip("Trigger prioritário da animação de empurrão.")]
    [SerializeField] private string triggerEmpurrado = "Empurrado";

    [Header("Detecção de Movimento")]
    [Tooltip("A partir de qual valor de 'Speed' consideramos 'andando'.")]
    [SerializeField, Min(0f)] private float limiarAndando = 0.05f;

    [Header("Prioridade Empurrado / Fila de Notificações")]
    [Tooltip("Triggers que NÃO devem disparar enquanto empurrado (serão enfileirados).")]
    [SerializeField] private List<string> triggersDeNotificacao = new List<string> { "ParadoDigitando", "AndandoDigitando" };

    [Tooltip("Nome (contém) do estado de empurrado. Se vazio, finalize via Animation Event.")]
    [SerializeField] private string nomeEstadoEmpurradoContem = "Empurrado";

    [Tooltip("Layer do Animator a observar para 'Empurrado' (geralmente 0).")]
    [SerializeField] private int animatorLayerEmpurrado = 0;

    [Tooltip("Timeout de segurança para empurrado (se não vier Animation Event).")]
    [SerializeField, Min(0.25f)] private float timeoutEmpurrado = 3f;

    [Header("Detecção de NPC Conectado")]
    [Tooltip("Detectar por Tag (ex.: 'NPCConectado').")]
    [SerializeField] private bool detectarPorTag = true;
    [SerializeField] private string tagNpcConectado = "NPCConectado";

    [Tooltip("Detectar por componente NPCEcoDigital com perfil Conectado.")]
    [SerializeField] private bool detectarPorComponente = true;

    [Header("Qualidade de vida")]
    [Tooltip("Tempo mínimo entre empurrões para evitar spam.")]
    [SerializeField, Min(0f)] private float cooldownEmpurrado = 0.6f;

    // ======== ESTADO INTERNO ========
    private readonly HashSet<string> setTriggersNotificacao = new HashSet<string>();
    private readonly Queue<string> filaTriggersBloqueados = new Queue<string>();
    private bool emEmpurrado = false;
    public bool EstaEmpurrado => emEmpurrado;
    private float ultimoEmpurradoTime = -999f;

    private void Reset()
    {
        if (!animator) animator = GetComponentInChildren<Animator>();
#if UNITY_2023_1_OR_NEWER
        if (!sistemaMensagens) sistemaMensagens = Object.FindFirstObjectByType<SistemaMensagens>();
#else
        if (!sistemaMensagens) sistemaMensagens = Object.FindObjectOfType<SistemaMensagens>();
#endif
        if (!playerInput) playerInput = GetComponent<PlayerInput>();
    }

    private void Awake()
    {
        if (!animator) animator = GetComponentInChildren<Animator>();
        setTriggersNotificacao.Clear();
        foreach (var t in triggersDeNotificacao)
            if (!string.IsNullOrWhiteSpace(t)) setTriggersNotificacao.Add(t);
    }

    private void OnEnable()
    {
#if UNITY_2023_1_OR_NEWER
        if (!sistemaMensagens) sistemaMensagens = Object.FindFirstObjectByType<SistemaMensagens>();
#else
        if (!sistemaMensagens) sistemaMensagens = Object.FindObjectOfType<SistemaMensagens>();
#endif
        if (sistemaMensagens != null)
        {
            // Assina o evento C# (int = contagem de não lidas). Não aparece no Inspector.
            sistemaMensagens.NotificacaoRecebida += OnRecebeuMensagem;
        }
    }

    private void OnDisable()
    {
        if (sistemaMensagens != null)
        {
            sistemaMensagens.NotificacaoRecebida -= OnRecebeuMensagem;
        }
    }

    // ===================== BLOQUEIO DE TRIGGERS (PRIORIDADE) =====================

    /// Use internamente em vez de animator.SetTrigger.
    private void SafeSetTrigger(string triggerNome)
    {
        if (animator == null || string.IsNullOrEmpty(triggerNome)) return;

        // Trigger de empurrado sempre pode disparar
        if (triggerNome == triggerEmpurrado)
        {
            animator.SetTrigger(triggerNome);
            return;
        }

        // Se está empurrado e o trigger é de notificação, enfileira e NÃO dispara agora
        if (emEmpurrado && setTriggersNotificacao.Contains(triggerNome))
        {
            filaTriggersBloqueados.Enqueue(triggerNome);
            return;
        }

        // Caso normal
        animator.SetTrigger(triggerNome);
    }

    // ===================== SISTEMA DE MENSAGENS -> TRIGGERS =====================

    // Handler chamado quando chega notificação
    private void OnRecebeuMensagem(int _naoLidas)
    {
        if (!animator) return;

        // Se está empurrado, apenas enfileiramos o trigger apropriado e saímos
        // (o enfileiramento acontece dentro do SafeSetTrigger).
        float speed = 0f;
        if (!string.IsNullOrEmpty(nomeParamVelocidade) &&
            animator.HasParameterOfType(nomeParamVelocidade, AnimatorControllerParameterType.Float))
        {
            speed = animator.GetFloat(nomeParamVelocidade);
        }

        bool andando = speed > limiarAndando;

        if (andando && !string.IsNullOrEmpty(triggerAndandoDigitando))
        {
            if (!string.IsNullOrEmpty(triggerParadoDigitando))
                animator.ResetTrigger(triggerParadoDigitando);

            SafeSetTrigger(triggerAndandoDigitando);
        }
        else if (!andando && !string.IsNullOrEmpty(triggerParadoDigitando))
        {
            if (!string.IsNullOrEmpty(triggerAndandoDigitando))
                animator.ResetTrigger(triggerAndandoDigitando);

            SafeSetTrigger(triggerParadoDigitando);
        }
    }

    // ===================== EMPURRADO: ENTRADA/SAÍDA =====================

    private void IniciarEmpurradoSePossivel()
    {
        if (emEmpurrado) return;
        if (Time.time - ultimoEmpurradoTime < cooldownEmpurrado) return;
        if (animator == null) return;

        emEmpurrado = true;
        ultimoEmpurradoTime = Time.time;

        // Pausa movimento / input (opcional)
        ToggleMovimento(false);

        // Limpa triggers de notificação atuais para não competir com o empurrado
        foreach (var t in setTriggersNotificacao) animator.ResetTrigger(t);

        // Dispara o empurrado com prioridade
        animator.ResetTrigger(triggerEmpurrado);
        animator.SetTrigger(triggerEmpurrado);

        // Se você não usar Animation Event, monitora o estado por nome
        if (!string.IsNullOrEmpty(nomeEstadoEmpurradoContem))
            StartCoroutine(RoutineAguardarEmpurradoPorEstado());
    }

    private System.Collections.IEnumerator RoutineAguardarEmpurradoPorEstado()
    {
        float t0 = Time.time;
        bool entrou = false;

        while (Time.time - t0 < timeoutEmpurrado)
        {
            var st = animator.GetCurrentAnimatorStateInfo(animatorLayerEmpurrado);

            if (!entrou)
            {
                if (st.IsName(nomeEstadoEmpurradoContem) ||
                    st.fullPathHash.ToString().Contains(nomeEstadoEmpurradoContem))
                    entrou = true;
            }
            else
            {
                if (!animator.IsInTransition(animatorLayerEmpurrado) && st.normalizedTime >= 0.99f)
                    break;
            }
            yield return null;
        }

        FinalizarEmpurrado();
    }

    /// Coloque um Animation Event no último frame da animação “Empurrado” chamando este método.
    public void AnimEvent_FimEmpurrado()
    {
        FinalizarEmpurrado();
    }

    private void FinalizarEmpurrado()
    {
        if (!emEmpurrado) return;
        emEmpurrado = false;

        // Retoma movimento / input
        ToggleMovimento(true);

        // Opcional: liberar UM trigger enfileirado (evita avalanche)
        if (filaTriggersBloqueados.Count > 0)
        {
            var prox = filaTriggersBloqueados.Dequeue();
            animator.SetTrigger(prox);
        }
    }

    private void ToggleMovimento(bool habilitar)
    {
        if (componentesMovimentoParaDesativar != null)
            foreach (var comp in componentesMovimentoParaDesativar)
                if (comp) comp.enabled = habilitar;

        if (playerInput) playerInput.enabled = habilitar;
    }

    // ===================== COLISÃO COM NPC CONECTADO =====================

    private void OnCollisionEnter(Collision collision) { TentarEmpurrar(collision.collider); }
    private void OnControllerColliderHit(ControllerColliderHit hit) { TentarEmpurrar(hit.collider); }
    private void OnTriggerEnter(Collider other) { TentarEmpurrar(other); }

    private void TentarEmpurrar(Collider col)
    {
        bool ehConectado = false;

        if (detectarPorTag && col.CompareTag(tagNpcConectado))
            ehConectado = true;

        if (!ehConectado && detectarPorComponente)
        {
            // Se você tiver a classe NPCEcoDigital, preferível: var npc = col.GetComponentInParent<NPCEcoDigital>();
            var npc = col.GetComponentInParent<MonoBehaviour>();
            if (npc != null)
            {
                var tp = npc.GetType();
                var campo = tp.GetField("perfil", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (campo != null)
                {
                    var val = campo.GetValue(npc)?.ToString();
                    if (!string.IsNullOrEmpty(val) && val.Contains("Conectado"))
                        ehConectado = true;
                }
            }
        }

        if (ehConectado) IniciarEmpurradoSePossivel();
    }
}

public static class AnimatorExtensions
{
    public static bool HasParameterOfType(this Animator self, string name, AnimatorControllerParameterType type)
    {
        foreach (var p in self.parameters)
            if (p.type == type && p.name == name) return true;
        return false;
    }
}
