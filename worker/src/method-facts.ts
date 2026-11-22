import * as ts from 'typescript';
import { isMeasuredFunction, spanOf, type MeasuredFunction } from './function-nodes';
import { measureFunction } from './metrics';
import { ordinal } from './paths';
import type { MethodFact } from './protocol';
import type { TokenIndex } from './token-index';
import type { UnitNamer } from './unit-namer';

function buildFact(namer: UnitNamer, tokens: TokenIndex, fn: MeasuredFunction): MethodFact {
  const sourceFile = namer.sourceFile;
  const measure = measureFunction(sourceFile, fn);
  return {
    unitId: namer.unitId(fn),
    name: namer.nameOf(fn)?.short ?? '<anonymous>',
    span: spanOf(sourceFile, fn),
    complexity: measure.complexity,
    nesting: measure.nesting,
    codeLines: tokens.countLines(fn.getStart(sourceFile), fn.end),
    parameterCount: measure.parameterCount,
    decisions: measure.decisions,
    nestingPoints: measure.nestingPoints,
  };
}

function compareFacts(a: MethodFact, b: MethodFact): number {
  return (
    a.span.startLine - b.span.startLine ||
    a.span.startColumn - b.span.startColumn ||
    ordinal(a.unitId, b.unitId)
  );
}

/** One MethodFact per executable function with a body, sorted by position. */
export function collectMethodFacts(namer: UnitNamer, tokens: TokenIndex): MethodFact[] {
  const facts: MethodFact[] = [];
  const visit = (node: ts.Node): void => {
    if (isMeasuredFunction(node)) {
      facts.push(buildFact(namer, tokens, node));
    }
    ts.forEachChild(node, visit);
  };
  visit(namer.sourceFile);
  return facts.sort(compareFacts);
}
