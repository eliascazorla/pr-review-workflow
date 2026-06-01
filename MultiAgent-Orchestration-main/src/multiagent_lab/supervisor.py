from __future__ import annotations

import logging
from dataclasses import dataclass
from concurrent.futures import ThreadPoolExecutor, as_completed
from time import perf_counter

from .agents.quality import QualityAgent
from .agents.security import SecurityAgent
from .agents.tests_coverage import TestsCoverageAgent
from .diff_parser import ParsedDiff
from .llm import OpenAICompatibleClient
from .models import ConsolidatedReport, CoordinationMetrics, DiffAnalysis, Dimension, SupervisorDecision, TokenUsage
from .prompts import SUPERVISOR_SYSTEM_PROMPT


logger = logging.getLogger(__name__)


@dataclass(slots=True)
class Supervisor:
    quality_agent: QualityAgent
    security_agent: SecurityAgent
    tests_coverage_agent: TestsCoverageAgent | None
    llm: OpenAICompatibleClient | None = None
    llm_startup_state: str = "disabled"
    default_fallback_reason: str = "missing_secret"

    @classmethod
    def create(
        cls,
        llm: OpenAICompatibleClient | None = None,
        include_tests_coverage: bool = True,
        llm_startup_state: str = "disabled",
        default_fallback_reason: str = "missing_secret",
    ) -> "Supervisor":
        return cls(
            quality_agent=QualityAgent(
                llm=llm,
                llm_startup_state=llm_startup_state,
                default_fallback_reason=default_fallback_reason,
            ),
            security_agent=SecurityAgent(
                llm=llm,
                llm_startup_state=llm_startup_state,
                default_fallback_reason=default_fallback_reason,
            ),
            tests_coverage_agent=(
                TestsCoverageAgent(
                    llm=llm,
                    llm_startup_state=llm_startup_state,
                    default_fallback_reason=default_fallback_reason,
                )
                if include_tests_coverage
                else None
            ),
            llm=llm,
            llm_startup_state=llm_startup_state,
            default_fallback_reason=default_fallback_reason,
        )

    @property
    def enabled_agent_count(self) -> int:
        count = 2
        if self.tests_coverage_agent is not None:
            count += 1
        return count

    def decide(self, diff: ParsedDiff) -> SupervisorDecision:
        if self.llm and self.llm.available:
            logger.info("supervisor routing with llm enabled")
            try:
                return self._decide_with_llm(diff)
            except Exception as exc:
                logger.warning("supervisor fell back to heuristics after llm routing error: %s", exc)
                return self._decide_with_heuristics(
                    diff,
                    fallback_reason=f"provider_error: {exc}",
                )

        logger.info("supervisor using heuristic routing because llm is unavailable or disabled")
        return self._decide_with_heuristics(diff, fallback_reason=self._heuristic_reason())

    def _decide_with_llm(self, diff: ParsedDiff) -> SupervisorDecision:
        tool = {
            "type": "function",
            "function": {
                "name": "route_review_agents",
                "description": "Select which specialist agents should review the diff.",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "agents_to_invoke": {
                            "type": "array",
                            "items": {
                                "type": "string",
                                "enum": self._allowed_dimensions(),
                            },
                        },
                        "rationale": {"type": "string"},
                    },
                    "required": ["agents_to_invoke", "rationale"],
                    "additionalProperties": False,
                },
            },
        }
        result = self.llm.call_tool(
            system_prompt=SUPERVISOR_SYSTEM_PROMPT,
            user_prompt=(
                "Decide which specialist agents should review this code change. "
                "Return only the structured decision.\n\n"
                f"{diff.raw}"
            ),
            tool=tool,
        )
        payload = result.payload

        invoked: list[Dimension] = []
        for item in payload.get("agents_to_invoke", []):
            try:
                dimension = Dimension(item)
            except ValueError:
                continue
            if dimension not in invoked:
                invoked.append(dimension)

        if not invoked:
            invoked.append(Dimension.QUALITY)

        return SupervisorDecision(
            invoked_agents=invoked,
            rationale=str(payload.get("rationale", "LLM did not provide a rationale.")),
            token_usage=result.usage,
            execution_mode="llm",
            final_execution_path="llm",
            llm_startup_state=self.llm_startup_state,
            fallback_reason="not_used",
            token_usage_source="provider",
        )

    def _allowed_dimensions(self) -> list[str]:
        dimensions = [Dimension.QUALITY.value, Dimension.SECURITY.value]
        if self.tests_coverage_agent is not None:
            dimensions.append(Dimension.TESTS_COVERAGE.value)
        return dimensions

    def _heuristic_reason(self) -> str:
        if not self.llm or not self.llm.available:
            return self.default_fallback_reason
        return "provider_error"

    def _decide_with_heuristics(self, diff: ParsedDiff, fallback_reason: str) -> SupervisorDecision:
        logger.info("supervisor heuristic routing reason: %s", fallback_reason)
        lower = diff.raw.lower()
        invoked: list[Dimension] = [Dimension.QUALITY]
        rationale_parts = ["Quality review runs by default."]

        security_signals = [
            "secret",
            "token",
            "password",
            "eval(",
            "exec(",
            "sql",
            "subprocess",
            "curl",
            "install",
        ]
        if any(signal in lower for signal in security_signals):
            invoked.append(Dimension.SECURITY)
            rationale_parts.append("Security signals were detected in the diff.")
        else:
            rationale_parts.append("No strong security signals were found, so security was skipped.")

        if self.tests_coverage_agent is not None:
            code_files = [
                file
                for file in diff.files
                if file.path
                and file.path.endswith(".py")
                and not file.path.startswith("tests/")
                and not file.path.startswith("test_")
            ]
            tests_changed = any(
                file.path.startswith("tests/")
                or file.path.startswith("test_")
                or file.path.endswith("_test.py")
                for file in diff.files
            )
            if code_files and not tests_changed:
                invoked.append(Dimension.TESTS_COVERAGE)
                rationale_parts.append("Code changed without matching test changes, so test coverage was added.")
            else:
                rationale_parts.append(
                    "Test coverage was not routed because the diff already includes tests or no code files changed."
                )

        return SupervisorDecision(
            invoked_agents=invoked,
            rationale=" ".join(rationale_parts),
            token_usage=TokenUsage(),
            execution_mode="heuristic",
            final_execution_path="heuristic",
            llm_startup_state=self.llm_startup_state,
            fallback_reason=fallback_reason,
            token_usage_source="none",
        )

    def run(self, diff: ParsedDiff, parallel_agents: bool = False) -> ConsolidatedReport:
        return self.run_with_context(diff, parallel_agents=parallel_agents)

    def run_with_context(
        self,
        diff: ParsedDiff,
        parallel_agents: bool = False,
        resolved_labels: list[str] | None = None,
    ) -> ConsolidatedReport:
        started = perf_counter()
        decision = self.decide(diff)
        resolved_labels = resolved_labels or []
        force_tests_coverage = "tests-coverage-review-needed" in resolved_labels
        if force_tests_coverage:
            if self.tests_coverage_agent is not None:
                logger.info(
                    "supervisor forcing tests coverage agent because tests-coverage-review-needed label is present"
                )
                if Dimension.TESTS_COVERAGE not in decision.invoked_agents:
                    decision.invoked_agents.append(Dimension.TESTS_COVERAGE)
                    decision.rationale = (
                        f"{decision.rationale} Test coverage was forced by the tests-coverage-review-needed label."
                    ).strip()
                else:
                    decision.rationale = (
                        f"{decision.rationale} Test coverage was requested by the tests-coverage-review-needed label."
                    ).strip()
            else:
                logger.warning(
                    "tests-coverage-review-needed label is present, but the tests coverage agent is disabled"
                )
        logger.info(
            "supervisor execution path resolved as %s with agents=%s",
            decision.final_execution_path,
            ",".join(item.value for item in decision.invoked_agents),
        )
        analyses_by_dimension: dict[Dimension, DiffAnalysis] = {}
        tasks: list[tuple[Dimension, object]] = []

        if Dimension.QUALITY in decision.invoked_agents:
            tasks.append((Dimension.QUALITY, self.quality_agent))
        if Dimension.SECURITY in decision.invoked_agents:
            tasks.append((Dimension.SECURITY, self.security_agent))
        if Dimension.TESTS_COVERAGE in decision.invoked_agents and self.tests_coverage_agent is not None:
            tasks.append((Dimension.TESTS_COVERAGE, self.tests_coverage_agent))

        actual_agent_execution_mode = "parallel" if parallel_agents and len(tasks) > 1 else "sequential"
        if tasks and (len(tasks) == 1 or not parallel_agents):
            logger.info("supervisor executing %s agent(s) sequentially", len(tasks))
            for dimension, agent in tasks:
                analyses_by_dimension[dimension] = agent.analyze(diff)
        elif tasks:
            logger.info("supervisor executing %s agent(s) in parallel", len(tasks))
            with ThreadPoolExecutor(max_workers=len(tasks)) as executor:
                future_to_dimension = {
                    executor.submit(agent.analyze, diff): dimension
                    for dimension, agent in tasks
                }
                for future in as_completed(future_to_dimension):
                    dimension = future_to_dimension[future]
                    analyses_by_dimension[dimension] = future.result()

        analyses = [
            analyses_by_dimension[dimension]
            for dimension in decision.invoked_agents
            if dimension in analyses_by_dimension
        ]

        findings = sum(len(analysis.findings) for analysis in analyses)
        discarded_findings = sum(analysis.discarded_findings for analysis in analyses)
        total_token_usage = TokenUsage(
            prompt_tokens=decision.token_usage.prompt_tokens,
            completion_tokens=decision.token_usage.completion_tokens,
            total_tokens=decision.token_usage.total_tokens,
        )
        for analysis in analyses:
            total_token_usage += analysis.token_usage
        duplicate_findings = findings - len(
            {
                (finding.file, finding.line, finding.message)
                for analysis in analyses
                for finding in analysis.findings
            }
        )
        runtime_ms = (perf_counter() - started) * 1000
        logger.info(
            "supervisor completed run in %.2f ms with total_tokens=%s and findings=%s",
            runtime_ms,
            total_token_usage.total_tokens,
            findings,
        )
        coordination = CoordinationMetrics(
            total_agents=self.enabled_agent_count,
            invoked_agents=len(decision.invoked_agents),
            analyses=len(analyses),
            findings=findings,
            duplicate_findings=max(0, duplicate_findings),
            discarded_findings=discarded_findings,
            agent_execution_mode=actual_agent_execution_mode,
            coverage_score=len(decision.invoked_agents) / self.enabled_agent_count,
            runtime_ms=runtime_ms,
            prompt_tokens=total_token_usage.prompt_tokens,
            completion_tokens=total_token_usage.completion_tokens,
            total_tokens=total_token_usage.total_tokens,
        )

        return ConsolidatedReport(
            title="Multi-Agent PR Review",
            overview="Consolidated report generated by the supervisor.",
            decisions=decision,
            analyses=analyses,
            coordination=coordination,
            resolved_labels=resolved_labels,
        )
