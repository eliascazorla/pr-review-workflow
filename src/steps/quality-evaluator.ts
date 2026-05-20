import { PipelineStep, WorkflowContext } from '../workflow';
import { LLMClient } from '../llm-client';
import {
  QualityMetricsResultSchema,
  QualityMetricsResult,
  CodeAnalysisResult,
  PRMetadata,
} from '../models';
import { QUALITY_METRICS_SCHEMA } from '../schemas';
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
    const codeAnalysis = context.code_analyzer as CodeAnalysisResult | undefined;

    logger.info(`Evaluating quality metrics for PR #${prMetadata.pr_number}`);

    const systemPrompt = `You are a software quality expert. Evaluate code quality based on the provided analysis and return metrics in JSON format.

Consider:
1. Code readability and clarity
2. Test coverage indicators
3. Performance implications
4. Overall code quality score

Provide numeric scores where required.`;

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

    const userMessage = `Evaluate the code quality for this PR:

Repository: ${prMetadata.repo_owner}/${prMetadata.repo_name}
PR Title: ${prMetadata.title}
${analysisSummary}

Provide quality metrics including readability score (0-10), test coverage estimate (0-100), performance concerns, and overall quality score (0-10).`;

    const result = await this.llmClient.callWithSchema(
      QualityMetricsResultSchema,
      systemPrompt,
      userMessage
    );

    logger.info(`Quality evaluation completed with score: ${result.overall_quality_score}/10`);
    return result;
  }
}
