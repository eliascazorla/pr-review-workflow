from __future__ import annotations

from .models import ConsolidatedReport


def serialize_report(report: ConsolidatedReport) -> dict:
    return _serialize_report(report, include_debug=False)


def serialize_report_debug(report: ConsolidatedReport) -> dict:
    return _serialize_report(report, include_debug=True)


def _serialize_report(report: ConsolidatedReport, *, include_debug: bool) -> dict:
    analyses = []
    for analysis in report.analyses:
        item = {
            "dimension": analysis.dimension.value,
            "summary": analysis.summary,
            "confidence": analysis.confidence,
            "token_usage": {
                "prompt_tokens": analysis.token_usage.prompt_tokens,
                "completion_tokens": analysis.token_usage.completion_tokens,
                "total_tokens": analysis.token_usage.total_tokens,
            },
            "execution_mode": analysis.execution_mode,
            "findings": [
                {
                    "file": finding.file,
                    "line": finding.line,
                    "message": finding.message,
                    "suggestion": finding.suggestion,
                    "severity": finding.severity.value,
                }
                for finding in analysis.findings
            ],
        }
        if include_debug:
            item.update(
                {
                    "final_execution_path": analysis.final_execution_path,
                    "llm_startup_state": analysis.llm_startup_state,
                    "fallback_reason": analysis.fallback_reason,
                    "token_usage_source": analysis.token_usage_source,
                    "discarded_findings": analysis.discarded_findings,
                }
            )
        analyses.append(item)

    decisions = {
        "invoked_agents": [item.value for item in report.decisions.invoked_agents],
        "rationale": report.decisions.rationale,
        "token_usage": {
            "prompt_tokens": report.decisions.token_usage.prompt_tokens,
            "completion_tokens": report.decisions.token_usage.completion_tokens,
            "total_tokens": report.decisions.token_usage.total_tokens,
        },
        "execution_mode": report.decisions.execution_mode,
    }
    if include_debug:
        decisions.update(
            {
                "final_execution_path": report.decisions.final_execution_path,
                "llm_startup_state": report.decisions.llm_startup_state,
                "fallback_reason": report.decisions.fallback_reason,
                "token_usage_source": report.decisions.token_usage_source,
            }
        )

    coordination = None
    if report.coordination is not None:
        coordination = {
            "total_agents": report.coordination.total_agents,
            "invoked_agents": report.coordination.invoked_agents,
            "analyses": report.coordination.analyses,
            "findings": report.coordination.findings,
            "agent_execution_mode": report.coordination.agent_execution_mode,
            "coverage_score": report.coordination.coverage_score,
            "runtime_ms": report.coordination.runtime_ms,
            "prompt_tokens": report.coordination.prompt_tokens,
            "completion_tokens": report.coordination.completion_tokens,
            "total_tokens": report.coordination.total_tokens,
        }
        if include_debug:
            coordination.update(
                {
                    "duplicate_findings": report.coordination.duplicate_findings,
                    "discarded_findings": report.coordination.discarded_findings,
                }
            )

    payload = {
        "title": report.title,
        "overview": report.overview,
        "decisions": decisions,
        "coordination": coordination,
        "analyses": analyses,
    }
    if include_debug:
        payload["resolved_labels"] = report.resolved_labels
    return payload


def render_markdown(report: ConsolidatedReport) -> str:
    lines: list[str] = [
        f"# {report.title}",
        "",
        report.overview,
        "",
        "## Supervisor Decision",
        "",
        f"- Invoked agents: {', '.join(item.value for item in report.decisions.invoked_agents)}",
        f"- Rationale: {report.decisions.rationale}",
        f"- Execution mode: {report.decisions.final_execution_path}",
        f"- Routing tokens: {report.decisions.token_usage.total_tokens} (prompt: {report.decisions.token_usage.prompt_tokens}, completion: {report.decisions.token_usage.completion_tokens})",
        "",
    ]

    if report.coordination is not None:
        lines.extend(
            [
                "## Coordination Metrics",
                "",
                f"- Total agents: {report.coordination.total_agents}",
                f"- Invoked agents: {report.coordination.invoked_agents}",
                f"- Analyses produced: {report.coordination.analyses}",
                f"- Total findings: {report.coordination.findings}",
                f"- Agent execution: {report.coordination.agent_execution_mode}",
                f"- Coverage score: {report.coordination.coverage_score:.2f}",
                f"- Runtime: {report.coordination.runtime_ms:.2f} ms",
                f"- Tokens used: {report.coordination.total_tokens} (prompt: {report.coordination.prompt_tokens}, completion: {report.coordination.completion_tokens})",
                "",
            ]
        )

    lines.extend(["## Findings by Dimension", ""])

    for analysis in report.analyses:
        lines.extend(
            [
                f"### {analysis.dimension.value.title()}",
                "",
                f"- Summary: {analysis.summary}",
                f"- Confidence: {analysis.confidence:.2f}",
                f"- Execution mode: {analysis.final_execution_path}",
                f"- Tokens used: {analysis.token_usage.total_tokens} (prompt: {analysis.token_usage.prompt_tokens}, completion: {analysis.token_usage.completion_tokens})",
            ]
        )
        if analysis.findings:
            lines.append("- Findings:")
            for finding in analysis.findings:
                lines.append(
                    f"  - `{finding.file}:{finding.line}` {finding.message} "
                    f"-> Suggestion: {finding.suggestion} ({finding.severity.value})"
                )
        else:
            lines.append("- Findings: none")
        lines.append("")

    return "\n".join(lines).rstrip() + "\n"
