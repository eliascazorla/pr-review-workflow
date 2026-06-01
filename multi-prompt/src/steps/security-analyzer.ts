import { PipelineStep, WorkflowContext } from '../workflow';
import { LLMClient } from '../llm-client';
import { CodeAnalysisResultSchema, CodeAnalysisResult, PRMetadata } from '../models';
import logger from '../logger';

/**
 * Security Analysis step - analyzes code changes in PR
 */
export class CodeAnalyzerStep extends PipelineStep {
  constructor(private llmClient: LLMClient) {
    super();
  }

  async execute(context: WorkflowContext): Promise<CodeAnalysisResult> {
    const prMetadata = context.pr_metadata as PRMetadata;

    logger.info(`Analyzing code for PR #${prMetadata.pr_number}`);

    // Prepare diff summary (limit size for LLM)
    const diffSummary = prMetadata.diff.substring(0, 60000);
    const limitedDiff =
      prMetadata.diff.length > 60000 ? diffSummary + '\n... (diff truncated)' : diffSummary;

    const systemPrompt = `You are a security reviewer. 
Focus only on secrets, injection risks, dangerous primitives, vulnerable dependency hints, and unsafe shell usage. 
Do not comment on naming or code style unless it creates a security risk. 
Return a concise summary and a short list of actionable findings.`;

    const userMessage = `Analyze this GitHub PR:

Repository: ${prMetadata.repo_owner}/${prMetadata.repo_name}
PR Title: ${prMetadata.title}
PR Description: ${prMetadata.description}

Code Diff:
${limitedDiff}

Return a detailed JSON analysis with complexity score, security issues, patterns, and technical debt.`;

    const result = await this.llmClient.callWithSchema(
      CodeAnalysisResultSchema,
      systemPrompt,
      userMessage
    );

    logger.info(`Code analysis completed with complexity score: ${result.complexity_score}`);
    return result;
  }
}
