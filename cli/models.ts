import { z } from 'zod';

/**
 * Severity levels for issues and comments
 */
export const SeveritySchema = z.enum(['low', 'medium', 'high']);
export type Severity = z.infer<typeof SeveritySchema>;

/**
 * Review verdict options
 */
export const ReviewVerdictSchema = z.enum(['approve', 'request_changes', 'comment']);
export type ReviewVerdict = z.infer<typeof ReviewVerdictSchema>;

/**
 * Security issue found in code
 */
export const SecurityIssueSchema = z.object({
  severity: SeveritySchema,
  description: z.string(),
  cweId: z.string().optional(),
});
export type SecurityIssue = z.infer<typeof SecurityIssueSchema>;

/**
 * Result of code analysis step
 */
export const CodeAnalysisResultSchema = z.object({
  complexity_score: z.number().min(0).max(10),
  security_issues: z.array(SecurityIssueSchema).default([]),
  patterns_found: z.array(z.string()).default([]),
  tech_debt: z.array(z.string()).default([]),
  summary: z.string(),
});
export type CodeAnalysisResult = z.infer<typeof CodeAnalysisResultSchema>;

/**
 * Result of quality evaluation step
 */
export const QualityMetricsResultSchema = z.object({
  readability_score: z.number().min(0).max(10),
  test_coverage_score: z.number().min(0).max(100),
  performance_concerns: z.array(z.string()).default([]),
  overall_quality_score: z.number().min(0).max(10),
  summary: z.string(),
});
export type QualityMetricsResult = z.infer<typeof QualityMetricsResultSchema>;

/**
 * Individual review comment
 */
export const ReviewCommentSchema = z.object({
  file_path: z.string(),
  line_number: z.number().optional(),
  severity: SeveritySchema,
  comment: z.string(),
  category: z.string(),
});
export type ReviewComment = z.infer<typeof ReviewCommentSchema>;

/**
 * Result of review comment generation step
 */
export const ReviewCommentsResultSchema = z.object({
  review_comments: z.array(ReviewCommentSchema).default([]),
  overall_verdict: ReviewVerdictSchema,
  summary: z.string(),
});
export type ReviewCommentsResult = z.infer<typeof ReviewCommentsResultSchema>;

/**
 * GitHub PR metadata input
 */
export const PRMetadataSchema = z.object({
  repo_owner: z.string(),
  repo_name: z.string(),
  pr_number: z.number(),
  base_branch: z.string(),
  title: z.string(),
  description: z.string(),
  diff: z.string(),
  author: z.string(),
});
export type PRMetadata = z.infer<typeof PRMetadataSchema>;

/**
 * Action to execute on GitHub
 */
export const ReviewActionSchema = z.object({
  repo_owner: z.string(),
  repo_name: z.string(),
  pr_number: z.number(),
  comments: z.array(ReviewCommentSchema),
  verdict: ReviewVerdictSchema,
  summary: z.string(),
});
export type ReviewAction = z.infer<typeof ReviewActionSchema>;

/**
 * HTTP request to review a PR
 */
export const PRReviewRequestSchema = z.object({
  repo_owner: z.string(),
  repo_name: z.string(),
  pr_number: z.number(),
  github_token: z.string().optional(),
});
export type PRReviewRequest = z.infer<typeof PRReviewRequestSchema>;

/**
 * HTTP response from PR review
 */
export const PRReviewResponseSchema = z.object({
  status: z.string(),
  review_id: z.string(),
  message: z.string(),
  verdict: ReviewVerdictSchema.optional(),
  comments_posted: z.number().default(0),
  analysis: z.record(z.any()).optional(),
  error: z.string().optional(),
});
export type PRReviewResponse = z.infer<typeof PRReviewResponseSchema>;
