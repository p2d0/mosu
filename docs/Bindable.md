# Bindable ValueChanged Bug

## Symptom

`Bindable<string>.ValueChanged` fires only on the **first** value change. Subsequent sets of `Value` to a different string do not trigger the event, even though the value itself changes (confirmed by reading `Value` back).

## Reproduction

```csharp
var b = new Bindable<string>("A");
b.ValueChanged += e => Logger.Log($"{e.OldValue} -> {e.NewValue}");

b.Value = "B";  // fires: "A -> B"
b.Value = "A";  // does NOT fire
b.Value = "B";  // does NOT fire
b.Value = "A";  // does NOT fire
```

`b.Value` reads correctly each time. But `ValueChanged` only fires once.

## Root cause analysis

`Bindable<T>.Value` setter (osu.Framework/Bindables/Bindable.cs:99):
```csharp
if (EqualityComparer<T>.Default.Equals(this.value, value)) return;
SetValue(this.value, value);
```

This correctly checks equality and calls `SetValue` when values differ. `SetValue` calls `TriggerValueChange`.

`TriggerValueChange` (line 315):
```csharp
T beforePropagation = value;  // reads field

if (propagateToBindings && Bindings != null)
{
    foreach (var b in Bindings)
    {
        if (b == source) continue;
        b.SetValue(previousValue, value, bypassChecks, this);
    }
}

if (EqualityComparer<T>.Default.Equals(beforePropagation, value))
    ValueChanged?.Invoke(new ValueChangedEvent<T>(previousValue, value));
```

The event only fires if `beforePropagation == value` (the field) after bindings propagate. If a bound bindable pushes a value back during propagation, the field changes and the event is suppressed.

**Hypothesis**: In our case, `Refresh()` in `ProfileCardRow` is called from a `BindValueChanged` callback during the first profile change. `Refresh()` clears old `ProfileCard` instances and creates new ones, each subscribing to `activeProfile.BindValueChanged`. The old cards' subscriptions are never removed. This creates a growing list of callbacks.

More critically, `TabControlOverlayHeader` (parent of `LocalProfileHeader`) may use `BindTo` or `GetBoundCopy` on bindables passed through its `User` bindable, creating bidirectional bindings that push values back during `TriggerValueChange`, suppressing the event after the first fire.

## Workaround

Use a custom event instead of relying on `Bindable.ValueChanged`:

```csharp
public event Action<string>? ProfileChanged;

public void SetActiveProfile(string name)
{
    if (activeProfileBindable.Value == name) return;
    activeProfileBindable.Value = name;
    ProfileChanged?.Invoke(name);  // fires regardless of framework bug
}
```

All consumers subscribe to `ProfileChanged` instead of `ActiveProfile.BindValueChanged`.

## Files affected

- `LocalUserManager.cs` — `SetActiveProfile` + `ProfileChanged` event
- `ToolbarLocalUserButton.cs` — subscribes to `ProfileChanged`
- `LocalProfileHeader.cs` — subscribes to `ProfileChanged` for `Refresh()`
- `LocalUserProfileOverlay.cs` — subscribes to `ProfileChanged`
- `ProfileCardRow.cs` — calls `SetActiveProfile` on click
