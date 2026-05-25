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

    const systemPrompt = `You are an experienced code reviewer. Based on the code analysis, quality metrics, and the actual code diff, generate constructive review comments in JSON format.

Guidelines:
1. Be specific and actionable in your comments
2. ONLY reference file paths that appear in the diff below — never invent file names
3. ONLY reference line numbers that exist in those files within the diff — leave line_number null if unsure
4. Use severity levels: low (suggestion), medium (minor issue), high (major issue)
5. Provide categories: performance, readability, security, testing, design
6. Determine overall verdict: approve, request_changes, or comment
7. If the code looks good and you have no real issues to raise, return an empty review_comments array and set the verdict to approve — do not invent problems

Only comment on genuine issues. It is perfectly valid to approve with no comments.`;

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

    const diffSummary = prMetadata.diff.substring(0, 3000);
    const limitedDiff =
      prMetadata.diff.length > 3000 ? diffSummary + '\n... (diff truncated)' : diffSummary;

    const userMessage = `Generate a comprehensive review for this PR:

Repository: ${prMetadata.repo_owner}/${prMetadata.repo_name}
PR Title: ${prMetadata.title}
PR Description: ${prMetadata.description}
Author: ${prMetadata.author}

${analysisSummary}
Code Diff (use ONLY these files and line numbers in your comments):
${limitedDiff}

Generate detailed review comments referencing only files and lines from the diff above. Leave line_number null for general comments. Include an overall verdict.`;

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
