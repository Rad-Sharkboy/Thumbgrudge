---
name: mobile-jam-winner
description: Use whenever designing, scripting, or polishing Unity C# gameplay, UI, touch controls, audio, or game feel for a 90-minute mobile game jam.
---
# Mobile Game Jam Architecture & Juice Rules
1. **Juice on Every Action:** Include micro-screen shake (0.08s-0.15s), brief hit-stop on major impacts, randomized AudioSource.pitch (0.92f-1.08f) on SFX, scale-punch animations on score/UI, and ParticleSystem bursts.
2. **Mobile-First Controls & UI:** Support touch + mouse fallback in the same input wrapper. Favor one-thumb tap/hold/drag controls. Keep HUD inside Screen.safeArea with CanvasScaler (Scale With Screen Size, Match 0.5).
3. **Zero-Friction Wiring:** Keep gameplay in a single scene with a GameState enum (Title, Playing, GameOver). Use RequireComponent/GetComponent in Awake() instead of fragile manual Inspector links, and never rename public fields listed in AI_CONTEXT.md.
4. **Submission Tracking:** Append new scripts, Inspector wiring, and AI usage to AI_CONTEXT.md after every change.
