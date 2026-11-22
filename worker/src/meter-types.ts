import type * as ts from 'typescript';

/** Operations the construct handlers need from the function meter. */
export interface MeterVisitor {
  decide(kind: string, node: ts.Node): void;
  nest(kind: string, node: ts.Node, depth: number): void;
  visit(node: ts.Node, depth: number): void;
  children(node: ts.Node, depth: number): void;
}

export type ConstructHandler = (meter: MeterVisitor, node: ts.Node, depth: number) => void;
