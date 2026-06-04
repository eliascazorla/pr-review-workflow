import 'dotenv/config';
import { z } from 'zod';

/**
 * Application configuration schema
 */
const ConfigSchema = z.object({
  // Provider — derived from model name, validated below
  provider: z.enum(['azure', 'bedrock']),

  // Azure OpenAI (required only when provider === 'azure')
  azureApiEndpoint: z.string().optional(),
  azureApiKey: z.string().optional(),
  modelDeploymentName: z.string().min(1),
  apiVersion: z.string().default('2024-02-15-preview'),

  // AWS Bedrock (used only when provider === 'bedrock')
  awsRegion: z.string().default('us-east-1'),

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
}).superRefine((data, ctx) => {
  if (data.provider === 'azure') {
    if (!data.azureApiEndpoint) {
      ctx.addIssue({ code: z.ZodIssueCode.custom, path: ['azureApiEndpoint'], message: 'AZURE_API_ENDPOINT is required for non-Claude models' });
    } else {
      try { new URL(data.azureApiEndpoint); } catch {
        ctx.addIssue({ code: z.ZodIssueCode.custom, path: ['azureApiEndpoint'], message: 'AZURE_API_ENDPOINT must be a valid URL' });
      }
    }
    if (!data.azureApiKey) {
      ctx.addIssue({ code: z.ZodIssueCode.custom, path: ['azureApiKey'], message: 'AZURE_API_KEY is required for non-Claude models' });
    }
  }
});

export type Config = z.infer<typeof ConfigSchema>;

/**
 * Load and validate configuration from environment variables
 */
export function loadConfig(): Config {
  const env = process.env;

  const modelDeploymentName = env.MODEL_DEPLOYMENT_NAME || '';
  const provider = modelDeploymentName.toLowerCase().startsWith('claude') ? 'bedrock' : 'azure';

  const raw = {
    provider,
    azureApiEndpoint: env.AZURE_API_ENDPOINT || undefined,
    azureApiKey: env.AZURE_API_KEY || undefined,
    modelDeploymentName,
    apiVersion: env.API_VERSION || '2024-02-15-preview',
    awsRegion: env.AWS_REGION || 'us-east-1',
    githubToken: env.GITHUB_TOKEN || '',
    logLevel: (env.LOG_LEVEL || 'info') as 'debug' | 'info' | 'warn' | 'error',
    host: env.HOST || '0.0.0.0',
    port: parseInt(env.PORT || '8000', 10),
    maxRetries: parseInt(env.MAX_RETRIES || '3', 10),
    retryDelayMs: parseInt(env.RETRY_DELAY_MS || '1000', 10),
    otelEnabled: env.OTEL_ENABLED === 'true',
    otelExporterOtlpEndpoint: env.OTEL_EXPORTER_OTLP_ENDPOINT,
  };

  return ConfigSchema.parse(raw);
}

export const config = loadConfig();
