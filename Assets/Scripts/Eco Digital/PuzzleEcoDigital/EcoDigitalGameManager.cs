using System.Collections;
using System.Collections.Generic;
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

    [Tooltip("Nome do parâmetro Trigger no Animator (ex.: 'CameraJogo6').")]
    [SerializeField] private string nomeTriggerCamera = "CameraJogo6";

    [Header("Disparo / Alvos")]
    [Tooltip("Transform do Eco (alvo).")]
    [SerializeField] private Transform eco;

    [Tooltip("Spawners (4 no seu caso). Ordem livre, mas lembre qual é qual).")]
    [SerializeField] private Transform[] spawners = new Transform[4];

    [Tooltip("Prefab do projétil (deve ter Rigidbody e o script EcoTiroProjetil).")]
    [SerializeField] private GameObject prefabProjetil;

    [System.Serializable]
    public struct RegraDeTiroPorEstado
    {
        [Tooltip("Nome do estado no Animator (pode ser só o nome do State). Ex.: 'CameraJogo6'")]
        public string stateName;

        [Tooltip("Índice do spawner que dispara (0 a N-1).")]
        public int spawnerIndex;

        [Tooltip("Quantidade de tiros ao entrar nesse estado.")]
        public int quantidadeTiros;

        [Tooltip("Tempo entre cada tiro (s).")]
        public float intervaloEntreTiros;

        [Tooltip("Velocidade do projétil (m/s).")]
        public float velocidadeProjetil;
    }

    [Header("Regras por Estado")]
    [Tooltip("Mapeie aqui: Estado da câmera -> Quem atira? Quantos tiros? Velocidade?")]
    [SerializeField] private RegraDeTiroPorEstado[] regras = new RegraDeTiroPorEstado[]
    {
        // Exemplo pronto; ajuste se quiser:
        // new RegraDeTiroPorEstado { stateName = "CameraJogo6", spawnerIndex = 0, quantidadeTiros = 3, intervaloEntreTiros = 0.25f, velocidadeProjetil = 10f },
    };

    [Header("Eventos")]
    public UnityEvent OnPainelInicioMostrado;

    // ===== controle interno =====
    private int _lastFullPathHash = 0;

    private void Start()
    {
        if (painelInicio != null)
            painelInicio.SetActive(mostrarNoStart);
    }

    private void Update()
    {
        // Monitora entrada em novos estados de câmera (Layer 0).
        if (animatorCamera == null) return;

        AnimatorStateInfo s = animatorCamera.GetCurrentAnimatorStateInfo(0);
        int currentHash = s.fullPathHash;

        if (currentHash != _lastFullPathHash)
        {
            _lastFullPathHash = currentHash;
            // Entrou em um novo estado → aplica regras
            AplicarRegrasParaEstadoAtual(s);
        }
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
            animatorCamera = GetComponent<Animator>();
            if (animatorCamera == null)
            {
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
        // Debug.Log($"[EcoDigitalGameManager] Trigger '{nomeTriggerCamera}' acionado em '{animatorCamera.gameObject.name}'.");
    }

    // ======== Regras / Disparo ========
    private void AplicarRegrasParaEstadoAtual(AnimatorStateInfo stateInfo)
    {
        if (regras == null || regras.Length == 0) return;

        // Coleta todas as regras cujo nome bate com o estado atual
        // Usamos IsName para permitir "StateName" ou "Base Layer.StateName".
        List<RegraDeTiroPorEstado> regrasAlvo = new List<RegraDeTiroPorEstado>();
        foreach (var r in regras)
        {
            if (string.IsNullOrWhiteSpace(r.stateName)) continue;
            if (stateInfo.IsName(r.stateName) || stateInfo.IsName("Base Layer." + r.stateName))
                regrasAlvo.Add(r);
        }

        if (regrasAlvo.Count == 0) return;

        foreach (var regra in regrasAlvo)
        {
            // Valida spawner
            if (spawners == null || spawners.Length == 0 || regra.spawnerIndex < 0 || regra.spawnerIndex >= spawners.Length)
            {
                Debug.LogWarning($"[EcoDigitalGameManager] SpawnerIndex inválido para estado '{regra.stateName}'.");
                continue;
            }
            if (spawners[regra.spawnerIndex] == null)
            {
                Debug.LogWarning($"[EcoDigitalGameManager] Spawner {regra.spawnerIndex} não atribuído no Inspector.");
                continue;
            }
            if (eco == null)
            {
                Debug.LogWarning("[EcoDigitalGameManager] Transform do Eco não atribuído.");
                continue;
            }
            if (prefabProjetil == null)
            {
                Debug.LogWarning("[EcoDigitalGameManager] Prefab do projétil não atribuído.");
                continue;
            }

            StartCoroutine(SequenciaDeTiros(spawners[regra.spawnerIndex], regra.quantidadeTiros,
                Mathf.Max(0f, regra.intervaloEntreTiros), Mathf.Max(0.1f, regra.velocidadeProjetil)));
        }
    }

    private IEnumerator SequenciaDeTiros(Transform spawner, int quantidade, float intervalo, float velocidade)
    {
        int qtd = Mathf.Max(1, quantidade);
        for (int i = 0; i < qtd; i++)
        {
            // Direção "congelada" no instante do disparo (reto, sem perseguição).
            Vector3 dir = (eco.position - spawner.position);
            dir.y = 0f; // opcional: trava no plano XZ; remova se quiser 3D completo
            Vector3 dirNorm = dir.sqrMagnitude > 0.0001f ? dir.normalized : spawner.forward;

            GameObject go = Instantiate(prefabProjetil, spawner.position, Quaternion.LookRotation(dirNorm, Vector3.up));
            // Se tiver o script EcoTiroProjetil, inicializa por ele:
            var tiro = go.GetComponent<EcoTiroProjetil>();
            if (tiro != null)
            {
                tiro.Lancar(dirNorm, velocidade);
            }
            else
            {
                // fallback: tenta achar Rigidbody e setar velocity
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null) rb.velocity = dirNorm * velocidade;
            }

            if (intervalo > 0f && i < qtd - 1)
                yield return new WaitForSeconds(intervalo);
        }
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
