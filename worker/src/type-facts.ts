import * as ts from 'typescript';
import { ordinal } from './paths';
import type { TypeFact } from './protocol';
import type { UnitNamer } from './unit-namer';

function isAbstractType(node: ts.ClassDeclaration | ts.InterfaceDeclaration): boolean {
  const flags = ts.getCombinedModifierFlags(node);
  return ts.isInterfaceDeclaration(node) || (flags & ts.ModifierFlags.Abstract) !== 0;
}

/** Declared classes and interfaces of a file; merged interface declarations count once. */
export function collectTypeFacts(namer: UnitNamer): TypeFact[] {
  const facts = new Map<string, TypeFact>();
  const visit = (node: ts.Node): void => {
    if (ts.isClassDeclaration(node) || ts.isInterfaceDeclaration(node)) {
      const unitId = namer.unitId(node);
      facts.set(unitId, { unitId, file: namer.file, isAbstract: isAbstractType(node) });
    }
    ts.forEachChild(node, visit);
  };
  visit(namer.sourceFile);
  return [...facts.values()].sort((a, b) => ordinal(a.unitId, b.unitId));
}
