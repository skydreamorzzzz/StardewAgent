from __future__ import annotations

import json
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
PROTOCOL = ROOT / "protocol"
SCHEMA_FILES = {
    "common.schema.json",
    "tool-definition.schema.json",
    "provider-manifest.schema.json",
    "tool-catalog-snapshot.schema.json",
    "invocation.schema.json",
}


def _load(name: str) -> dict[str, Any]:
    return json.loads((PROTOCOL / name).read_text(encoding="utf-8"))


def _walk(value: Any):
    yield value
    if isinstance(value, dict):
        for child in value.values():
            yield from _walk(child)
    elif isinstance(value, list):
        for child in value:
            yield from _walk(child)


def test_protocol_is_valid_json_schema_set_with_unique_ids():
    schemas = [_load(name) for name in sorted(SCHEMA_FILES)]

    assert {path.name for path in PROTOCOL.glob("*.schema.json")} == SCHEMA_FILES
    assert all(schema["$schema"] == "https://json-schema.org/draft/2020-12/schema" for schema in schemas)
    assert len({schema["$id"] for schema in schemas}) == len(schemas)


def test_all_cross_file_schema_references_resolve_to_known_definitions():
    schemas = {name: _load(name) for name in SCHEMA_FILES}

    for schema in schemas.values():
        for node in _walk(schema):
            if not isinstance(node, dict) or "$ref" not in node:
                continue
            reference = node["$ref"]
            if reference.startswith("#/"):
                target = schema
                fragment = reference[2:]
            else:
                filename, fragment = reference.split("#/", maxsplit=1)
                assert filename in schemas
                target = schemas[filename]
            for part in fragment.split("/"):
                target = target[part]


def test_tool_identity_is_open_and_there_is_no_central_tool_enum():
    serialized = "\n".join(json.dumps(_load(name), sort_keys=True) for name in SCHEMA_FILES)

    assert "movement.bounded" not in serialized
    assert "input.move_left" not in serialized
    tool_ref = _load("common.schema.json")["$defs"]["ToolReference"]
    assert tool_ref["properties"]["id"] == {"$ref": "#/$defs/Identifier"}


def test_tool_definition_owns_its_contract_and_governance_metadata():
    definition = _load("tool-definition.schema.json")["$defs"]["ToolDefinition"]
    required = set(definition["required"])

    assert {
        "id",
        "version",
        "description",
        "input_contract",
        "output_contract",
        "provider",
        "exposure",
        "permissions",
        "side_effects",
        "execution_bounds",
        "preconditions",
    } <= required


def test_provider_manifest_registers_definition_envelopes_dynamically():
    manifest = _load("provider-manifest.schema.json")["$defs"]["ProviderManifest"]
    tool_items = manifest["properties"]["tools"]["items"]

    assert tool_items["$ref"].endswith("#/$defs/ToolDefinitionEnvelope")


def test_catalog_snapshot_is_revisioned_and_contains_exact_definitions():
    schema = _load("tool-catalog-snapshot.schema.json")
    snapshot = schema["$defs"]["ToolCatalogSnapshot"]
    entry = schema["$defs"]["CatalogEntry"]

    assert {"snapshot_id", "snapshot_hash", "registry_revision", "tools"} <= set(snapshot["required"])
    assert {"tool_ref", "definition", "resolution"} == set(entry["required"])
    assert entry["properties"]["resolution"]["properties"]["llm_visible"] == {"const": True}


def test_invocation_requires_exact_snapshot_and_tool_references():
    schema = _load("invocation.schema.json")
    request = schema["$defs"]["InvocationRequest"]
    checks = schema["$defs"]["FreshAdmissionChecks"]

    assert {"tool_ref", "catalog_ref", "arguments", "operation_id"} <= set(request["required"])
    assert set(checks["required"]) == {
        "tool_exists",
        "definition_matches",
        "input_valid",
        "permissions_allowed",
        "preconditions_satisfied",
        "budget_allowed",
        "side_effects_allowed",
        "cancellation_allowed",
    }
