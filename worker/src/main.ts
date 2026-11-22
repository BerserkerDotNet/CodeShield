import { analyze } from './analyze';
import type { WorkerRequest } from './protocol';
import { tokenize } from './tokenize';

const PROTOCOL_VERSION = 1;

async function readStdin(): Promise<string> {
  const chunks: Buffer[] = [];
  for await (const chunk of process.stdin) {
    chunks.push(Buffer.from(chunk as Uint8Array));
  }
  return Buffer.concat(chunks).toString('utf8');
}

function parseRequest(text: string): WorkerRequest {
  const request = JSON.parse(text.replace(/^\uFEFF/, '')) as Partial<WorkerRequest>;
  if (request.protocol !== PROTOCOL_VERSION) {
    throw new Error(`Unsupported protocol ${String(request.protocol)}`);
  }
  if (request.command !== 'analyze' && request.command !== 'tokenize') {
    throw new Error(`Unknown command ${String(request.command)}`);
  }
  if (typeof request.root !== 'string' || !Array.isArray(request.files)) {
    throw new Error('Request requires root and files.');
  }
  return request as WorkerRequest;
}

async function run(request: WorkerRequest): Promise<unknown> {
  if (request.command === 'tokenize') {
    return tokenize(request);
  }
  return analyze({
    ...request,
    focusFiles: request.focusFiles ?? [],
    changedConfigFiles: request.changedConfigFiles ?? [],
    correctnessRules: request.correctnessRules ?? [],
  });
}

async function main(): Promise<void> {
  const response = await run(parseRequest(await readStdin()));
  process.stdout.write(JSON.stringify(response));
}

main().catch((error: unknown) => {
  process.stderr.write(`${error instanceof Error ? (error.stack ?? error.message) : String(error)}\n`);
  process.exitCode = 1;
});
