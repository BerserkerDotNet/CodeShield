import * as ts from 'typescript';

let kindNames: Map<number, string> | undefined;

function isRealKindName(name: string, value: unknown, known: Map<number, string>): value is number {
  return typeof value === 'number' && !/^(First|Last)/.test(name) && !known.has(value);
}

function buildKindNames(): Map<number, string> {
  const names = new Map<number, string>();
  for (const [name, value] of Object.entries(ts.SyntaxKind)) {
    if (isRealKindName(name, value, names)) {
      names.set(value, name);
    }
  }
  return names;
}

/** `ts.SyntaxKind[kind]` can return range markers such as FirstAssignment; prefer the real member name. */
export function syntaxKindName(kind: ts.SyntaxKind): string {
  kindNames ??= buildKindNames();
  return kindNames.get(kind) ?? ts.SyntaxKind[kind] ?? String(kind);
}

function isJsDoc(node: ts.Node): boolean {
  return node.kind >= ts.SyntaxKind.FirstJSDocNode && node.kind <= ts.SyntaxKind.LastJSDocNode;
}

/** Leaf tokens (comments and trivia excluded) in source order, taken from the parsed syntax tree. */
export function leafTokens(sourceFile: ts.SourceFile): ts.Node[] {
  const tokens: ts.Node[] = [];
  const visit = (node: ts.Node): void => {
    for (const child of node.getChildren(sourceFile)) {
      collect(child);
    }
  };
  const collect = (child: ts.Node): void => {
    if (isJsDoc(child)) {
      return;
    }
    if (child.getChildCount(sourceFile) > 0) {
      visit(child);
    } else if (child.end > child.getStart(sourceFile)) {
      tokens.push(child);
    }
  };
  visit(sourceFile);
  return tokens;
}
