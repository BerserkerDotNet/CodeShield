import * as ts from 'typescript';
import { leafTokens } from './token-walk';

interface IndexedToken {
  start: number;
  end: number;
  startLine: number;
  endLine: number;
}

/** Physical-line accounting for non-comment, non-whitespace tokens of one source file. */
export class TokenIndex {
  private readonly tokens: IndexedToken[];

  constructor(sourceFile: ts.SourceFile) {
    this.tokens = leafTokens(sourceFile).map((node) => {
      const start = node.getStart(sourceFile);
      return {
        start,
        end: node.end,
        startLine: sourceFile.getLineAndCharacterOfPosition(start).line,
        endLine: sourceFile.getLineAndCharacterOfPosition(node.end - 1).line,
      };
    });
  }

  totalLines(): number {
    return this.countLines(0, Number.MAX_SAFE_INTEGER);
  }

  /** Distinct lines holding at least one token that lies fully inside [start, end). */
  countLines(start: number, end: number): number {
    const lines = new Set<number>();
    for (let i = this.firstAtOrAfter(start); i < this.tokens.length; i++) {
      const token = this.tokens[i]!;
      if (token.start >= end) {
        break;
      }
      addLines(lines, token);
    }
    return lines.size;
  }

  private firstAtOrAfter(offset: number): number {
    let low = 0;
    let high = this.tokens.length;
    while (low < high) {
      const mid = (low + high) >> 1;
      if (this.tokens[mid]!.start < offset) {
        low = mid + 1;
      } else {
        high = mid;
      }
    }
    return low;
  }
}

function addLines(lines: Set<number>, token: IndexedToken): void {
  for (let line = token.startLine; line <= token.endLine; line++) {
    lines.add(line);
  }
}
