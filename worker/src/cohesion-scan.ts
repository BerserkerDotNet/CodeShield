import * as ts from 'typescript';
import type { ClassModel, Member } from './cohesion-model';
import { isMeasuredFunction } from './function-nodes';

export interface MemberAccesses {
  fields: Set<string>;
  calls: Set<string>;
}

function isCallee(access: ts.PropertyAccessExpression): boolean {
  return ts.isCallExpression(access.parent) && access.parent.expression === access;
}

class AccessScanner {
  readonly accesses: MemberAccesses = { fields: new Set(), calls: new Set() };

  constructor(
    private readonly checker: ts.TypeChecker,
    private readonly model: ClassModel,
  ) {}

  scan(node: ts.Node): void {
    if (isMeasuredFunction(node)) {
      return;
    }
    if (ts.isPropertyAccessExpression(node) && node.expression.kind === ts.SyntaxKind.ThisKeyword) {
      this.record(node);
    }
    ts.forEachChild(node, (child) => this.scan(child));
  }

  private record(access: ts.PropertyAccessExpression): void {
    const symbol = this.checker.getSymbolAtLocation(access.name);
    if (!symbol) {
      return;
    }
    const field = this.model.fieldsBySymbol.get(symbol);
    if (field !== undefined) {
      this.accesses.fields.add(field);
    }
    for (const member of this.model.membersBySymbol.get(symbol) ?? []) {
      this.recordMember(member, access);
    }
  }

  private recordMember(member: Member, access: ts.PropertyAccessExpression): void {
    if (!ts.isMethodDeclaration(member.node) || isCallee(access)) {
      this.accesses.calls.add(member.id);
    }
  }
}

/** Fields and sibling members a method reads through `this`, excluding nested functions. */
export function scanMemberAccesses(member: Member, checker: ts.TypeChecker, model: ClassModel): MemberAccesses {
  const scanner = new AccessScanner(checker, model);
  scanner.scan(member.node.body);
  return scanner.accesses;
}
