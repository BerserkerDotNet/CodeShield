import * as ts from 'typescript';
import { isMeasuredFunction, lineOf, type MeasuredFunction } from './function-nodes';
import { CONSTRUCT_HANDLERS } from './metric-handlers';
import type { MeterVisitor } from './meter-types';
import type { DecisionEvidence, NestingEvidence } from './protocol';

export interface FunctionMeasure {
  complexity: number;
  nesting: number;
  decisions: DecisionEvidence[];
  nestingPoints: NestingEvidence[];
  parameterCount: number;
}

class Meter {
  readonly decisions: DecisionEvidence[] = [];
  readonly nestingPoints: NestingEvidence[] = [];
  maxDepth = 0;

  constructor(private readonly sourceFile: ts.SourceFile) {}

  decide(kind: string, node: ts.Node): void {
    this.decisions.push({ decisionKind: kind, line: lineOf(this.sourceFile, node) });
  }

  nest(kind: string, node: ts.Node, depth: number): void {
    this.nestingPoints.push({ constructKind: kind, line: lineOf(this.sourceFile, node), depth });
    this.maxDepth = Math.max(this.maxDepth, depth);
  }
}

/** Walks a function body, dispatching construct handlers; nested functions are measured on their own. */
function createVisitor(meter: Meter): MeterVisitor {
  const visitor: MeterVisitor = {
    decide: (kind, node) => meter.decide(kind, node),
    nest: (kind, node, depth) => meter.nest(kind, node, depth),
    visit: (node, depth) => {
      if (isMeasuredFunction(node)) {
        return;
      }
      const handler = CONSTRUCT_HANDLERS.get(node.kind);
      if (handler) {
        handler(visitor, node, depth);
      } else {
        visitor.children(node, depth);
      }
    },
    children: (node, depth) => ts.forEachChild(node, (child) => visitor.visit(child, depth)),
  };
  return visitor;
}

function countParameters(fn: MeasuredFunction): number {
  const isThisParameter = (p: ts.ParameterDeclaration): boolean =>
    ts.isIdentifier(p.name) && p.name.text === 'this';
  return fn.parameters.filter((p) => !isThisParameter(p)).length;
}

/** Measures one function body; nested functions are skipped and measured on their own. */
export function measureFunction(sourceFile: ts.SourceFile, fn: MeasuredFunction): FunctionMeasure {
  const meter = new Meter(sourceFile);
  createVisitor(meter).visit((fn as { body: ts.Node }).body, 0);
  return {
    complexity: 1 + meter.decisions.length,
    nesting: meter.maxDepth,
    decisions: [...meter.decisions].sort((a, b) => a.line - b.line),
    nestingPoints: [...meter.nestingPoints].sort((a, b) => a.line - b.line || a.depth - b.depth),
    parameterCount: countParameters(fn),
  };
}
