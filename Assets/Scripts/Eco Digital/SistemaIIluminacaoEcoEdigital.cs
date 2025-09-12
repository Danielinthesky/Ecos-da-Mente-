using UnityEngine;

/// Ajusta a iluminação global conforme o personagem caminha da esquerda (claro) para a direita (escuro).
/// - Usa o tamanho total da plataforma para normalizar a posição.
/// - Por padrão, mede com Renderer.bounds do objeto "plataformaRoot".
/// - Opcionalmente, você pode usar dois marcadores (esquerda/direita) para definir o range com precisão.
///
/// Como usar:
/// 1) Arraste sua Directional Light em "luzDirecional".
/// 2) Arraste o Transform do personagem em "personagem".
/// 3) Em "plataformaRoot", arraste o GameObject que representa a plataforma (com Renderer).
///    (OU marque "usarMarcadores" e preencha "marcadorEsquerda" e "marcadorDireita").
/// 4) Ajuste "intensidadeEsquerda" (100%) e "intensidadeDireita" (mais escuro).
/// 5) [Opcional] Ative "ajustarAmbiente" para também reduzir RenderSettings.ambientIntensity.
[DisallowMultipleComponent]
public class SistemaIluminacaoEcoDigital : MonoBehaviour
{
    [Header("Referências")]
    [Tooltip("Directional Light usada como luz global.")]
    [SerializeField] private Light luzDirecional;

    [Tooltip("Transform do personagem que se move na plataforma.")]
    [SerializeField] private Transform personagem;

    [Tooltip("Raiz da plataforma (usa Renderer.bounds para medir largura X).")]
    [SerializeField] private GameObject plataformaRoot;

    [Header("Marcadores (opcional)")]
    [Tooltip("Se verdadeiro, ignora Renderer.bounds e usa marcadores explícitos (esquerda/direita).")]
    [SerializeField] private bool usarMarcadores = false;

    [Tooltip("Ponto extremo ESQUERDO em mundo.")]
    [SerializeField] private Transform marcadorEsquerda;

    [Tooltip("Ponto extremo DIREITO em mundo.")]
    [SerializeField] private Transform marcadorDireita;

    [Header("Iluminação")]
    [Tooltip("Intensidade da Directional Light quando o personagem está totalmente à ESQUERDA.")]
    [SerializeField, Min(0f)] private float intensidadeEsquerda = 1.0f;

    [Tooltip("Intensidade da Directional Light quando o personagem está totalmente à DIREITA.")]
    [SerializeField, Min(0f)] private float intensidadeDireita = 0.2f;

    [Tooltip("Curva para mapear a progressão (0=esquerda, 1=direita) -> peso. Use linear por padrão.")]
    [SerializeField] private AnimationCurve curvaEscurecer = AnimationCurve.Linear(0, 0, 1, 1);

    [Tooltip("Suavização (segundos) para a mudança de intensidade. 0 = sem suavizar.")]
    [SerializeField, Min(0f)] private float suavizacao = 0.1f;

    [Header("Ambiente (opcional)")]
    [Tooltip("Se verdadeiro, ajusta também RenderSettings.ambientIntensity.")]
    [SerializeField] private bool ajustarAmbiente = false;

    [Tooltip("AmbientIntensity à ESQUERDA.")]
    [SerializeField, Min(0f)] private float ambienteEsquerda = 1.0f;

    [Tooltip("AmbientIntensity à DIREITA.")]
    [SerializeField, Min(0f)] private float ambienteDireita = 0.2f;

    // estado interno
    private float alvoIntensidadeLuz;
    private float velIntensidadeLuz; // para SmoothDamp

    private float alvoIntensidadeAmb;
    private float velIntensidadeAmb; // para SmoothDamp

    // limites calculados
    private float xEsquerda;
    private float xDireita;
    private bool limitesValidos;

    private void Reset()
    {
        if (!luzDirecional)
        {
#if UNITY_2023_1_OR_NEWER
            luzDirecional = Object.FindFirstObjectByType<Light>();
#else
            luzDirecional = Object.FindObjectOfType<Light>();
#endif
            if (luzDirecional && luzDirecional.type != LightType.Directional)
                luzDirecional = null; // só aceitamos Directional
        }

        if (!personagem)
        {
#if UNITY_2023_1_OR_NEWER
            var player = Object.FindFirstObjectByType<CharacterController>();
            if (player) personagem = player.transform;
#endif
        }
    }

