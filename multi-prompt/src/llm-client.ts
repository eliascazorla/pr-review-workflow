import OpenAI from 'openai';
import { BedrockRuntimeClient, ConverseCommand } from '@aws-sdk/client-bedrock-runtime';
import { z, ZodTypeAny } from 'zod';
import logger from './logger';
import { config } from './config';
import zodToJsonSchema from 'zod-to-json-schema';

export interface TokenUsage {
  prompt_tokens: number;
  completion_tokens: number;
  total_tokens: number;
}

export interface StepTokenUsage {
  step: string;
  prompt_tokens: number;
  completion_tokens: number;
  total_tokens: number;
}

/**
 * LLM Client for making structured output calls with JSON validation.
 * Supports Azure OpenAI (default) and AWS Bedrock (when MODEL_DEPLOYMENT_NAME starts with "claude").
 */
export class LLMClient {
  private openaiClient: OpenAI | null = null;
  private bedrockClient: BedrockRuntimeClient | null = null;
  private modelDeploymentName: string;
  private maxRetries: number;
  private tokenUsage: TokenUsage = { prompt_tokens: 0, completion_tokens: 0, total_tokens: 0 };
  private stepTokenUsage: Map<string, TokenUsage> = new Map();

  constructor() {
    this.modelDeploymentName = config.modelDeploymentName;
    this.maxRetries = config.maxRetries;

    if (config.provider === 'bedrock') {
      this.bedrockClient = new BedrockRuntimeClient({ region: config.awsRegion });
      logger.info(`LLM client initialized with AWS Bedrock model: ${this.modelDeploymentName} (region: ${config.awsRegion})`);
    } else {
      this.openaiClient = new OpenAI({
        baseURL: config.azureApiEndpoint,
        apiKey: config.azureApiKey,
        defaultHeaders: { 'api-key': config.azureApiKey },
      });
      logger.info(`LLM client initialized with Azure OpenAI model: ${this.modelDeploymentName}`);
    }
  }

  /**
   * Call LLM with structured output requirements
   */
  async callWithSchema<T extends ZodTypeAny>(
    schema: T,
    systemPrompt: string,
    userMessage: string,
    temperature: number = 0,
    maxTokens: number = 12000,
    stepName: string = 'unknown'
  ): Promise<z.output<T>> {
    const fullSystemPrompt = `${systemPrompt}

You MUST respond with valid JSON that conforms to this schema:
${JSON.stringify(zodToJsonSchema(schema as any), null, 2)}

Ensure the JSON is valid and complete.`;

    logger.info(`Calling model '${this.modelDeploymentName}' for step '${stepName}'`);

    if (config.provider === 'bedrock') {
      return this.callBedrock(schema, fullSystemPrompt, userMessage, temperature, maxTokens, stepName);
    }
    return this.callAzureOpenAI(schema, fullSystemPrompt, userMessage, temperature, maxTokens, stepName);
  }

  /**
   * Azure OpenAI call path
   */
  private async callAzureOpenAI<T extends ZodTypeAny>(
    schema: T,
    fullSystemPrompt: string,
    userMessage: string,
    temperature: number,
    maxTokens: number,
    stepName: string
  ): Promise<z.output<T>> {
    for (let attempt = 1; attempt <= this.maxRetries; attempt++) {
      try {
        logger.info(`LLM call attempt ${attempt}/${this.maxRetries}`);

        const response = await this.openaiClient!.chat.completions.create({
          model: this.modelDeploymentName,
          messages: [
            { role: 'system', content: fullSystemPrompt },
            { role: 'user', content: userMessage },
          ],
          temperature,
          max_completion_tokens: maxTokens,
          response_format: { type: 'json_object' },
        });

        const content = response.choices[0].message.content?.trim() ?? '';
        if (response.usage) {
          const pt = response.usage.prompt_tokens ?? 0;
          const ct = response.usage.completion_tokens ?? 0;
          const tt = response.usage.total_tokens ?? 0;
          this.accumulateTokenUsage(stepName, pt, ct, tt);
        }

        let jsonContent: unknown;
        try {
          jsonContent = this.extractJSON(content);
        } catch (extractErr) {
          logger.warn(`JSON extraction failed. Raw content (first 500 chars): ${content.slice(0, 500)}`);
          throw extractErr;
        }

        const parsed = schema.parse(jsonContent) as z.output<T>;
        logger.info('Successfully parsed LLM response');
        return parsed;
      } catch (error) {
        this.logRetryError(error, attempt);
        if (attempt >= this.maxRetries) {
          throw new Error(`Failed to get valid response after ${this.maxRetries} attempts: ${error}`);
        }
        await this.backoff(attempt);
      }
    }
    throw new Error(`Failed to get valid response after ${this.maxRetries} attempts`);
  }

