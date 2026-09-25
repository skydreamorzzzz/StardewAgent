from sdv_agent.games.stardew.primitives import PrimitiveTools
from sdv_agent.harness.executor import OperationExecutor
from sdv_agent.harness.models import OperationView
from sdv_agent.harness.smapi import SmapiConfig


def test_default_actor_is_single_agent():
    assert SmapiConfig().actor_id == "agent_player"


class FakeAdapter:
    def __init__(self) -> None:
        self.submitted_action = None

    def submit_primitive(self, action_id: str) -> OperationView:
        self.submitted_action = action_id
        return _operation("accepted")

    def status(self, operation_id: str) -> OperationView:
        return _operation("settled", outcome="succeeded")


def _operation(status: str, outcome: str | None = None) -> OperationView:
    return OperationView("operation-1", "input.move_left", status, outcome, None, 1, 2, {})


def test_primitive_uses_generic_executor_without_business_claims():
    adapter = FakeAdapter()
    result = PrimitiveTools(OperationExecutor(adapter, poll_s=0)).move_once("left")

    assert adapter.submitted_action == "input.move_left"
    assert result.status == "settled"
    assert result.outcome == "succeeded"
