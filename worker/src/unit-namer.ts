import * as ts from 'typescript';
import { isMeasuredFunction } from './function-nodes';

export interface UnitName {
  qualified: string;
  short: string;
}

interface Scope {
  name: string;
  isFunction: boolean;
}

function isUnit(node: ts.Node): boolean {
  return (
    isMeasuredFunction(node) ||
    ts.isClassDeclaration(node) ||
    ts.isClassExpression(node) ||
    ts.isInterfaceDeclaration(node)
  );
}

function nameText(name: ts.Node): string {
  if (ts.isComputedPropertyName(name)) {
    return `[${name.expression.getText()}]`;
  }
  const text = (name as { text?: unknown }).text;
  return typeof text === 'string' ? text : name.getText();
}

function ownName(node: ts.Node): string | undefined {
  const name = (node as { name?: ts.Node }).name;
  if (!name) {
    return undefined;
  }
  const text = nameText(name);
  if (ts.isGetAccessorDeclaration(node)) {
    return `get ${text}`;
  }
  return ts.isSetAccessorDeclaration(node) ? `set ${text}` : text;
}

function inferredName(node: ts.Node): string | undefined {
  const parent = node.parent as ts.Node | undefined;
  const named = parent && (ts.isVariableDeclaration(parent) || ts.isPropertyAssignment(parent) || ts.isPropertyDeclaration(parent));
  return named ? nameText(parent.name) : undefined;
}

function localName(node: ts.Node): string {
  if (ts.isConstructorDeclaration(node)) {
    return 'constructor';
  }
  return ownName(node) ?? inferredName(node) ?? '<anonymous>';
}

/**
 * Assigns stable qualified names to the functions, classes and interfaces of one file:
 * `Class.method`, `outer.inner@line` for nested functions and `<anonymous>@line` for anonymous ones.
 */
export class UnitNamer {
  private readonly names = new Map<ts.Node, UnitName>();
  private readonly used = new Set<string>();

  constructor(
    readonly file: string,
    readonly sourceFile: ts.SourceFile,
  ) {
    this.walk(sourceFile, undefined);
  }

  nameOf(node: ts.Node): UnitName | undefined {
    return this.names.get(node);
  }

  unitId(node: ts.Node): string {
    return `${this.file}#${this.names.get(node)?.qualified ?? '<module>'}`;
  }

  /** Unit id of the innermost named unit containing `pos`, or the module unit. */
  enclosingUnitId(pos: number): string {
    const startOf = (node: ts.Node): number => node.getStart(this.sourceFile);
    const containing = [...this.names.keys()].filter((node) => startOf(node) <= pos && pos < node.end);
    const best = containing.sort((a, b) => startOf(b) - startOf(a) || a.end - b.end)[0];
    return best ? this.unitId(best) : `${this.file}#<module>`;
  }

  private walk(node: ts.Node, scope: Scope | undefined): void {
    let next = scope;
    if (isUnit(node)) {
      this.names.set(node, this.makeName(node, scope));
      next = { name: this.names.get(node)!.qualified, isFunction: !ts.isClassLike(node) && !ts.isInterfaceDeclaration(node) };
    }
    ts.forEachChild(node, (child) => this.walk(child, next));
  }

  private makeName(node: ts.Node, scope: Scope | undefined): UnitName {
    const short = localName(node);
    let qualified = scope ? `${scope.name}.${short}` : short;
    if (short === '<anonymous>' || scope?.isFunction) {
      qualified += `@${this.sourceFile.getLineAndCharacterOfPosition(node.getStart(this.sourceFile)).line + 1}`;
    }
    if (!ts.isInterfaceDeclaration(node)) {
      qualified = this.unique(qualified, node);
    }
    return { qualified, short };
  }

  private unique(name: string, node: ts.Node): string {
    let result = name;
    if (this.used.has(result)) {
      result = `${name}:${this.sourceFile.getLineAndCharacterOfPosition(node.getStart(this.sourceFile)).character + 1}`;
    }
    this.used.add(result);
    return result;
  }
}
