import { PipelineStep, WorkflowContext } from '../workflow';
import { LLMClient } from '../llm-client';
import {
  QualityMetricsResultSchema,
  QualityMetricsResult,
  SecurityAnalysisResult,
  PRMetadata,
} from '../models';
import logger from '../logger';

/**
 * Quality Evaluation step - evaluates code quality metrics
 */
export class QualityEvaluatorStep extends PipelineStep {
  constructor(private llmClient: LLMClient) {
    super();
  }

  async execute(context: WorkflowContext): Promise<QualityMetricsResult> {
    const prMetadata = context.pr_metadata as PRMetadata;
    const codeAnalysis = context.code_analyzer as SecurityAnalysisResult | undefined;

    logger.info(`Evaluating quality metrics for PR #${prMetadata.pr_number}`);

    const systemPrompt = `You are a software quality expert. Evaluate code quality based on the provided code diff and return metrics in JSON format.

Consider:
1. Code readability and clarity
2. Test coverage indicators
3. Performance implications
4. Overall code quality score

For each specific issue you find in the diff, add an entry to code_findings with:
- severity (low/medium/high), category (readability/testing/performance/design), description
- file_path and line_number extracted from the diff headers ("diff --git a/X b/X" and "@@ -L +L @@"); set to null only for cross-cutting concerns with no single location

Provide numeric scores where required.
Include a confidence score (0.0–1.0) reflecting how certain you are in your evaluation given the available context.`;

    let analysisSummary = '';
    if (codeAnalysis) {
      analysisSummary = `
Previous Code Analysis:
- Complexity: ${codeAnalysis.complexity_score}/10
- Security Issues: ${codeAnalysis.security_issues.length}
- Patterns Found: ${codeAnalysis.patterns_found.join(', ') || 'None'}
- Tech Debt Items: ${codeAnalysis.tech_debt.length}
`;
    }

    const diffSummary = prMetadata.diff.substring(0, 40000);
    const limitedDiff =
      prMetadata.diff.length > 40000 ? diffSummary + '\n... (diff truncated)' : diffSummary;

    const userMessage = `Evaluate the code quality for this PR:

Repository: ${prMetadata.repo_owner}/${prMetadata.repo_name}
PR Title: ${prMetadata.title}
${analysisSummary}
Code Diff:
${limitedDiff}

Provide quality metrics including readability score (0-10), test coverage estimate (0-100), performance concerns, and overall quality score (0-10).`;

    const result = await this.llmClient.callWithSchema(
      QualityMetricsResultSchema,
      systemPrompt,
      userMessage,
      0,
      4096,
      'quality_evaluator'
    );

    logger.info(`Quality evaluation completed with score: ${result.overall_quality_score}/10, confidence: ${result.confidence}`);
    return result;
  }
}
