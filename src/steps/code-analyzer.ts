import { PipelineStep, WorkflowContext } from '../workflow';
import { LLMClient } from '../llm-client';
import { CodeAnalysisResultSchema, CodeAnalysisResult, PRMetadata } from '../models';
import { CODE_ANALYSIS_SCHEMA } from '../schemas';
import logger from '../logger';

/**
 * Code Analysis step - analyzes code changes in PR
 */
export class CodeAnalyzerStep extends PipelineStep {
  constructor(private llmClient: LLMClient) {
    super();
  }

  async execute(context: WorkflowContext): Promise<CodeAnalysisResult> {
    const prMetadata = context.pr_metadata as PRMetadata;

    logger.info(`Analyzing code for PR #${prMetadata.pr_number}`);

    // Prepare diff summary (limit size for LLM)
    const diffSummary = prMetadata.diff.substring(0, 3000);
    const limitedDiff =
      prMetadata.diff.length > 3000 ? diffSummary + '\n... (diff truncated)' : diffSummary;

    const systemPrompt = `You are a code review expert. Analyze the provided GitHub PR diff and return a comprehensive code analysis in JSON format.

Focus on:
1. Code complexity and maintainability
2. Security vulnerabilities and concerns
3. Design patterns used or violated
4. Technical debt and code smell indicators

Be specific and technical in your analysis.`;

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
