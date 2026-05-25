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
      await this.postReviewToGitHub(action);
      logger.info(`Successfully posted review for PR #${prMetadata.pr_number}`);
    } catch (error) {
      logger.error(`Failed to post review to GitHub: ${error}`);
      // Continue anyway - review was generated successfully
      logger.info('Review generated successfully even though posting failed');
    }

    return action;
  }

  /**
   * Post review to GitHub.
   * - Comments with a line number are posted as inline review comments (event: COMMENT).
   * - Comments without a line number are grouped into a single general PR comment.
   */
  private async postReviewToGitHub(action: ReviewAction): Promise<void> {
    const octokit = new Octokit({ auth: this.githubToken });

    const lineComments = action.comments.filter(c => c.line_number != null);
    const generalComments = action.comments.filter(c => c.line_number == null);

    // Post inline review comments with no approve/request_changes verdict.
    // Falls back to a general issue comment if GitHub rejects the paths/lines.
    try {
      await octokit.pulls.createReview({
        owner: action.repo_owner,
        repo: action.repo_name,
        pull_number: action.pr_number,
        body: action.summary,
        event: 'COMMENT',
        comments: lineComments.map(c => ({
          path: c.file_path,
          line: c.line_number!,
          body: `**[${c.severity.toUpperCase()}] ${c.category}**\n\n${c.comment}`,
        })),
      });
      logger.info(`Posted review with ${lineComments.length} inline comment(s)`);
    } catch (err) {
      logger.warn(`Inline review failed (path/line not in diff), falling back to general comment: ${err}`);
      const fallbackBody = lineComments
        .map(c => `**[${c.severity.toUpperCase()}] ${c.category}** — \`${c.file_path}:${c.line_number}\`\n\n${c.comment}`)
        .join('\n\n---\n\n');
      await octokit.issues.createComment({
        owner: action.repo_owner,
        repo: action.repo_name,
        issue_number: action.pr_number,
        body: `${action.summary}\n\n---\n\n${fallbackBody}`,
      });
      logger.info(`Posted fallback general comment with ${lineComments.length} inline comment(s)`);
    }

    // Post general (non-line-specific) issues as a single PR comment
    if (generalComments.length > 0) {
      const body = generalComments
        .map(c => `**[${c.severity.toUpperCase()}] ${c.category}** — \`${c.file_path}\`\n\n${c.comment}`)
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
