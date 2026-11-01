# Logger

A basic, practical, thread safe logger for Unity. Log from any thread with no setup, and send
messages to the console or your own sinks.

## Installation

In Unity, open **Window > Package Manager**, click **+ > Add package from git URL...** and enter:

```
https://github.com/RadTadDev/logger.git?path=/Assets/Logger
```

To pin a release, append a tag such as `#v0.1.0`. Requires Unity 6000.4 or newer.

## Usage

```csharp
using RadTadDev.Logger;

Log.Info("Player joined");
Log.Warn("Low memory");
Log.Error("Save failed");
Log.Exception(exception);

Log.MinimumLevel = LogLevel.Warn;
Log.AddSink(new MySink());
```

Derive from `ThreadSafeSink` for your own destination, or `UnitySafeSink` if it needs the Unity API.
See the [documentation](Assets/Logger/Documentation~/logger.md) for filtering, sinks, limits and
lifetime.

## Development

This repository is a Unity project and the package lives in [`Assets/Logger`](Assets/Logger). Open it
in Unity 6000.4.6f1 and run tests from **Window > General > Test Runner**.

Style rules come from [cs-style](https://github.com/RadTadDev/cs-style). Pull the latest with
`upkg style`, and check locally with the .NET SDK:

```
dotnet format whitespace ci/StyleCheck/StyleCheck.csproj --verify-no-changes
dotnet format style ci/StyleCheck/StyleCheck.csproj --verify-no-changes
dotnet run --project ci/DocCheck/DocCheck.csproj -- .
```

## Releasing

1. Bump `version` in [`Assets/Logger/package.json`](Assets/Logger/package.json).
2. Move the `Unreleased` notes in [`CHANGELOG.md`](Assets/Logger/CHANGELOG.md) under the new version.
3. Commit, then tag and push: `git tag v<version> && git push --tags`.
