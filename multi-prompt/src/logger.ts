import pino from 'pino';
import { config } from './config';

/**
 * Configure and create a logger instance
 */
const logger = pino(
  {
    level: config.logLevel,
    transport:
      process.env.NODE_ENV === 'production'
        ? undefined
        : {
            target: 'pino-pretty',
            options: {
              colorize: true,
              singleLine: false,
              translateTime: 'SYS:standard',
            },
          },
  }
);

export default logger;
