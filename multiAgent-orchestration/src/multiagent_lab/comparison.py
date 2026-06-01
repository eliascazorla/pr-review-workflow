from __future__ import annotations

from .diff_parser import ParsedDiff
from .llm import OpenAICompatibleClient
from .models import ComparativeMetric, ComparativeReport, ConsolidatedReport, Dimension
from .report import serialize_report, serialize_report_debug
from .supervisor import Supervisor


def build_comparative_report(
    diff: ParsedDiff,
    llm: OpenAICompatibleClient | None = None,
    parallel_agents: bool = False,
    llm_startup_state: str = "disabled",
    default_fallback_reason: str = "missing_secret",
    resolved_labels: list[str] | None = None,
) -> ComparativeReport:
    baseline_supervisor = Supervisor.create(
        llm=llm,
        include_tests_coverage=False,
        llm_startup_state=llm_startup_state,
        default_fallback_reason=default_fallback_reason,
    )
    extended_supervisor = Supervisor.create(
        llm=llm,
        include_tests_coverage=True,
        llm_startup_state=llm_startup_state,
        default_fallback_reason=default_fallback_reason,
    )
    baseline = baseline_supervisor.run_with_context(
        diff,
        parallel_agents=parallel_agents,
        resolved_labels=resolved_labels,
    )
    extended = extended_supervisor.run_with_context(
        diff,
        parallel_agents=parallel_agents,
        resolved_labels=resolved_labels,
    )

    metrics = _build_metrics(baseline, extended)
    conclusion = _build_conclusion(baseline, extended)

    return ComparativeReport(
        title="Supervisor Coordination Comparison",
        overview="Comparison between a 2-agent baseline and the full 3-agent supervisor.",
        baseline_label="2 agents (quality + security)",
        extended_label="3 agents (quality + security + tests coverage)",
        baseline=baseline,
        extended=extended,
        metrics=metrics,
        conclusion=conclusion,
    )


def serialize_comparative_report(report: ComparativeReport) -> dict:
    return {
        "title": report.title,
        "overview": report.overview,
        "baseline_label": report.baseline_label,
        "extended_label": report.extended_label,
        "baseline": serialize_report(report.baseline),
        "extended": serialize_report(report.extended),
        "metrics": [
            {
                "name": metric.name,
                "baseline": metric.baseline,
                "extended": metric.extended,
                "delta": metric.delta,
            }
            for metric in report.metrics
        ],
        "conclusion": report.conclusion,
    }


def serialize_comparative_report_debug(report: ComparativeReport) -> dict:
    return {
        "title": report.title,
        "overview": report.overview,
        "baseline_label": report.baseline_label,
        "extended_label": report.extended_label,
        "baseline": serialize_report_debug(report.baseline),
        "extended": serialize_report_debug(report.extended),
        "metrics": [
            {
                "name": metric.name,
                "baseline": metric.baseline,
                "extended": metric.extended,
                "delta": metric.delta,
            }
            for metric in report.metrics
        ],
        "conclusion": report.conclusion,
    }


def render_comparative_markdown(report: ComparativeReport) -> str:
    lines: list[str] = [
        f"# {report.title}",
        "",
        report.overview,
        "",
        "## Configurations Compared",
        "",
        f"- Baseline: {report.baseline_label}",
        f"- Extended: {report.extended_label}",
        "",
        "## Coordination Metrics",
        "",
        "| Metric | Baseline | Extended | Delta |",
        "|---|---:|---:|---:|",
    ]

    for metric in report.metrics:
        lines.append(f"| {metric.name} | {metric.baseline} | {metric.extended} | {metric.delta} |")

    lines.extend(
        [
            "",
            "## Dimension Findings",
            "",
            "| Dimension | Baseline | Extended | Delta |",
            "|---|---:|---:|---:|",
        ]
    )

    baseline_counts = _counts_by_dimension(report.baseline)
    extended_counts = _counts_by_dimension(report.extended)
    for dimension in (Dimension.QUALITY, Dimension.SECURITY, Dimension.TESTS_COVERAGE):
        baseline_count = baseline_counts.get(dimension.value, 0)
        extended_count = extended_counts.get(dimension.value, 0)
        lines.append(
            f"| {dimension.value} | {baseline_count} | {extended_count} | {extended_count - baseline_count:+d} |"
        )

    lines.extend(
        [
            "",
            "## Conclusion",
            "",
            report.conclusion,
            "",
            "## Baseline Snapshot",
            "",
        ]
    )
    lines.extend(_report_snapshot(report.baseline))
    lines.extend(["", "## Extended Snapshot", ""])
    lines.extend(_report_snapshot(report.extended))
    return "\n".join(lines).rstrip() + "\n"


