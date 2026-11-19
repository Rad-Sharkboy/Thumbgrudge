---
name: unity-6-2d-android
description: Use whenever writing or debugging Unity 6.6 (6000.6) C# scripts, 2D URP components, Input System touch controls, or Android builds.
---

# Unity 6.6 (6000.6) 2D URP & Android C# Rules

1. **Unity 6 API Compliance (No Deprecated Calls):**
   - Use Rigidbody2D.linearVelocity (NEVER b.velocity).
   - Use Object.FindFirstObjectByType<T>() (NEVER FindObjectOfType<T>()).
   - Use TMPro.TMP_Text / TextMeshProUGUI (NEVER legacy UnityEngine.UI.Text).
   - Use 2D physics & rendering strictly (Rigidbody2D, Collider2D, Physics2D, SpriteRenderer).
2. **Input System (Touch + Mouse Editor Fallback):**
   - Prefer UnityEngine.InputSystem.Pointer.current or EnhancedTouch so tap, hold, and drag work identically with a mouse in the Unity Editor and a finger on Android.
   - Always check EventSystem.current.IsPointerOverGameObject() before triggering gameplay taps so UI buttons do not fire player actions.
3. **Android Build & Performance Safety:**
   - Avoid Resources.Load or heavy per-frame allocations (GetComponent, Find, string concatenation) inside Update().
   - Provide complete, copy-paste-ready C# files with exact using statements and explicit Unity Inspector steps when a new component or tag is introduced.
