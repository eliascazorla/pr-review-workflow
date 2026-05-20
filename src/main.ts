import express, { Express, Request, Response } from 'express';
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
import { PRReviewRequestSchema, PRMetadata, PRReviewResponseSchema } from './models';

const app: Express = express();
let llmClient: LLMClient | null = null;

/**
 * Middleware
 */
app.use(express.json());

/**
 * Request logging middleware
 */
app.use((req: Request, res: Response, next) => {
  logger.info(`${req.method} ${req.path}`);
  next();
});

/**
 * Initialize on startup
 */
async function initialize(): Promise<void> {
  logger.info('Initializing PR Review Workflow application');
  llmClient = new LLMClient();
  logger.info(`Connected to model: ${config.modelDeploymentName}`);
}

/**
 * Health check endpoint
 */
app.get('/health', (req: Request, res: Response) => {
  res.json({
    status: 'healthy',
    service: 'pr-review-workflow',
    timestamp: new Date().toISOString(),
  });
});

/**
 * Root endpoint
 */
app.get('/', (req: Request, res: Response) => {
  res.json({
    service: 'PR Review Workflow',
    version: '0.1.0',
    endpoints: {
      health: '/health',
      review: '/review-pr',
      docs: 'See README.md for documentation',
    },
  });
});

/**
 * Review PR endpoint
 */
app.post('/review-pr', async (req: Request, res: Response) => {
  try {
    // Validate request
    const request = PRReviewRequestSchema.parse(req.body);
    logger.info(
      `Received PR review request: ${request.repo_owner}/${request.repo_name}#${request.pr_number}`
    );

    if (!llmClient) {
      throw new Error('LLM client not initialized');
    }

    // Initialize workflow with all steps
    const workflow = new PRReviewWorkflow();
    workflow.registerStep('code_analyzer', new CodeAnalyzerStep(llmClient));
    workflow.registerStep('quality_evaluator', new QualityEvaluatorStep(llmClient));
    workflow.registerStep('review_generator', new ReviewGeneratorStep(llmClient));
    workflow.registerStep('action_executor', new ActionExecutorStep(request.github_token));

    // Create PR metadata (in real scenario, fetch from GitHub API)
    const prMetadata: PRMetadata = {
      repo_owner: request.repo_owner,
      repo_name: request.repo_name,
      pr_number: request.pr_number,
      base_branch: 'main',
      title: 'Sample PR Title',
      description: 'Sample PR description with changes',
      diff: `
--- a/src/example.ts
+++ b/src/example.ts
@@ -1,5 +1,10 @@
 function processData(items: any[]) {
-  const result: any[] = [];
+  const result: Record<string, number> = {};
   for (const item of items) {
-    result.push(item * 2);
+    const key = item.id;
+    if (!key) continue;
+    result[key] = (item.value || 0) * 2;
   }
   return result;
}`,
      author: 'developer@example.com',
    };

    // Execute workflow
    const result = await workflow.execute(prMetadata);

    if (result.status === 'failed') {
      return res.status(500).json({
        status: 'failed',
        review_id: result.review_id,
        message: `Workflow failed: ${result.error}`,
        error: result.error,
      });
    }

    // Get results from context
    const reviewComments = workflow.getReviewComments();

    const response = {
      status: 'completed',
      review_id: result.review_id,
      message: 'PR review completed successfully',
      verdict: (reviewComments as any)?.overall_verdict || 'comment',
      comments_posted: (reviewComments as any)?.review_comments?.length || 0,
      analysis: {
        code_analysis: workflow.getAnalysisResult(),
        quality_metrics: workflow.getQualityResult(),
        review_comments: workflow.getReviewComments(),
      },
    };

    // Validate response
    const validatedResponse = PRReviewResponseSchema.parse(response);
    res.json(validatedResponse);
  } catch (error) {
    logger.error(`Request error: ${error}`);
    res.status(400).json({
      status: 'failed',
      review_id: 'unknown',
      message: String(error),
      error: String(error),
    });
  }
});

/**
 * 404 handler
 */
app.use((req: Request, res: Response) => {
  res.status(404).json({
    error: 'Not found',
    path: req.path,
  });
});

/**
 * Error handler
 */
app.use((err: Error, req: Request, res: Response, next: any) => {
  logger.error(`Unhandled error: ${err}`);
  res.status(500).json({
    status: 'failed',
    review_id: 'unknown',
    message: 'Internal server error',
    error: err.message,
  });
});

/**
 * Start server
 */
async function start(): Promise<void> {
  try {
    await initialize();

    app.listen(config.port, config.host, () => {
      logger.info(`Server running at http://${config.host}:${config.port}`);
      logger.info(`Health check: http://${config.host}:${config.port}/health`);
      logger.info(`API docs: See README.md for endpoint documentation`);
    });
  } catch (error) {
    logger.error(`Failed to start server: ${error}`);
    process.exit(1);
  }
}

/**
 * Handle graceful shutdown
 */
process.on('SIGINT', async () => {
  logger.info('Shutting down gracefully...');
  if (llmClient) {
    await llmClient.close();
  }
  process.exit(0);
});

process.on('SIGTERM', async () => {
  logger.info('Shutting down gracefully...');
  if (llmClient) {
    await llmClient.close();
  }
  process.exit(0);
});

// Start the server
if (require.main === module) {
  start();
}

export default app;
export { app };
