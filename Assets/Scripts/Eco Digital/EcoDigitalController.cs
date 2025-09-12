using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class EcoDigitalController : MonoBehaviour
{
    [Header("Input / Câmera")]
    [SerializeField] private Transform transformCamera;
    [SerializeField] private bool relativoACamera = true;
    [SerializeField, Min(0f)] private float velocidadeMovimento = 4.5f;
    [SerializeField, Range(0f, 1f)] private float zonaMorta = 0.08f;
    [SerializeField] private bool compensarZonaMortaRadial = true;

    [Header("Visual / Rotação")]
    [SerializeField] private Transform pivoModelo;
    [SerializeField] private float deslocamentoYawModelo = 0f;
    [SerializeField, Range(0f, 40f)] private float interpolacaoGiro = 16f;
    [SerializeField, Range(0.01f, 0.25f)] private float limiarRotacao = 0.06f;
    [SerializeField] private bool girarQuandoParado = false;

    [Header("Animator")]
    [SerializeField] private Animator animator;
    [SerializeField] private string nomeParametroSpeed = "Speed";

    private Rigidbody rb;
    private Vector2 entradaMovimento;
    private Vector3 ultimaDirecaoPlanar = Vector3.forward;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        if (transformCamera == null && Camera.main != null)
            transformCamera = Camera.main.transform;
        if (pivoModelo == null) pivoModelo = transform;
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    public void OnMove(InputValue valor) => entradaMovimento = valor.Get<Vector2>();

    private void FixedUpdate()
    {
        // 1) Processa input
        Vector2 bruto = entradaMovimento;
        float mag = bruto.magnitude;
        Vector2 filtrado = mag < zonaMorta ? Vector2.zero : bruto;

        float intensidade;
        if (filtrado == Vector2.zero)
        {
            intensidade = 0f;
        }
        else if (compensarZonaMortaRadial && zonaMorta > 0f && zonaMorta < 1f)
        {
            float magReesc = Mathf.InverseLerp(zonaMorta, 1f, Mathf.Clamp(filtrado.magnitude, zonaMorta, 1f));
            intensidade = magReesc;
            filtrado = filtrado.normalized;
        }
        else
        {
            intensidade = Mathf.Clamp01(filtrado.magnitude);
            filtrado = filtrado.normalized;
        }

        Vector3 direcaoPlanar = CalcularDirecaoPlanarNormalizada(filtrado);

        if (direcaoPlanar.sqrMagnitude >= limiarRotacao * limiarRotacao)
            ultimaDirecaoPlanar = direcaoPlanar;

        // 2) Movimento
        Vector3 velocidadeDesejada = direcaoPlanar * (velocidadeMovimento * intensidade);

        // aplica somente XZ e preserva Y da física
        #if UNITY_600_OR_NEWER
        Vector3 curVel = rb.linearVelocity;
        rb.linearVelocity = new Vector3(velocidadeDesejada.x, curVel.y, velocidadeDesejada.z);
        #else
        Vector3 curVel = rb.velocity;
        rb.velocity = new Vector3(velocidadeDesejada.x, curVel.y, velocidadeDesejada.z);
        #endif

        // 3) Rotação visual
        AtualizarRotacaoVisual(direcaoPlanar);

        // 4) Animator
        if (animator != null && !string.IsNullOrEmpty(nomeParametroSpeed))
            animator.SetFloat(nomeParametroSpeed, velocidadeDesejada.magnitude);
    }

    private void AtualizarRotacaoVisual(Vector3 direcaoPlanar)
    {
        if (pivoModelo == null) return;

        Vector3 dir = direcaoPlanar;
        if (dir.sqrMagnitude < limiarRotacao * limiarRotacao)
        {
            if (!girarQuandoParado) return;
            dir = ultimaDirecaoPlanar;
            if (dir.sqrMagnitude < 1e-6f) return;
        }

        Quaternion rotBase = Quaternion.LookRotation(dir, Vector3.up);
        Quaternion rotCorrigida = rotBase * Quaternion.Euler(0f, deslocamentoYawModelo, 0f);

        if (interpolacaoGiro <= 0f)
            pivoModelo.rotation = rotCorrigida;
        else
            pivoModelo.rotation = Quaternion.Slerp(pivoModelo.rotation, rotCorrigida, Mathf.Clamp01(interpolacaoGiro * Time.deltaTime));
    }

    private Vector3 CalcularDirecaoPlanarNormalizada(Vector2 inputUnitario)
    {
        if (inputUnitario == Vector2.zero) return Vector3.zero;

        if (relativoACamera && transformCamera != null)
        {
            Vector3 frente = Vector3.ProjectOnPlane(transformCamera.forward, Vector3.up).normalized;
            if (frente.sqrMagnitude < 1e-6f) frente = Vector3.forward;

            Vector3 direita = Vector3.Cross(Vector3.up, frente).normalized;
            if (direita.sqrMagnitude < 1e-6f) direita = Vector3.right;

            Vector3 combinado = direita * inputUnitario.x + frente * inputUnitario.y;
            return combinado.sqrMagnitude > 1e-6f ? combinado.normalized : Vector3.zero;
        }
        else
        {
            Vector3 v = new Vector3(inputUnitario.x, 0f, inputUnitario.y);
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.zero;
        }
    }
}
