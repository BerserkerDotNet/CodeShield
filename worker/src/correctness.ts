import * as ts from 'typescript';
import type { AnalysisEnv } from './env';
import { isTypeAwareRule, ruleExists } from './eslint-config';
import { runEslint, type LintMessage } from './eslint-runner';
import { canonicalPath, ordinal } from './paths';
import { reason, ReasonCodes, type CorrectnessRuleRequest, type DiagnosticFact, type Reason } from './protocol';
import type { SourceBundle } from './source-bundle';
import { findSuppressions } from './suppression';

export interface CorrectnessInput {
  env: AnalysisEnv;
  contextId: string;
  tsconfigPath: string;
  program: ts.Program;
  bundles: Map<string, SourceBundle>;
  rules: CorrectnessRuleRequest[];
}

export interface CorrectnessOutcome {
  diagnostics: DiagnosticFact[];
  failures: Reason[];
}

const MISSING_GLOBAL_TYPE = 2318;
const MISSING_LIB_FILE = 6053;

function hasTypeInformation(program: ts.Program): boolean {
  const problems = [...program.getGlobalDiagnostics(), ...program.getOptionsDiagnostics()];
  return !problems.some((d) => d.code === MISSING_GLOBAL_TYPE || d.code === MISSING_LIB_FILE);
}

function failure(code: string, message: string, rule: CorrectnessRuleRequest, input: CorrectnessInput): Reason {
  return reason(code, message, rule.evaluatorId, input.contextId);
}

/** Splits requested rules into runnable ones and failures for rules that cannot be assessed. */
function selectRunnable(input: CorrectnessInput): { runnable: CorrectnessRuleRequest[]; failures: Reason[] } {
  const typed = hasTypeInformation(input.program);
  const runnable: CorrectnessRuleRequest[] = [];
  const failures: Reason[] = [];
  for (const rule of input.rules) {
    if (!ruleExists(rule.analyzerId)) {
      failures.push(failure(ReasonCodes.MissingAnalyzer, `Analyzer ${rule.analyzerId} is not installed.`, rule, input));
    } else if (!typed && isTypeAwareRule(rule.analyzerId)) {
      failures.push(failure(ReasonCodes.MissingDependency, 'Type information is unavailable (missing lib typings).', rule, input));
    } else {
      runnable.push(rule);
    }
  }
  return { runnable, failures };
}

function suppressionFailures(input: CorrectnessInput, rules: CorrectnessRuleRequest[]): Reason[] {
  return [...input.bundles.values()].flatMap((bundle) =>
    findSuppressions(bundle.sourceFile, bundle.file, rules, input.contextId),
  );
}

function toDiagnostic(input: CorrectnessInput, message: LintMessage): DiagnosticFact | undefined {
  const file = input.env.paths.toRel(canonicalPath(message.file));
  const bundle = file === undefined ? undefined : input.bundles.get(file);
  if (!file || !bundle || !message.ruleId) {
    return undefined;
  }
  const pos = bundle.sourceFile.getPositionOfLineAndCharacter(message.line - 1, Math.max(message.column - 1, 0));
  return {
    analyzerId: message.ruleId,
    file,
    span: { startLine: message.line, startColumn: message.column, endLine: message.endLine, endColumn: message.endColumn },
    message: message.message,
    unitId: bundle.namer.enclosingUnitId(pos),
  };
}

function compareDiagnostics(a: DiagnosticFact, b: DiagnosticFact): number {
  return (
    ordinal(a.file, b.file) ||
    a.span.startLine - b.span.startLine ||
    a.span.startColumn - b.span.startColumn ||
    ordinal(a.analyzerId, b.analyzerId)
  );
}

function loadFailure(input: CorrectnessInput, detail: string): Reason {
  return reason(ReasonCodes.ProjectLoadFailed, `ESLint could not analyze ${input.tsconfigPath}: ${detail}`, null, input.contextId);
}

async function lint(input: CorrectnessInput, rules: CorrectnessRuleRequest[]): Promise<CorrectnessOutcome> {
  let messages: LintMessage[];
  try {
    messages = await runEslint({
      root: input.env.paths.root,
      tsconfigPath: input.tsconfigPath,
      files: [...input.bundles.keys()].map((file) => input.env.paths.toAbs(file)),
      rules,
    });
  } catch (error) {
    return { diagnostics: [], failures: [loadFailure(input, String(error))] };
  }
  const fatal = messages.filter((m) => m.fatal);
  const diagnostics = messages
    .filter((m) => !m.fatal)
    .map((m) => toDiagnostic(input, m))
    .filter((d): d is DiagnosticFact => d !== undefined);
  return {
    diagnostics: diagnostics.sort(compareDiagnostics),
    failures: fatal.slice(0, 1).map((m) => loadFailure(input, m.message)),
  };
}

/** Runs the selected ESLint rules for one context and reports rules that could not be assessed. */
export async function runCorrectness(input: CorrectnessInput): Promise<CorrectnessOutcome> {
  const { runnable, failures } = selectRunnable(input);
  const suppressed = suppressionFailures(input, input.rules.filter((rule) => ruleExists(rule.analyzerId)));
  if (runnable.length === 0) {
    return { diagnostics: [], failures: [...failures, ...suppressed] };
  }
  const outcome = await lint(input, runnable);
  return { diagnostics: outcome.diagnostics, failures: [...failures, ...suppressed, ...outcome.failures] };
}
