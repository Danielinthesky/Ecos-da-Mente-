// DestroyTargetsOnEvent.cs
using UnityEngine;

public class DestroyTargetsOnEvent : MonoBehaviour
{
    [Header("Como identificar alvos")]
    [SerializeField] private bool destruirPorComponente = true;
    [SerializeField] private bool destruirPorTag = false;
    [SerializeField] private string tagAlvo = "Destruivel";

    // Conecte ESTE método ao OnCircleRecognizedSimple do seu GestureCircleRecognizer
    public void DestroyTargets()
    {
        if (destruirPorComponente)
        {
            var alvos = FindObjectsOfType<DestroyOnCircle>(false);
            for (int i = 0; i < alvos.Length; i++)
                if (alvos[i] != null) Destroy(alvos[i].gameObject);
        }

        if (destruirPorTag && !string.IsNullOrEmpty(tagAlvo))
        {
            var gos = GameObject.FindGameObjectsWithTag(tagAlvo);
            for (int i = 0; i < gos.Length; i++)
                if (gos[i] != null) Destroy(gos[i]);
        }
    }
}
