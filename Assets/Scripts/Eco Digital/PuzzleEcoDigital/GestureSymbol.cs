// GestureSymbols.cs
using System.Collections.Generic;
using UnityEngine;

public enum GestureSymbol
{
    Triangulo,
    Quadrado,
    Circulo,
    Vee,
    Raio,
    Barra
}

public static class GestureTemplates
{
    // Gera alguns templates procedurais simples em espaço [0..1]
    public static Dictionary<GestureSymbol, List<Vector2>> Load()
    {
        var dict = new Dictionary<GestureSymbol, List<Vector2>>();

        // Triângulo
        dict[GestureSymbol.Triangulo] = new List<Vector2>{
            new(0.1f,0.1f), new(0.5f,0.9f), new(0.9f,0.1f), new(0.1f,0.1f)
        };

        // Quadrado
        dict[GestureSymbol.Quadrado] = new List<Vector2>{
            new(0.2f,0.2f), new(0.8f,0.2f), new(0.8f,0.8f), new(0.2f,0.8f), new(0.2f,0.2f)
        };

        // Círculo (aproximação)
        dict[GestureSymbol.Circulo] = Circle(0.5f, 0.5f, 0.35f, 40);

        // V
        dict[GestureSymbol.Vee] = new List<Vector2>{
            new(0.15f,0.8f), new(0.5f,0.2f), new(0.85f,0.8f)
        };

        // Raio (zig-zag 3 segmentos)
        dict[GestureSymbol.Raio] = new List<Vector2>{
            new(0.2f,0.8f), new(0.55f,0.55f), new(0.35f,0.55f), new(0.8f,0.2f)
        };

        // Barra “/”
        dict[GestureSymbol.Barra] = new List<Vector2>{
            new(0.2f,0.2f), new(0.8f,0.8f)
        };

        return dict;
    }

    private static List<Vector2> Circle(float cx, float cy, float r, int steps)
    {
        var pts = new List<Vector2>(steps+1);
        for (int i=0;i<=steps;i++)
        {
            float t = i/(float)steps * Mathf.PI*2f;
            pts.Add(new Vector2(cx + Mathf.Cos(t)*r, cy + Mathf.Sin(t)*r));
        }
        return pts;
    }
}
