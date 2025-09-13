using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class NPCEcoDigital : MonoBehaviour
{
    public enum PerfilNPC { Feliz, Conectado }
    public enum Estado { Idle, Andando, OlhandoAoRedor, ChecandoCelular }

    [Header("Perfil")]
    [SerializeField] private PerfilNPC perfil = PerfilNPC.Feliz;

    [Header("Referências")]
    [SerializeField] private Animator animator; // opcional
    [Tooltip("Limites opcionais para manter o NPC dentro. Deixe vazio para ignorar.")]
    [SerializeField] private BoxCollider limites;

    [Header("Movimento")]
    [Tooltip("Raio máximo para buscar novos destinos aleatórios.")]
    [SerializeField, Min(1f)] private float raioBuscaDestino = 30f;
    [Tooltip("Distância mínima para considerar que chegou no destino.")]
    [SerializeField, Min(0.1f)] private float distanciaChegada = 1.0f;

    [Tooltip("Velocidade base mínima e máxima. O script varia dentro desse range.")]
    [SerializeField] private Vector2 velocidadeRange = new Vector2(1.1f, 2.6f);

    [Tooltip("Quanto tempo parado entre caminhadas (Idle).")]
    [SerializeField] private Vector2 pausaIdleSeg = new Vector2(1.0f, 3.0f);

    [Tooltip("Tempo olhando ao redor (Feliz).")]
    [SerializeField] private Vector2 tempoOlharSeg = new Vector2(1.0f, 2.0f);

    [Tooltip("Tempo checando celular (Conectado).")]
    [SerializeField] private Vector2 tempoChecarSeg = new Vector2(2.0f, 4.0f);

    [Header("Variedade")]
    [Tooltip("Probabilidade de escolher um PontoInteresse (0..1).")]
    [SerializeField, Range(0f, 1f)] private float probPontoInteresse = 0.35f;

    [Tooltip("Peso para escolher destinos mais longos às vezes (0..1).")]
    [SerializeField, Range(0f, 1f)] private float pesoDestinoLongo = 0.4f;

    [Tooltip("Armazena posições recentes para evitar voltar pro mesmo lugar.")]
    [SerializeField] private int memoriaPosicoes = 6;
    [SerializeField, Min(1f)] private float distanciaMinimaDePosicaoMemorizada = 6f;

    [Tooltip("Se ficar preso por esse tempo sem progresso, replaneja destino.")]
    [SerializeField, Min(0.5f)] private float tempoSemProgressoReplanejar = 3f;

    [Header("Suavização de rota")]
    [Tooltip("Alcance para considerar mudança de corner.")]
    [SerializeField, Min(0.1f)] private float cornerTolerance = 0.4f;

    // ========== ANIM: novos parâmetros opcionais ==========
    [Header("Animator (Parado/Andando por Perfil)")]
    [Tooltip("Nome do parâmetro INT que indica o perfil no Animator (0=Feliz, 1=Conectado).")]
    [SerializeField] private string nomeParamPerfilIndex = "PerfilIndex";

    [Tooltip("Nome do parâmetro BOOL que indica se está andando.")]
    [SerializeField] private string nomeParamAndando = "Andando";
    // ======================================================

    // Internos
    private NavMeshAgent agent;
    private Estado estadoAtual = Estado.Idle;
    private float cronometroEstado = 0f;
    private float alvoDuracaoEstado = 0f;
    private Vector3 ultimoPontoProgresso;
    private float tempoSemProgresso = 0f;

    private Queue<Vector3> ultimasPosicoes = new Queue<Vector3>();
    private readonly List<Transform> pontosInteresse = new List<Transform>();
    private readonly List<Vector3> rotaCorners = new List<Vector3>();
    private int cornerIndex = 0;

    // Animator params (existentes)
    private static readonly int HASH_Speed = Animator.StringToHash("Speed");
    private static readonly int HASH_ChecandoCelular = Animator.StringToHash("ChecandoCelular");
    private static readonly int HASH_Olhando = Animator.StringToHash("Olhando");

    // ANIM: caches para evitar StringToHash em todo frame (se o usuário quiser posteriormente)
    // Aqui mantemos como string para ser editável no Inspector.

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (!animator) animator = GetComponentInChildren<Animator>();
    }

    void Start()
    {
        // Ajustes iniciais por perfil
        switch (perfil)
        {
            case PerfilNPC.Feliz:
                SetRandomSpeed(scaleMin: 0.9f, scaleMax: 1.1f);
                break;
            case PerfilNPC.Conectado:
                SetRandomSpeed(scaleMin: 0.8f, scaleMax: 1.2f);
                break;
        }

        // Carrega Pontos de Interesse (tag opcional)
        var gos = GameObject.FindGameObjectsWithTag("PontoInteresse");
        foreach (var go in gos) pontosInteresse.Add(go.transform);

        // ANIM: seta o PerfilIndex uma vez ao iniciar
        SetPerfilIndexNoAnimator();

        ultimoPontoProgresso = transform.position;
        TrocarEstado(Estado.Idle, Random.Range(pausaIdleSeg.x, pausaIdleSeg.y));
    }

    void Update()
    {
        cronometroEstado += Time.deltaTime;

        AtualizarAnimator();

        // Detecção de “travamento”
        float avancou = Vector3.Distance(transform.position, ultimoPontoProgresso);
        if (avancou > 0.05f)
        {
            ultimoPontoProgresso = transform.position;
            tempoSemProgresso = 0f;
        }
        else
        {
            tempoSemProgresso += Time.deltaTime;
            if (tempoSemProgresso >= tempoSemProgressoReplanejar)
            {
                // Replaneja
                tempoSemProgresso = 0f;
                if (estadoAtual == Estado.Andando)
                {
                    EscolherNovoDestino();
                }
            }
        }

        switch (estadoAtual)
        {
            case Estado.Idle:
                if (cronometroEstado >= alvoDuracaoEstado)
                {
                    if (perfil == PerfilNPC.Feliz && Random.value < 0.25f)
                        TrocarEstado(Estado.OlhandoAoRedor, Random.Range(tempoOlharSeg.x, tempoOlharSeg.y));
                    else if (perfil == PerfilNPC.Conectado && Random.value < 0.35f)
                        TrocarEstado(Estado.ChecandoCelular, Random.Range(tempoChecarSeg.x, tempoChecarSeg.y));
                    else
                        IniciarCaminhada();
                }
                break;

            case Estado.OlhandoAoRedor:
                if (cronometroEstado >= alvoDuracaoEstado)
                    TrocarEstado(Estado.Idle, Random.Range(pausaIdleSeg.x, pausaIdleSeg.y));
                break;

            case Estado.ChecandoCelular:
                if (cronometroEstado >= alvoDuracaoEstado)
                    TrocarEstado(Estado.Idle, Random.Range(pausaIdleSeg.x, pausaIdleSeg.y));
                break;

            case Estado.Andando:
                AtualizarSeguirRota();
                if (!agent.pathPending && agent.remainingDistance <= distanciaChegada)
                {
                    MemorizarPosicao(transform.position);
                    TrocarEstado(Estado.Idle, Random.Range(pausaIdleSeg.x, pausaIdleSeg.y));
                }
                else
                {
                    if (Random.value < 0.01f)
                        SetRandomSpeed(0.9f, 1.1f);
                }
                break;
        }
    }

    // ======= LÓGICA DE ROTA/DESTINOS =======

    private void IniciarCaminhada()
    {
        EscolherNovoDestino();
        TrocarEstado(Estado.Andando, 0f);
    }

    private void EscolherNovoDestino()
    {
        Vector3 alvo;

        // 1) Às vezes escolhe um Ponto de Interesse
        if (pontosInteresse.Count > 0 && Random.value < probPontoInteresse)
        {
            Transform t = pontosInteresse[Random.Range(0, pontosInteresse.Count)];
            if (TentarProjetarNoNavMesh(t.position, out alvo))
            {
                SetDestino(alvo);
                return;
            }
        }

        // 2) Aleatório dentro de um raio (com pesos para distâncias)
        float raioEscolhido = EscolherRaioPonderado();
        for (int i = 0; i < 20; i++)
        {
            Vector3 tentativa = PontoAleatorio(raioEscolhido);
            if (DentroDosLimites(tentativa) && TentarProjetarNoNavMesh(tentativa, out alvo))
            {
                if (!MuitoPertoDePosicaoMemorizada(alvo))
                {
                    SetDestino(alvo);
                    return;
                }
            }
        }

        // 3) fallback: tenta mais amplo
        for (int i = 0; i < 20; i++)
        {
            Vector3 tentativa = PontoAleatorio(raioBuscaDestino);
            if (DentroDosLimites(tentativa) && TentarProjetarNoNavMesh(tentativa, out alvo))
            {
                SetDestino(alvo);
                return;
            }
        }
    }

    private void SetDestino(Vector3 destino)
    {
        NavMeshPath path = new NavMeshPath();
        if (agent.CalculatePath(destino, path) && path.corners.Length > 1)
        {
            rotaCorners.Clear();
            rotaCorners.AddRange(path.corners);
            cornerIndex = 1;
            agent.SetPath(path);
        }
        else
        {
            agent.SetDestination(destino); // fallback
        }

        if (perfil == PerfilNPC.Conectado)
        {
            if (Random.value < 0.3f && rotaCorners.Count > 1)
            {
                Vector3 c = rotaCorners[rotaCorners.Count - 1];
                Vector2 jitter = Random.insideUnitCircle * 1.2f;
                Vector3 jig = new Vector3(c.x + jitter.x, c.y, c.z + jitter.y);
                if (TentarProjetarNoNavMesh(jig, out var jigNav))
                {
                    agent.SetDestination(jigNav);
                }
            }
        }
    }

    private void AtualizarSeguirRota()
    {
        if (rotaCorners.Count > 1 && cornerIndex < rotaCorners.Count)
        {
            Vector3 alvo = rotaCorners[cornerIndex];
            Vector3 flatPos = new Vector3(transform.position.x, alvo.y, transform.position.z);
            float d = Vector3.Distance(flatPos, alvo);

            if (d <= cornerTolerance)
            {
                cornerIndex++;
            }
        }
    }

    private float EscolherRaioPonderado()
    {
        float r = Random.value;
        if (r < 0.3f) return Mathf.Max(raioBuscaDestino * 0.33f, 4f);
        if (r < 0.6f) return Mathf.Max(raioBuscaDestino * 0.66f, 8f);
        if (Random.value < pesoDestinoLongo) return raioBuscaDestino;
        return Mathf.Max(raioBuscaDestino * 0.66f, 8f);
    }

    private Vector3 PontoAleatorio(float raio)
    {
        Vector2 v = Random.insideUnitCircle * raio;
        Vector3 basePos = transform.position + new Vector3(v.x, 0f, v.y);
        return new Vector3(basePos.x, transform.position.y, basePos.z);
    }

    private bool TentarProjetarNoNavMesh(Vector3 pos, out Vector3 projetado)
    {
        if (NavMesh.SamplePosition(pos, out var hit, 5f, NavMesh.AllAreas))
        {
            projetado = hit.position;
            return true;
        }
        projetado = pos;
        return false;
    }

    private bool DentroDosLimites(Vector3 p)
    {
        if (!limites) return true;
        Vector3 local = limites.transform.InverseTransformPoint(p);
        Vector3 ext = limites.size * 0.5f;
        return Mathf.Abs(local.x) <= ext.x && Mathf.Abs(local.y) <= ext.y && Mathf.Abs(local.z) <= ext.z;
    }

    private void MemorizarPosicao(Vector3 p)
    {
        if (ultimasPosicoes.Count >= memoriaPosicoes)
            ultimasPosicoes.Dequeue();
        ultimasPosicoes.Enqueue(p);
    }

    private bool MuitoPertoDePosicaoMemorizada(Vector3 p)
    {
        foreach (var m in ultimasPosicoes)
        {
            if (Vector3.Distance(p, m) < distanciaMinimaDePosicaoMemorizada)
                return true;
        }
        return false;
    }

    private void SetRandomSpeed(float scaleMin, float scaleMax)
    {
        float baseMin = Mathf.Max(0.1f, velocidadeRange.x);
        float baseMax = Mathf.Max(baseMin + 0.01f, velocidadeRange.y);
        float baseSpeed = Random.Range(baseMin, baseMax);
        float scale = Random.Range(scaleMin, scaleMax);
        agent.speed = baseSpeed * scale;
        agent.acceleration = Mathf.Max(4f, agent.speed * 3f);
        agent.angularSpeed = Mathf.Lerp(180f, 300f, 0.5f);
        agent.autoBraking = false;
        agent.stoppingDistance = distanciaChegada * 0.5f;
    }

    private void TrocarEstado(Estado novo, float duracao)
    {
        estadoAtual = novo;
        cronometroEstado = 0f;
        alvoDuracaoEstado = duracao;

        switch (novo)
        {
            case Estado.Idle:
                agent.isStopped = true;
                break;
            case Estado.Andando:
                agent.isStopped = false;
                break;
            case Estado.OlhandoAoRedor:
                agent.isStopped = true;
                break;
            case Estado.ChecandoCelular:
                agent.isStopped = true;
                break;
        }
    }

    private void AtualizarAnimator()
{
    if (!animator) return;

    float vel = agent.velocity.magnitude;
    bool andando = vel > 0.05f && !agent.isStopped;

    // Atualiza o parâmetro Andando (idle vs movimento)
    if (animator.HasParameterOfType("Andando", AnimatorControllerParameterType.Bool))
        animator.SetBool("Andando", andando);
}


    // ANIM: helper para setar o INT de perfil no Animator
    private void SetPerfilIndexNoAnimator()
    {
        if (!animator || string.IsNullOrEmpty(nomeParamPerfilIndex)) return;
        if (!animator.HasParameterOfType(nomeParamPerfilIndex, AnimatorControllerParameterType.Int)) return;

        int idx = perfil == PerfilNPC.Feliz ? 0 : 1;
        if (animator.GetInteger(nomeParamPerfilIndex) != idx)
            animator.SetInteger(nomeParamPerfilIndex, idx);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, raioBuscaDestino);

        if (limites)
        {
            Gizmos.color = new Color(1f, 0.6f, 0f, 0.35f);
            Matrix4x4 m = Matrix4x4.TRS(limites.transform.position, limites.transform.rotation, limites.transform.lossyScale);
            using (new UnityEditor.Handles.DrawingScope(m))
            {
                UnityEditor.Handles.DrawWireCube(limites.center, limites.size);
            }
        }
    }
#endif
}
