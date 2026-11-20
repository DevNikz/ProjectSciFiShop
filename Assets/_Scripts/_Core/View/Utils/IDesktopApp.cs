using System;
using UnityEngine;

/// <summary>
/// A screen the DesktopScreen can show as a window (OrderScreen, FurnitureScreen). While it is a window,
/// the desktop owns the cursor, the input block and Esc. The screen only shows, fills and hides its panel.
/// </summary>
public interface IDesktopApp
{
    /// <summary>True while shown as a window by the desktop.</summary>
    bool IsWindowOpen { get; }

    /// <summary>The panel the desktop parents under its window layer (BuiltIn mode). Null = leave it where it is.</summary>
    Transform WindowRoot { get; }

    /// <summary>Raised when the window closes: its own close button, or HideWindow.</summary>
    event Action WindowClosed;

    /// <summary>Show as a window. Does not touch the input.</summary>
    void ShowWindow();

    /// <summary>Hide the window. Safe to call twice.</summary>
    void HideWindow();
}
