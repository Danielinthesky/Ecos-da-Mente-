// Notification.cs
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class Notification : MonoBehaviour
{
    public GestureSymbol simboloRequerido = GestureSymbol.Circulo;
    public float velocidade = 2f;
    public Transform alvo;        // Eco (centro)
    public float raioFalha = 0.6f; // se chegar nisso, falha

    System.Action<Notification> onDestruir;
    bool ativa = true;

    public void Init(Transform target, GestureSymbol simb, System.Action<Notification> onKill)
    {
        alvo = target;
        simboloRequerido = simb;
        onDestruir = onKill;
        ativa = true;
    }

    void Update()
    {
        if (!ativa || alvo == null) return;
        Vector3 dir = (alvo.position - transform.position);
        float dist = dir.magnitude;
        if (dist <= raioFalha)
        {
            // TODO: penalidade/vida do Eco
            Destruir(false);
            return;
        }
        Vector3 step = dir.normalized * velocidade * Time.deltaTime;
        transform.position += step;
        transform.forward = dir; // opcional: olhar pro alvo
    }

    public bool TentarQuebrar(GestureSymbol g)
    {
        if (!ativa) return false;
        if (g != simboloRequerido) return false;
        Destruir(true);
        return true;
    }

    void Destruir(bool sucesso)
    {
        ativa = false;
        onDestruir?.Invoke(this);
        Destroy(gameObject);
    }
}
