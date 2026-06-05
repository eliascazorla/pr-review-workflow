import { PipelineStep, WorkflowContext } from '../workflow';
import { LLMClient } from '../llm-client';
import { SecurityAnalysisResultSchema, SecurityAnalysisResult, PRMetadata } from '../models';
import logger from '../logger';

/**
 * Security Analysis step - analyzes code changes in PR
 */
export class SecurityAnalyzerStep extends PipelineStep {
  constructor(private llmClient: LLMClient) {
    super();
  }

  async execute(context: WorkflowContext): Promise<SecurityAnalysisResult> {
    const prMetadata = context.pr_metadata as PRMetadata;

    logger.info(`Analyzing code for PR #${prMetadata.pr_number}`);

    // Prepare diff summary (limit size for LLM)
    const diffSummary = prMetadata.diff.substring(0, 60000);
    const limitedDiff =
      prMetadata.diff.length > 60000 ? diffSummary + '\n... (diff truncated)' : diffSummary;

    const systemPrompt = `You are a security reviewer. 
Focus only on secrets, injection risks, dangerous primitives, vulnerable dependency hints, and unsafe shell usage. 
Do not comment on naming or code style unless it creates a security risk. 
Return a concise summary and a short list of actionable findings.
For each security issue, extract the exact file_path (e.g. "src/foo/bar.ts") and line_number from the diff header lines (lines starting with "diff --git" and "@@ -L +L @@"). Set them to null only when the issue is not tied to a specific location.
Include a confidence score (0.0–1.0) reflecting how certain you are in your analysis given the available context.`;

    const userMessage = `Analyze this GitHub PR:

Repository: ${prMetadata.repo_owner}/${prMetadata.repo_name}
PR Title: ${prMetadata.title}
PR Description: ${prMetadata.description}

Code Diff:
${limitedDiff}

Return a detailed JSON analysis with complexity score, security issues, patterns, and technical debt.`;

    const result = await this.llmClient.callWithSchema(
      SecurityAnalysisResultSchema,
      systemPrompt,
      userMessage,
      0,
      10000,
      'security_analyzer'
    );

    logger.info(`Code analysis completed with complexity score: ${result.complexity_score}, confidence: ${result.confidence}`);
    return result;
  }
}
