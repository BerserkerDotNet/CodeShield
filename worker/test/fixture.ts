import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { analyze } from '../src/analyze';
import type { AnalyzeRequest, ContextFacts, CorrectnessRuleRequest, LanguageAnalysis, MethodFact } from '../src/protocol';

export const TSCONFIG = JSON.stringify({ compilerOptions: { strict: true, noEmit: true, target: 'ES2022' }, include: ['*.ts'] });
export const PACKAGE = JSON.stringify({ name: 'fixture', version: '1.0.0', private: true });

export function makeFixture(files: Record<string, string>): string {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'codeshield-'));
  for (const [rel, content] of Object.entries(files)) {
    const target = path.join(dir, rel);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, content);
  }
  return dir;
}

export function inventory(dir: string, base = ''): string[] {
  return fs.readdirSync(path.join(dir, base), { withFileTypes: true }).flatMap((entry) => {
    const rel = base === '' ? entry.name : `${base}/${entry.name}`;
    return entry.isDirectory() ? inventory(dir, rel) : [rel];
  });
}

export interface RunOptions {
  focus?: string[];
  changed?: string[];
  rules?: CorrectnessRuleRequest[];
}

export function run(dir: string, options: RunOptions = {}): Promise<LanguageAnalysis> {
  const request: AnalyzeRequest = {
    protocol: 1,
    command: 'analyze',
    root: dir,
    files: inventory(dir),
    focusFiles: options.focus ?? inventory(dir).filter((f) => /\.tsx?$/.test(f)),
    changedConfigFiles: options.changed ?? [],
    isBaseline: false,
    correctnessRules: options.rules ?? [],
  };
  return analyze(request);
}

export function singleContext(result: LanguageAnalysis): ContextFacts {
  if (result.contexts.length !== 1) {
    throw new Error(`Expected one context, got ${result.contexts.length}`);
  }
  return result.contexts[0]!;
}

/** Runs a one-file TypeScript fixture and returns its method facts. */
export async function methodsFor(source: string): Promise<MethodFact[]> {
  const dir = makeFixture({ 'package.json': PACKAGE, 'tsconfig.json': TSCONFIG, 'a.ts': source });
  const context = singleContext(await run(dir));
  return context.files[0]!.methods;
}

export const CORRECTNESS_RULES: CorrectnessRuleRequest[] = [
  ['@typescript-eslint/no-floating-promises', [{ ignoreVoid: false }]],
  ['@typescript-eslint/no-misused-promises', []],
  ['@typescript-eslint/await-thenable', []],
  ['array-callback-return', []],
  ['for-direction', []],
  ['no-async-promise-executor', []],
  ['no-constant-binary-expression', []],
  ['no-dupe-else-if', []],
  ['no-self-assign', []],
  ['no-unsafe-finally', []],
  ['no-unsafe-optional-chaining', []],
  ['use-isnan', []],
].map(([analyzerId, options]) => ({
  analyzerId: analyzerId as string,
  evaluatorId: `correctness.typescript.${(analyzerId as string).split('/').pop()}`,
  options: options as unknown[],
}));
