from __future__ import annotations

import json
import re
from dataclasses import fields
from pathlib import Path

from sdv_agent.games.stardew.primitives import BoundedMoveSpec
from sdv_agent.harness.models import ControlView, OperationView


ROOT = Path(__file__).resolve().parents[1]


def _schema_properties(def_name: str) -> set[str]:
    schema = json.loads((ROOT / "protocol" / "wire.schema.json").read_text(encoding="utf-8"))
    return set(schema["$defs"][def_name]["properties"])


def _csharp_wire_names(record_name: str) -> set[str]:
    source = (ROOT / "bridge" / "StardewAgentBridge" / "WireModels.cs").read_text(encoding="utf-8")
    match = re.search(
        rf"internal sealed record {record_name}\((.*?)\n\);",
        source,
        flags=re.DOTALL,
    )
    assert match is not None, f"missing C# wire record {record_name}"
    return set(re.findall(r'JsonPropertyName\("([^"]+)"\)', match.group(1)))


def test_wire_schema_covers_csharp_operation_and_control_contracts():
    for name in ("OperationRequest", "OperationView", "ControlRequest", "ControlView"):
        assert _csharp_wire_names(name) == _schema_properties(name)


def test_wire_schema_covers_python_result_models():
    operation_fields = {field.name for field in fields(OperationView)} - {"raw"}
    control_fields = {field.name for field in fields(ControlView)} - {"raw"}

    assert operation_fields == _schema_properties("OperationView")
    assert control_fields == _schema_properties("ControlView")


def test_bounded_move_args_match_action_schema():
    schema = json.loads((ROOT / "protocol" / "actions.schema.json").read_text(encoding="utf-8"))
    expected = set(schema["$defs"]["BoundedMoveArgs"]["required"])
    actual = set(BoundedMoveSpec("left", 12, 1000).as_args())
    assert actual == expected
