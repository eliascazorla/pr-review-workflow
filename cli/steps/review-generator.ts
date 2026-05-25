import { PipelineStep, WorkflowContext } from '../workflow';
import { LLMClient } from '../llm-client';
import {
  ReviewCommentsResultSchema,
  ReviewCommentsResult,
  CodeAnalysisResult,
  QualityMetricsResult,
  PRMetadata,
} from '../models';
import logger from '../logger';

/**
 * Review Generator step - generates review comments
 */
export class ReviewGeneratorStep extends PipelineStep {
  constructor(private llmClient: LLMClient) {
    super();
  }

  async execute(context: WorkflowContext): Promise<ReviewCommentsResult> {
    const prMetadata = context.pr_metadata as PRMetadata;
    const codeAnalysis = context.code_analyzer as CodeAnalysisResult | undefined;
    const qualityMetrics = context.quality_evaluator as QualityMetricsResult | undefined;

    logger.info(`Generating review comments for PR #${prMetadata.pr_number}`);

    const systemPrompt = `You are an experienced code reviewer. Based on the code analysis and quality metrics, generate constructive review comments in JSON format.

Guidelines:
1. Be specific and actionable in your comments
2. Reference specific files and line numbers when possible
3. Use severity levels: low (suggestion), medium (minor issue), high (major issue)
4. Provide categories: performance, readability, security, testing, design
5. Determine overall verdict: approve, request_changes, or comment

Focus on helping the developer improve their code.`;

    let analysisSummary = '';
    if (codeAnalysis) {
      analysisSummary += `
Code Analysis:
- Complexity: ${codeAnalysis.complexity_score}/10
- Security Issues: ${codeAnalysis.security_issues.length}
- Tech Debt: ${codeAnalysis.tech_debt.length}
`;
    }

    if (qualityMetrics) {
      analysisSummary += `
Quality Metrics:
- Readability: ${qualityMetrics.readability_score}/10
- Test Coverage: ${qualityMetrics.test_coverage_score}%
- Overall Quality: ${qualityMetrics.overall_quality_score}/10
`;
    }

    const userMessage = `Generate a comprehensive review for this PR:

Repository: ${prMetadata.repo_owner}/${prMetadata.repo_name}
PR Title: ${prMetadata.title}
PR Description: ${prMetadata.description}
Author: ${prMetadata.author}

${analysisSummary}

Generate detailed review comments with file paths, line numbers, and specific feedback. Include an overall verdict.`;

    const result = await this.llmClient.callWithSchema(
      ReviewCommentsResultSchema,
      systemPrompt,
      userMessage
    );

    logger.info(
      `Review generated with ${result.review_comments.length} comments, verdict: ${result.overall_verdict}`
    );
    return result;
  }
}
