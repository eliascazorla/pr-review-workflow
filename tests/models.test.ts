import { describe, it, expect } from '@jest/globals';
import {
  CodeAnalysisResultSchema,
  QualityMetricsResultSchema,
  ReviewCommentsResultSchema,
  ReviewVerdictSchema,
  SeveritySchema,
} from '../src/models';

describe('Data Models', () => {
  describe('CodeAnalysisResult', () => {
    it('should validate valid code analysis', () => {
      const data = {
        complexity_score: 7,
        security_issues: [],
        patterns_found: ['singleton'],
        tech_debt: [],
        summary: 'Good code',
      };
      const result = CodeAnalysisResultSchema.parse(data);
      expect(result.complexity_score).toBe(7);
    });

    it('should reject invalid complexity score', () => {
      const data = {
        complexity_score: 15,
        security_issues: [],
        patterns_found: [],
        tech_debt: [],
        summary: 'Good code',
      };
      expect(() => CodeAnalysisResultSchema.parse(data)).toThrow();
    });
  });

  describe('QualityMetricsResult', () => {
    it('should validate valid quality metrics', () => {
      const data = {
        readability_score: 8,
        test_coverage_score: 75,
        performance_concerns: [],
        overall_quality_score: 8,
        summary: 'Good quality',
      };
      const result = QualityMetricsResultSchema.parse(data);
      expect(result.overall_quality_score).toBe(8);
    });
  });

  describe('ReviewCommentsResult', () => {
    it('should validate valid review comments', () => {
      const data = {
        review_comments: [
          {
            file_path: 'src/service.ts',
            line_number: 42,
            severity: 'medium',
            comment: 'Add type hints',
            category: 'readability',
          },
        ],
        overall_verdict: 'comment',
        summary: 'Review complete',
      };
      const result = ReviewCommentsResultSchema.parse(data);
      expect(result.review_comments).toHaveLength(1);
    });
  });

  describe('Enums', () => {
    it('should validate severity levels', () => {
      expect(SeveritySchema.parse('low')).toBe('low');
      expect(SeveritySchema.parse('medium')).toBe('medium');
      expect(SeveritySchema.parse('high')).toBe('high');
    });

    it('should reject invalid severity', () => {
      expect(() => SeveritySchema.parse('invalid')).toThrow();
    });

    it('should validate review verdicts', () => {
      expect(ReviewVerdictSchema.parse('approve')).toBe('approve');
      expect(ReviewVerdictSchema.parse('request_changes')).toBe('request_changes');
      expect(ReviewVerdictSchema.parse('comment')).toBe('comment');
    });
  });
});
