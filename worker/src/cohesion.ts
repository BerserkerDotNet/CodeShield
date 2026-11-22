import * as ts from 'typescript';
import { buildClassModel, type Member } from './cohesion-model';
import { scanMemberAccesses, type MemberAccesses } from './cohesion-scan';
import { spanOf } from './function-nodes';
import { ordinal } from './paths';
import type { ClassFact, CohesionEdge } from './protocol';
import type { UnitNamer } from './unit-namer';

function pairEdges(ids: string[], reason: string): CohesionEdge[] {
  const sorted = [...ids].sort(ordinal);
  return sorted.flatMap((from, i) => sorted.slice(i + 1).map((to) => ({ from, to, reason })));
}

function callEdges(id: string, accesses: MemberAccesses): CohesionEdge[] {
  return [...accesses.calls]
    .filter((callee) => callee !== id)
    .map((callee) => {
      const [from, to] = [id, callee].sort(ordinal);
      return { from: from!, to: to!, reason: 'call' };
    });
}

function fieldEdges(accessesById: Map<string, MemberAccesses>): CohesionEdge[] {
  const byField = new Map<string, string[]>();
  for (const [id, accesses] of accessesById) {
    for (const field of accesses.fields) {
      byField.set(field, [...(byField.get(field) ?? []), id]);
    }
  }
  return [...byField].flatMap(([field, ids]) => pairEdges(ids, `field:${field}`));
}

function buildEdges(accessesById: Map<string, MemberAccesses>): CohesionEdge[] {
  const calls = [...accessesById].flatMap(([id, accesses]) => callEdges(id, accesses));
  return uniqueSorted([...calls, ...fieldEdges(accessesById)]);
}

function uniqueSorted(edges: CohesionEdge[]): CohesionEdge[] {
  const unique = new Map(edges.map((edge) => [`${edge.from}|${edge.to}|${edge.reason}`, edge]));
  return [...unique.values()].sort(
    (a, b) => ordinal(a.from, b.from) || ordinal(a.to, b.to) || ordinal(a.reason, b.reason),
  );
}

function connectedComponents(ids: string[], edges: CohesionEdge[]): string[][] {
  const parent = new Map(ids.map((id) => [id, id]));
  const find = (id: string): string => {
    const up = parent.get(id)!;
    return up === id ? id : find(up);
  };
  for (const edge of edges) {
    parent.set(find(edge.from), find(edge.to));
  }
  const groups = new Map<string, string[]>();
  for (const id of ids) {
    groups.set(find(id), [...(groups.get(find(id)) ?? []), id]);
  }
  return [...groups.values()].map((group) => group.sort(ordinal)).sort((a, b) => ordinal(a[0]!, b[0]!));
}

function sortedMembers(members: Member[]): Member[] {
  return [...members].sort((a, b) => ordinal(a.id, b.id));
}

function analyzeClass(cls: ts.ClassDeclaration, checker: ts.TypeChecker, namer: UnitNamer): ClassFact {
  const model = buildClassModel(cls, checker, namer);
  const members = sortedMembers(model.members);
  const applicable = model.fieldsBySymbol.size > 0 && members.length >= 2;
  const accesses = new Map(members.map((m) => [m.id, scanMemberAccesses(m, checker, model)]));
  const edges = applicable ? buildEdges(accesses) : [];
  return {
    unitId: namer.unitId(cls),
    name: namer.nameOf(cls)?.short ?? '<anonymous>',
    locations: [{ file: namer.file, span: spanOf(namer.sourceFile, cls) }],
    applicable,
    nodes: members.map((m) => ({ id: m.id, name: m.name })),
    edges,
    components: applicable ? connectedComponents(members.map((m) => m.id), edges) : [],
  };
}

/** LCOM4 facts for every class declaration in a source file. */
export function collectClassFacts(namer: UnitNamer, checker: ts.TypeChecker): ClassFact[] {
  const facts: ClassFact[] = [];
  const visit = (node: ts.Node): void => {
    if (ts.isClassDeclaration(node)) {
      facts.push(analyzeClass(node, checker, namer));
    }
    ts.forEachChild(node, visit);
  };
  visit(namer.sourceFile);
  return facts.sort((a, b) => ordinal(a.unitId, b.unitId));
}
