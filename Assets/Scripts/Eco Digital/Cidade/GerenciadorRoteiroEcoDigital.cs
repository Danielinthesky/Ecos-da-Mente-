using UnityEngine;

public class GerenciarRoteiroEcoDigital : MonoBehaviour
{
    [Header("Referências do Eco")]
    [SerializeField] private GameObject eco;                    // opcional: apenas para referência/inspector
    [SerializeField] private MonoBehaviour ecoDigitalController;
    [SerializeField] private MonoBehaviour sistemaMensagens;
    [SerializeField] private GameObject holderStick;
    [SerializeField] private Animator animator;

    [Header("Configuração da animação")]
    [SerializeField] private string animTriggerCair = "Cair";

    private bool jaExecutou = false;

    private void OnTriggerEnter(Collider other)
    {
        if (jaExecutou) return;

        // AQUI estava o problema: 'other' é o gatilho.
        // Basta checar a tag do gatilho:
        if (other.CompareTag("IniciarQueda"))
        {
            jaExecutou = true;

            Debug.Log("[GerenciarRoteiroEcoDigital] Trigger 'IniciarQueda' detectado pelo Eco. Iniciando animação de cair.");

            if (ecoDigitalController != null) ecoDigitalController.enabled = false;
            if (sistemaMensagens != null) sistemaMensagens.enabled = false;
            if (holderStick != null) holderStick.SetActive(false);

            if (animator != null && !string.IsNullOrEmpty(animTriggerCair))
                animator.SetTrigger(animTriggerCair);
        }
    }
}
