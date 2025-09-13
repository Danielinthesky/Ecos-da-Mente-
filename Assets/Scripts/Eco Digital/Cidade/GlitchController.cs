using UnityEngine;

public class GlitchController : MonoBehaviour
{
    public static GlitchController Instance { get; private set; }

    [Header("Material do Full Screen Pass")]
    [SerializeField] private Material glitchMaterial;

    [Header("Valores padrão")]
    [SerializeField, Range(0,2)] private float defaultIntensity = 0f;
    [SerializeField, Range(2,128)] private float blockSize = 24f;
    [SerializeField, Range(0,5)] private float colorSplit = 0.8f;
    [SerializeField, Range(0,2)] private float scanlines = 0.4f;
    [SerializeField, Range(0,2)] private float jitter = 0.6f;
    [SerializeField, Range(0,5)] private float timeScale = 1.0f;

    [SerializeField] private bool testOscillate = true;
    [SerializeField] private float testSpeed = 6f;
    [SerializeField] private float testAmp = 0.7f;  // pico ~0.7
    [SerializeField] private float testBase = 0.2f; // base ~0.2

    private float currentIntensity;
    private float targetIntensity;
    private float decayPerSecond;

    void Awake()
    {
        Instance = this;
        ApplyStaticParams();
        SetIntensity(defaultIntensity);
    }

    void Update()
    {
        if (testOscillate && glitchMaterial)
    {
        float v = testBase + (Mathf.Sin(Time.time * testSpeed) * 0.5f + 0.5f) * testAmp;
        glitchMaterial.SetFloat("_Intensity", v);
    }

        if (decayPerSecond > 0f && currentIntensity > targetIntensity)
        {
            currentIntensity = Mathf.MoveTowards(currentIntensity, targetIntensity, decayPerSecond * Time.deltaTime);
            SetIntensity(currentIntensity);
        }
    }

    private void ApplyStaticParams()
    {
        if (!glitchMaterial) return;
        glitchMaterial.SetFloat("_BlockSize", blockSize);
        glitchMaterial.SetFloat("_ColorSplit", colorSplit);
        glitchMaterial.SetFloat("_Scanlines", scanlines);
        glitchMaterial.SetFloat("_Jitter", jitter);
        glitchMaterial.SetFloat("_TimeScale", timeScale);
    }

    private void SetIntensity(float v)
    {
        if (!glitchMaterial) return;
        glitchMaterial.SetFloat("_Intensity", v);
    }

    /// <summary>
    /// Dispara um pico de glitch que decai com o tempo.
    /// ex.: Burst(1.2f, 0.7f) => sobe para 1.2 e volta até 0.7 em ~0.5s (se não setar outro decay).
    /// </summary>
    public void Burst(float peak = 1.0f, float settle = 0.0f, float decayTime = 0.5f)
    {
        if (!glitchMaterial) return;
        currentIntensity = Mathf.Max(currentIntensity, peak);
        SetIntensity(currentIntensity);
        targetIntensity = Mathf.Clamp01(settle);
        decayPerSecond = (decayTime <= 0f) ? 0f : Mathf.Abs(currentIntensity - targetIntensity) / decayTime;
    }

    /// <summary> Liga/desliga o efeito globalmente. </summary>
    public void Enable(bool on)
    {
        if (!glitchMaterial) return;
        SetIntensity(on ? currentIntensity : 0f);
    }
}
