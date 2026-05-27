from __future__ import annotations

import argparse
import json
import logging
import os
from pathlib import Path

from .comparison import (
    build_comparative_report,
    render_comparative_markdown,
    serialize_comparative_report,
    serialize_comparative_report_debug,
)
from .diff_parser import load_input, parse_unified_diff
from .llm import OpenAICompatibleClient
from .report import render_markdown, serialize_report, serialize_report_debug
from .supervisor import Supervisor


logger = logging.getLogger(__name__)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Multi-agent PR review lab")
    parser.add_argument("--input", required=True, help="Path to a unified diff file, a directory, or raw diff text")
    parser.add_argument("--output", help="Optional path to write the Markdown report")
    parser.add_argument("--json-output", help="Optional path to write the JSON report")
    parser.add_argument("--debug-output", help="Optional path to write the debug JSON report")
    parser.add_argument(
        "--compare-agents",
        action="store_true",
        help="Generate a comparative report for the 2-agent baseline versus the full 3-agent supervisor.",
    )
    parser.add_argument(
        "--parallel-agents",
        action="store_true",
        help="Run invoked specialist agents in parallel instead of sequentially.",
    )
    parser.add_argument(
        "--mode",
        choices=["auto", "llm", "heuristic"],
        default="auto",
        help="Analysis mode. auto uses LLM when credentials are available, otherwise heuristics.",
    )
    parser.add_argument("--model", help="Override the model name for LLM mode.")
    parser.add_argument("--base-url", help="Override the OpenAI-compatible base URL.")
    parser.add_argument(
        "--resolved-labels-json",
        help="Optional JSON array of labels resolved from the PR.",
        default=os.getenv("MULTIAGENT_RESOLVED_LABELS_JSON", "[]"),
    )
    parser.add_argument(
        "--log-level",
        default=os.getenv("MULTIAGENT_LOG_LEVEL", "INFO"),
        help="Logging level for debug traces.",
    )
    return parser


def build_llm(args: argparse.Namespace) -> tuple[OpenAICompatibleClient | None, str, str]:
    if args.mode == "heuristic":
        logger.info("CLI forced heuristic mode via --mode heuristic")
        return None, "disabled", "heuristic_forced"

    llm = OpenAICompatibleClient.from_env()
    if llm is None:
        if args.mode == "llm":
            raise RuntimeError(
                "LLM mode requested, but OPENAI_API_KEY is not set. "
                "Set OPENAI_API_KEY or use --mode heuristic."
            )
        logger.info("CLI did not find LLM credentials; falling back to heuristics")
        return None, "disabled", "missing_secret"

    if args.model:
        llm.model = args.model
    if args.base_url:
        llm.base_url = args.base_url

    logger.info(
        "CLI enabled LLM startup with model=%s base_url=%s timeout=%s",
        llm.model,
        llm.base_url,
        llm.timeout,
    )
    return llm, "enabled", "not_used"


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    logging.basicConfig(
        level=getattr(logging, str(args.log_level).upper(), logging.INFO),
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
    )
    logger.info("MultiAgent CLI starting with mode=%s compare_agents=%s parallel_agents=%s", args.mode, args.compare_agents, args.parallel_agents)
    try:
        resolved_labels = json.loads(args.resolved_labels_json)
        if not isinstance(resolved_labels, list):
            resolved_labels = []
        resolved_labels = [str(label) for label in resolved_labels if str(label).strip()]
    except json.JSONDecodeError:
        resolved_labels = []

    raw = load_input(args.input)
    parsed = parse_unified_diff(raw)
    llm, llm_startup_state, fallback_reason = build_llm(args)

    if args.compare_agents:
        comparison = build_comparative_report(
            parsed,
            llm=llm,
            parallel_agents=args.parallel_agents,
            llm_startup_state=llm_startup_state,
            default_fallback_reason=fallback_reason,
            resolved_labels=resolved_labels,
        )
        markdown = render_comparative_markdown(comparison)
        json_report = serialize_comparative_report(comparison)
        debug_json_report = serialize_comparative_report_debug(comparison)
    else:
        supervisor = Supervisor.create(
            llm=llm,
            llm_startup_state=llm_startup_state,
            default_fallback_reason=fallback_reason,
        )
        report = supervisor.run_with_context(
            parsed,
            parallel_agents=args.parallel_agents,
            resolved_labels=resolved_labels,
        )
        markdown = render_markdown(report)
        json_report = serialize_report(report)
        debug_json_report = serialize_report_debug(report)

    if args.output:
        Path(args.output).write_text(markdown, encoding="utf-8")
    else:
        print(markdown)

    if args.json_output:
        Path(args.json_output).write_text(json.dumps(json_report, indent=2, ensure_ascii=False), encoding="utf-8")
    if args.debug_output:
        Path(args.debug_output).write_text(json.dumps(debug_json_report, indent=2, ensure_ascii=False), encoding="utf-8")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
