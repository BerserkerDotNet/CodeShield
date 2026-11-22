import { createRequire } from 'node:module';
import { ESLint } from 'eslint';
import * as ts from 'typescript';
import { loadConfigs, standaloneConfigs } from './config-discovery';
import { analyzeContext } from './context-analysis';
import { selectContexts } from './context-selection';
import { createEnv } from './env';
import { buildModuleGraph } from './module-graph';
import { ordinal } from './paths';
import type { AnalyzeRequest, ContextFacts, LanguageAnalysis } from './protocol';
import { ReasonCodes, reason } from './protocol';

const nodeRequire = createRequire(__filename);

export function analyzerVersions(): Record<string, string> {
  const tsEslint = (nodeRequire('@typescript-eslint/parser') as { version: string }).version;
  return {
    typescript: ts.version,
    eslint: ESLint.version,
    'typescript-eslint': tsEslint,
    node: process.versions.node,
  };
}

/** Analyzes one snapshot: tsconfig contexts, module graph, input hashes and unowned focus files. */
export async function analyze(request: AnalyzeRequest): Promise<LanguageAnalysis> {
  const env = createEnv(request.root, request.files);
  const configs = loadConfigs(env);
  const selection = selectContexts(env, standaloneConfigs(configs), request.focusFiles, request.changedConfigFiles);
  const focus = new Set(request.focusFiles);
  const contexts: ContextFacts[] = [];
  for (const config of selection.selected) {
    contexts.push(await analyzeContext({ env, config, focus, rules: request.correctnessRules }));
  }
  const graph = buildModuleGraph(env, standaloneConfigs(configs));
  return {
    language: 'typescript',
    analyzerVersions: analyzerVersions(),
    contexts: contexts.sort((a, b) => ordinal(a.contextId, b.contextId)),
    graph,
    inputs: env.ledger.snapshot(),
    unownedFiles: selection.unowned,
    failures: env.ledger
      .readFailures()
      .map((failure) => reason(ReasonCodes.UnreadableSource, `Cannot read ${failure.path}: ${failure.message}`, null, failure.path)),
  };
}
