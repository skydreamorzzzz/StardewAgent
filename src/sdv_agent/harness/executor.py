from __future__ import annotations

import time
from dataclasses import dataclass

from .adapter import GameAdapter
from .models import OperationView


@dataclass
class OperationExecutor:
    """Minimal operation lifecycle used by the verified primitive slice.

    This executor submits one immutable operation ID and only polls that same ID.
    It does not retry side effects or contain Stardew-specific business logic.
    """

    adapter: GameAdapter
    timeout_s: float = 2.0
    poll_s: float = 0.05

    def execute_primitive(self, action_id: str) -> OperationView:
        accepted = self.adapter.submit_primitive(action_id)
        return self.wait(accepted.operation_id)

    def wait(self, operation_id: str) -> OperationView:
        deadline = time.monotonic() + self.timeout_s
        latest = self.adapter.status(operation_id)
        while latest.status not in {"settled", "rejected", "unknown"} and time.monotonic() < deadline:
            time.sleep(self.poll_s)
            latest = self.adapter.status(operation_id)
        return latest
