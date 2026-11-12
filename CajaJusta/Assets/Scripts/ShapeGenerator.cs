using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Genera figuras geométricas convexas al azar: polígonos regulares, rectángulos,
/// triángulos, trapecios, paralelogramos y polígonos irregulares.
/// Devuelve los vértices en sentido antihorario, centrados en el centroide.
/// </summary>
public static class ShapeGenerator
{
    public static Vector2[] RandomShape(float minArea = 2.2f, float maxArea = 5.5f)
    {
        Vector2[] p;
        switch (Random.Range(0, 8))
        {
            case 0: // polígono regular (triángulo equilátero ... octógono)
                p = Regular(Random.Range(3, 9));
                break;
            case 1: // rectángulo / cuadrado
            {
                float w = Random.Range(0.6f, 2.4f), h = Random.Range(0.6f, 1.4f);
                p = new[] { new Vector2(0, 0), new Vector2(w, 0), new Vector2(w, h), new Vector2(0, h) };
                break;
            }
            case 2: // triángulo rectángulo
            {
                float a = Random.Range(0.8f, 2.2f), b = Random.Range(0.8f, 2.2f);
                p = new[] { new Vector2(0, 0), new Vector2(a, 0), new Vector2(0, b) };
                break;
            }
            case 3: // triángulo cualquiera (no demasiado finito)
                p = RandomTriangle();
                break;
            case 4: // trapecio
            {
                float w = Random.Range(1.4f, 2.4f), top = w * Random.Range(0.3f, 0.8f), h = Random.Range(0.7f, 1.4f);
                float off = Random.Range(0f, w - top);
                p = new[] { new Vector2(0, 0), new Vector2(w, 0), new Vector2(off + top, h), new Vector2(off, h) };
                break;
            }
            case 5: // paralelogramo / rombo
            {
                float w = Random.Range(1f, 2f), h = Random.Range(0.7f, 1.4f), skew = Random.Range(0.3f, 0.9f) * (Random.value < 0.5f ? -1 : 1);
                p = new[] { new Vector2(0, 0), new Vector2(w, 0), new Vector2(w + skew, h), new Vector2(skew, h) };
                break;
            }
            default: // polígono irregular convexo
                p = RandomConvex(Random.Range(5, 10));
                break;
        }

        p = ConvexHull(p);                       // asegura orden antihorario y convexidad
        var c = Centroid(p);
        for (int i = 0; i < p.Length; i++) p[i] -= c;
        float s = Mathf.Sqrt(Random.Range(minArea, maxArea) / Area(p));
        for (int i = 0; i < p.Length; i++) p[i] *= s;
        return p;
    }

    static Vector2[] Regular(int n)
    {
        var p = new Vector2[n];
        float start = Random.value * Mathf.PI * 2;
        for (int i = 0; i < n; i++)
        {
            float a = start + i * Mathf.PI * 2 / n;
            p[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }
        return p;
    }

    static Vector2[] RandomTriangle()
    {
        for (int tries = 0; tries < 100; tries++)
        {
            var a = new float[3];
            for (int i = 0; i < 3; i++) a[i] = Random.value * 360f;
            System.Array.Sort(a);
            float g1 = a[1] - a[0], g2 = a[2] - a[1], g3 = 360f - a[2] + a[0];
            if (Mathf.Min(g1, Mathf.Min(g2, g3)) < 55f) continue;
            var p = new Vector2[3];
            for (int i = 0; i < 3; i++) p[i] = new Vector2(Mathf.Cos(a[i] * Mathf.Deg2Rad), Mathf.Sin(a[i] * Mathf.Deg2Rad));
            return p;
        }
        return Regular(3);
    }

    static Vector2[] RandomConvex(int n)
    {
        float rx = Random.Range(0.8f, 1.4f), ry = Random.Range(0.6f, 1.2f);
        var a = new float[n];
        for (int i = 0; i < n; i++) a[i] = Random.value * Mathf.PI * 2;
        System.Array.Sort(a);
        var p = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float r = Random.Range(0.85f, 1f);
            p[i] = new Vector2(Mathf.Cos(a[i]) * rx * r, Mathf.Sin(a[i]) * ry * r);
        }
        var hull = ConvexHull(p);
        return hull.Length >= 3 ? hull : Regular(5);
    }