  /**
   * AWS Bedrock Converse API call path
   */
  private async callBedrock<T extends ZodTypeAny>(
    schema: T,
    fullSystemPrompt: string,
    userMessage: string,
    temperature: number,
    maxTokens: number,
    stepName: string
  ): Promise<z.output<T>> {
    for (let attempt = 1; attempt <= this.maxRetries; attempt++) {
      try {
        logger.info(`LLM call attempt ${attempt}/${this.maxRetries}`);

        const command = new ConverseCommand({
          modelId: this.modelDeploymentName,
          system: [{ text: fullSystemPrompt }],
          messages: [{ role: 'user', content: [{ text: userMessage }] }],
          inferenceConfig: { temperature, maxTokens },
        });

        const response = await this.bedrockClient!.send(command);

        const content = response.output?.message?.content?.[0]?.text?.trim() ?? '';
        if (response.usage) {
          const pt = response.usage.inputTokens ?? 0;
          const ct = response.usage.outputTokens ?? 0;
          const tt = response.usage.totalTokens ?? 0;
          this.accumulateTokenUsage(stepName, pt, ct, tt);
        }

        let jsonContent: unknown;
        try {
          jsonContent = this.extractJSON(content);
        } catch (extractErr) {
          logger.warn(`JSON extraction failed. Raw content (first 500 chars): ${content.slice(0, 500)}`);
          throw extractErr;
        }

        const parsed = schema.parse(jsonContent) as z.output<T>;
        logger.info('Successfully parsed LLM response');
        return parsed;
      } catch (error) {
        this.logRetryError(error, attempt);
        if (attempt >= this.maxRetries) {
          throw new Error(`Failed to get valid response after ${this.maxRetries} attempts: ${error}`);
        }
        await this.backoff(attempt);
      }
    }
    throw new Error(`Failed to get valid response after ${this.maxRetries} attempts`);
  }

  private accumulateTokenUsage(stepName: string, pt: number, ct: number, tt: number): void {
    this.tokenUsage.prompt_tokens += pt;
    this.tokenUsage.completion_tokens += ct;
    this.tokenUsage.total_tokens += tt;
    const existing = this.stepTokenUsage.get(stepName) ?? { prompt_tokens: 0, completion_tokens: 0, total_tokens: 0 };
    this.stepTokenUsage.set(stepName, {
      prompt_tokens: existing.prompt_tokens + pt,
      completion_tokens: existing.completion_tokens + ct,
      total_tokens: existing.total_tokens + tt,
    });
  }

  private logRetryError(error: unknown, attempt: number): void {
    if (error instanceof z.ZodError) {
      logger.warn(`Validation error on attempt ${attempt}: ${error.message}`);
    } else if (error instanceof OpenAI.APIError) {
      logger.warn(`API error on attempt ${attempt}: ${error.status} ${error.message} — ${JSON.stringify(error.error)}`);
    } else {
      logger.warn(`Error on attempt ${attempt}: ${error}`);
    }
  }

  private async backoff(attempt: number): Promise<void> {
    const delay = config.retryDelayMs * Math.pow(2, attempt - 1);
    await new Promise((resolve) => setTimeout(resolve, delay));
  }

  /**
   * Extract JSON from content, handling markdown code blocks
   */
  private extractJSON(content: string): unknown {
    // Try direct parsing first
    try {
      return JSON.parse(content);
    } catch {
      // Try extracting from markdown code blocks
    }

    const patterns = [
      /```json\s*([\s\S]*?)\s*```/,
      /```\s*([\s\S]*?)\s*```/,
      /(\{[\s\S]*\})/,
    ];

    for (const pattern of patterns) {
      const matches = content.match(pattern);
      if (matches) {
        try {
          return JSON.parse(matches[1]);
        } catch {
          continue;
        }
      }
    }

    throw new Error('Could not extract valid JSON from content');
  }

  getTokenUsage(): TokenUsage {
    return { ...this.tokenUsage };
  }

  getTokenUsageByStep(): StepTokenUsage[] {
    return Array.from(this.stepTokenUsage.entries()).map(([step, usage]) => ({
      step,
      ...usage,
    }));
  }

  async close(): Promise<void> {
    logger.info('Closing LLM client');
  }
}
