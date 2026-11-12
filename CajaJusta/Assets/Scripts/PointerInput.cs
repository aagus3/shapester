using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Entrada táctil / mouse. Funciona con el Input System nuevo y con el viejo.
/// Posiciones en píxeles, origen abajo a la izquierda.
/// </summary>
public static class PointerInput
{
    /// <summary>Dedo principal (o mouse en la PC).</summary>
    public static bool Get(out Vector2 pos, out bool down, out bool held, out bool up)
    {
        pos = Vector2.zero; down = held = up = false;
#if ENABLE_INPUT_SYSTEM
        var ts = Touchscreen.current;
        if (ts != null)
        {
            var t = ts.primaryTouch;
            if (t.press.isPressed || t.press.wasReleasedThisFrame)
            {
                pos = t.position.ReadValue();
                down = t.press.wasPressedThisFrame;
                held = t.press.isPressed;
                up = t.press.wasReleasedThisFrame;
                return true;
            }
        }
        var m = Mouse.current;
        if (m != null)
        {
            pos = m.position.ReadValue();
            down = m.leftButton.wasPressedThisFrame;
            held = m.leftButton.isPressed;
            up = m.leftButton.wasReleasedThisFrame;
            return true;
        }
        return false;
#else
        if (Input.touchCount > 0)
        {
            // el primer dedo que sigue apoyado (así, al levantar uno de dos, seguimos con el otro)
            int idx = -1;
            for (int i = 0; i < Input.touchCount; i++)
                if (Active(Input.GetTouch(i))) { idx = i; break; }
            var t = Input.GetTouch(idx >= 0 ? idx : 0);
            pos = t.position;
            down = t.phase == TouchPhase.Began;
            held = idx >= 0;
            up = idx < 0;
            return true;
        }
        if (!Input.mousePresent) return false;
        pos = Input.mousePosition;
        down = Input.GetMouseButtonDown(0);
        held = Input.GetMouseButton(0);
        up = Input.GetMouseButtonUp(0);
        return true;
#endif
    }

    /// <summary>Si hay dos dedos apoyados, devuelve sus posiciones (para girar con dos dedos).</summary>
    public static bool TwoFingers(out Vector2 a, out Vector2 b, out int idA, out int idB)
    {
        a = b = Vector2.zero; idA = idB = -1;
#if ENABLE_INPUT_SYSTEM
        var ts = Touchscreen.current;
        if (ts == null) return false;
        int n = 0;
        foreach (var t in ts.touches)
        {
            if (!t.press.isPressed) continue;
            if (n == 0) { a = t.position.ReadValue(); idA = t.touchId.ReadValue(); }
            else { b = t.position.ReadValue(); idB = t.touchId.ReadValue(); }
            if (++n == 2) return true;
        }
        return false;
#else
        int n = 0;
        for (int i = 0; i < Input.touchCount; i++)
        {
            var t = Input.GetTouch(i);
            if (!Active(t)) continue;
            if (n == 0) { a = t.position; idA = t.fingerId; }
            else { b = t.position; idB = t.fingerId; }
            if (++n == 2) return true;
        }
        return false;
#endif
    }

    /// <summary>Giro pedido con rueda del mouse o teclas Q/E (grados este frame, positivo = antihorario).</summary>
    public static float RotateInput()
    {
        float r = 0, dt = Time.deltaTime;
#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current;
        if (m != null) { float s = m.scroll.ReadValue().y; if (s > 0) r += 10; else if (s < 0) r -= 10; }
        var k = Keyboard.current;
        if (k != null) { if (k.qKey.isPressed) r += 180 * dt; if (k.eKey.isPressed) r -= 180 * dt; }
#else
        float s = Input.mouseScrollDelta.y;
        if (s > 0) r += 10; else if (s < 0) r -= 10;
        if (Input.GetKey(KeyCode.Q)) r += 180 * dt;
        if (Input.GetKey(KeyCode.E)) r -= 180 * dt;
#endif
        return r;
    }

#if !ENABLE_INPUT_SYSTEM
    static bool Active(Touch t) => t.phase != TouchPhase.Ended && t.phase != TouchPhase.Canceled;
#endif
}
