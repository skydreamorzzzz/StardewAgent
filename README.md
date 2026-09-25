# StardewAgent

Early development. Architecture baseline and real-game SMAPI bridge bring-up completed.

StardewAgent currently contains the first verified vertical slice for one AI-controlled Stardew Valley player:

- a C# SMAPI Bridge loaded by Stardew Valley;
- Python ↔ localhost ↔ Bridge communication;
- structured, player-visible observations;
- bounded primitive game input;
- actor-aware contracts currently bound to `agent_player`.

The project does **not** yet implement an autonomous planner, long-term memory, RAG, high-level farming capabilities, autonomous mining/fishing, multiplayer, or multi-agent coordination.

The design target is [Architecture Specification v1.1](docs/architecture-v1.1.md). The specification describes intended architecture; it does not mean every module is implemented.

## Current repository boundaries

```text
bridge/StardewAgentBridge/   C# SMAPI lifecycle, public server, observation, operations, input driver
protocol/                    current cross-language wire and action schemas
src/sdv_agent/harness/       GameAdapter, SMAPI transport, operation executor, wire-facing models
src/sdv_agent/games/stardew/ verified primitive definitions
tests/                       current Python boundary tests
```

Future Architecture v1.1 modules are intentionally absent until they have real implementations.

## Requirements

- Stardew Valley 1.6.14 or later;
- SMAPI 4.4 or later;
- .NET SDK capable of building `net6.0` (verified with .NET SDK 8.0.425);
- Python 3.11 or later.

## Build and install the Bridge

From `bridge/StardewAgentBridge`:

```powershell
dotnet build
```

`Pathoschild.Stardew.ModBuildConfig` detects the local game installation and deploys the mod when possible. If detection fails, configure `GamePath` through the mod build package's supported mechanism; local game paths must not be committed.

The Bridge binds only to `127.0.0.1`. On first launch it generates a local token in the deployed mod's `config.json`. Keep that runtime file private and provide the same value to the Python process:

```powershell
$env:STARDEW_AGENT_TOKEN = "<local token from the deployed mod config>"
```

The default endpoint is `http://127.0.0.1:8765` and the default actor is `agent_player`.

## Install and verify Python

From the repository root:

```powershell
python -m pip install -e .
python -m pytest -q
sdv-smoke health
sdv-smoke observe
```

After loading a save, one low-risk primitive can be checked with:

```powershell
sdv-smoke move left
sdv-smoke observe
```

A primitive result means that the bounded input was injected. It does not claim that movement succeeded or that a higher-level game objective was completed. Such claims require later postcondition-aware capabilities.

## Information boundary

`ObservationProjector` exposes a deliberately small player-visible view: date/time, current location, player tile/facing, health, stamina, money, current item/tool, menu state, and world readiness. It does not expose remote container contents, hidden map objects, global NPC coordinates, RNG, or hidden flags.

## Known unverified behavior

Behavior while Stardew Valley loses focus or is minimized is not yet verified. In particular, simulation time, SMAPI update ticks, Bridge responsiveness, observations, and input/controller behavior need a dedicated follow-up investigation.
