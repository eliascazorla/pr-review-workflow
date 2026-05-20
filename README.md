# PR Review LLM Workflow - TypeScript Edition

A production-ready multi-step LLM workflow for GitHub PR review with structured JSON outputs, parsing, and automated actions built with **TypeScript**, **Express**, and **Microsoft Agent Framework**.

## Features

✨ **Multi-Step Pipeline**
- **Step 1: Code Analysis** — Analyzes code complexity, security issues, patterns, technical debt
- **Step 2: Quality Evaluation** — Evaluates readability, test coverage, performance
- **Step 3: Review Generation** — Creates structured review comments with locations and severity
- **Step 4: Action Execution** — Posts review to GitHub with verdict and comments

✅ **Structured Outputs**
- All pipeline steps produce validated JSON outputs using Zod schemas
- Type-safe data handling with full TypeScript support
- Runtime validation ensures correct format before processing

🔐 **Production Ready**
- Built with TypeScript for type safety
- Express.js HTTP server ready for containerization
- Async/await support throughout
- Comprehensive error handling and retry logic
- Full tracing and logging support with Pino
- VS Code debug configurations included

🚀 **Easy to Deploy**
- Express HTTP server ready for containerization
- Environment-based configuration
- Designed for Azure AI Foundry deployment

## Project Structure

```
pr-review-workflow/
├── src/
│   ├── main.ts                    # Express application
│   ├── config.ts                  # Configuration management
│   ├── models.ts                  # Zod data models and schemas
│   ├── logger.ts                  # Pino logger setup
│   ├── llm-client.ts             # LLM integration with structured outputs
│   ├── workflow.ts                # Workflow orchestrator
│   ├── steps/                     # Pipeline steps
│   │   ├── code-analyzer.ts
│   │   ├── quality-evaluator.ts
│   │   ├── review-generator.ts
│   │   └── action-executor.ts
│   └── schemas/
│       └── json-schemas.ts        # JSON schema definitions
├── tests/
│   └── models.test.ts
├── .vscode/
│   ├── launch.json               # Debug configuration
│   ├── tasks.json                # Build/run tasks
│   └── extensions.json
├── package.json
├── tsconfig.json
├── jest.config.js
├── .env.template
├── .gitignore
└── README.md
```

## Technology Stack

| Component | Technology | Purpose |
|-----------|-----------|---------|
| **Runtime** | Node.js 18+ | JavaScript runtime |
| **Language** | TypeScript 5.3+ | Type-safe development |
| **Framework** | Express 4.18 | HTTP server |
| **Validation** | Zod 3.22 | Runtime schema validation |
| **LLM** | Azure OpenAI (@azure/openai) | LLM API calls |
| **GitHub** | @octokit/rest | GitHub API integration |
| **Logging** | Pino | Structured logging |
| **Testing** | Jest + ts-jest | Unit testing |
| **Build** | TypeScript Compiler | Build and compilation |

## Setup

### 1. Prerequisites

