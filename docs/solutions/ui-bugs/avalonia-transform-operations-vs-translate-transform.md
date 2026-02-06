---
title: Avalonia TranslateTransform bypasses CSS transitions — use TransformOperations.Parse
category: ui-bugs
tags: [avalonia, animation, transform, transition, css]
module: TaskerTray
date: 2026-02-06
severity: medium
symptoms:
  - Elements teleport instead of animating
  - TransformOperationsTransition has no effect
  - RenderTransform changes are instant despite transition being set
root_cause: >
  Setting RenderTransform = new TranslateTransform(0, N) creates a
  concrete transform object that bypasses Avalonia's TransformOperationsTransition.
  The transition only interpolates between TransformOperations values (string-parsed).
related:
  - docs/solutions/ui-bugs/pointer-pressed-handler-conflict-prevents-drag.md
  - docs/solutions/ui-bugs/cursor-inheritance-on-interactive-children.md
---

# Avalonia TranslateTransform Bypasses CSS Transitions

## Problem

Setting `RenderTransform = new TranslateTransform(0, offset)` in code causes elements to teleport instantly, even though `TransformOperationsTransition` is defined in XAML.

## Root Cause

Avalonia's `TransformOperationsTransition` only interpolates between `ITransform` values created by `TransformOperations.Parse()`. A concrete `TranslateTransform` object is a different type that the transition system doesn't handle.

Additionally, the XAML base value must use the same transform operation type. If the base is `translateX(0)` but you set `translateY(Npx)` in code, the transition can't interpolate between different operation types.

## Solution

```csharp
// WRONG — bypasses transitions, elements teleport
border.RenderTransform = new TranslateTransform(0, offset);

// RIGHT — transitions animate smoothly
border.RenderTransform = TransformOperations.Parse($"translateY({offset}px)");
```

Ensure the XAML base value matches the axis:

```xml
<!-- Base must use translateY if code sets translateY -->
<Setter Property="RenderTransform" Value="translateY(0px)"/>
```

Requires `using Avalonia.Media.Transformation;`

## Prevention

When animating transforms in Avalonia code-behind, always use `TransformOperations.Parse()` — never instantiate transform classes directly.
