using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/// <summary>Asks the player for a name using the device keyboard.</summary>
public static class NamePrompt
{
    public const int MaxLength = 10;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern string TB_Prompt(string title, string defaultValue);
#endif

    static TouchScreenKeyboard keyboard;
    static Action<string> pending;

    /// <summary>Ask for a name; "done" receives the cleaned name (or null if cancelled).</summary>
    public static void Ask(string current, Action<string> done)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        done(Clean(TB_Prompt("YOUR NAME (MAX 10)", current)));
#else
        if (TouchScreenKeyboard.isSupported)
        {
            keyboard = TouchScreenKeyboard.Open(current, TouchScreenKeyboardType.Default, false, false, false, false, "YOUR NAME");
            pending = done;
        }
        else done(RandomName());   // editor / desktop: just roll a new random name
#endif
    }

    /// <summary>Call every frame (native keyboard polling).</summary>
    public static void Update()
    {
        if (keyboard == null) return;
        if (keyboard.status == TouchScreenKeyboard.Status.Visible) return;
        var cb = pending; var kb = keyboard;
        keyboard = null; pending = null;
        cb?.Invoke(kb.status == TouchScreenKeyboard.Status.Done ? Clean(kb.text) : null);
    }

    public static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        var sb = new StringBuilder();
        foreach (char c in s.ToUpperInvariant())
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == ' ')
                if (sb.Length < MaxLength) sb.Append(c);
        var r = sb.ToString().Trim();
        return r.Length == 0 ? null : r;
    }

    static readonly string[] Words = { "BOX", "CUBE", "TILE", "PACK", "FIT", "EDGE", "SNUG", "BLOCK" };
    public static string RandomName() => Words[UnityEngine.Random.Range(0, Words.Length)] + UnityEngine.Random.Range(10, 99);
}