- **Node.js** 18.0+ (download from [nodejs.org](https://nodejs.org))
- **npm** 8+ (comes with Node.js)
- **Azure OpenAI** access or compatible LLM
- **GitHub token** (for posting reviews)

### 2. Installation

1. **Clone/open the project** in VS Code

2. **Install dependencies**:
   ```bash
   npm install
   ```

3. **Configure environment**:
   ```bash
   cp .env.template .env
   ```

4. **Edit `.env`** with your credentials:
   ```env
   AZURE_API_ENDPOINT=https://your-resource.openai.azure.com/
   AZURE_API_KEY=your-api-key-here
   MODEL_DEPLOYMENT_NAME=gpt-4-turbo
   API_VERSION=2024-02-15-preview
   GITHUB_TOKEN=ghp_your-token-here
   LOG_LEVEL=info
   PORT=8000
   ```

## Usage

### Starting the Server

**Option 1: Debug Mode** (Recommended for development)
```bash
# In VS Code, press F5 or use Run → Start Debugging
# Select "TypeScript: Express Server"
```

**Option 2: Development Mode**
```bash
npm run dev
```

**Option 3: Production Build**
```bash
npm run build
npm start
```

### Accessing the API

**Health Check**:
```bash
curl http://localhost:8000/health
```

**Review a PR**:
```bash
curl -X POST http://localhost:8000/review-pr \
  -H "Content-Type: application/json" \
  -d '{
    "repo_owner": "microsoft",
    "repo_name": "agent-framework",
    "pr_number": 42,
    "github_token": "ghp_your-token-here"
  }'
```

**API Documentation**:
View interactive API documentation at `http://localhost:8000/` for available endpoints.

## How It Works

### Pipeline Execution Flow

```
1. Input: PR metadata (repo, PR number)
   ↓
2. Step 1: Code Analyzer
   - Analyzes code diff using LLM
   - Produces: CodeAnalysisResult (validated JSON)
   ↓
3. Step 2: Quality Evaluator
   - Evaluates metrics based on analysis
   - Produces: QualityMetricsResult (validated JSON)
   ↓
4. Step 3: Review Generator
   - Generates comments with locations
   - Produces: ReviewCommentsResult (validated JSON)
   ↓
5. Step 4: Action Executor
   - Posts review to GitHub
   - Produces: ReviewAction (executed)
   ↓
6. Output: Review posted with verdict and comments
```

### Structured Output Example

Each step validates output using Zod schemas:

```typescript
{
  complexity_score: 7,
  security_issues: [
    {
      severity: "medium",
      description: "SQL injection risk in query builder"
    }
  ],
  patterns_found: ["singleton", "factory"],
  tech_debt: ["needs_refactoring"],
  summary: "Code has moderate complexity with security concerns"
}
```

## Configuration

All configuration is managed via environment variables (see `.env.template`):

| Variable | Description | Required |
|----------|-------------|----------|
| `AZURE_API_ENDPOINT` | Azure OpenAI endpoint URL | ✓ |
| `AZURE_API_KEY` | Azure OpenAI API key | ✓ |
| `MODEL_DEPLOYMENT_NAME` | LLM model name (e.g., gpt-4-turbo) | ✓ |
| `API_VERSION` | Azure API version | ✓ |
| `GITHUB_TOKEN` | GitHub personal access token | ✓ |
| `LOG_LEVEL` | Logging level (debug, info, warn, error) | ✗ |
| `MAX_RETRIES` | LLM call retry count (1-10) | ✗ |
| `RETRY_DELAY_MS` | Retry delay in milliseconds | ✗ |
| `HOST` | Server host (default: 0.0.0.0) | ✗ |
| `PORT` | Server port (default: 8000) | ✗ |
| `OTEL_ENABLED` | Enable OpenTelemetry tracing | ✗ |

## Development

### Building

```bash
# Compile TypeScript to JavaScript
npm run build

# Output goes to dist/ directory
```

### Testing

```bash
# Run all tests
npm test

# Run tests in watch mode
npm run test:watch

# Run with coverage
npm test -- --coverage
```

### Linting & Formatting

```bash
# Lint TypeScript files
npm run lint

# Format code
npm run format
```

### Available npm Scripts

```json
{
  "dev": "ts-node src/main.ts",           // Run in development
  "build": "tsc",                          // Compile TypeScript
  "start": "node dist/src/main.js",       // Run compiled version
  "test": "jest",                          // Run tests
  "test:watch": "jest --watch",           // Run tests in watch mode
  "lint": "eslint src/**/*.ts",           // Lint code
  "format": "prettier --write src/**/*.ts" // Format code
}
```

## Debugging

### VS Code Debug Mode

1. **Set breakpoints** in any `.ts` file by clicking on the line number
2. **Press F5** or go to Run → Start Debugging
3. **Select** "TypeScript: Express Server"
4. **Inspect variables** in the Debug Console
5. **View logs** in the integrated Terminal

### Logging

- Logs are printed to console with pretty-printing in development
- Set `LOG_LEVEL=debug` in `.env` for verbose output
- Each workflow execution has a unique `review_id` for tracing

### Troubleshooting

**"Cannot find module"**
- Run `npm install` to ensure all dependencies are installed
- Check `node_modules/` exists

**"TypeScript compilation error"**
- Check `tsconfig.json` is valid
- Ensure all imports use correct paths
- Run `npm run build` to get detailed error messages

**"Invalid API key"**
- Verify `AZURE_API_KEY` in `.env`
- Ensure endpoint URL matches your Azure region

**"Failed to parse LLM response"**
- Increase `MAX_RETRIES` in `.env`
- Check LLM model supports JSON mode
- Verify schema is valid in `src/schemas/json-schemas.ts`

## Architecture

### Type Safety with Zod

Zod provides runtime schema validation with full TypeScript integration:

```typescript
// Define schema
export const CodeAnalysisResultSchema = z.object({
  complexity_score: z.number().min(0).max(10),
  security_issues: z.array(SecurityIssueSchema).default([]),
  // ...
});

// TypeScript type inference
export type CodeAnalysisResult = z.infer<typeof CodeAnalysisResultSchema>;

// Runtime validation
const result = CodeAnalysisResultSchema.parse(data);
```

### LLM Integration

- **Client**: `LLMClient` class handles all Azure OpenAI API calls
- **Structured Output**: JSON schema validation using Zod
- **Parsing**: Automatic JSON extraction from responses
- **Retry Logic**: Automatic retries with exponential backoff

### Workflow Engine

- **Orchestrator**: `PRReviewWorkflow` coordinates pipeline steps
- **Pipeline Steps**: Each step implements `PipelineStep` abstract class
- **State Management**: Context passed between steps as `WorkflowContext`
- **Error Handling**: Comprehensive try-catch with detailed logging

### Express Server

- **Routes**: GET `/` (info), GET `/health`, POST `/review-pr`
- **Middleware**: JSON parsing, request logging, error handling
- **Graceful Shutdown**: Handles SIGINT and SIGTERM signals
- **Error Responses**: Consistent error format for all endpoints

## Containerization

Build Docker image for production:

```dockerfile
# Dockerfile
FROM node:18-alpine

WORKDIR /app

COPY package*.json ./
RUN npm ci --only=production

COPY dist ./dist

EXPOSE 8000

CMD ["node", "dist/src/main.js"]
```

Build and run:

```bash
# Build
docker build -t pr-review-workflow .

# Run
docker run -p 8000:8000 \
  -e AZURE_API_ENDPOINT=... \
  -e AZURE_API_KEY=... \
  -e GITHUB_TOKEN=... \
  pr-review-workflow
```

## Next Steps for Production

### 1. **Add Tracing** (Observability)
- Integrate OpenTelemetry for distributed tracing
- Export traces to Azure Application Insights
- Monitor LLM latency and performance

### 2. **Enhance GitHub Integration**
- Fetch actual PR diff from GitHub API using @octokit/rest
- Post detailed comments to specific lines
- Set review status (approve/request changes)
- Handle webhook events from GitHub

### 3. **Add Evaluation** (Quality Metrics)
- Set up evaluation framework to measure review quality
- Track reviewer satisfaction scores
- Analyze common patterns in reviews

### 4. **Deploy to Production**
- Containerize with Docker
- Deploy to Azure Container Apps or AKS
- Set up CI/CD pipeline with GitHub Actions
- Use Azure Key Vault for secrets management

### 5. **Advanced Features**
- Custom review rules per repository
- Historical review tracking and analytics
- Multi-model support for different analysis types
- Review feedback loop and learning

## Contributing

When adding new workflow steps:

1. **Create step class** in `src/steps/`:
   ```typescript
   export class MyStep extends PipelineStep {
     async execute(context: WorkflowContext): Promise<MyResult> {
       // Implementation
     }
   }
   ```

2. **Define data models** in `src/models.ts`:
   ```typescript
   export const MyResultSchema = z.object({
     // Schema definition
   });
   export type MyResult = z.infer<typeof MyResultSchema>;
   ```

3. **Register in workflow** in `src/main.ts`

4. **Add tests** in `tests/`

## License

MIT License - Feel free to use and modify

## Support

For issues or questions:
1. Check logs with `LOG_LEVEL=debug` in `.env`
2. Review error messages and stack traces
3. Check the troubleshooting section above
4. Consult the code documentation and examples
5. Open an issue with detailed error messages and logs

## References

- [Express.js Documentation](https://expressjs.com/)
- [TypeScript Handbook](https://www.typescriptlang.org/docs/)
- [Zod Documentation](https://zod.dev/)
- [Azure OpenAI API](https://learn.microsoft.com/en-us/azure/cognitive-services/openai/)
- [GitHub API Documentation](https://docs.github.com/en/rest)
- [Pino Logger](https://getpino.io/)