    // ------------------------------------------------------------------ geometría

    public static float Area(Vector2[] p)
    {
        float a = 0;
        for (int i = 0; i < p.Length; i++)
        {
            var u = p[i]; var v = p[(i + 1) % p.Length];
            a += u.x * v.y - v.x * u.y;
        }
        return Mathf.Abs(a) * 0.5f;
    }

    public static Vector2 Centroid(Vector2[] p)
    {
        float a = 0, cx = 0, cy = 0;
        for (int i = 0; i < p.Length; i++)
        {
            var u = p[i]; var v = p[(i + 1) % p.Length];
            float cr = u.x * v.y - v.x * u.y;
            a += cr; cx += (u.x + v.x) * cr; cy += (u.y + v.y) * cr;
        }
        a *= 0.5f;
        return Mathf.Abs(a) < 1e-6f ? p[0] : new Vector2(cx / (6 * a), cy / (6 * a));
    }

    /// <summary>Envolvente convexa (monotone chain), sentido antihorario.</summary>
    public static Vector2[] ConvexHull(Vector2[] pts)
    {
        var p = new List<Vector2>(pts);
        p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        if (p.Count < 3) return p.ToArray();
        var h = new List<Vector2>();
        for (int pass = 0; pass < 2; pass++)
        {
            int start = h.Count;
            for (int i = 0; i < p.Count; i++)
            {
                var q = pass == 0 ? p[i] : p[p.Count - 1 - i];
                while (h.Count >= start + 2 && Cross(h[h.Count - 2], h[h.Count - 1], q) <= 1e-6f) h.RemoveAt(h.Count - 1);
                h.Add(q);
            }
            h.RemoveAt(h.Count - 1);
        }
        return h.ToArray();
    }

    static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

    /// <summary>
    /// Choque entre dos polígonos convexos (teorema del eje separador).
    /// Si se pisan, "normal" apunta de b hacia a y "depth" es cuánto hay que correr a "a" para separarlos.
    /// </summary>
    public static bool Overlap(Vector2[] a, Vector2[] b, out Vector2 normal, out float depth)
    {
        normal = Vector2.zero; depth = float.MaxValue;
        if (!TestAxes(a, b, ref normal, ref depth)) return false;
        if (!TestAxes(b, a, ref normal, ref depth)) return false;
        if (Vector2.Dot(Mean(a) - Mean(b), normal) < 0) normal = -normal;
        return true;
    }

    static bool TestAxes(Vector2[] p, Vector2[] q, ref Vector2 normal, ref float depth)
    {
        for (int i = 0; i < p.Length; i++)
        {
            var e = p[(i + 1) % p.Length] - p[i];
            var axis = new Vector2(-e.y, e.x).normalized;
            Project(p, axis, out float minP, out float maxP);
            Project(q, axis, out float minQ, out float maxQ);
            float o = Mathf.Min(maxP, maxQ) - Mathf.Max(minP, minQ);
            if (o <= 0) return false;
            if (o < depth) { depth = o; normal = axis; }
        }
        return true;
    }

    static void Project(Vector2[] p, Vector2 axis, out float min, out float max)
    {
        min = max = Vector2.Dot(p[0], axis);
        for (int i = 1; i < p.Length; i++)
        {
            float d = Vector2.Dot(p[i], axis);
            if (d < min) min = d; else if (d > max) max = d;
        }
    }

    static Vector2 Mean(Vector2[] p)
    {
        var s = Vector2.zero;
        foreach (var v in p) s += v;
        return s / p.Length;
    }

    /// <summary>¿El punto está dentro del polígono convexo (antihorario)?</summary>
    public static bool Contains(Vector2[] p, Vector2 pt)
    {
        for (int i = 0; i < p.Length; i++)
            if (Cross(p[i], p[(i + 1) % p.Length], pt) < 0) return false;
        return true;
    }
}
