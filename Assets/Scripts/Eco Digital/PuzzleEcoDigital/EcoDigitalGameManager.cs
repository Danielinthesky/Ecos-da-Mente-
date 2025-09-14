using UnityEngine;
using UnityEngine.Events;

public class EcoDigitalGameManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject painelInicio;
    [SerializeField] private bool mostrarNoStart = false;

    [Header("Câmera / Animator")]
    [Tooltip("Animator que receberá o trigger (ex.: Animator da Virtual Camera).")]
    [SerializeField] private Animator animatorCamera;

    [Tooltip("Nome do parâmetro Trigger no Animator.")]
    [SerializeField] private string nomeTriggerCamera = "Camera6";

    [Header("Eventos")]
    public UnityEvent OnPainelInicioMostrado;

    private void Start()
    {
        if (painelInicio != null)
            painelInicio.SetActive(mostrarNoStart);
    }

    /// <summary>Mostra o painel de início do jogo (idempotente).</summary>
    public void MostrarPainelInicio()
    {
        if (painelInicio == null) return;

        if (!painelInicio.activeSelf)
        {
            painelInicio.SetActive(true);
            OnPainelInicioMostrado?.Invoke();
        }
    }

    /// <summary>Esconder painel.</summary>
    public void EsconderPainelInicio()
    {
        if (painelInicio == null) return;
        if (painelInicio.activeSelf) painelInicio.SetActive(false);
    }

    /// <summary>Chamado pelo botão "Jogar". Fecha o painel e aciona o trigger da câmera.</summary>
    public void IniciarJogo()
    {
        // 1) Fecha o painel
        EsconderPainelInicio();

        // 2) Garante referência do Animator
        if (animatorCamera == null)
        {
            // tenta pegar no mesmo objeto do GameManager (caso esteja junto)
            animatorCamera = GetComponent<Animator>();
            if (animatorCamera == null)
            {
                // fallback simples: pega o primeiro Animator da cena
                animatorCamera = FindFirstObjectByType<Animator>();
            }
        }

        if (animatorCamera == null)
        {
            Debug.LogWarning("[EcoDigitalGameManager] Nenhum Animator encontrado/atribuído para acionar o trigger.");
            return;
        }

        // 3) Verifica se o parâmetro existe e é Trigger
        if (!HasTrigger(animatorCamera, nomeTriggerCamera))
        {
            Debug.LogWarning(FormatarParametros(animatorCamera,
                $"[EcoDigitalGameManager] Parâmetro Trigger '{nomeTriggerCamera}' não encontrado no Animator:"));
            return;
        }

        // 4) Dispara o Trigger
        animatorCamera.ResetTrigger(nomeTriggerCamera);
        animatorCamera.SetTrigger(nomeTriggerCamera);
        // log opcional:
        // Debug.Log($"[EcoDigitalGameManager] Trigger '{nomeTriggerCamera}' acionado em '{animatorCamera.gameObject.name}'.");
    }

    // ===== util =====
    private static bool HasTrigger(Animator anim, string triggerName)
    {
        if (anim == null || string.IsNullOrEmpty(triggerName)) return false;
        foreach (var p in anim.parameters)
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == triggerName)
                return true;
        return false;
    }

    private static string FormatarParametros(Animator anim, string cabecalho)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine(cabecalho);
        if (anim == null) { sb.AppendLine("- (Animator nulo)"); return sb.ToString(); }

        foreach (var p in anim.parameters)
            sb.AppendLine($"- {p.name} ({p.type})");
        return sb.ToString();
    }
}
