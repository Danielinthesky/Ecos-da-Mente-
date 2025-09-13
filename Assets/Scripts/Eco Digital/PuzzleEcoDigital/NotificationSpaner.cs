// NotificationSpawner.cs
using System.Collections.Generic;
using UnityEngine;

public class NotificationSpawner : MonoBehaviour
{
    [Header("Refs")]
    public GestureInputRecognizer recognizer;
    public Transform eco;                 // alvo no centro
    public Notification prefab;

    [Header("Spawn")]
    public float raioSpawn = 6f;
    public float intervalo = 1.2f;
    public Vector2 velocidadeMinMax = new(1.6f, 2.8f);

    [Header("Dificuldade")]
    public AnimationCurve chancePorSimbolo = AnimationCurve.Linear(0,1,1,1); // manter simples
    public List<GestureSymbol> simbolosPossiveis = new(){ GestureSymbol.Triangulo, GestureSymbol.Quadrado, GestureSymbol.Circulo, GestureSymbol.Vee, GestureSymbol.Raio, GestureSymbol.Barra };

    readonly List<Notification> vivas = new();
    float timer;

    void OnEnable()
    {
        if (recognizer != null)
            recognizer.OnGestureRecognized.AddListener(OnGesture);
    }

    void OnDisable()
    {
        if (recognizer != null)
            recognizer.OnGestureRecognized.RemoveListener(OnGesture);
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= intervalo)
        {
            timer = 0f;
            Spawn();
        }
    }

    void Spawn()
    {
        if (prefab == null || eco == null) return;

        Vector2 dir = Random.insideUnitCircle.normalized;
        Vector3 pos = eco.position + new Vector3(dir.x, 0f, dir.y) * raioSpawn;

        var go = Instantiate(prefab, pos, Quaternion.identity);
        var n = go.GetComponent<Notification>();
        var simb = simbolosPossiveis[Random.Range(0, simbolosPossiveis.Count)];
        float vel = Random.Range(velocidadeMinMax.x, velocidadeMinMax.y);

        n.velocidade = vel;
        n.Init(eco, simb, OnNotificacaoMorreu);

        // visual: opcionalmente mostre um texto/ícone do símbolo acima dela
        vivas.Add(n);
    }

    void OnNotificacaoMorreu(Notification n)
    {
        vivas.Remove(n);
    }

    void OnGesture(GestureSymbol g, float score)
    {
        // pega a notificação com esse símbolo mais próxima do Eco
        Notification alvo = null;
        float melhor = float.MaxValue;
        foreach (var n in vivas)
        {
            if (n == null) continue;
            if (n.simboloRequerido != g) continue;
            float d = (n.transform.position - eco.position).sqrMagnitude;
            if (d < melhor) { melhor = d; alvo = n; }
        }
        if (alvo != null)
            alvo.TentarQuebrar(g);
    }
}
