using System.Collections;
using UnityEngine;
using TMPro;
using System;


[DisallowMultipleComponent]
public class SistemaMensagens : MonoBehaviour
{
    [Header("Geração automática")]
    [Tooltip("Se verdadeiro, recebe 1 notificação a cada 'intervaloSegundos'.")]
    [SerializeField] private bool iniciarAutomatico = true;

    [Tooltip("Intervalo (s) entre notificações.")]
    [SerializeField, Min(0.1f)] private float intervaloSegundos = 5f;

    [Tooltip("Se falso, usa tempo real (WaitForSecondsRealtime).")]
    [SerializeField] private bool usarTimeScale = true;

    [Header("UI - Não lidas (sempre ativo)")]
    [Tooltip("Painel que SEMPRE fica ativo.")]
    [SerializeField] private GameObject painelNaoLidas;

    [Tooltip("TMP com o texto 'X notificações não lidas'.")]
    [SerializeField] private TextMeshProUGUI textoNaoLidas;

    [Header("UI - Notificação recebida")]
    [Tooltip("Painel que aparece SOMENTE quando chega notificação.")]
    [SerializeField] private GameObject painelRecebida;

    [Tooltip("TMP do painel de recebida ('1 Notificação recebida').")]
    [SerializeField] private TextMeshProUGUI textoRecebida;

    [Tooltip("Quanto tempo (s) o painel 'recebida' fica visível após cada chegada.")]
    [SerializeField, Min(0.1f)] private float tempoExibicaoRecebida = 1.5f;
    public event Action<int> NotificacaoRecebida;


    // ----- Estado -----
    private int naoLidas = 0;
    private Coroutine coLoop;          // loop de chegada
    private bool timerAtivo = false;   // controla o temporizador do painel "recebida"
    private int versaoExibicao = 0;    // truque para reiniciar o timer sem empilhar coroutines

    private void Awake()
    {
        naoLidas = 0; // sempre começa do zero
    }

    private void Start()
    {
        if (painelNaoLidas) painelNaoLidas.SetActive(true);
        if (painelRecebida) painelRecebida.SetActive(false);
        AtualizarTextoNaoLidas();

        if (iniciarAutomatico) IniciarMensagensAutomaticas();
    }

    private void OnDisable()
    {
        PararMensagensAutomaticas();
        if (painelRecebida) painelRecebida.SetActive(false);
        timerAtivo = false;
    }

    // ================= API =================

    /// Inicia o recebimento: 1 notificação a cada 'intervaloSegundos'.
    public void IniciarMensagensAutomaticas()
    {
        PararMensagensAutomaticas();
        coLoop = StartCoroutine(CoLoopRecebimento());
    }

    /// Para o recebimento automático.
    public void PararMensagensAutomaticas()
    {
        if (coLoop != null)
        {
            StopCoroutine(coLoop);
            coLoop = null;
        }
    }

    /// Dispara MANUALMENTE uma notificação (soma +1 nas não lidas).
    public void ReceberNotificacao()
    {
        // 1) soma +1 nas não lidas
        naoLidas++;
        AtualizarTextoNaoLidas();

        // 2) mostra "Notificação recebida"
        if (painelRecebida) painelRecebida.SetActive(true);
        if (textoRecebida) textoRecebida.text = "1 Notificação recebida";

        // 3) reinicia o timer de exibição SEM empilhar coroutines
        versaoExibicao++; // invalida timers antigos
        if (!timerAtivo) StartCoroutine(CoOcultarRecebidaDepois(versaoExibicao));
        NotificacaoRecebida?.Invoke(naoLidas);
    }

    /// Zera o contador (opcional).
    public void MarcarTodasComoLidas()
    {
        naoLidas = 0;
        AtualizarTextoNaoLidas();
    }

    public int ObterNaoLidas() => naoLidas;

    // ================= Coroutines =================

    private IEnumerator CoLoopRecebimento()
    {
        while (true)
        {
            if (usarTimeScale) yield return new WaitForSeconds(intervaloSegundos);
            else               yield return new WaitForSecondsRealtime(intervaloSegundos);

            ReceberNotificacao(); // soma +1 e mostra painel
        }
    }

    private IEnumerator CoOcultarRecebidaDepois(int versaoLocal)
    {
        timerAtivo = true;

        if (usarTimeScale) yield return new WaitForSeconds(tempoExibicaoRecebida);
        else               yield return new WaitForSecondsRealtime(tempoExibicaoRecebida);

        // Só oculta se ninguém reiniciou o timer nesse meio tempo
        if (versaoLocal == versaoExibicao && painelRecebida)
            painelRecebida.SetActive(false);

        timerAtivo = false;
    }

    // ================= UI =================

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
