import * as ts from 'typescript';
import { TokenIndex } from './token-index';
import { UnitNamer } from './unit-namer';

/** A parsed source file with its unit names and (lazily built) token index. */
export class SourceBundle {
  readonly namer: UnitNamer;
  private tokenIndex: TokenIndex | undefined;

  constructor(
    readonly file: string,
    readonly sourceFile: ts.SourceFile,
  ) {
    this.namer = new UnitNamer(file, sourceFile);
  }

  get tokens(): TokenIndex {
    this.tokenIndex ??= new TokenIndex(this.sourceFile);
    return this.tokenIndex;
  }
}
