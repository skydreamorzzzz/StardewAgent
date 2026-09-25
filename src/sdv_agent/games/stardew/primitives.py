from __future__ import annotations

from dataclasses import dataclass

from sdv_agent.harness.executor import OperationExecutor
from sdv_agent.harness.models import OperationView


@dataclass
class PrimitiveTools:
    """The first primitive layer.

    These are intentionally below any LLM-facing capability. They only prove that
    the agent can cause bounded, player-equivalent input in the game.
    """

    executor: OperationExecutor

    def move_once(self, direction: str) -> OperationView:
        action = {
            "up": "input.move_up",
            "right": "input.move_right",
            "down": "input.move_down",
            "left": "input.move_left",
        }.get(direction.lower())
        if action is None:
            raise ValueError("direction must be one of: up, right, down, left")
        return self.executor.execute_primitive(action)

    def interact(self) -> OperationView:
        return self.executor.execute_primitive("input.action")

    def use_tool(self) -> OperationView:
        return self.executor.execute_primitive("input.use_tool")
