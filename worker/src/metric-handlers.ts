import * as ts from 'typescript';
import type { ConstructHandler, MeterVisitor } from './meter-types';

const SK = ts.SyntaxKind;

const LOGICAL_OPERATORS = new Set<ts.SyntaxKind>([
  SK.AmpersandAmpersandToken,
  SK.BarBarToken,
  SK.QuestionQuestionToken,
  SK.QuestionQuestionEqualsToken,
]);

function visitElse(meter: MeterVisitor, elseStatement: ts.Statement | undefined, depth: number): void {
  if (elseStatement) {
    meter.visit(elseStatement, ts.isIfStatement(elseStatement) ? depth : depth + 1);
  }
}

const ifHandler: ConstructHandler = (meter, node, depth) => {
  const statement = node as ts.IfStatement;
  meter.decide('if', statement);
  meter.visit(statement.expression, depth);
  meter.nest('if', statement, depth + 1);
  meter.visit(statement.thenStatement, depth + 1);
  visitElse(meter, statement.elseStatement, depth);
};

const loopHandler: ConstructHandler = (meter, node, depth) => {
  const body = (node as ts.IterationStatement).statement;
  meter.decide('loop', node);
  meter.nest('loop', node, depth + 1);
  ts.forEachChild(node, (child) => meter.visit(child, child === body ? depth + 1 : depth));
};

const switchHandler: ConstructHandler = (meter, node, depth) => {
  const statement = node as ts.SwitchStatement;
  meter.visit(statement.expression, depth);
  meter.nest('switch', statement, depth + 1);
  meter.visit(statement.caseBlock, depth + 1);
};

const caseHandler: ConstructHandler = (meter, node, depth) => {
  meter.decide('case', node);
  meter.children(node, depth);
};

const catchHandler: ConstructHandler = (meter, node, depth) => {
  const block = (node as ts.CatchClause).block;
  meter.decide('catch', node);
  meter.nest('catch', node, depth + 1);
  ts.forEachChild(node, (child) => meter.visit(child, child === block ? depth + 1 : depth));
};

const conditionalHandler: ConstructHandler = (meter, node, depth) => {
  const conditional = node as ts.ConditionalExpression;
  meter.decide('conditional', conditional);
  meter.visit(conditional.condition, depth);
  meter.nest('conditional', conditional, depth + 1);
  meter.visit(conditional.whenTrue, depth + 1);
  meter.visit(conditional.whenFalse, depth + 1);
};

const binaryHandler: ConstructHandler = (meter, node, depth) => {
  const operator = (node as ts.BinaryExpression).operatorToken;
  if (LOGICAL_OPERATORS.has(operator.kind)) {
    meter.decide('logical', operator);
  }
  meter.children(node, depth);
};

export const CONSTRUCT_HANDLERS: ReadonlyMap<ts.SyntaxKind, ConstructHandler> = new Map([
  [SK.IfStatement, ifHandler],
  [SK.ForStatement, loopHandler],
  [SK.ForInStatement, loopHandler],
  [SK.ForOfStatement, loopHandler],
  [SK.WhileStatement, loopHandler],
  [SK.DoStatement, loopHandler],
  [SK.SwitchStatement, switchHandler],
  [SK.CaseClause, caseHandler],
  [SK.CatchClause, catchHandler],
  [SK.ConditionalExpression, conditionalHandler],
  [SK.BinaryExpression, binaryHandler],
]);
