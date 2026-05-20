/**
 * JSON Schema definitions for LLM structured outputs
 */

export const CODE_ANALYSIS_SCHEMA = {
  type: 'object',
  properties: {
    complexity_score: {
      type: 'integer',
      minimum: 0,
      maximum: 10,
      description: 'Code complexity score (0-10)',
    },
    security_issues: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          severity: {
            type: 'string',
            enum: ['low', 'medium', 'high'],
            description: 'Severity level',
          },
          description: {
            type: 'string',
            description: 'Issue description',
          },
          cweId: {
            type: ['string', 'null'],
            description: 'CWE identifier',
          },
        },
        required: ['severity', 'description'],
      },
      description: 'Security issues found',
    },
    patterns_found: {
      type: 'array',
      items: { type: 'string' },
      description: 'Design patterns identified',
    },
    tech_debt: {
      type: 'array',
      items: { type: 'string' },
      description: 'Technical debt items',
    },
    summary: {
      type: 'string',
      description: 'Summary of analysis',
    },
  },
  required: ['complexity_score', 'security_issues', 'patterns_found', 'tech_debt', 'summary'],
};

export const QUALITY_METRICS_SCHEMA = {
  type: 'object',
  properties: {
    readability_score: {
      type: 'integer',
      minimum: 0,
      maximum: 10,
      description: 'Code readability score (0-10)',
    },
    test_coverage_score: {
      type: 'integer',
      minimum: 0,
      maximum: 100,
      description: 'Test coverage percentage (0-100)',
    },
    performance_concerns: {
      type: 'array',
      items: { type: 'string' },
      description: 'Performance issues',
    },
    overall_quality_score: {
      type: 'integer',
      minimum: 0,
      maximum: 10,
      description: 'Overall quality score (0-10)',
    },
    summary: {
      type: 'string',
      description: 'Summary of quality evaluation',
    },
  },
  required: [
    'readability_score',
    'test_coverage_score',
    'performance_concerns',
    'overall_quality_score',
    'summary',
  ],
};

export const REVIEW_COMMENTS_SCHEMA = {
  type: 'object',
  properties: {
    review_comments: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          file_path: {
            type: 'string',
            description: 'Path to the file',
          },
          line_number: {
            type: ['integer', 'null'],
            description: 'Line number',
          },
          severity: {
            type: 'string',
            enum: ['low', 'medium', 'high'],
            description: 'Severity level',
          },
          comment: {
            type: 'string',
            description: 'Comment text',
          },
          category: {
            type: 'string',
            description: 'Comment category',
          },
        },
        required: ['file_path', 'severity', 'comment', 'category'],
      },
      description: 'List of review comments',
    },
    overall_verdict: {
      type: 'string',
      enum: ['approve', 'request_changes', 'comment'],
      description: 'Overall review verdict',
    },
    summary: {
      type: 'string',
      description: 'Summary of the review',
    },
  },
  required: ['review_comments', 'overall_verdict', 'summary'],
};
