import * as ts from 'typescript';
import { leafTokens } from './token-walk';

export interface CommentInfo {
  text: string;
  line: number;
}

function commentsBefore(sourceFile: ts.SourceFile, pos: number): CommentInfo[] {
  const ranges = ts.getLeadingCommentRanges(sourceFile.text, pos) ?? [];
  return ranges.map((range) => {
    const raw = sourceFile.text.slice(range.pos, range.end);
    const body = range.kind === ts.SyntaxKind.SingleLineCommentTrivia ? raw.slice(2) : raw.slice(2, -2);
    return { text: body, line: sourceFile.getLineAndCharacterOfPosition(range.pos).line + 1 };
  });
}

/** Every comment in the file, found as leading trivia of each token (including end of file). */
export function collectComments(sourceFile: ts.SourceFile): CommentInfo[] {
  const anchors = [...leafTokens(sourceFile).map((token) => token.pos), sourceFile.endOfFileToken.pos];
  const seen = new Map<string, CommentInfo>();
  for (const pos of anchors) {
    for (const comment of commentsBefore(sourceFile, pos)) {
      seen.set(`${comment.line}|${comment.text}`, comment);
    }
  }
  return [...seen.values()];
}
