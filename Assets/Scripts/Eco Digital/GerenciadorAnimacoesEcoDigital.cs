using UnityEngine;

[DisallowMultipleComponent]
public class GerenciadorAnimacoesEcoDigital : MonoBehaviour
{
    [Header("Referências")]
    [Tooltip("Animator do personagem (deve conter os parâmetros below).")]
    [SerializeField] private Animator animator;

    [Tooltip("Sistema de mensagens simples que dispara evento C# ao receber notificação.")]
    [SerializeField] private SistemaMensagens sistemaMensagens;

    [Header("Parâmetros do Animator")]
    [Tooltip("Nome do parâmetro float que representa a velocidade (ex.: 'Speed').")]
    [SerializeField] private string nomeParamVelocidade = "Speed";

    [Tooltip("Trigger para entrar na animação 'ParadoDigitando'.")]
    [SerializeField] private string triggerParadoDigitando = "ParadoDigitando";

    [Tooltip("Trigger para entrar na animação 'AndandoDigitando'.")]
    [SerializeField] private string triggerAndandoDigitando = "AndandoDigitando";

    [Header("Detecção de Movimento")]
    [Tooltip("A partir de qual valor de 'Speed' consideramos 'andando'.")]
    [SerializeField, Min(0f)] private float limiarAndando = 0.05f;

    private void Reset()
    {
        if (!animator) animator = GetComponentInChildren<Animator>();
#if UNITY_2023_1_OR_NEWER
        if (!sistemaMensagens) sistemaMensagens = Object.FindFirstObjectByType<SistemaMensagens>();
#else
        if (!sistemaMensagens) sistemaMensagens = Object.FindObjectOfType<SistemaMensagens>();
#endif
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

    // Handler chamado quando chega notificação
    private void OnRecebeuMensagem(int _naoLidas)
    {
        if (!animator) return;

        float speed = 0f;
        if (!string.IsNullOrEmpty(nomeParamVelocidade) && animator.HasParameterOfType(nomeParamVelocidade, AnimatorControllerParameterType.Float))
        {
            speed = animator.GetFloat(nomeParamVelocidade);
        }

        bool andando = speed > limiarAndando;

        if (andando && !string.IsNullOrEmpty(triggerAndandoDigitando))
        {
            if (!string.IsNullOrEmpty(triggerParadoDigitando))
                animator.ResetTrigger(triggerParadoDigitando);

            animator.SetTrigger(triggerAndandoDigitando);
        }
        else if (!andando && !string.IsNullOrEmpty(triggerParadoDigitando))
        {
            if (!string.IsNullOrEmpty(triggerAndandoDigitando))
                animator.ResetTrigger(triggerAndandoDigitando);

            animator.SetTrigger(triggerParadoDigitando);
        }
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
