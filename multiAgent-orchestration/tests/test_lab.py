from __future__ import annotations

import unittest
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src"
if str(SRC) not in sys.path:
    sys.path.insert(0, str(SRC))

from multiagent_lab.comparison import build_comparative_report
from multiagent_lab.diff_parser import parse_unified_diff
from multiagent_lab.agents.base import BaseAgent
from multiagent_lab.models import Dimension
from multiagent_lab.supervisor import Supervisor


class DummyAgent(BaseAgent):
    name = "dummy"

    def analyze(self, diff):  # type: ignore[override]
        raise NotImplementedError


class LabTests(unittest.TestCase):
    def test_quality_diff_invokes_quality(self) -> None:
        diff = parse_unified_diff(Path("examples/quality.diff").read_text(encoding="utf-8"))
        report = Supervisor.create().run(diff)

        self.assertIn(Dimension.QUALITY, report.decisions.invoked_agents)
        self.assertIn(Dimension.TESTS_COVERAGE, report.decisions.invoked_agents)
        self.assertNotIn(Dimension.SECURITY, report.decisions.invoked_agents)
        self.assertEqual(len(report.analyses), 2)
        self.assertTrue(any(item.dimension == Dimension.QUALITY for item in report.analyses))
        self.assertTrue(any(item.dimension == Dimension.TESTS_COVERAGE for item in report.analyses))
        quality = [item for item in report.analyses if item.dimension == Dimension.QUALITY][0]
        self.assertTrue(quality.findings)
        self.assertEqual(report.decisions.execution_mode, "heuristic")
        self.assertEqual(report.decisions.token_usage_source, "none")
        self.assertEqual(report.decisions.llm_startup_state, "disabled")
        self.assertEqual(report.decisions.fallback_reason, "missing_secret")
        self.assertEqual(quality.execution_mode, "heuristic")
        self.assertEqual(quality.token_usage_source, "none")
        self.assertEqual(quality.llm_startup_state, "disabled")
        self.assertEqual(quality.fallback_reason, "missing_secret")
        self.assertEqual(report.coordination.total_tokens, 0)
        self.assertEqual(quality.token_usage.total_tokens, 0)

    def test_security_diff_invokes_both_when_signals_present(self) -> None:
        diff = parse_unified_diff(Path("examples/security.diff").read_text(encoding="utf-8"))
        report = Supervisor.create().run(diff)

        self.assertIn(Dimension.QUALITY, report.decisions.invoked_agents)
        self.assertIn(Dimension.SECURITY, report.decisions.invoked_agents)
        self.assertIn(Dimension.TESTS_COVERAGE, report.decisions.invoked_agents)
        self.assertEqual(len(report.analyses), 3)
        security = [item for item in report.analyses if item.dimension == Dimension.SECURITY][0]
        self.assertTrue(security.findings)
        self.assertEqual(report.decisions.execution_mode, "heuristic")
        self.assertEqual(report.decisions.token_usage_source, "none")
        self.assertEqual(report.decisions.llm_startup_state, "disabled")
        self.assertEqual(report.decisions.fallback_reason, "missing_secret")
        self.assertEqual(report.coordination.total_tokens, 0)

    def test_comparative_report_highlights_third_agent_value(self) -> None:
        diff = parse_unified_diff(Path("examples/bad_pr.diff").read_text(encoding="utf-8"))
        comparison = build_comparative_report(diff)

        self.assertEqual(comparison.baseline.coordination.total_agents, 2)
        self.assertEqual(comparison.extended.coordination.total_agents, 3)
        self.assertGreater(comparison.extended.coordination.findings, comparison.baseline.coordination.findings)
        self.assertTrue(comparison.conclusion)
        self.assertTrue(any(metric.name == "Tests coverage findings" for metric in comparison.metrics))
        self.assertTrue(any(metric.name == "Total tokens" for metric in comparison.metrics))

    def test_tests_coverage_label_forces_agent_execution(self) -> None:
        diff_text = """diff --git a/tests/test_sample.py b/tests/test_sample.py
new file mode 100644
index 0000000..1111111
--- /dev/null
+++ b/tests/test_sample.py
@@ -0,0 +1,3 @@
+def test_example():
+    assert True
+
"""
        diff = parse_unified_diff(diff_text)
        report = Supervisor.create().run_with_context(
            diff,
            resolved_labels=["tests-coverage-review-needed"],
        )

        self.assertIn(Dimension.QUALITY, report.decisions.invoked_agents)
        self.assertIn(Dimension.TESTS_COVERAGE, report.decisions.invoked_agents)
        self.assertNotIn(Dimension.SECURITY, report.decisions.invoked_agents)
        self.assertEqual(len(report.analyses), 2)
        self.assertTrue(any(item.dimension == Dimension.TESTS_COVERAGE for item in report.analyses))

    def test_llm_line_resolution_requires_exact_match(self) -> None:
        diff_text = """diff --git a/app.py b/app.py
index 0000000..1111111 100644
--- a/app.py
+++ b/app.py
@@ -0,0 +1,2 @@
+def hello_world():
+    return "Hello, world!"
"""
        diff = parse_unified_diff(diff_text)
        agent = DummyAgent()

        self.assertEqual(agent._resolve_line(diff, "app.py", 1), 1)
        self.assertIsNone(agent._resolve_line(diff, "app.py", 3))
        self.assertIsNone(agent._resolve_line(diff, "missing.py", 1))

    def test_llm_context_includes_exact_line_numbers(self) -> None:
        diff_text = """diff --git a/app.py b/app.py
index 0000000..1111111 100644
--- a/app.py
+++ b/app.py
@@ -0,0 +1,2 @@
+def hello_world():
+    return "Hello, world!"
"""
        diff = parse_unified_diff(diff_text)
        agent = DummyAgent()
        context = agent._render_llm_context(diff)

        self.assertIn("Use only exact file and line numbers", context)
        self.assertIn("FILE: app.py", context)
        self.assertIn("1: def hello_world():", context)
        self.assertIn("2:     return \"Hello, world!\"", context)


if __name__ == "__main__":
    unittest.main()
