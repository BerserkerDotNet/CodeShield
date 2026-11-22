import { builtinRules } from 'eslint/use-at-your-own-risk';
import { parser, plugin } from 'typescript-eslint';
import type { Linter } from 'eslint';
import type { CorrectnessRuleRequest } from './protocol';

const TYPESCRIPT_ESLINT_PREFIX = '@typescript-eslint/';

export function isTypeAwareRule(analyzerId: string): boolean {
  return analyzerId.startsWith(TYPESCRIPT_ESLINT_PREFIX);
}

/** True when the pinned ESLint or typescript-eslint package ships the rule. */
export function ruleExists(analyzerId: string): boolean {
  if (isTypeAwareRule(analyzerId)) {
    const analyzer = analyzerId.slice(TYPESCRIPT_ESLINT_PREFIX.length);
    return analyzer in (plugin as unknown as { rules: Record<string, unknown> }).rules;
  }
  return builtinRules.has(analyzerId);
}

/** Flat config containing only the requested rules; repository config and inline directives are ignored. */
export function buildEslintConfig(root: string, tsconfigPath: string, rules: CorrectnessRuleRequest[]): Linter.Config[] {
  const ruleEntries = Object.fromEntries(rules.map((rule) => [rule.analyzerId, ['error', ...rule.options]]));
  return [
    {
      files: ['**/*.ts', '**/*.tsx', '**/*.mts', '**/*.cts'],
      languageOptions: {
        parser: parser as Linter.Parser,
        parserOptions: { project: [tsconfigPath], tsconfigRootDir: root },
      },
      plugins: { [TYPESCRIPT_ESLINT_PREFIX.slice(0, -1)]: plugin as unknown as Record<string, unknown> },
      linterOptions: { noInlineConfig: true, reportUnusedDisableDirectives: 'off' },
      rules: ruleEntries as Linter.RulesRecord,
    },
  ];
}
