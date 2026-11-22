import { ESLint } from 'eslint';
import { buildEslintConfig } from './eslint-config';
import type { CorrectnessRuleRequest } from './protocol';

export interface LintMessage {
  file: string;
  ruleId: string | null;
  fatal: boolean;
  message: string;
  line: number;
  column: number;
  endLine: number;
  endColumn: number;
}

export interface LintRequest {
  root: string;
  tsconfigPath: string;
  files: string[];
  rules: CorrectnessRuleRequest[];
}

/** Runs the selected rules (flat config, type-aware via the context's tsconfig) over the given files. */
export async function runEslint(request: LintRequest): Promise<LintMessage[]> {
  const eslint = new ESLint({
    cwd: request.root,
    overrideConfigFile: true,
    overrideConfig: buildEslintConfig(request.root, request.tsconfigPath, request.rules),
    ignore: false,
    warnIgnored: false,
    errorOnUnmatchedPattern: false,
  });
  const results = await eslint.lintFiles(request.files);
  return results.flatMap((result) =>
    result.messages.map((message) => ({
      file: result.filePath,
      ruleId: message.ruleId,
      fatal: message.fatal === true,
      message: message.message,
      line: message.line,
      column: message.column,
      endLine: message.endLine ?? message.line,
      endColumn: message.endColumn ?? message.column,
    })),
  );
}
