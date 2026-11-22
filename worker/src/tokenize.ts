import * as fs from 'node:fs';
import * as ts from 'typescript';
import { ordinal, RepoPaths } from './paths';
import { reason, ReasonCodes, type FileTokens, type Token, type TokenizeRequest, type TokenizeResult } from './protocol';
import { leafTokens, syntaxKindName } from './token-walk';

function toToken(sourceFile: ts.SourceFile, node: ts.Node): Token {
  const start = node.getStart(sourceFile);
  const begin = sourceFile.getLineAndCharacterOfPosition(start);
  const end = sourceFile.getLineAndCharacterOfPosition(node.end);
  return {
    kind: syntaxKindName(node.kind),
    text: sourceFile.text.slice(start, node.end),
    line: begin.line + 1,
    column: begin.character + 1,
    endLine: end.line + 1,
    endColumn: end.character + 1,
    offset: start,
  };
}

export function tokenizeText(file: string, text: string): FileTokens {
  const sourceFile = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true);
  return { path: file, tokens: leafTokens(sourceFile).map((node) => toToken(sourceFile, node)) };
}

/** Scanner-accurate tokens (comments and trivia excluded) for each requested source file. */
export function tokenize(request: TokenizeRequest): TokenizeResult {
  const paths = new RepoPaths(request.root);
  const files: FileTokens[] = [];
  const failures: TokenizeResult['failures'] = [];
  for (const file of [...new Set(request.files)].sort(ordinal)) {
    try {
      const text = fs.readFileSync(paths.toAbs(file), 'utf8').replace(/^\uFEFF/, '');
      files.push(tokenizeText(file, text));
    } catch (error) {
      failures.push(reason(ReasonCodes.UnreadableSource, `Cannot tokenize ${file}: ${String(error)}`, null, file));
    }
  }
  return { files, failures };
}
