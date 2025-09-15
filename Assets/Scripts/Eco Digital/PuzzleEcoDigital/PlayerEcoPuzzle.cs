using UnityEngine;

[DisallowMultipleComponent]
public class PlayerEcoPuzzle : MonoBehaviour
{
    [Header("Animator do Eco")]
    [Tooltip("Animator que possui o Trigger 'Atingido'. Se vazio, busca no próprio GameObject ou no pai.")]
    [SerializeField] private Animator animatorEco;

    [Tooltip("Nome do Trigger que será acionado quando o Eco for atingido.")]
    [SerializeField] private string triggerAtingido = "Atingido";

    [Header("Detecção de Projétil")]
    [Tooltip("Se verdadeiro, só considera objetos que tenham o componente EcoTiroProjetil.")]
    [SerializeField] private bool aceitarSomenteEcoTiroProjetil = true;

    [Tooltip("Opcional: além do componente, também aceita objetos com esta Tag (ex.: 'ProjetilEco'). Deixe vazio para ignorar.")]
    [SerializeField] private string tagProjetil = "";

    [Tooltip("Destruir o projétil assim que atingir o Eco (o projétil já pode se destruir sozinho).")]
    [SerializeField] private bool destruirProjetilAoAtingir = false;

    [Header("Proteção / Cooldown")]
    [Tooltip("Tempo mínimo entre duas ativações consecutivas do trigger (segundos).")]
    [SerializeField, Min(0f)] private float hitCooldown = 0.15f;

    private float _ultimoHitTime = -999f;

    private void Awake()
    {
        if (animatorEco == null)
        {
            animatorEco = GetComponent<Animator>();
            if (animatorEco == null)
                animatorEco = GetComponentInParent<Animator>();
        }
    }

    // ===== Entradas de colisão =====
    private void OnTriggerEnter(Collider other)
    {
        TentarProcessarHit(other.gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision != null && collision.collider != null)
            TentarProcessarHit(collision.collider.gameObject);
    }

    // Útil se você usa CharacterController e quiser capturar quando o Eco empurra o projétil
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.collider != null)
            TentarProcessarHit(hit.collider.gameObject);
    }

    // ===== Núcleo da lógica =====
    private void TentarProcessarHit(GameObject outro)
    {
        if (!EhProjetilValido(outro)) return;

        if (Time.time - _ultimoHitTime < hitCooldown)
            return;

        _ultimoHitTime = Time.time;

        if (animatorEco == null)
        {
            Debug.LogWarning("[PlayerEcoPuzzle] Animator não atribuído/encontrado no Eco.");
            return;
        }

        if (!HasTrigger(animatorEco, triggerAtingido))
        {
            Debug.LogWarning($"[PlayerEcoPuzzle] Trigger '{triggerAtingido}' não existe no Animator '{animatorEco.gameObject.name}'.");
            // Ainda assim tentamos disparar:
        }

        animatorEco.ResetTrigger(triggerAtingido);
        animatorEco.SetTrigger(triggerAtingido);

        if (destruirProjetilAoAtingir)
        {
            // Evita destruir pais por engano (caso projétil esteja aninhado)
            var rb = outro.GetComponent<Rigidbody>();
            if (rb != null)
                Destroy(outro.gameObject);
            else
            {
                // Se o collider não está no root do projétil, tenta subir um nível
                var proj = outro.GetComponentInParent<EcoTiroProjetil>();
                if (proj != null) Destroy(proj.gameObject);
            }
        }
    }

    private bool EhProjetilValido(GameObject go)
    {
        if (go == null) return false;

        bool temComponente = go.GetComponent<EcoTiroProjetil>() != null
                             || go.GetComponentInParent<EcoTiroProjetil>() != null;

        bool tagOk = !string.IsNullOrEmpty(tagProjetil) && (go.CompareTag(tagProjetil)
                      || (go.transform.parent != null && go.transform.parent.CompareTag(tagProjetil)));

        if (aceitarSomenteEcoTiroProjetil)
            return temComponente || tagOk;

        // Se não exigir estritamente o componente, aceita qualquer um com a tag,
        // e se a tag estiver vazia, aceita todos (não recomendado).
        return temComponente || (!string.IsNullOrEmpty(tagProjetil) ? tagOk : true);
    }

    private static bool HasTrigger(Animator anim, string triggerName)
    {
        if (anim == null || string.IsNullOrEmpty(triggerName)) return false;
        foreach (var p in anim.parameters)
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == triggerName)
                return true;
        return false;
    }
}
