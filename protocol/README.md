# Dynamic tool protocol

This directory defines how tools are described, discovered, snapshotted, and
invoked. It does not define which tools exist. Tool IDs are open strings, and
there is deliberately no central action or tool enum.

The schemas are design contracts. The current bootstrap runtime still uses its
older operation DTOs and hard-coded SMAPI handlers; migrating that runtime is a
separate implementation task.

## Files

| File | Responsibility |
| --- | --- |
| `common.schema.json` | Shared identifiers, content hashes, exact tool/catalog references, JSON values, and execution bounds. |
| `tool-definition.schema.json` | One provider-owned tool definition, including its input/output JSON Schemas, exposure policy, permissions, side effects, preconditions, and bounds. |
| `provider-manifest.schema.json` | A provider's revisioned discovery document containing any number of independently defined tools. |
| `tool-catalog-snapshot.schema.json` | An immutable, resolver-produced set of exact tool definitions visible to one Planner context at one registry revision. |
| `invocation.schema.json` | Generic request, fresh-admission decision, result, and cancellation envelopes used for every tool. |

## Required separation of states

These states are independent:

```text
registered != available != LLM-visible != authorized to execute
```

- **Registered** means the Registry accepted a definition from a Provider.
- **Available** is a contextual Resolver observation. It can change without a
  registration change.
- **LLM-visible** means the Resolver selected an available, exposure-eligible
  definition for one immutable catalog snapshot.
- **Authorized** can only be established by fresh admission immediately before
  dispatch. Catalog membership is never an authorization grant.

## Identity and immutability

`definition_hash` is `sha256:` plus the lowercase SHA-256 of the RFC 8785
canonical JSON representation of the `definition` object. The hash itself is
outside that object, so hashing is not self-referential.

`snapshot_hash` uses the same algorithm over the complete snapshot with the
`snapshot_hash` member omitted. A published `snapshot_id` is immutable; a
different tool set or resolution context requires a new snapshot ID and hash.

Registry and manifest revisions are opaque strings. Consumers compare them for
equality; they must not infer ordering unless the owning component documents an
ordering scheme.

The Registry MUST verify that an envelope's `definition_hash` matches its
definition and that the definition's provider reference matches the enclosing
manifest. The Resolver MUST copy the exact verified definition and construct a
matching `tool_ref` in each catalog entry.

## Data flow

```text
Provider manifest
  -> Registry registration and definition-hash verification
  -> Resolver evaluates current availability and exposure
  -> immutable Tool Catalog Snapshot at registry_revision
  -> Planner/LLM selects an exact tool_ref
  -> generic InvocationRequest with tool_ref + catalog_ref + arguments
  -> fresh admission against current Registry/world/policy/budget/cancel state
  -> Provider Handler only after an admitted decision
  -> generic InvocationResult revisions
```

1. A Provider publishes a full manifest revision. Each tool owns its input and
   output JSON Schema; no parameter schema is copied into a global registry.
2. The Registry validates and stores definitions, assigning a new registry
   revision. Registration alone says nothing about current availability.
3. The Resolver evaluates current provider availability, exposure rules, and
   Planner context. It emits a new immutable snapshot containing only exact,
   Planner-visible definitions.
4. The Planner receives that snapshot, not a live mutable registry view. It
   creates an invocation using the snapshot's `id + version + definition_hash`
   and its catalog reference. A bare tool name is invalid.
5. Fresh admission resolves that exact reference again and validates every
   check represented by `FreshAdmissionChecks`: existence, definition match,
   arguments, current permissions, current preconditions, budget, side-effect
   policy, and cancellation state. Hints supplied by the caller are not proof.
6. Only an admitted invocation is routed to the provider handler. Requested
   bounds may tighten the definition's bounds but MUST NOT widen them. Handler
   output is validated against that definition's output contract.

An admitted decision requires every mandatory check to be `pass`. `unknown` is
not `pass`. A snapshot may be stale by invocation time; rejection is therefore
normal and must not be bypassed by changing the operation ID.

## Provider and Bridge rules

The SMAPI Bridge is a Provider. It discovers primitive handlers by publishing a
provider manifest like every other provider. Python consumes the manifest and
catalog; the protocol never needs advance knowledge of primitive names.

Cross-process execution uses `InvocationRequest` for all tools. Providers may
route by the exact `tool_ref`, but the transport does not add per-tool request
types or branches. Driver methods such as `key_down` and `key_up` remain private
implementation APIs unless a Provider deliberately wraps and registers a
separately governed tool definition. Merely existing in GameDriver does not
make an API an Agent Tool.

## Adding a tool

To add a tool, its Provider adds one `ToolDefinitionEnvelope` to a new manifest
revision and publishes it. The Registry, Resolver, catalog, Planner, admission,
and invocation envelopes are generic. No central protocol file, tool enum, or
global argument union changes.
