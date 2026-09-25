import pytest

from sdv_agent.games.stardew.primitives import BoundedMoveSpec, PrimitiveTools
from sdv_agent.harness.executor import OperationExecutor
from sdv_agent.harness.models import OperationView
from sdv_agent.harness.smapi import SmapiAdapter, SmapiConfig


def test_default_actor_is_single_agent():
    assert SmapiConfig().actor_id == "agent_player"


class FakeAdapter:
    def __init__(self) -> None:
        self.submitted_action = None

    def submit_operation(self, action_id: str, args=None) -> OperationView:
        self.submitted_action = action_id
        return _operation("accepted")

    def status(self, operation_id: str) -> OperationView:
        return _operation("settled", outcome="succeeded")

    def cancel(self, operation_id: str):
        self.cancelled = operation_id
        return None


def _operation(status: str, outcome: str | None = None) -> OperationView:
    return OperationView("operation-1", "input.move_left", status, outcome, None, 1, 2, {})


def test_primitive_uses_generic_executor_without_business_claims():
    adapter = FakeAdapter()
    result = PrimitiveTools(OperationExecutor(adapter, poll_s=0)).move_once("left")

    assert adapter.submitted_action == "input.move_left"
    assert result.status == "settled"
    assert result.outcome == "succeeded"


class HangingAdapter(FakeAdapter):
    def status(self, operation_id: str) -> OperationView:
        return _operation("running")


def test_executor_timeout_is_explicit_unknown_and_requests_cancel():
    adapter = HangingAdapter()
    result = OperationExecutor(adapter, timeout_s=0, poll_s=0).wait("operation-1")

    assert result.status == "unknown"
    assert result.outcome == "unknown"
    assert result.error == "EXECUTOR_TIMEOUT"
    assert result.quiescent is None
    assert adapter.cancelled == "operation-1"


@pytest.mark.parametrize("direction", ["up", "down", "left", "right"])
def test_bounded_move_spec_is_typed_and_bounded(direction):
    assert BoundedMoveSpec(direction, 12, 1000).as_args()["direction"] == direction


def test_bounded_move_rejects_invalid_direction_and_unbounded_limits():
    with pytest.raises(ValueError):
        BoundedMoveSpec("diagonal", 12, 1000)
    with pytest.raises(ValueError):
        BoundedMoveSpec("left", 0, 1000)
    with pytest.raises(ValueError):
        BoundedMoveSpec("left", 12, 6000)


def test_operation_result_parses_bounded_movement_evidence():
    result = SmapiAdapter._to_operation(
        {
            "operation_id": "operation-1",
            "action_id": "movement.bounded",
            "status": "settled",
            "outcome": "succeeded",
            "effect_status": "all",
            "quiescent": True,
            "start_tile_x": 9,
            "start_tile_y": 9,
            "end_tile_x": 8,
            "end_tile_y": 9,
            "ticks_used": 12,
            "elapsed_ms": 196,
            "postcondition": "satisfied",
        }
    )

    assert result.effect_status == "all"
    assert (result.start_tile_x, result.start_tile_y) == (9, 9)
    assert (result.end_tile_x, result.end_tile_y) == (8, 9)
    assert result.quiescent is True