    private void Start()
    {
        CalcularLimites();
        // inicia já com a luz "cheia" (à esquerda)
        if (luzDirecional) luzDirecional.intensity = intensidadeEsquerda;
        if (ajustarAmbiente) RenderSettings.ambientIntensity = ambienteEsquerda;
    }

    private void OnValidate()
    {
        // Garante que esquerda/direita façam sentido
        if (intensidadeDireita > intensidadeEsquerda && Application.isPlaying == false)
        {
            // não é erro, mas costuma ser desejável esquerda >= direita
        }
        if (usarMarcadores && marcadorEsquerda && marcadorDireita)
        {
            // nada a fazer aqui; limites serão lidos no Update caso mude no editor
        }
    }

    private void Update()
    {
        if (!luzDirecional || !personagem)
            return;

        if (usarMarcadores)
        {
            if (marcadorEsquerda && marcadorDireita)
            {
                xEsquerda = marcadorEsquerda.position.x;
                xDireita  = marcadorDireita.position.x;
                limitesValidos = !Mathf.Approximately(xEsquerda, xDireita);
            }
            else
            {
                limitesValidos = false;
            }
        }
        else
        {
            // se não for marcadores, tenta recalcular só uma vez (ou se ainda não temos)
            if (!limitesValidos) CalcularLimites();
        }

        if (!limitesValidos)
            return;

        // normaliza a posição do personagem entre [0..1]
        float t = Mathf.InverseLerp(xEsquerda, xDireita, personagem.position.x);
        t = Mathf.Clamp01(t);

        // curva permite não-linear (ease-in/out)
        float peso = Mathf.Clamp01(curvaEscurecer.Evaluate(t));

        // interpola intensidades
        alvoIntensidadeLuz = Mathf.Lerp(intensidadeEsquerda, intensidadeDireita, peso);

        if (suavizacao > 0f)
            luzDirecional.intensity = Mathf.SmoothDamp(luzDirecional.intensity, alvoIntensidadeLuz, ref velIntensidadeLuz, suavizacao);
        else
            luzDirecional.intensity = alvoIntensidadeLuz;

        // opcional: ambiente
        if (ajustarAmbiente)
        {
            alvoIntensidadeAmb = Mathf.Lerp(ambienteEsquerda, ambienteDireita, peso);
            if (suavizacao > 0f)
                RenderSettings.ambientIntensity = Mathf.SmoothDamp(RenderSettings.ambientIntensity, alvoIntensidadeAmb, ref velIntensidadeAmb, suavizacao);
            else
                RenderSettings.ambientIntensity = alvoIntensidadeAmb;
        }
    }

    private void CalcularLimites()
    {
        limitesValidos = false;
        if (usarMarcadores)
        {
            if (marcadorEsquerda && marcadorDireita)
            {
                xEsquerda = marcadorEsquerda.position.x;
                xDireita  = marcadorDireita.position.x;
                limitesValidos = !Mathf.Approximately(xEsquerda, xDireita);
            }
            return;
        }

        if (!plataformaRoot) return;

        // tenta pegar um Renderer para medir bounds (mundo)
        var rend = plataformaRoot.GetComponentInChildren<Renderer>();
        if (rend)
        {
            var b = rend.bounds;
            xEsquerda = b.min.x;
            xDireita  = b.max.x;
            limitesValidos = !Mathf.Approximately(xEsquerda, xDireita);
        }
        // Se não tiver Renderer, poderia-se expandir para Colliders; mantive simples por agora.
    }

    private void OnDrawGizmosSelected()
    {
        // Visualiza o range de medição
        Gizmos.color = Color.yellow;

        if (usarMarcadores && marcadorEsquerda && marcadorDireita)
        {
            Vector3 a = marcadorEsquerda.position;
            Vector3 b = marcadorDireita.position;
            Gizmos.DrawSphere(a, 0.05f);
            Gizmos.DrawSphere(b, 0.05f);
            Gizmos.DrawLine(a, b);
        }
        else if (plataformaRoot)
        {
            var rend = plataformaRoot.GetComponentInChildren<Renderer>();
            if (rend)
            {
                var b = rend.bounds;
                var a = new Vector3(b.min.x, b.center.y, b.center.z);
                var c = new Vector3(b.max.x, b.center.y, b.center.z);
                Gizmos.DrawSphere(a, 0.05f);
                Gizmos.DrawSphere(c, 0.05f);
                Gizmos.DrawLine(a, c);
            }
        }
    }
}
