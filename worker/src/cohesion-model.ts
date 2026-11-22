import * as ts from 'typescript';
import type { UnitNamer } from './unit-namer';

type MemberNode = (ts.MethodDeclaration | ts.GetAccessorDeclaration | ts.SetAccessorDeclaration) & { body: ts.Block };

export interface Member {
  id: string;
  name: string;
  node: MemberNode;
}

/** Instance state and eligible methods/accessors of one class, keyed by checker symbols. */
export interface ClassModel {
  members: Member[];
  membersBySymbol: Map<ts.Symbol, Member[]>;
  fieldsBySymbol: Map<ts.Symbol, string>;
}

function hasFlag(node: ts.Node, flag: ts.ModifierFlags): boolean {
  return (ts.getCombinedModifierFlags(node as ts.Declaration) & flag) !== 0;
}

function isInstanceMember(node: ts.Node): boolean {
  return !hasFlag(node, ts.ModifierFlags.Static) && !hasFlag(node, ts.ModifierFlags.Abstract);
}

function isAccessorOrMethod(node: ts.ClassElement): node is MemberNode {
  return ts.isMethodDeclaration(node) || ts.isGetAccessorDeclaration(node) || ts.isSetAccessorDeclaration(node);
}

function isEligibleMember(node: ts.ClassElement): node is MemberNode {
  if (!isAccessorOrMethod(node) || !node.body || !ts.isBlock(node.body)) {
    return false;
  }
  return node.body.statements.length > 0 && isInstanceMember(node);
}

function addField(model: ClassModel, symbol: ts.Symbol | undefined, name: string): void {
  if (symbol) {
    model.fieldsBySymbol.set(symbol, name);
  }
}

function addParameterProperties(model: ClassModel, checker: ts.TypeChecker, ctor: ts.ConstructorDeclaration): void {
  for (const parameter of ctor.parameters) {
    if (ts.isParameterPropertyDeclaration(parameter, ctor) && ts.isIdentifier(parameter.name)) {
      const symbols = checker.getSymbolsOfParameterPropertyDeclaration(parameter, parameter.name.text);
      addField(model, symbols[1], parameter.name.text);
    }
  }
}

function addMember(model: ClassModel, checker: ts.TypeChecker, namer: UnitNamer, node: MemberNode): void {
  const member: Member = { id: namer.unitId(node), name: namer.nameOf(node)?.short ?? '<anonymous>', node };
  model.members.push(member);
  const symbol = checker.getSymbolAtLocation(node.name!);
  if (symbol) {
    model.membersBySymbol.set(symbol, [...(model.membersBySymbol.get(symbol) ?? []), member]);
  }
}

function addElement(model: ClassModel, checker: ts.TypeChecker, namer: UnitNamer, element: ts.ClassElement): void {
  if (ts.isConstructorDeclaration(element)) {
    addParameterProperties(model, checker, element);
  } else if (ts.isPropertyDeclaration(element) && isInstanceMember(element)) {
    addField(model, checker.getSymbolAtLocation(element.name), element.name.getText());
  } else if (isEligibleMember(element)) {
    addMember(model, checker, namer, element);
  }
}

export function buildClassModel(cls: ts.ClassDeclaration, checker: ts.TypeChecker, namer: UnitNamer): ClassModel {
  const model: ClassModel = { members: [], membersBySymbol: new Map(), fieldsBySymbol: new Map() };
  for (const element of cls.members) {
    addElement(model, checker, namer, element);
  }
  return model;
}
