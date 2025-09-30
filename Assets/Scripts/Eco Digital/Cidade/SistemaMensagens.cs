using System;
using System.Collections;
using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class SistemaMensagens : MonoBehaviour
{
    [Header("Geração automática (opcional)")]
    [SerializeField] private bool iniciarAutomatico = true;
    [SerializeField, Min(0.1f)] private float intervaloSegundos = 5f;
    [Tooltip("Se falso, usa tempo real (WaitForSecondsRealtime).")]
    [SerializeField] private bool usarTimeScale = true;

    [Header("UI - Contador de não lidas (apenas TEXTO)")]
    [SerializeField] private TextMeshProUGUI textoNaoLidas;

    [Header("UI - Painel ANIMADO de recebimento")]
    [Tooltip("Animator do painel que abre com a notificação.")]
    [SerializeField] private Animator animatorRecebida;
    [Tooltip("Nome do trigger que abre o painel.")]
    [SerializeField] private string triggerNovaMensagem = "NovaMensagemRecebida";
    [Tooltip("TMP que será ATIVADO no final da animação (não ative por código aqui).")]
    [SerializeField] private TextMeshProUGUI textoRecebida;

    [Header("Fechamento automático")]
    [Tooltip("Trigger para fechar o painel após a exibição da mensagem.")]
    [SerializeField] private string triggerFechar = "FecharNotificao";
    [Tooltip("Tempo (s) para fechar após a mensagem aparecer (TMP ativado).")]
    [SerializeField, Min(0.1f)] private float tempoFecharSegundos = 3f;

    [Header("Comportamento")]
    [Tooltip("Mensagem padrão quando não for informado um texto.")]
    [SerializeField] private string mensagemPadrao = "Nova mensagem";
    [Tooltip("Tempo máximo (s) esperando o TMP ser ativado pela animação antes de desistir.")]
    [SerializeField, Min(0.05f)] private float timeoutEsperaTMP = 2f;
    [SerializeField] private bool logDebug = false;

    public event Action<int> NotificacaoRecebida;

    // ----- Estado -----
    private int naoLidas = 0;
    private Coroutine coLoop;               // loop de chegada automática
    private int versaoMensagem = 0;         // invalida esperas antigas
    private string ultimaMensagemPendente;  // texto da última mensagem
    private Coroutine coFechar;             // timer de fechamento

    private void Awake()
    {
        naoLidas = 0;
        ultimaMensagemPendente = string.Empty;
    }

    private void Start()
    {
        AtualizarTextoNaoLidas();

        // Painel/ativação do TMP são controlados pela ANIMAÇÃO.
        // Não habilitar/desabilitar aqui por código.

        if (iniciarAutomatico)
            IniciarMensagensAutomaticas();
    }

    private void OnDisable()
    {
        PararMensagensAutomaticas();
        CancelarFechamento();
    }

    // =============== API PÚBLICA ===============

    public void IniciarMensagensAutomaticas()
    {
        PararMensagensAutomaticas();
        coLoop = StartCoroutine(CoLoopRecebimento());
    }

    public void PararMensagensAutomaticas()
    {
        if (coLoop != null)
        {
            StopCoroutine(coLoop);
            coLoop = null;
        }
    }

    public void MarcarTodasComoLidas()
    {
        naoLidas = 0;
        AtualizarTextoNaoLidas();
    }

    public int ObterNaoLidas() => naoLidas;

    /// Dispara manualmente uma notificação com texto customizado.
    public void ReceberNotificacao(string mensagem)
    {
        naoLidas++;
        AtualizarTextoNaoLidas();

        // Guarda a mensagem mais recente
        ultimaMensagemPendente = string.IsNullOrWhiteSpace(mensagem) ? mensagemPadrao : mensagem;
        versaoMensagem++;

        // Dispara a animação de abertura
        if (animatorRecebida != null)
        {
            animatorRecebida.ResetTrigger(triggerNovaMensagem);
            animatorRecebida.SetTrigger(triggerNovaMensagem);
            if (logDebug) Debug.Log("[SistemaMensagens] Trigger NovaMensagemRecebida disparado.");
        }
        else if (logDebug)
        {
            Debug.LogWarning("[SistemaMensagens] Animator do painel não atribuído.");
        }

        // Se o TMP já estiver ativo (animação já habilitou), preenche agora; senão, espera.
        if (textoRecebida != null && textoRecebida.isActiveAndEnabled)
        {
            PreencherTextoAtual();
            ProgramarFechamento(); // inicia o timer de 3s após mostrar o texto
        }
        else
        {
            StartCoroutine(CoAguardarTMPAtivarEPreencher(versaoMensagem));
        }

        NotificacaoRecebida?.Invoke(naoLidas);
    }

    public void ReceberNotificacao() => ReceberNotificacao(mensagemPadrao);

    // =============== SUPORTE À ANIMAÇÃO ===============

    /// Chame via Animation Event no exato frame em que o TMP (textoRecebida) é ativado.
    public void OnTMPRecebidaAtivado()
    {
        PreencherTextoAtual();
        ProgramarFechamento();
        if (logDebug) Debug.Log("[SistemaMensagens] TMP ativado por animação. Texto preenchido e timer de fechamento iniciado.");
    }

    // =============== Rotinas internas ===============

    private IEnumerator CoLoopRecebimento()
    {
        while (true)
        {
            if (usarTimeScale) yield return new WaitForSeconds(intervaloSegundos);
            else               yield return new WaitForSecondsRealtime(intervaloSegundos);

            ReceberNotificacao();
        }
    }

    /// Aguarda até o TMP ser ativado pela animação, então preenche o texto e agenda fechamento.
    private IEnumerator CoAguardarTMPAtivarEPreencher(int versaoLocal)
    {
        float t = 0f;

        while (versaoLocal == versaoMensagem)
        {
            if (textoRecebida != null && textoRecebida.isActiveAndEnabled)
                break;

            if (t >= timeoutEsperaTMP)
            {
                if (logDebug) Debug.LogWarning("[SistemaMensagens] Timeout aguardando TMP ser ativado pela animação.");
                yield break;
            }

            yield return null;
            t += usarTimeScale ? Time.deltaTime : Time.unscaledDeltaTime;
        }

        if (versaoLocal != versaoMensagem) yield break;

        PreencherTextoAtual();
        ProgramarFechamento();
    }

    private void PreencherTextoAtual()
    {
        if (textoRecebida == null) return;

        textoRecebida.text = string.IsNullOrEmpty(ultimaMensagemPendente)
            ? mensagemPadrao
            : ultimaMensagemPendente;
    }

    private void ProgramarFechamento()
    {
        CancelarFechamento();
        coFechar = StartCoroutine(CoFecharDepois(tempoFecharSegundos));
    }

    private void CancelarFechamento()
    {
        if (coFechar != null)
        {
            StopCoroutine(coFechar);
            coFechar = null;
        }
    }

    private IEnumerator CoFecharDepois(float segundos)
    {
        if (usarTimeScale) yield return new WaitForSeconds(segundos);
        else               yield return new WaitForSecondsRealtime(segundos);

        if (animatorRecebida != null)
        {
            animatorRecebida.ResetTrigger(triggerFechar);
            animatorRecebida.SetTrigger(triggerFechar);
            if (logDebug) Debug.Log("[SistemaMensagens] Trigger de fechamento disparado.");
        }
        else if (logDebug)
        {
            Debug.LogWarning("[SistemaMensagens] Animator do painel não atribuído para fechar.");
        }
    }

    private void AtualizarTextoNaoLidas()
    {
        if (!textoNaoLidas) return;

        if (naoLidas == 0)
            textoNaoLidas.text = "Sem notificações não lidas";
        else if (naoLidas == 1)
            textoNaoLidas.text = "1 notificação não lida";
        else
            textoNaoLidas.text = $"{naoLidas} notificações não lidas";
    }
}
