using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class EcoTiroProjetil : MonoBehaviour
{
    [Header("Configuração")]
    [SerializeField, Min(0.1f)] private float lifetime = 6f;

    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    /// <summary>
    /// Define a direção e velocidade do disparo uma única vez.
    /// O projétil segue em linha reta — não persegue o alvo.
    /// </summary>
    public void Lancar(Vector3 direcaoNormalizada, float velocidade)
    {
        if (_rb == null) return;

        Vector3 dir = direcaoNormalizada.sqrMagnitude > 0.0001f
            ? direcaoNormalizada.normalized
            : transform.forward;

        _rb.velocity = dir * Mathf.Max(0.1f, velocidade);
        transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

        if (lifetime > 0f) Destroy(gameObject, lifetime);
    }

    // Ajuste isto conforme sua colisão/jogo
    private void OnCollisionEnter(Collision collision)
    {
        // Ex.: destruir ao tocar em qualquer coisa que não seja outro projétil
        Destroy(gameObject);
    }
}
