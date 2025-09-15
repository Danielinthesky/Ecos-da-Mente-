using UnityEngine;
using System.Collections;

/// <summary>
/// Faz fade-out (alpha) e opcionalmente afina a espessura de um LineRenderer ao longo do tempo.
/// Destroi o GameObject ao final (se destroyOnEnd = true).
/// </summary>
[DisallowMultipleComponent]
public class StrokeFader : MonoBehaviour
{
    private LineRenderer _lr;
    private Gradient _startGradient;
    private float _startWidth;
    private bool _active;

    public void Begin(LineRenderer lr, float delay, float duration, bool shrinkWidth, bool destroyOnEnd)
    {
        if (lr == null) return;
        _lr = lr;
        _startWidth = lr.startWidth;
        _startGradient = lr.colorGradient; // snapshot
        if (!_active) StartCoroutine(FadeRoutine(delay, duration, shrinkWidth, destroyOnEnd));
    }

    private IEnumerator FadeRoutine(float delay, float duration, bool shrinkWidth, bool destroyOnEnd)
    {
        _active = true;

        if (delay > 0f) yield return new WaitForSeconds(delay);

        float t = 0f;
        while (t < duration && _lr != null)
        {
            float k = 1f - (t / duration); // 1 -> 0
            ApplyAlpha(k);
            if (shrinkWidth) _lr.startWidth = _lr.endWidth = _startWidth * k;

            t += Time.deltaTime;
            yield return null;
        }

        if (_lr != null)
        {
            ApplyAlpha(0f);
            if (shrinkWidth) _lr.startWidth = _lr.endWidth = 0f;
        }

        if (destroyOnEnd) Destroy(gameObject);
        _active = false;
    }

    private void ApplyAlpha(float a)
    {
        if (_lr == null) return;

        // Recria um gradient com alpha escalado
        var g = new Gradient();
        var cks = _startGradient.colorKeys;
        var aks = _startGradient.alphaKeys;

        for (int i = 0; i < aks.Length; i++)
            aks[i].alpha = aks[i].alpha * a;

        g.SetKeys(cks, aks);
        _lr.colorGradient = g;
    }
}
