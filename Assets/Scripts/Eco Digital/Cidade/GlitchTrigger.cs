using System.Collections;
using UnityEngine;

/// Coloque este script no Eco (player).
/// Ao entrar num trigger com Tag "Glitch", dispara uma sequência de glitches.
/// A sequência roda por `sequenceDuration` com bursts a cada `interval` (ou intervalo aleatório),
/// e repete automaticamente `extraRepeats` vezes após `repeatDelay` (ex.: 15s).
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class GlitchTrigger : MonoBehaviour
{
    [Header("Detecção")]
    [Tooltip("Tag dos volumes que disparam glitch.")]
    [SerializeField] private string glitchTag = "Glitch";

    [Header("Sequência de Glitch")]
    [Tooltip("Duração da sequência inicial de glitches (segundos).")]
    [SerializeField] private float sequenceDuration = 2.0f;
    [Tooltip("Número de repetições automáticas após a sequência inicial.")]
    [SerializeField] private int extraRepeats = 1;
    [Tooltip("Tempo entre o fim de uma sequência e o início da próxima (segundos).")]
    [SerializeField] private float repeatDelay = 15f;

    [Header("Burst (cada disparo)")]
    [Tooltip("Intensidade máxima (pico) do burst.")]
    [SerializeField] private Vector2 peakRange = new Vector2(0.9f, 1.2f);
    [Tooltip("Intensidade alvo após decair.")]
    [SerializeField] private float settle = 0.35f;
    [Tooltip("Tempo para decair do pico até o 'settle'.")]
    [SerializeField] private float decayTime = 0.5f;

    [Header("Intervalo entre bursts")]
    [Tooltip("Usar intervalo aleatório entre bursts?")]
    [SerializeField] private bool randomInterval = true;
    [Tooltip("Intervalo fixo entre bursts (se randomInterval = false).")]
    [SerializeField] private float fixedInterval = 0.25f;
    [Tooltip("Intervalo aleatório (min,max) entre bursts (se randomInterval = true).")]
    [SerializeField] private Vector2 randomIntervalRange = new Vector2(0.15f, 0.35f);

    // estado
    private bool inGlitchVolume = false;
    private bool sequenceRunning = false;
    private bool armedForRetrigger = true;   // evita reentrância enquanto ainda estamos no mesmo volume
    private Coroutine currentRoutine;

    private void OnTriggerEnter(Collider other)
    {
        if (!other || !other.CompareTag(glitchTag)) return;
        Debug.Log("Evento Glitch Iniciado");

        inGlitchVolume = true;
        TryStartWholeCycle(); // sequência + repetições
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other || !other.CompareTag(glitchTag)) return;

        inGlitchVolume = false;
        // Ao sair, rearmamos para permitir novo disparo quando entrar novamente.
        armedForRetrigger = true;
    }

    private void TryStartWholeCycle()
    {
        if (!armedForRetrigger || sequenceRunning) return;
        if (GlitchController.Instance == null) return;

        armedForRetrigger = false;
        currentRoutine = StartCoroutine(RunSequences());
    }

    private IEnumerator RunSequences()
    {
        sequenceRunning = true;

        // sequência inicial
        yield return RunOneSequence();

        // repetições extras (ex.: repetir 1x após 15s)
        for (int i = 0; i < extraRepeats; i++)
        {
            yield return new WaitForSeconds(repeatDelay);
            yield return RunOneSequence();
        }

        sequenceRunning = false;

        // se ainda estamos dentro do volume, não re-dispara automaticamente;
        // fica armado apenas quando sair e entrar novamente
        if (!inGlitchVolume)
            armedForRetrigger = true;

        currentRoutine = null;
    }

    private IEnumerator RunOneSequence()
    {
        float t = 0f;
        while (t < sequenceDuration)
        {
            // dispara um burst
            float peak = Random.Range(peakRange.x, peakRange.y);
            GlitchController.Instance.Burst(peak, settle, decayTime);

            // espera próximo burst
            float wait = randomInterval
                ? Random.Range(randomIntervalRange.x, randomIntervalRange.y)
                : fixedInterval;

            // garante que não estoura o tempo total
            if (t + wait > sequenceDuration)
                wait = Mathf.Max(0f, sequenceDuration - t);

            yield return new WaitForSeconds(wait);
            t += wait;
        }
    }

    // utilitário caso queira disparar manualmente (Timeline, cutscene, etc.)
    [ContextMenu("Testar sequência agora")]
    public void DebugStartNow()
    {
        if (sequenceRunning || GlitchController.Instance == null) return;
        if (currentRoutine != null) StopCoroutine(currentRoutine);
        currentRoutine = StartCoroutine(RunSequences());
    }
}
