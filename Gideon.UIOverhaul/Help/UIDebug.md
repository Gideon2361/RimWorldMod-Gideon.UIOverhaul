# UIDebug

The switch every piece of diagnostic instrumentation in the framework reads. **Off by default** — a consumer
that never sets it gets silence.

```csharp
UIDebug.Enabled = mySettings.debugLogging;   // push your own preference in

UIDebug.Log("cache rebuilt, " + count + " entries");
UIDebug.Warning("Focus diagnostics: CONFIRMED. id 213 -> 264");
```

Messages are prefixed and frame-stamped: `[Gideon.UIFramework] [debug f2944] ...`. The frame number is there
because most of what gets instrumented is about *when* something changed relative to something else, and two
lines from the same frame mean something different from two lines a frame apart.

## Members

| Member | Meaning |
|---|---|
| `Enabled` | Whether diagnostic logging is wanted. Set by the consuming mod. Takes effect immediately. |
| `InstrumentControlIds` | `Enabled` **as it stood at launch**. For instrumentation that allocates control ids. |
| `Log(string)` | A diagnostic message, or nothing when disabled. |
| `Warning(string)` | A diagnostic finding worth standing out in the log. |

`Warning` rather than `Log` for things you are actively waiting to see — a confirmed fault, a verdict from a
probe — since a `Message` scrolls past in the noise of a normal startup.

## Why this lives in the framework

The instrumentation does. `Gideon.UIFramework` cannot read `Gideon.UIOverhaul`'s settings file — that dependency
runs the wrong way — so the mod pushes its preference in here, and any other consumer can do the same.

In this mod the value comes from `debugLogging` in `UIOverhaul_Settings.xml`, exposed as **Diagnostics → "Write
debug detail to the log"** in the UI options page. It is deliberately *not* tied to RimWorld's dev mode: dev mode
stays on for whole sessions for unrelated reasons, and this is noisy enough to be worth choosing on purpose.

## Why there are two flags

`InstrumentControlIds` exists because the obvious implementation of the gate is a bug.

IMGUI derives a control's id from **draw order**. An id that is allocated only while a setting is on therefore
*becomes* a draw-order change the moment that setting is toggled — shifting every id after it and dropping
keyboard focus from whatever text field was in use. Instrumentation built to investigate that exact fault must
not be capable of causing it.

So `InstrumentControlIds` latches `Enabled` as it stood on the first frame after launch:

| | Logging | Id-allocating probes |
|---|---|---|
| On at launch | active | active |
| Off at launch | silent, zero cost | not allocated, zero cost |
| Toggled mid-session | follows immediately | unchanged until next launch |

Turning debug logging on mid-game starts the logging at once; probes that allocate ids wait for a restart. That
is the right trade — the alternative is a setting that can break keyboard focus while you flip it.

The options page says so in as many words: *"some of it only starts collecting after a restart."*

## Writing instrumentation against it

Gate the **logging** on `Enabled` and anything that **allocates a control id** on `InstrumentControlIds`:

```csharp
// Allocates an id -- must follow the latched flag, and must not be conditional on anything
// that can change within a session.
int sentinel = UIDebug.InstrumentControlIds ? GUIUtility.GetControlID(FocusType.Passive) : -1;

// Formatting is deferred, so nothing is built unless a report actually fires.
UITextBoxControl.DiagnosticContext = () => $"rows={rows.Count}, search=\"{search.Text}\"";
```

Two conventions worth keeping:

- **Report once, then disarm.** A fault that happens every frame would otherwise fill the log with the same line.
  `UITextBoxControl`'s focus probe reports once per launch and says so.
- **Make it able to refute, not just confirm.** A probe that can only report agreement tells you nothing when it
  stays quiet. The focus probe distinguishes an id shift from focus lost with the id unchanged, and attributes a
  shift to the consumer or to something upstream of it.

See [UITextBoxControl.md](UITextBoxControl.md) for the worked example.
