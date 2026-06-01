import 'dotenv/config';
import * as fs from 'fs';
import { Octokit } from '@octokit/rest';
import { config } from './config';
import logger from './logger';
import { LLMClient } from './llm-client';
import { PRReviewWorkflow } from './workflow';
import {
  CodeAnalyzerStep,
  QualityEvaluatorStep,
  ReviewGeneratorStep,
  ActionExecutorStep,
} from './steps';
import { PRMetadata } from './models';
import type {
  CodeAnalysisResult,
  QualityMetricsResult,
  ReviewCommentsResult,
} from './models';

interface PRContext {
  owner: string;
  repo: string;
  prNumber: number;
  githubToken: string;
}

/**
 * Resolves PR context from GitHub Actions environment variables.
 * Returns null if not running inside a GitHub Actions PR event.
 */
function resolveFromActions(): Omit<PRContext, 'githubToken'> | null {
  if (process.env.GITHUB_ACTIONS !== 'true') return null;

  const repository = process.env.GITHUB_REPOSITORY;
  const eventPath = process.env.GITHUB_EVENT_PATH;

  if (!repository || !eventPath) return null;

  const [owner, repo] = repository.split('/');

  let prNumber: number | undefined;

  try {
    const event = JSON.parse(fs.readFileSync(eventPath, 'utf8'));
    prNumber = event?.pull_request?.number;
  } catch {
    // fall through to ref-based parsing
  }

  if (!prNumber) {
    const ref = process.env.GITHUB_REF ?? '';
    const match = ref.match(/refs\/pull\/(\d+)\//);
    prNumber = match ? parseInt(match[1], 10) : undefined;
  }

  if (!owner || !repo || !prNumber) return null;

  return { owner, repo, prNumber };
}

/**
 * Fetches PR metadata and diff from GitHub API
 */
async function fetchPRMetadata(
  octokit: Octokit,
  owner: string,
  repo: string,
  prNumber: number
): Promise<PRMetadata> {
  logger.info(`Fetching PR #${prNumber} from ${owner}/${repo}`);

  const { data: pr } = await octokit.pulls.get({
    owner,
    repo,
    pull_number: prNumber,
  });

  const diffResponse = await octokit.request(
    'GET /repos/{owner}/{repo}/pulls/{pull_number}',
    {
      owner,
      repo,
      pull_number: prNumber,
      headers: { accept: 'application/vnd.github.v3.diff' },
    }
  );

  return {
    repo_owner: owner,
    repo_name: repo,
    pr_number: prNumber,
    base_branch: pr.base.ref,
    title: pr.title,
    description: pr.body || '',
    diff: diffResponse.data as unknown as string,
    author: pr.user?.login || 'unknown',
  };
}

/**
 * Print structured review output to stdout
 */
function printResults(
  reviewId: string,
  prMetadata: PRMetadata,
  codeAnalysis: CodeAnalysisResult | undefined,
  qualityMetrics: QualityMetricsResult | undefined,
  reviewComments: ReviewCommentsResult | undefined
): void {
  console.log('\n========================================');
  console.log('           PR REVIEW COMPLETE           ');
  console.log('========================================\n');
  console.log(`Review ID : ${reviewId}`);
  console.log(`PR        : ${prMetadata.repo_owner}/${prMetadata.repo_name}#${prMetadata.pr_number}`);
  console.log(`Title     : ${prMetadata.title}`);
  console.log(`Author    : ${prMetadata.author}`);
  console.log(`Verdict   : ${reviewComments?.overall_verdict?.toUpperCase() ?? 'UNKNOWN'}`);

  console.log('\n--- Code Analysis ---');
  console.log(`Complexity Score : ${codeAnalysis?.complexity_score ?? 'N/A'}/10`);
  console.log(`Security Issues  : ${codeAnalysis?.security_issues?.length ?? 0}`);
  console.log(`Tech Debt Items  : ${codeAnalysis?.tech_debt?.length ?? 0}`);
  if (codeAnalysis?.patterns_found?.length) {
    console.log(`Patterns Found   : ${codeAnalysis.patterns_found.join(', ')}`);
  }
  if (codeAnalysis?.summary) {
    console.log(`Summary          : ${codeAnalysis.summary}`);
  }

  console.log('\n--- Quality Metrics ---');
  console.log(`Overall Quality : ${qualityMetrics?.overall_quality_score ?? 'N/A'}/10`);
  console.log(`Readability     : ${qualityMetrics?.readability_score ?? 'N/A'}/10`);
  console.log(`Test Coverage   : ${qualityMetrics?.test_coverage_score ?? 'N/A'}%`);
  if (qualityMetrics?.performance_concerns?.length) {
    console.log('Performance Concerns:');
    for (const concern of qualityMetrics.performance_concerns) {
      console.log(`  - ${concern}`);
    }
  }

  console.log('\n--- Review Summary ---');
  console.log(reviewComments?.summary ?? 'No summary available.');

  const comments = reviewComments?.review_comments ?? [];
  if (comments.length > 0) {
    console.log(`\n--- Review Comments (${comments.length}) ---`);
    for (const comment of comments) {
      const location = comment.line_number
        ? `${comment.file_path}:${comment.line_number}`
        : comment.file_path;
      console.log(`\n[${comment.severity.toUpperCase()}] ${location}`);
      console.log(`Category : ${comment.category}`);
      console.log(`Comment  : ${comment.comment}`);
    }
  }

  console.log('\n========================================\n');
}

async function main(): Promise<void> {
  // Prefer GitHub Actions context; fall back to CLI args for local runs
  const actionsContext = resolveFromActions();

  let owner: string;
  let repo: string;
  let prNumber: number;
  let githubToken: string;

  if (actionsContext) {
    ({ owner, repo, prNumber } = actionsContext);
    githubToken = process.env.GITHUB_TOKEN ?? config.githubToken;
    logger.info('Running in GitHub Actions context');
  } else {
    const args = process.argv.slice(2);
    if (args.length < 3) {
      console.error('Usage: ts-node --project cli/tsconfig.json cli/review-pr.ts <owner> <repo> <pr_number> [github_token]');
      console.error('');
      console.error('Arguments:');
      console.error('  owner         GitHub repository owner (user or org)');
      console.error('  repo          GitHub repository name');
      console.error('  pr_number     Pull request number');
      console.error('  github_token  GitHub personal access token (optional, falls back to GITHUB_TOKEN env var)');
      console.error('');
      console.error('Example:');
      console.error('  ts-node --project cli/tsconfig.json cli/review-pr.ts microsoft vscode 12345');
      process.exit(1);
    }
    const [argOwner, argRepo, prNumberStr, cliGithubToken] = args;
    prNumber = parseInt(prNumberStr, 10);
    if (isNaN(prNumber) || prNumber <= 0) {
      console.error(`Invalid PR number: "${prNumberStr}". Must be a positive integer.`);
      process.exit(1);
    }
    owner = argOwner;
    repo = argRepo;
    githubToken = cliGithubToken ?? config.githubToken;
  }

  logger.info(`Starting PR review for ${owner}/${repo}#${prNumber}`);

  const octokit = new Octokit({ auth: githubToken });

  let prMetadata: PRMetadata;
  try {
    prMetadata = await fetchPRMetadata(octokit, owner, repo, prNumber);
  } catch (error) {
    logger.error(`Failed to fetch PR from GitHub: ${error}`);
    process.exit(1);
  }

  const llmClient = new LLMClient();

  const workflow = new PRReviewWorkflow();
  workflow.registerStep('code_analyzer', new CodeAnalyzerStep(llmClient));
  workflow.registerStep('quality_evaluator', new QualityEvaluatorStep(llmClient));
  workflow.registerStep('review_generator', new ReviewGeneratorStep(llmClient));
  workflow.registerStep('action_executor', new ActionExecutorStep(githubToken));

  const result = await workflow.execute(prMetadata);

  if (result.status === 'failed') {
    logger.error(`Workflow failed: ${result.error}`);
    process.exit(1);
  }

  printResults(
    result.review_id,
    prMetadata,
    workflow.getAnalysisResult() as CodeAnalysisResult | undefined,
    workflow.getQualityResult() as QualityMetricsResult | undefined,
    workflow.getReviewComments() as ReviewCommentsResult | undefined
  );

  await llmClient.close();
}

main().catch((error) => {
  logger.error(`Unhandled error: ${error}`);
  process.exit(1);
});