def _build_metrics(baseline: ConsolidatedReport, extended: ConsolidatedReport) -> list[ComparativeMetric]:
    baseline_coordination = baseline.coordination
    extended_coordination = extended.coordination
    if baseline_coordination is None or extended_coordination is None:
        return []

    baseline_counts = _counts_by_dimension(baseline)
    extended_counts = _counts_by_dimension(extended)

    metrics = [
        ComparativeMetric(
            name="Enabled agents",
            baseline=str(baseline_coordination.total_agents),
            extended=str(extended_coordination.total_agents),
            delta=_format_int_delta(extended_coordination.total_agents - baseline_coordination.total_agents),
        ),
        ComparativeMetric(
            name="Invoked agents",
            baseline=str(baseline_coordination.invoked_agents),
            extended=str(extended_coordination.invoked_agents),
            delta=_format_int_delta(extended_coordination.invoked_agents - baseline_coordination.invoked_agents),
        ),
        ComparativeMetric(
            name="Analyses produced",
            baseline=str(baseline_coordination.analyses),
            extended=str(extended_coordination.analyses),
            delta=_format_int_delta(extended_coordination.analyses - baseline_coordination.analyses),
        ),
        ComparativeMetric(
            name="Total findings",
            baseline=str(baseline_coordination.findings),
            extended=str(extended_coordination.findings),
            delta=_format_int_delta(extended_coordination.findings - baseline_coordination.findings),
        ),
        ComparativeMetric(
            name="Coverage score",
            baseline=f"{baseline_coordination.coverage_score:.2f}",
            extended=f"{extended_coordination.coverage_score:.2f}",
            delta=_format_float_delta(extended_coordination.coverage_score - baseline_coordination.coverage_score),
        ),
        ComparativeMetric(
            name="Runtime (ms)",
            baseline=f"{baseline_coordination.runtime_ms:.2f}",
            extended=f"{extended_coordination.runtime_ms:.2f}",
            delta=_format_float_delta(extended_coordination.runtime_ms - baseline_coordination.runtime_ms),
        ),
        ComparativeMetric(
            name="Agent execution",
            baseline=baseline_coordination.agent_execution_mode,
            extended=extended_coordination.agent_execution_mode,
            delta="n/a",
        ),
        ComparativeMetric(
            name="Total tokens",
            baseline=str(baseline_coordination.total_tokens),
            extended=str(extended_coordination.total_tokens),
            delta=_format_int_delta(extended_coordination.total_tokens - baseline_coordination.total_tokens),
        ),
        ComparativeMetric(
            name="Prompt tokens",
            baseline=str(baseline_coordination.prompt_tokens),
            extended=str(extended_coordination.prompt_tokens),
            delta=_format_int_delta(extended_coordination.prompt_tokens - baseline_coordination.prompt_tokens),
        ),
        ComparativeMetric(
            name="Completion tokens",
            baseline=str(baseline_coordination.completion_tokens),
            extended=str(extended_coordination.completion_tokens),
            delta=_format_int_delta(
                extended_coordination.completion_tokens - baseline_coordination.completion_tokens
            ),
        ),
        ComparativeMetric(
            name="Quality findings",
            baseline=str(baseline_counts.get(Dimension.QUALITY.value, 0)),
            extended=str(extended_counts.get(Dimension.QUALITY.value, 0)),
            delta=_format_int_delta(
                extended_counts.get(Dimension.QUALITY.value, 0) - baseline_counts.get(Dimension.QUALITY.value, 0)
            ),
        ),
        ComparativeMetric(
            name="Security findings",
            baseline=str(baseline_counts.get(Dimension.SECURITY.value, 0)),
            extended=str(extended_counts.get(Dimension.SECURITY.value, 0)),
            delta=_format_int_delta(
                extended_counts.get(Dimension.SECURITY.value, 0) - baseline_counts.get(Dimension.SECURITY.value, 0)
            ),
        ),
        ComparativeMetric(
            name="Tests coverage findings",
            baseline=str(baseline_counts.get(Dimension.TESTS_COVERAGE.value, 0)),
            extended=str(extended_counts.get(Dimension.TESTS_COVERAGE.value, 0)),
            delta=_format_int_delta(
                extended_counts.get(Dimension.TESTS_COVERAGE.value, 0)
                - baseline_counts.get(Dimension.TESTS_COVERAGE.value, 0)
            ),
        ),
    ]
    return metrics


