import type * as ts from 'typescript';
import { collectComments } from './comments';
import { reason, ReasonCodes, type Reason } from './protocol';

export interface SelectedRule {
  analyzerId: string;
  evaluatorId: string;
}

interface Directive {
  line: number;
  rules: string[];
}

const DIRECTIVE = /^\s*eslint-disable(?:-next-line|-line)?(?=\s|$)(.*)$/s;

function parseDirective(text: string, line: number): Directive | undefined {
  const match = DIRECTIVE.exec(text);
  if (!match) {
    return undefined;
  }
  const list = match[1]!.split(/\s--(?:\s|$)/)[0]!;
  const rules = list.split(',').map((rule) => rule.trim()).filter((rule) => rule.length > 0);
  return { line, rules };
}

function suppresses(directive: Directive, rule: SelectedRule): boolean {
  return directive.rules.length === 0 || directive.rules.includes(rule.analyzerId);
}

/** SUPPRESSED_RULE failures for selected rules disabled (named or blanket) by comments in this file. */
export function findSuppressions(
  sourceFile: ts.SourceFile,
  file: string,
  rules: SelectedRule[],
  contextId: string,
): Reason[] {
  const directives = collectComments(sourceFile)
    .map((comment) => parseDirective(comment.text, comment.line))
    .filter((directive): directive is Directive => directive !== undefined);
  return directives.flatMap((directive) =>
    rules
      .filter((rule) => suppresses(directive, rule))
      .map((rule) =>
        reason(
          ReasonCodes.SuppressedRule,
          `${file}:${directive.line} disables ${rule.analyzerId}, so the rule cannot be assessed.`,
          rule.evaluatorId,
          contextId,
        ),
      ),
  );
}
