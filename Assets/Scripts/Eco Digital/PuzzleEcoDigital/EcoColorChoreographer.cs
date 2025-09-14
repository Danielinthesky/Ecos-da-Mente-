using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class EcoColorChoreographer : MonoBehaviour
{
    [Header("ALVOS")]
    [SerializeField] private Light directionalLight;          // Directional Light principal
    [SerializeField] private Renderer ecoRenderer;            // SkinnedMeshRenderer/MeshRenderer do Eco
    [SerializeField] private Material ecoMaterial;            // (opcional) material do Eco (instanciado abaixo, se setado)
    [SerializeField] private int materialSlot = 0;            // slot no renderer, se usar ecoMaterial

    [Header("FLAGS (começam ligadas)")]
    [Tooltip("Liga/desliga a animação da LUZ.")]
    [SerializeField] private bool habilitarLuz = true;
    [Tooltip("Liga/desliga a animação do ECO.")]
    [SerializeField] private bool habilitarEco = true;

    [Header("TEMPO")]
    [Tooltip("Duração total da sequência (segundos).")]
    [SerializeField, Min(0.01f)] private float duracaoTotal = 90f;
    [SerializeField] private bool loop = false;

    // ---------- LUZ ----------
    [Header("LUZ (Directional Light)")]
    [Tooltip("Cores da luz ao longo do tempo (0..1).")]
    [SerializeField] private Gradient luzGradient = DefaultLightGradient();
    [Tooltip("Intensidade da luz ao longo do tempo (0..1 → tempo normalizado).")]
    [SerializeField] private AnimationCurve intensidadeLuz = AnimationCurve.EaseInOut(0, 1f, 1, 1.1f);

    [Tooltip("Rotação inicial da luz (Euler).")]
    [SerializeField] private Vector3 luzEulerInicio = new Vector3(35f, -30f, 0f);
    [Tooltip("Rotação final da luz (Euler).")]
    [SerializeField] private Vector3 luzEulerFim = new Vector3(10f, 20f, 0f);

    // ---------- ECO ----------
    [Header("ECO (Material)")]
    [Tooltip("Nome da propriedade de cor do shader. URP/HDRP = _BaseColor | Standard = _Color")]
    [SerializeField] private string nomePropriedadeCor = "_BaseColor";
    [Tooltip("Usar também emissão (glow)? Shader precisa ter _EmissionColor.")]
    [SerializeField] private bool usarEmissao = true;
    [Tooltip("Multiplicador da emissão ao longo do tempo (0..1 → tempo normalizado).")]
    [SerializeField] private AnimationCurve emissaoIntensidade = AnimationCurve.Linear(0, 0.0f, 1, 1.0f);

    // A cor do Eco segue o gradiente da LUZ, mas invertido (tEco = 1 - t)
    // Se quiser um gradiente próprio pro Eco, habilite abaixo:
    [Tooltip("Se setado, usa este gradiente em vez de inverter o da luz.")]
    [SerializeField] private Gradient ecoGradientOverride = null;

    // ---------- CENÁRIO / AMBIENTE ----------
    [Header("AMBIENTE (opcional)")]
    [Tooltip("Animar cor da névoa (RenderSettings.fogColor) ao longo do tempo?")]
    [SerializeField] private bool animarFog = true;
    [SerializeField] private Gradient fogGradient = DefaultFogGradient();
    [Tooltip("Densidade da névoa ao longo do tempo.")]
    [SerializeField] private AnimationCurve fogDensidade = AnimationCurve.EaseInOut(0, 0.01f, 1, 0.06f);

    [Tooltip("Animar Skybox (_Tint/_Exposure) se existir?")]
    [SerializeField] private bool animarSkybox = true;
    [SerializeField] private AnimationCurve skyboxExposure = AnimationCurve.EaseInOut(0, 1.0f, 1, 1.15f);
    [SerializeField] private Gradient skyboxTint = DefaultSkyboxGradient();

    // internos
    private MaterialPropertyBlock _mpb;
    private bool _rodando;
    private Material _ecoMaterialInst; // instância local, se usar ecoMaterial

    private void Reset()
    {
        if (!directionalLight)
        {
            foreach (var l in FindObjectsOfType<Light>())
                if (l.type == LightType.Directional) { directionalLight = l; break; }
        }
        if (!ecoRenderer) ecoRenderer = GetComponentInChildren<Renderer>();
    }

    private void Awake()
    {
        if (_mpb == null) _mpb = new MaterialPropertyBlock();

        // Instancia material local (se fornecido) e aplica no slot indicado
        if (ecoMaterial && ecoRenderer)
        {
            _ecoMaterialInst = new Material(ecoMaterial);
            var mats = ecoRenderer.sharedMaterials;
            if (materialSlot >= 0 && materialSlot < mats.Length)
            {
                mats[materialSlot] = _ecoMaterialInst;
                ecoRenderer.sharedMaterials = mats;
            }
        }

        // Fallback do nome de propriedade de cor
        Shader shaderRef = null;
        if (_ecoMaterialInst) shaderRef = _ecoMaterialInst.shader;
        else if (ecoRenderer && ecoRenderer.sharedMaterial) shaderRef = ecoRenderer.sharedMaterial.shader;

        if (shaderRef)
        {
            if (!HasColorProperty(shaderRef, nomePropriedadeCor))
                nomePropriedadeCor = "_Color"; // padrão do Standard
        }
    }

    private void OnEnable()
    {
        if (!_rodando && isActiveAndEnabled) StartCoroutine(RunSequence());
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        _rodando = false;
    }

    private IEnumerator RunSequence()
    {
        _rodando = true;

        // Segurança
        if (duracaoTotal <= 0.01f) duracaoTotal = 0.01f;

        // Garante Fog ligado se vamos animar
        if (animarFog) RenderSettings.fog = true;

        do
        {
            float t = 0f;
            while (t < duracaoTotal)
            {
                float u = Mathf.Clamp01(t / duracaoTotal); // tempo normalizado 0..1
                float s = Smooth(u);

                // LUZ
                if (habilitarLuz && directionalLight)
                {
                    directionalLight.color = luzGradient.Evaluate(s);
                    directionalLight.intensity = intensidadeLuz.Evaluate(s);
                    var rot = Quaternion.Slerp(Quaternion.Euler(luzEulerInicio), Quaternion.Euler(luzEulerFim), s);
                    directionalLight.transform.rotation = rot;
                }

                // ECO (cor invertendo o gradiente da luz, ou gradiente próprio)
                if (habilitarEco)
                {
                    float ecoT = 1f - s;
                    Color ecoColor = (ecoGradientOverride != null)
                    ? ecoGradientOverride.Evaluate(s)
                    : luzGradient.Evaluate(ecoT);
                    ApplyEcoColor(ecoColor);

                    if (usarEmissao) ApplyEcoEmission(ecoColor, emissaoIntensidade.Evaluate(s));
                }

                // AMBIENTE
                if (animarFog)
                {
                    RenderSettings.fogColor = fogGradient.Evaluate(s);
                    // FogMode.Exponential/Linear não importam aqui, só densidade
                    RenderSettings.fogDensity = Mathf.Max(0f, fogDensidade.Evaluate(s));
                }

                if (animarSkybox && RenderSettings.skybox)
                {
                    if (RenderSettings.skybox.HasProperty("_Tint"))
                        RenderSettings.skybox.SetColor("_Tint", skyboxTint.Evaluate(s));
                    if (RenderSettings.skybox.HasProperty("_Exposure"))
                        RenderSettings.skybox.SetFloat("_Exposure", skyboxExposure.Evaluate(s));
                }

                t += Time.deltaTime;
                yield return null;
            }

            // Garante estado final
            float end = 1f;
            if (habilitarLuz && directionalLight)
            {
                directionalLight.color = luzGradient.Evaluate(end);
                directionalLight.intensity = intensidadeLuz.Evaluate(end);
                directionalLight.transform.rotation =
                    Quaternion.Slerp(Quaternion.Euler(luzEulerInicio), Quaternion.Euler(luzEulerFim), 1f);
            }

            if (habilitarEco)
            {
                Color ecoColorEnd = (ecoGradientOverride != null)
                ? ecoGradientOverride.Evaluate(1f)
                : luzGradient.Evaluate(0f);
                ApplyEcoColor(ecoColorEnd);
                if (usarEmissao) ApplyEcoEmission(ecoColorEnd, emissaoIntensidade.Evaluate(1f));
            }

            if (animarFog)
            {
                RenderSettings.fogColor = fogGradient.Evaluate(end);
                RenderSettings.fogDensity = Mathf.Max(0f, fogDensidade.Evaluate(end));
            }

            if (animarSkybox && RenderSettings.skybox)
            {
                if (RenderSettings.skybox.HasProperty("_Tint"))
                    RenderSettings.skybox.SetColor("_Tint", skyboxTint.Evaluate(end));
                if (RenderSettings.skybox.HasProperty("_Exposure"))
                    RenderSettings.skybox.SetFloat("_Exposure", skyboxExposure.Evaluate(end));
            }

        } while (loop && isActiveAndEnabled);

        _rodando = false;
    }

    // ---------- helpers ----------
    private float Smooth(float x) => x * x * (3f - 2f * x); // ease in-out suave

    private void ApplyEcoColor(Color c)
    {
        if (_ecoMaterialInst)
        {
            _ecoMaterialInst.SetColor(nomePropriedadeCor, c);
            return;
        }

        if (!ecoRenderer) return;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        ecoRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(nomePropriedadeCor, c);

        // Aplica no slot específico, se válido; senão aplica geral
        if (materialSlot >= 0 && materialSlot < ecoRenderer.sharedMaterials.Length)
            ecoRenderer.SetPropertyBlock(_mpb, materialSlot);
        else
            ecoRenderer.SetPropertyBlock(_mpb);
    }

    private void ApplyEcoEmission(Color baseColor, float intensity)
    {
        // Precisa que o shader tenha _EmissionColor e que Emission esteja habilitada
        Color emis = baseColor * Mathf.LinearToGammaSpace(Mathf.Max(0f, intensity));
        if (_ecoMaterialInst)
        {
            if (_ecoMaterialInst.HasProperty("_EmissionColor"))
            {
                _ecoMaterialInst.EnableKeyword("_EMISSION");
                _ecoMaterialInst.SetColor("_EmissionColor", emis);
            }
            return;
        }

        if (!ecoRenderer) return;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        ecoRenderer.GetPropertyBlock(_mpb);

        // MPB para emissao por-renderer (se o shader suportar via propriedade)
        // OBS: nem todos shaders leem _EmissionColor via MPB; se não funcionar, use material instanciado.
        _mpb.SetColor("_EmissionColor", emis);

        if (materialSlot >= 0 && materialSlot < ecoRenderer.sharedMaterials.Length)
            ecoRenderer.SetPropertyBlock(_mpb, materialSlot);
        else
            ecoRenderer.SetPropertyBlock(_mpb);
    }

    private bool HasColorProperty(Shader shader, string propName)
    {
        int count = shader.GetPropertyCount();
        for (int i = 0; i < count; i++)
            if (shader.GetPropertyName(i) == propName &&
                shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color)
                return true;
        return false;
    }

    // ---------- Gradientes padrão (cores do seu mood) ----------
    private static Gradient DefaultLightGradient()
    {
        // Azul profundo -> azul ardósia -> violeta leitoso -> off-white frio
        var g = new Gradient();
        g.colorKeys = new[]
        {
            new GradientColorKey(new Color(0.055f,0.078f,0.106f), 0f), // #0E141B
            new GradientColorKey(new Color(0.165f,0.204f,0.251f), 0.33f), // #2A3440
            new GradientColorKey(new Color(0.420f,0.427f,0.667f), 0.66f), // #6B6DAA
            new GradientColorKey(new Color(0.902f,0.914f,0.937f), 1f) // #E6E9EF
        };
        g.alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };
        return g;
    }

    private static Gradient DefaultFogGradient()
    {
        var g = new Gradient();
        g.colorKeys = new[]
        {
            new GradientColorKey(new Color(0.067f,0.102f,0.133f), 0f),   // #111A22
            new GradientColorKey(new Color(0.184f,0.212f,0.255f), 0.5f), // #2F3641
            new GradientColorKey(new Color(0.267f,0.306f,0.373f), 1f)    // #445066
        };
        g.alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };
        return g;
    }

    private static Gradient DefaultSkyboxGradient()
    {
        var g = new Gradient();
        g.colorKeys = new[]
        {
            new GradientColorKey(new Color(0.10f,0.13f,0.18f), 0f),
            new GradientColorKey(new Color(0.20f,0.24f,0.30f), 1f)
        };
        g.alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };
        return g;
    }

    // --- APIs públicas para ligar/desligar em runtime ---
    public void SetHabilitarLuz(bool ligado) => habilitarLuz = ligado;
    public void SetHabilitarEco(bool ligado) => habilitarEco = ligado;
    public void ReiniciarSequencia()
    {
        StopAllCoroutines();
        _rodando = false;
        if (isActiveAndEnabled) StartCoroutine(RunSequence());
    }
}
