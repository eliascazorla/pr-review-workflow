import axios from 'axios';
import { z, ZodTypeAny } from 'zod';
import logger from './logger';
import { config } from './config';
import zodToJsonSchema from 'zod-to-json-schema';

/**
 * LLM Client for making structured output calls with JSON validation
 */
export class LLMClient {
  private apiEndpoint: string;
  private apiKey: string;
  private modelDeploymentName: string;
  private maxRetries: number;

  constructor() {
    this.apiEndpoint = config.azureApiEndpoint;
    this.apiKey = config.azureApiKey;
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
    temperature: number = 0.7,
    maxTokens: number = 4096
  ): Promise<z.output<T>> {
    const fullSystemPrompt = `${systemPrompt}

You MUST respond with valid JSON that conforms to this schema:
${JSON.stringify(zodToJsonSchema(schema as any), null, 2)}

Ensure the JSON is valid and complete.`;

    for (let attempt = 1; attempt <= this.maxRetries; attempt++) {
      try {
        logger.info(`LLM call attempt ${attempt}/${this.maxRetries}`);

        // Call Azure OpenAI API
        const response = await axios.post(
          `${this.apiEndpoint}/openai/deployments/${this.modelDeploymentName}/chat/completions?api-version=${config.apiVersion}`,
          {
            messages: [
              {
                role: 'system',
                content: fullSystemPrompt,
              },
              {
                role: 'user',
                content: userMessage,
              },
            ],
            temperature,
            max_tokens: maxTokens,
            response_format: { type: 'json_object' },
          },
          {
            headers: {
              'api-key': this.apiKey,
              'Content-Type': 'application/json',
            },
          }
        );

        const content = response.data.choices[0].message.content.trim();
        const jsonContent = this.extractJSON(content);

        // Parse with Zod schema
        const parsed = schema.parse(jsonContent) as z.output<T>;
        logger.info('Successfully parsed LLM response');
        return parsed;
      } catch (error) {
        if (error instanceof z.ZodError) {
          logger.warn(`Validation error on attempt ${attempt}: ${error.message}`);
        } else if (axios.isAxiosError(error)) {
          logger.warn(`API error on attempt ${attempt}: ${error.message}`);
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

  async close(): Promise<void> {
    logger.info('Closing LLM client');
  }
}
