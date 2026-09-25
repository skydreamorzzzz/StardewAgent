from __future__ import annotations

import argparse
import dataclasses
import json
import os

from sdv_agent.games.stardew.primitives import PrimitiveTools
from sdv_agent.harness.executor import OperationExecutor
from sdv_agent.harness.smapi import SmapiAdapter, SmapiConfig


def dump(value) -> None:
    if dataclasses.is_dataclass(value):
        value = dataclasses.asdict(value)
    print(json.dumps(value, ensure_ascii=False, indent=2))


def main() -> None:
    parser = argparse.ArgumentParser(description="Smoke-test the Stardew SMAPI bridge")
    parser.add_argument("--url", default="http://127.0.0.1:8765")
    parser.add_argument("--token", default=os.environ.get("STARDEW_AGENT_TOKEN", ""))
    sub = parser.add_subparsers(dest="cmd", required=True)
    sub.add_parser("health")
    sub.add_parser("observe")
    p_move = sub.add_parser("move")
    p_move.add_argument("direction", choices=["up", "right", "down", "left"])
    p_bounded = sub.add_parser("bounded-move")
    p_bounded.add_argument("direction", choices=["up", "right", "down", "left"])
    p_bounded.add_argument("--max-ticks", type=int, default=12)
    p_bounded.add_argument("--max-duration-ms", type=int, default=1000)
    sub.add_parser("interact")
    sub.add_parser("use-tool")
    p_status = sub.add_parser("status")
    p_status.add_argument("operation_id")

    args = parser.parse_args()
    adapter = SmapiAdapter(SmapiConfig(base_url=args.url, token=args.token))
    tools = PrimitiveTools(OperationExecutor(adapter))

    if args.cmd == "health":
        dump(adapter.health())
    elif args.cmd == "observe":
        dump(adapter.observe())
    elif args.cmd == "move":
        dump(tools.move_once(args.direction))
    elif args.cmd == "bounded-move":
        dump(tools.bounded_move(args.direction, max_ticks=args.max_ticks, max_duration_ms=args.max_duration_ms))
    elif args.cmd == "interact":
        dump(tools.interact())
    elif args.cmd == "use-tool":
        dump(tools.use_tool())
    elif args.cmd == "status":
        dump(adapter.status(args.operation_id))


if __name__ == "__main__":
    main()
