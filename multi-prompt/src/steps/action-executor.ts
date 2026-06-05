import { PipelineStep, WorkflowContext } from '../workflow';
import { ReviewCommentsResult, ReviewAction, PRMetadata } from '../models';
import { config } from '../config';
import logger from '../logger';
import { Octokit } from '@octokit/rest';

/**
 * Action Executor step - executes actions on GitHub
 */
export class ActionExecutorStep extends PipelineStep {
  private githubToken: string;

  constructor(githubToken?: string) {
    super();
    this.githubToken = githubToken || config.githubToken;
  }

  async execute(context: WorkflowContext): Promise<ReviewAction> {
    const prMetadata = context.pr_metadata as PRMetadata;
    const reviewComments = context.review_generator as ReviewCommentsResult | undefined;

    logger.info(`Executing actions for PR #${prMetadata.pr_number}`);

    const action: ReviewAction = {
      repo_owner: prMetadata.repo_owner,
      repo_name: prMetadata.repo_name,
      pr_number: prMetadata.pr_number,
      comments: reviewComments?.review_comments || [],
      verdict: reviewComments?.overall_verdict || 'comment',
      summary: reviewComments?.summary || '',
    };

    try {
      // Try to post review to GitHub
      // This is a placeholder - actual implementation would use @octokit/rest
      await this.postReviewToGitHub(action, prMetadata.diff);
      logger.info(`Successfully posted review for PR #${prMetadata.pr_number}`);
    } catch (error) {
      logger.error(`Failed to post review to GitHub: ${error}`);
      // Continue anyway - review was generated successfully
      logger.info('Review generated successfully even though posting failed');
    }

    return action;
  }

  /**
   * Extract file paths from diff headers
   */
  private extractFilesFromDiff(diff: string): Set<string> {
    const files = new Set<string>();
    const matches = diff.match(/^diff --git a\/(.+) b\/\1$/gm);
    if (matches) {
      matches.forEach(m => {
        const filePath = m.replace(/^diff --git a\/(.+) b\/\1$/, '$1');
        files.add(filePath);
      });
    }
    return files;
  }

  /**
   * Post review to GitHub.
   * - Comments with a line number are posted as inline review comments (event: COMMENT).
   * - Comments without a line number are grouped into a single general PR comment.
   */
  private async postReviewToGitHub(action: ReviewAction, diff: string): Promise<void> {
    const octokit = new Octokit({ auth: this.githubToken });

    const lineComments = action.comments.filter(c => c.line_number != null && c.file_path != null);
    const generalComments = action.comments.filter(c => c.line_number == null);

    if (lineComments.length > 0) {
      logger.info(`Attempting to post ${lineComments.length} inline comments:`);
      lineComments.forEach((c, i) => {
        logger.info(`  ${i + 1}. ${c.file_path}:${c.line_number} - ${c.category}`);
      });
    }

    // Validate comments against diff
    const diffFiles = this.extractFilesFromDiff(diff);
    logger.info(`Files in diff: ${Array.from(diffFiles).join(', ')}`);

    const validComments: typeof lineComments = [];
    const invalidComments: typeof lineComments = [];

    lineComments.forEach(c => {
      if (diffFiles.has(c.file_path!)) {
        validComments.push(c);
      } else {
        invalidComments.push(c);
        logger.warn(`Comment file not in diff: ${c.file_path}:${c.line_number}`);
      }
    });

    logger.info(`Valid comments: ${validComments.length}, Invalid comments: ${invalidComments.length}`);

    // Post inline review comments with no approve/request_changes verdict.
    // Falls back to a general issue comment if GitHub rejects the paths/lines.
    try {
      await octokit.pulls.createReview({
        owner: action.repo_owner,
        repo: action.repo_name,
        pull_number: action.pr_number,
        body: action.summary,
        event: 'COMMENT',
        comments: validComments.map(c => ({
          path: c.file_path!,
          line: c.line_number!,
          body: `**[${c.severity.toUpperCase()}] ${c.category}**\n\n${c.comment}`,
        })),
      });
      logger.info(`Posted review with ${validComments.length} inline comment(s)`);
    } catch (err) {
      logger.warn(`Inline review failed (path/line not in diff), falling back to general comment: ${err}`);
      const fallbackBody = validComments
        .map(c => `**[${c.severity.toUpperCase()}] ${c.category}** — \`${c.file_path ?? 'unknown'}:${c.line_number}\`\n\n${c.comment}`)
        .join('\n\n---\n\n');
      await octokit.issues.createComment({
        owner: action.repo_owner,
        repo: action.repo_name,
        issue_number: action.pr_number,
        body: `${action.summary}\n\n---\n\n${fallbackBody}`,
      });
      logger.info(`Posted fallback general comment with ${validComments.length} inline comment(s)`);
    }

    // Post invalid comments as general comments
    if (invalidComments.length > 0) {
      const invalidBody = invalidComments
        .map(c => `**[${c.severity.toUpperCase()}] ${c.category}** — \`${c.file_path ?? 'unknown'}:${c.line_number}\`\n\n${c.comment}`)
        .join('\n\n---\n\n');
      await octokit.issues.createComment({
        owner: action.repo_owner,
        repo: action.repo_name,
        issue_number: action.pr_number,
        body: `**Note: The following comments reference files not in the diff:**\n\n${invalidBody}`,
      });
      logger.info(`Posted ${invalidComments.length} invalid comments as general comment`);
    }

    // Post general (non-line-specific) issues as a single PR comment
    if (generalComments.length > 0) {
      const body = generalComments
        .map(c => `**[${c.severity.toUpperCase()}] ${c.category}** — \`${c.file_path ?? 'general'}\`\n\n${c.comment}`)
        .join('\n\n---\n\n');

      await octokit.issues.createComment({
        owner: action.repo_owner,
        repo: action.repo_name,
        issue_number: action.pr_number,
        body,
      });

      logger.info(`Posted general comment with ${generalComments.length} issue(s)`);
    }
  }
}
