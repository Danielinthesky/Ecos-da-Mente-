using UnityEngine;

/// <summary>
/// Mantém o Rigidbody do player dentro do retângulo superior da plataforma (X/Z)
/// e cola o Y no topo. Evita “cair” da borda.
/// Plataforma pode ter BoxCollider (recomendado) ou apenas MeshRenderer.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class LimitarAreaPlataforma : MonoBehaviour
{
    [Header("Referências")]
    [Tooltip("Transform/GO da plataforma. Procura BoxCollider primeiro; se não houver, usa MeshRenderer.")]
    [SerializeField] private Transform plataforma;

    [Header("Borda / Margem")]
    [Tooltip("Acolchoamento extra além do raio do CapsuleCollider, para não 'vazar' na beirada.")]
    [SerializeField, Min(0f)] private float acolchoamentoBorda = 0.02f;

    [Tooltip("Se não houver CapsuleCollider no player, use este raio manual.")]
    [SerializeField, Min(0f)] private float raioManual = 0.25f;

    [Header("Comportamento")]
    [Tooltip("Zera a velocidade planar quando o player bate na borda (impede 'deslizar' para fora).")]
    [SerializeField] private bool zerarVelocidadeNaBorda = true;

    [Tooltip("Atualizar em FixedUpdate (recom.) – igual ao ciclo da física. Se falso, usa LateUpdate.")]
    [SerializeField] private bool usarFixedUpdate = true;

    private Rigidbody rb;
    private BoxCollider boxPlataforma;
    private MeshRenderer rendererPlataforma;

    // cache de dados locais da plataforma
    private float topoLocalY;
    private Vector3 centroRetLocal;
    private Vector2 tamanhoRetLocal; // x,z em espaço local

    private CapsuleCollider capsule; // do player, se existir

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponentInChildren<CapsuleCollider>();

        if (!plataforma)
        {
            Debug.LogError("[LimitarAreaPlataforma] Plataforma não atribuída.", this);
            enabled = false;
            return;
        }

        boxPlataforma = plataforma.GetComponent<BoxCollider>();
        rendererPlataforma = plataforma.GetComponent<MeshRenderer>();

        RecalcularTopoERetangulo();
    }

    private void OnValidate()
    {
        if (plataforma)
        {
            boxPlataforma = plataforma.GetComponent<BoxCollider>();
            rendererPlataforma = plataforma.GetComponent<MeshRenderer>();
        }
    }

    private void FixedUpdate()
    {
        if (usarFixedUpdate) AplicarLimite();
    }

    private void LateUpdate()
    {
        if (!usarFixedUpdate) AplicarLimite();
    }

    /// <summary>
    /// Recalcula topoLocalY, centroRetLocal e tamanhoRetLocal com base no BoxCollider
    /// ou no MeshRenderer (fallback).
    /// </summary>
    private void RecalcularTopoERetangulo()
    {
        if (!plataforma) return;

        if (boxPlataforma != null)
        {
            centroRetLocal = boxPlataforma.center;
            tamanhoRetLocal = new Vector2(boxPlataforma.size.x, boxPlataforma.size.z);
            topoLocalY = boxPlataforma.center.y + boxPlataforma.size.y * 0.5f;
        }
        else if (rendererPlataforma != null)
        {
            Bounds wb = rendererPlataforma.bounds;

            // Converte bounds mundo -> local para obter retângulo em XZ
            Vector3 c = wb.center;
            Vector3 e = wb.extents;

            Vector3[] cantos = new Vector3[8];
            int i = 0;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        cantos[i++] = plataforma.InverseTransformPoint(c + Vector3.Scale(e, new Vector3(sx, sy, sz)));

            Vector3 min = cantos[0], max = cantos[0];
            for (int k = 1; k < cantos.Length; k++) { min = Vector3.Min(min, cantos[k]); max = Vector3.Max(max, cantos[k]); }

            centroRetLocal = (min + max) * 0.5f;
            tamanhoRetLocal = new Vector2(max.x - min.x, max.z - min.z);

            // topoLocalY: pega o topo em Y local
            Vector3 topoMundo = wb.center + Vector3.up * wb.extents.y;
            topoLocalY = plataforma.InverseTransformPoint(topoMundo).y;
        }
        else
        {
            // fallback padrão
            centroRetLocal = Vector3.zero;
            tamanhoRetLocal = new Vector2(2f, 2f);
            topoLocalY = 0f;
        }
    }

    private float ObterRaioPersonagem()
    {
        if (capsule != null) return Mathf.Max(0f, capsule.radius);
        return Mathf.Max(0f, raioManual);
    }

    private void AplicarLimite()
    {
        if (!plataforma) return;

        // Recalcula eventualmente (caso a plataforma se mova/escale)
        RecalcularTopoERetangulo();

        Vector3 posW = rb.position;
        // Converte para local da plataforma
        Vector3 local = plataforma.InverseTransformPoint(posW);

        float meiaX = tamanhoRetLocal.x * 0.5f;
        float meiaZ = tamanhoRetLocal.y * 0.5f;

        // margem: raio + acolchoamento (corrigidos pela escala)
        float raio = ObterRaioPersonagem() + acolchoamentoBorda;
        Vector3 esc = plataforma.lossyScale;
        float mx = raio / Mathf.Max(Mathf.Abs(esc.x), 0.0001f);
        float mz = raio / Mathf.Max(Mathf.Abs(esc.z), 0.0001f);

        float minX = centroRetLocal.x - meiaX + mx;
        float maxX = centroRetLocal.x + meiaX - mx;
        float minZ = centroRetLocal.z - meiaZ + mz;
        float maxZ = centroRetLocal.z + meiaZ - mz;

        // cola Y no topo da plataforma
        local.y = topoLocalY;

        // clamp XZ
        float xAntes = local.x, zAntes = local.z;
        local.x = Mathf.Clamp(local.x, minX, maxX);
        local.z = Mathf.Clamp(local.z, minZ, maxZ);

        // Volta para mundo
        Vector3 clampedW = plataforma.TransformPoint(local);

        // Se bateu na borda, opcionalmente zera a velocidade planar
        bool bateuBorda = !Mathf.Approximately(xAntes, local.x) || !Mathf.Approximately(zAntes, local.z);

        #if UNITY_600_OR_NEWER
        Vector3 vel = rb.linearVelocity;
        #else
        Vector3 vel = rb.velocity;
        #endif

        if (bateuBorda && zerarVelocidadeNaBorda)
        {
            vel.x = 0f;
            vel.z = 0f;
        }

        // Aplica posição clampada e mantém a componente Y da física
        clampedW.y = plataforma.TransformPoint(new Vector3(0f, topoLocalY, 0f)).y;

        rb.MovePosition(clampedW);

        #if UNITY_600_OR_NEWER
        rb.linearVelocity = vel;
        #else
        rb.velocity = vel;
        #endif
    }

    #if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!plataforma) return;
        // desenha o retângulo no topo
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.35f);

        // Tenta obter dados atuais
        if (!Application.isPlaying)
        {
            boxPlataforma = plataforma.GetComponent<BoxCollider>();
            rendererPlataforma = plataforma.GetComponent<MeshRenderer>();
            RecalcularTopoERetangulo();
        }

        float meiaX = tamanhoRetLocal.x * 0.5f;
        float meiaZ = tamanhoRetLocal.y * 0.5f;
        Vector3 c = new Vector3(centroRetLocal.x, topoLocalY, centroRetLocal.z);

        Vector3 l1 = plataforma.TransformPoint(c + new Vector3(+meiaX, 0f, +meiaZ));
        Vector3 l2 = plataforma.TransformPoint(c + new Vector3(-meiaX, 0f, +meiaZ));
        Vector3 l3 = plataforma.TransformPoint(c + new Vector3(-meiaX, 0f, -meiaZ));
        Vector3 l4 = plataforma.TransformPoint(c + new Vector3(+meiaX, 0f, -meiaZ));

        Gizmos.DrawLine(l1, l2); Gizmos.DrawLine(l2, l3); Gizmos.DrawLine(l3, l4); Gizmos.DrawLine(l4, l1);
    }
    #endif
}
