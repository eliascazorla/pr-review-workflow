import { z } from 'zod';

/**
 * Application configuration schema
 */
const ConfigSchema = z.object({
  // Azure OpenAI
  azureApiEndpoint: z.string().url(),
  azureApiKey: z.string().min(1),
  modelDeploymentName: z.string().default('gpt-4-turbo'),
  apiVersion: z.string().default('2024-02-15-preview'),

  // GitHub
  githubToken: z.string().min(1),

  // Logging
  logLevel: z.enum(['debug', 'info', 'warn', 'error']).default('info'),

  // Server
  host: z.string().default('0.0.0.0'),
  port: z.number().min(1).max(65535).default(8000),

  // Retry
  maxRetries: z.number().min(1).max(10).default(3),
  retryDelayMs: z.number().min(100).default(1000),

  // OpenTelemetry
  otelEnabled: z.boolean().default(false),
  otelExporterOtlpEndpoint: z.string().optional(),
});

export type Config = z.infer<typeof ConfigSchema>;

/**
 * Load and validate configuration from environment variables
 */
export function loadConfig(): Config {
  const env = process.env;

  const config = {
    azureApiEndpoint: env.AZURE_API_ENDPOINT || '',
    azureApiKey: env.AZURE_API_KEY || '',
    modelDeploymentName: env.MODEL_DEPLOYMENT_NAME || 'gpt-4-turbo',
    apiVersion: env.API_VERSION || '2024-02-15-preview',
    githubToken: env.GITHUB_TOKEN || '',
    logLevel: (env.LOG_LEVEL || 'info') as 'debug' | 'info' | 'warn' | 'error',
    host: env.HOST || '0.0.0.0',
    port: parseInt(env.PORT || '8000', 10),
    maxRetries: parseInt(env.MAX_RETRIES || '3', 10),
    retryDelayMs: parseInt(env.RETRY_DELAY_MS || '1000', 10),
    otelEnabled: env.OTEL_ENABLED === 'true',
    otelExporterOtlpEndpoint: env.OTEL_EXPORTER_OTLP_ENDPOINT,
  };

  return ConfigSchema.parse(config);
}

export const config = loadConfig();
