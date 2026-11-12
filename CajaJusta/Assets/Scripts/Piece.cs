using UnityEngine;

/// <summary>
/// Una figura: polígono convexo con cuerpo físico 2D.
/// Quieta = cuerpo estático (no la empuja nadie). Seleccionada = cuerpo dinámico, así
/// al chocar contra otra figura gira y se desliza como en la vida real.
/// </summary>
public class Piece
{
    public readonly Vector2[] Local;   // vértices relativos al centro, antihorario
    public readonly float Area;
    public readonly float Radius;      // radio del círculo que la envuelve
    public readonly Color Color;
    public Vector2 Pos;                // copia de la posición/ángulo actuales (se actualiza con Sync)
    public float Angle;
    public readonly Transform Root;
    public readonly Rigidbody2D Body;

    readonly Transform visual;
    readonly Mesh borderMesh, fillMesh;
    readonly MeshRenderer borderR, fillR;
    float pop;
    bool selected;

    static readonly Color BorderColor = new Color(0.08f, 0.07f, 0.13f, 1f);   // retro dark outline
    static readonly Color SelectedBorder = Color.white;

    public Piece(Vector2[] local, Color color, Material mat, Transform parent)
    {
        Local = local;
        Color = color;
        Area = ShapeGenerator.Area(local);
        foreach (var v in local) Radius = Mathf.Max(Radius, v.magnitude);

        Root = new GameObject("Piece").transform;
        Root.SetParent(parent, false);

        Body = Root.gameObject.AddComponent<Rigidbody2D>();
        Body.bodyType = RigidbodyType2D.Static;
        Body.gravityScale = 0;
        Body.interpolation = RigidbodyInterpolation2D.Interpolate;
        Body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        Body.useAutoMass = true;
        var col = Root.gameObject.AddComponent<PolygonCollider2D>();
        col.points = local;
        col.density = 1f;

        // lo visual va en un hijo, así la animación de "rebote" no cambia el tamaño del colisionador
        visual = new GameObject("Visual").transform;
        visual.SetParent(Root, false);
        borderMesh = BuildMesh(local, 0f);
        fillMesh = BuildMesh(local, 0.14f);
        borderR = AddMesh("Border", borderMesh, mat);
        fillR = AddMesh("Fill", fillMesh, mat);
        SetColors(BorderColor, color);
        SetSorting(10);
    }

    MeshRenderer AddMesh(string name, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(visual, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        return r;
    }

    static Mesh BuildMesh(Vector2[] poly, float inset)
    {
        int n = poly.Length;
        var verts = new Vector3[n + 1];
        verts[0] = Vector3.zero;
        for (int i = 0; i < n; i++)
        {
            var v = poly[i];
            float len = v.magnitude;
            verts[i + 1] = len > inset ? v * ((len - inset) / len) : Vector2.zero;
        }
        var tris = new int[n * 3];
        for (int i = 0; i < n; i++)
        {
            tris[i * 3] = 0;
            tris[i * 3 + 1] = 1 + (i + 1) % n;
            tris[i * 3 + 2] = 1 + i;
        }
        var m = new Mesh { vertices = verts, triangles = tris, colors = new Color[n + 1] };
        m.RecalculateBounds();
        return m;
    }

    static void Paint(Mesh m, Color c)
    {
        var cols = new Color[m.vertexCount];
        for (int i = 0; i < cols.Length; i++) cols[i] = c;
        m.colors = cols;
    }

    void SetColors(Color border, Color fill) { Paint(borderMesh, border); Paint(fillMesh, fill); }

    public void Place(Vector2 pos, float angle)
    {
        Pos = pos; Angle = angle;
        Root.SetPositionAndRotation(new Vector3(pos.x, pos.y, 0), Quaternion.Euler(0, 0, angle));
        Body.position = pos;
        Body.rotation = angle;
    }

    /// <summary>Seleccionada: cuerpo dinámico que reacciona a los choques. Si no: estático.</summary>
    public void SetSelected(bool on)
    {
        selected = on;
        SetColors(on ? SelectedBorder : BorderColor, Color);
        if (on)
        {
            pop = 1f;
            Body.bodyType = RigidbodyType2D.Dynamic;
            Body.gravityScale = 0;
            Body.linearDamping = 3f;
            Body.angularDamping = 5f;
            Body.linearVelocity = Vector2.zero;
            Body.angularVelocity = 0;
        }
        else
        {
            Body.bodyType = RigidbodyType2D.Static;
        }
    }

    public void Stop()
    {
        if (Body.bodyType != RigidbodyType2D.Dynamic) return;
        Body.linearVelocity = Vector2.zero;
        Body.angularVelocity = 0;
    }

    public void SetSorting(int order) { borderR.sortingOrder = order; fillR.sortingOrder = order + 1; }

    public void Sync()
    {
        Pos = Root.position;
        Angle = Root.eulerAngles.z;
    }

    /// <summary>Vértices en coordenadas del mundo.</summary>
    public Vector2[] World()
    {
        float r = Angle * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        var w = new Vector2[Local.Length];
        for (int i = 0; i < Local.Length; i++)
        {
            var v = Local[i];
            w[i] = new Vector2(Pos.x + v.x * c - v.y * s, Pos.y + v.x * s + v.y * c);
        }
        return w;
    }

    public void Animate(float dt)
    {
        pop = Mathf.MoveTowards(pop, 0, dt * 5f);
        float s = 1f + Mathf.Sin(pop * Mathf.PI) * 0.06f;
        visual.localScale = new Vector3(s, s, 1);
    }

    public void Destroy()
    {
        Object.Destroy(borderMesh);
        Object.Destroy(fillMesh);
        Object.Destroy(Root.gameObject);
    }
}
