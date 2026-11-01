# Logger

## Getting started

Logging works with no setup. Messages go to the Unity console from any thread.

```csharp
using RadTadDev.Logger;

Log.Trace("Verbose detail");
Log.Info("Something happened");
Log.Warn("Something looks wrong");
Log.Error("Something failed");
Log.Exception(exception);
Log.Debug("Quick, temporary message");
```

Messages are formatted as `[Level] [CallerFile]: message`. `Log.Debug` goes straight to the console
regardless of level and sinks.

## Filtering

`Log.MinimumLevel` hides anything below it. It defaults to `Info` and returns to that whenever play
mode starts, so set it from startup code. `LogLevel.None` turns logging off.

Use `Log.IsEnabled` to avoid building a message nobody will see:

```csharp
if (Log.IsEnabled(LogLevel.Trace))
{
    Log.Trace(BuildExpensiveDescription());
}
```

## Sinks

A sink is a destination for messages. `Log.AddSink` adds one, `Log.RemoveSink` removes and closes one,
and `Log.ClearSinks` removes and closes all of them, including the default console sink. A closed sink
cannot be added again, so create a new one.

| Base type | Use it when |
| --- | --- |
| `ThreadSafeSink` | The destination does not need the Unity API. Writes are serialized and run on the logging thread. |
| `UnitySafeSink` | The destination uses the Unity API. Writes are delivered on the main thread. |
| `ISink` | You want to handle thread safety yourself. |

```csharp
public class ListSink : UnitySafeSink
{
    public List<string> Lines { get; } = new();

    protected override void WriteOnMainThread(string message, LogLevel level) => Lines.Add(message);
}
```

## Limits

`UnitySafeSink` delivers at most `drainPerFrameLimit` queued logs per frame (default 1000) and queues at
most `queueLimit` logs (default 10000). Logs over the queue limit are dropped. Both limits warn in the
console. Pass your own values to the constructor, and use `Log.ClearSinks` first to replace the default
console sink with your own `new ConsoleSink(drainPerFrameLimit, queueLimit)`.

## Lifetime

Sinks are closed when play mode starts, when the application quits, and before an editor assembly
reload, and the default console sink is restored. Add your sinks again from startup code.

Closing happens on `Application.quitting`, which is before `OnDisable` and `OnDestroy` run during quit.
Logs from those go only to the console. Logs still queued from a worker thread that closes a sink right
before quit can be lost, because the main loop may not run again to deliver them.