def _build_conclusion(baseline: ConsolidatedReport, extended: ConsolidatedReport) -> str:
    baseline_coordination = baseline.coordination
    extended_coordination = extended.coordination
    if baseline_coordination is None or extended_coordination is None:
        return "The comparison could not be evaluated because coordination metrics were not available."

    baseline_counts = _counts_by_dimension(baseline)
    extended_counts = _counts_by_dimension(extended)
    extra_findings = extended_coordination.findings - baseline_coordination.findings
    extra_runtime = extended_coordination.runtime_ms - baseline_coordination.runtime_ms
    tests_gain = extended_counts.get(Dimension.TESTS_COVERAGE.value, 0) - baseline_counts.get(
        Dimension.TESTS_COVERAGE.value, 0
    )
    duplicate_delta = extended_coordination.duplicate_findings - baseline_coordination.duplicate_findings

    if tests_gain > 0 and extra_findings > 0 and duplicate_delta <= 0:
        if abs(extra_runtime) < 5.0:
            runtime_note = "Runtime stayed within measurement noise."
        elif extra_runtime >= 0:
            runtime_note = f"Runtime increased by {extra_runtime:.2f} ms."
        else:
            runtime_note = f"Runtime was {abs(extra_runtime):.2f} ms lower in this run."
        return (
            "The 3-agent configuration scales well: the tests coverage agent added "
            f"{tests_gain} finding(s), total findings increased by {extra_findings}, "
            f"and duplicate findings did not increase. {runtime_note}"
        )

    if tests_gain > 0 and extra_findings <= 0:
        return (
            "The 3-agent configuration increased coordination overhead without adding new findings. "
            "That suggests the extra agent should be routed more selectively."
        )

    if extra_runtime > 0:
        return (
            "The 3-agent configuration added coordination overhead but the extra agent did not change the "
            "finding set enough to clearly justify the cost."
        )

    return "The 3-agent configuration did not introduce a noticeable coordination penalty."


def _counts_by_dimension(report: ConsolidatedReport) -> dict[str, int]:
    return {
        analysis.dimension.value: len(analysis.findings)
        for analysis in report.analyses
    }


def _format_int_delta(value: int) -> str:
    return f"{value:+d}"


def _format_float_delta(value: float) -> str:
    return f"{value:+.2f}"


def _report_snapshot(report: ConsolidatedReport) -> list[str]:
    lines = [
        f"- Invoked agents: {', '.join(item.value for item in report.decisions.invoked_agents)}",
        f"- Rationale: {report.decisions.rationale}",
        f"- Execution mode: {report.decisions.final_execution_path}",
    ]
    if report.coordination is not None:
        lines.extend(
            [
                f"- Analyses produced: {report.coordination.analyses}",
                f"- Total findings: {report.coordination.findings}",
                f"- Agent execution: {report.coordination.agent_execution_mode}",
                f"- Coverage score: {report.coordination.coverage_score:.2f}",
                f"- Runtime: {report.coordination.runtime_ms:.2f} ms",
                f"- Tokens used: {report.coordination.total_tokens} (prompt: {report.coordination.prompt_tokens}, completion: {report.coordination.completion_tokens})",
            ]
        )
    if report.analyses:
        lines.append("- Findings by dimension:")
        for analysis in report.analyses:
            lines.append(f"  - {analysis.dimension.value}: {len(analysis.findings)} finding(s)")
    return lines
