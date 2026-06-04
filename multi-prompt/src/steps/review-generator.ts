import { PipelineStep, WorkflowContext } from '../workflow';
import { LLMClient } from '../llm-client';
import {
  ReviewCommentsResultSchema,
  ReviewCommentsResult,
  SecurityAnalysisResult,
  QualityMetricsResult,
  QualityFinding,
  SecurityIssue,
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
    const securityAnalysis = context.code_analyzer as SecurityAnalysisResult | undefined;
    const qualityMetrics = context.quality_evaluator as QualityMetricsResult | undefined;

    logger.info(`Generating review comments for PR #${prMetadata.pr_number}`);

    const systemPrompt = `You are an experienced code reviewer. Based on the structured findings from the security analysis and quality evaluation steps below, generate constructive review COMMENTS in JSON format.

Guidelines:
1. Be specific and actionable in your comments — base them strictly on the provided findings
2. When a finding includes a file_path or line_number, use them directly for the comment location. Only leave file_path and line_number null when the finding has no location info
3. Use severity levels: low (suggestion), medium (minor issue), high (major issue)
4. Provide categories: performance, readability, security, testing, design
5. Determine overall verdict: approve, request_changes, or comment
6. If the findings show no real issues, return an empty review_comments array and set the verdict to approve — do not invent problems
7. Include a confidence score (0.0–1.0) reflecting how certain you are in your review given the available context

Only comment on genuine issues surfaced by the previous analysis steps. It is perfectly valid to approve with no comments.`;

    let analysisSummary = '';
    if (securityAnalysis) {
      const securityLines = securityAnalysis.security_issues.length
        ? securityAnalysis.security_issues
            .map((i: SecurityIssue) => {
              const loc = i.file_path ? ` @ ${i.file_path}${i.line_number != null ? `:${i.line_number}` : ''}` : '';
              return `  [${i.severity}]${loc} ${i.description}${i.cweId ? ` (${i.cweId})` : ''}`;
            })
            .join('\n')
        : '  None';
      const techDebtLines = securityAnalysis.tech_debt.length
        ? securityAnalysis.tech_debt.map((t: string) => `  - ${t}`).join('\n')
        : '  None';
      analysisSummary += `
Security Analysis (complexity: ${securityAnalysis.complexity_score}/10):
Summary: ${securityAnalysis.summary}
Patterns found: ${securityAnalysis.patterns_found.join(', ') || 'None'}
Security issues:
${securityLines}
Tech debt:
${techDebtLines}
`;
    }

    if (qualityMetrics) {
      const perfLines = qualityMetrics.performance_concerns.length
        ? qualityMetrics.performance_concerns.map((p: string) => `  - ${p}`).join('\n')
        : '  None';
      const findingLines = qualityMetrics.code_findings.length
        ? qualityMetrics.code_findings
            .map((f: QualityFinding) => {
              const loc = f.file_path ? ` @ ${f.file_path}${f.line_number != null ? `:${f.line_number}` : ''}` : '';
              return `  [${f.severity}][${f.category}]${loc} ${f.description}`;
            })
            .join('\n')
        : '  None';
      analysisSummary += `
Quality Metrics (overall: ${qualityMetrics.overall_quality_score}/10):
Summary: ${qualityMetrics.summary}
Readability: ${qualityMetrics.readability_score}/10
Test coverage estimate: ${qualityMetrics.test_coverage_score}%
Performance concerns:
${perfLines}
Code findings:
${findingLines}
`;
    }

    const userMessage = `Generate a comprehensive review for this PR:

Repository: ${prMetadata.repo_owner}/${prMetadata.repo_name}
PR Title: ${prMetadata.title}
PR Description: ${prMetadata.description}
Author: ${prMetadata.author}

${analysisSummary}
Convert the findings above into structured review comments. Leave file_path and line_number null for general comments. Include an overall verdict.`;

    const result = await this.llmClient.callWithSchema(
      ReviewCommentsResultSchema,
      systemPrompt,
      userMessage,
      0,
      4096,
      'review_generator'
    );

    logger.info(
      `Review generated with ${result.review_comments.length} comments, verdict: ${result.overall_verdict}, confidence: ${result.confidence}`
    );
    return result;
  }
}
