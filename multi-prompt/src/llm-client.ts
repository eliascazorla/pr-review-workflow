import OpenAI from 'openai';
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
 * LLM Client for making structured output calls with JSON validation
 */
export class LLMClient {
  private client: OpenAI;
  private modelDeploymentName: string;
  private maxRetries: number;
  private tokenUsage: TokenUsage = { prompt_tokens: 0, completion_tokens: 0, total_tokens: 0 };
  private stepTokenUsage: Map<string, TokenUsage> = new Map();

  constructor() {
    this.client = new OpenAI({
      baseURL: config.azureApiEndpoint,
      apiKey: config.azureApiKey,
      defaultHeaders: { 'api-key': config.azureApiKey },
    });
    this.modelDeploymentName = config.modelDeploymentName;
    this.maxRetries = config.maxRetries;
  }

  /**
   * Call LLM with structured output requirements
   */
  async callWithSchema<T extends ZodTypeAny>(
    schema: T,
    systemPrompt: string,
    userMessage: string,
    temperature: number = 0,
    maxTokens: number = 8192,
    stepName: string = 'unknown'
  ): Promise<z.output<T>> {
    const fullSystemPrompt = `${systemPrompt}

You MUST respond with valid JSON that conforms to this schema:
${JSON.stringify(zodToJsonSchema(schema as any), null, 2)}

Ensure the JSON is valid and complete.`;

    for (let attempt = 1; attempt <= this.maxRetries; attempt++) {
      try {
        logger.info(`LLM call attempt ${attempt}/${this.maxRetries}`);

        // Call Azure OpenAI API via SDK
        const response = await this.client.chat.completions.create({
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
        let jsonContent: unknown;
        try {
          jsonContent = this.extractJSON(content);
        } catch (extractErr) {
          logger.warn(`JSON extraction failed. Raw content (first 500 chars): ${content.slice(0, 500)}`);
          throw extractErr;
        }

        // Parse with Zod schema
        const parsed = schema.parse(jsonContent) as z.output<T>;
        logger.info('Successfully parsed LLM response');
        return parsed;
      } catch (error) {
        if (error instanceof z.ZodError) {
          logger.warn(`Validation error on attempt ${attempt}: ${error.message}`);
        } else if (error instanceof OpenAI.APIError) {
          logger.warn(`API error on attempt ${attempt}: ${error.status} ${error.message} — ${JSON.stringify(error.error)}`);
        } else {
          logger.warn(`Error on attempt ${attempt}: ${error}`);
        }

        if (attempt < this.maxRetries) {
          const delay = config.retryDelayMs * Math.pow(2, attempt - 1);
          await new Promise((resolve) => setTimeout(resolve, delay));
          continue;
        }

        throw new Error(
          `Failed to get valid response after ${this.maxRetries} attempts: ${error}`
        );
      }
    }

    throw new Error(`Failed to get valid response after ${this.maxRetries} attempts`);
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
