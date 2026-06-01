import { v4 as uuidv4 } from 'uuid';
import logger from './logger';
import { PRMetadata } from './models';

/**
 * Context passed through workflow steps
 */
export interface WorkflowContext {
  [key: string]: unknown;
  pr_metadata: PRMetadata;
  review_id: string;
}

/**
 * Base class for pipeline steps
 */
export abstract class PipelineStep {
  abstract execute(context: WorkflowContext): Promise<unknown>;
}

/**
 * Orchestrates the PR review workflow
 */
export class PRReviewWorkflow {
  private steps: Map<string, PipelineStep> = new Map();
  private context: WorkflowContext;
  private reviewId: string;

  constructor() {
    this.reviewId = uuidv4();
    this.context = {
      review_id: this.reviewId,
      pr_metadata: null as unknown as PRMetadata,
    };
  }

  /**
   * Register a workflow step
   */
  registerStep(name: string, step: PipelineStep): void {
    this.steps.set(name, step);
    logger.info(`Registered step: ${name}`);
  }

  /**
   * Execute the complete workflow
   */
  async execute(prMetadata: PRMetadata): Promise<{ status: string; review_id: string; context: WorkflowContext; error?: string }> {
    logger.info(`Starting PR review workflow ${this.reviewId}`);
    this.context.pr_metadata = prMetadata;

    try {
      // Execute steps in order
      for (const [stepName, step] of this.steps) {
        logger.info(`Executing step: ${stepName}`);
        const result = await step.execute(this.context);
        this.context[stepName] = result;
        logger.info(`Completed step: ${stepName}`);
      }

      logger.info(`Workflow ${this.reviewId} completed successfully`);
      return {
        status: 'completed',
        review_id: this.reviewId,
        context: this.context,
      };
    } catch (error) {
      logger.error(`Workflow ${this.reviewId} failed: ${error}`);
      return {
        status: 'failed',
        review_id: this.reviewId,
        context: this.context,
        error: String(error),
      };
    }
  }

  /**
   * Get results from workflow context
   */
  getAnalysisResult(): unknown {
    return this.context.code_analyzer;
  }

  getQualityResult(): unknown {
    return this.context.quality_evaluator;
  }

  getReviewComments(): unknown {
    return this.context.review_generator;
  }

  getReviewAction(): unknown {
    return this.context.action_executor;
  }
}
