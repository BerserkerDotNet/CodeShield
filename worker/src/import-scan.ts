import * as ts from 'typescript';

export interface ImportRef {
  specifier: string;
  typeOnly: boolean;
}

type Extractor = (node: ts.Node) => ImportRef | undefined;

function stringValue(node: ts.Node | undefined): string | undefined {
  return node && ts.isStringLiteralLike(node) ? node.text : undefined;
}

function allElementsTypeOnly(bindings: ts.NamedImportBindings | undefined): boolean {
  if (!bindings || !ts.isNamedImports(bindings)) {
    return false;
  }
  return bindings.elements.length > 0 && bindings.elements.every((e) => e.isTypeOnly);
}

function clauseIsTypeOnly(clause: ts.ImportClause | undefined): boolean {
  if (!clause) {
    return false;
  }
  if (clause.isTypeOnly) {
    return true;
  }
  return clause.name === undefined && allElementsTypeOnly(clause.namedBindings);
}

function exportIsTypeOnly(node: ts.ExportDeclaration): boolean {
  const clause = node.exportClause;
  if (node.isTypeOnly) {
    return true;
  }
  if (!clause || !ts.isNamedExports(clause)) {
    return false;
  }
  return clause.elements.length > 0 && clause.elements.every((e) => e.isTypeOnly);
}

function make(specifier: string | undefined, typeOnly: boolean): ImportRef | undefined {
  return specifier === undefined ? undefined : { specifier, typeOnly };
}

const importDeclaration: Extractor = (node) => {
  const decl = node as ts.ImportDeclaration;
  return make(stringValue(decl.moduleSpecifier), clauseIsTypeOnly(decl.importClause));
};

const exportDeclaration: Extractor = (node) => {
  const decl = node as ts.ExportDeclaration;
  return make(stringValue(decl.moduleSpecifier), exportIsTypeOnly(decl));
};

const importEquals: Extractor = (node) => {
  const decl = node as ts.ImportEqualsDeclaration;
  const reference = decl.moduleReference;
  const target = ts.isExternalModuleReference(reference) ? stringValue(reference.expression) : undefined;
  return make(target, decl.isTypeOnly);
};

const callExpression: Extractor = (node) => {
  const call = node as ts.CallExpression;
  const callee = call.expression;
  const isRequire = ts.isIdentifier(callee) && callee.text === 'require';
  const isDynamicImport = callee.kind === ts.SyntaxKind.ImportKeyword;
  return isRequire || isDynamicImport ? make(stringValue(call.arguments[0]), false) : undefined;
};

const importType: Extractor = (node) => {
  const argument = (node as ts.ImportTypeNode).argument;
  return make(ts.isLiteralTypeNode(argument) ? stringValue(argument.literal) : undefined, true);
};

const EXTRACTORS = new Map<ts.SyntaxKind, Extractor>([
  [ts.SyntaxKind.ImportDeclaration, importDeclaration],
  [ts.SyntaxKind.ExportDeclaration, exportDeclaration],
  [ts.SyntaxKind.ImportEqualsDeclaration, importEquals],
  [ts.SyntaxKind.CallExpression, callExpression],
  [ts.SyntaxKind.ImportType, importType],
]);

/** Module specifiers referenced by imports, re-exports, `import()`, `require()` and `import("x")` types. */
export function scanImports(sourceFile: ts.SourceFile): ImportRef[] {
  const refs: ImportRef[] = [];
  const visit = (node: ts.Node): void => {
    const ref = EXTRACTORS.get(node.kind)?.(node);
    if (ref) {
      refs.push(ref);
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return refs;
}
