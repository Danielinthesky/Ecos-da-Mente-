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

    [Tooltip("Prefab do projétil (global/opcional). Será usado se a regra não tiver sua própria lista.")]
    [SerializeField] private GameObject prefabProjetil;

    public enum ModoSelecaoPrefab
    {
        Primeiro,           // Usa o primeiro da lista da regra
        AleatorioCadaTiro,  // Sorteia a cada tiro
        RoundRobin          // Alterna ciclicamente entre os prefabs da lista
    }

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

        [Header("Prefabs desta seção")]
        [Tooltip("Se vazio/nulo, usa o prefab global (prefabProjetil).")]
        public GameObject[] prefabsProjetil;

        [Tooltip("Como escolher o prefab desta seção.")]
        public ModoSelecaoPrefab modoSelecao;
    }

    [Header("Regras por Estado")]
    [Tooltip("Mapeie aqui: Estado da câmera -> Quem atira? Quantos tiros? Velocidade? Quais prefabs?")]
    [SerializeField]
    private RegraDeTiroPorEstado[] regras = new RegraDeTiroPorEstado[] { };

    [Header("Eventos")]
    public UnityEvent OnPainelInicioMostrado;

    // ===== controle interno =====
    private int _lastFullPathHash = 0;

    // round-robin: índice atual por regra
    private int[] _rrIndex;

    [Header("Gestos / Desenho na tela")]
    [SerializeField] private GameObject holderGestos;         // arraste o GO com MecanicaDesenhoNaTela + MecanicaReconhecerFormas
    [SerializeField] private GameObject gestureCamGO;         // opcional: se sua câmera overlay é um GO separado
    [SerializeField] private bool desativarHolderNoStart = true;

    private void Start()
    {
        if (painelInicio != null)
            painelInicio.SetActive(mostrarNoStart);

        // inicializa round-robin por regra
        _rrIndex = (regras != null && regras.Length > 0) ? new int[regras.Length] : new int[0];

        // manter holder de gestos desativado no início, se for o caso
        if (desativarHolderNoStart && holderGestos != null)
            holderGestos.SetActive(false);
        if (desativarHolderNoStart && gestureCamGO != null)
            gestureCamGO.SetActive(false);
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

    /// <summary>Chamado pelo botão "Jogar". Fecha o painel, aciona o trigger e liga gestos.</summary>
    public void IniciarJogo()
    {
        // 1) Fecha painel
        EsconderPainelInicio();

        // 2) Liga gestos/overlay (independente da câmera acertar o trigger)
        if (holderGestos != null && !holderGestos.activeSelf)
            holderGestos.SetActive(true);
        if (gestureCamGO != null && !gestureCamGO.activeSelf)
            gestureCamGO.SetActive(true);

        // 3) Dispara trigger da câmera (se disponível)
        if (animatorCamera == null)
            animatorCamera = GetComponent<Animator>() ?? FindFirstObjectByType<Animator>();

        if (animatorCamera != null && HasTrigger(animatorCamera, nomeTriggerCamera))
        {
            animatorCamera.ResetTrigger(nomeTriggerCamera);
            animatorCamera.SetTrigger(nomeTriggerCamera);
        }
        else
        {
            Debug.LogWarning($"[EcoDigitalGameManager] Animator/Trigger '{nomeTriggerCamera}' indisponível. Gestos foram ativados.");
        }
    }

    // ======== Regras / Disparo ========
    private void AplicarRegrasParaEstadoAtual(AnimatorStateInfo stateInfo)
    {
        if (regras == null || regras.Length == 0) return;

        // Coleta índices das regras cujo nome bate com o estado atual
        List<int> idxRegrasAlvo = new List<int>();
        for (int i = 0; i < regras.Length; i++)
        {
            var r = regras[i];
            if (string.IsNullOrWhiteSpace(r.stateName)) continue;
            if (stateInfo.IsName(r.stateName) || stateInfo.IsName("Base Layer." + r.stateName))
                idxRegrasAlvo.Add(i);
        }
        if (idxRegrasAlvo.Count == 0) return;

        foreach (int idx in idxRegrasAlvo)
        {
            var regra = regras[idx];

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

            // Valida prefabs (regra ou global)
            bool temLista = (regra.prefabsProjetil != null && regra.prefabsProjetil.Length > 0);
            if (!temLista && prefabProjetil == null)
            {
                Debug.LogWarning($"[EcoDigitalGameManager] Nenhum prefab de projétil definido (regra '{regra.stateName}' sem lista e prefab global vazio).");
                continue;
            }

            StartCoroutine(SequenciaDeTiros(idx, spawners[regra.spawnerIndex],
                regra.quantidadeTiros, Mathf.Max(0f, regra.intervaloEntreTiros),
                Mathf.Max(0.1f, regra.velocidadeProjetil)));
        }
    }

    private IEnumerator SequenciaDeTiros(int regraIndex, Transform spawner, int quantidade, float intervalo, float velocidade)
    {
        int qtd = Mathf.Max(1, quantidade);

        for (int shot = 0; shot < qtd; shot++)
        {
            // Direção “congelada” no instante do disparo (reto, sem perseguir).
            Vector3 dir = (eco.position - spawner.position);
            dir.y = 0f; // opcional: trava no plano XZ; remova se quiser 3D completo
            Vector3 dirNorm = dir.sqrMagnitude > 0.0001f ? dir.normalized : spawner.forward;

            // Seleciona o prefab conforme a regra
            var regra = regras[regraIndex];
            GameObject prefab = SelecionarPrefabParaTiro(regraIndex, shot);

            if (prefab == null)
            {
                Debug.LogWarning($"[EcoDigitalGameManager] Prefab nulo na regra '{regra.stateName}'. Pulando tiro.");
            }
            else
            {
                GameObject go = Instantiate(prefab, spawner.position, Quaternion.LookRotation(dirNorm, Vector3.up));

                // Inicializa movimento
                var tiro = go.GetComponent<EcoTiroProjetil>();
                if (tiro != null)
                {
                    tiro.Lancar(dirNorm, velocidade);
                }
                else
                {
                    var rb = go.GetComponent<Rigidbody>();
                    if (rb != null) rb.velocity = dirNorm * velocidade;
                }
            }

            if (intervalo > 0f && shot < qtd - 1)
                yield return new WaitForSeconds(intervalo);
        }
    }

    private GameObject SelecionarPrefabParaTiro(int regraIndex, int shotNumber)
    {
        if (regras == null || regraIndex < 0 || regraIndex >= regras.Length) return prefabProjetil;

        var regra = regras[regraIndex];
        var lista = regra.prefabsProjetil;
        bool temLista = (lista != null && lista.Length > 0);

        if (!temLista) return prefabProjetil;

        switch (regra.modoSelecao)
        {
            case ModoSelecaoPrefab.Primeiro:
                return lista[0];

            case ModoSelecaoPrefab.AleatorioCadaTiro:
                return lista[Random.Range(0, lista.Length)];

            case ModoSelecaoPrefab.RoundRobin:
                if (_rrIndex == null || _rrIndex.Length != regras.Length)
                    _rrIndex = new int[regras.Length];
                int idx = _rrIndex[regraIndex] % Mathf.Max(1, lista.Length);
                _rrIndex[regraIndex] = (idx + 1) % lista.Length;
                return lista[idx];

            default:
                return lista[0];
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

    public void EncerrarGestos()
    {
        if (holderGestos) holderGestos.SetActive(false);
        if (gestureCamGO) gestureCamGO.SetActive(false);
    }
}
