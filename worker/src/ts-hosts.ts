import * as fs from 'node:fs';
import * as ts from 'typescript';
import type { AnalysisEnv } from './env';
import { canonicalPath } from './paths';

/** Config parsing host: file matching is restricted to the request inventory. */
export function createParseHost(env: AnalysisEnv): ts.ParseConfigHost {
  const inInventory = (abs: string): boolean => {
    const rel = env.paths.toRel(abs);
    return rel !== undefined && env.fileSet.has(rel);
  };
  return {
    useCaseSensitiveFileNames: ts.sys.useCaseSensitiveFileNames,
    readDirectory: (rootDir, extensions, excludes, includes, depth) =>
      ts.sys.readDirectory(rootDir, extensions, excludes, includes, depth).filter(inInventory),
    fileExists: (p) => ts.sys.fileExists(p),
    readFile: (p) => env.ledger.readText(p),
  };
}

/** Compiler host that hashes every source and library file it hands to the program. */
export function createCompilerHost(env: AnalysisEnv, options: ts.CompilerOptions): ts.CompilerHost {
  const host = ts.createCompilerHost(options, true);
  host.readFile = (p) => env.ledger.readText(p);
  host.getSourceFile = (fileName, languageVersionOrOptions) => {
    const text = env.ledger.readText(fileName);
    if (text === undefined) {
      return undefined;
    }
    return ts.createSourceFile(fileName, text, languageVersionOrOptions, true);
  };
  return host;
}

export function createResolutionHost(env: AnalysisEnv): ts.ModuleResolutionHost {
  return {
    fileExists: (p) => ts.sys.fileExists(p),
    directoryExists: (p) => ts.sys.directoryExists(p),
    readFile: (p) => env.ledger.readText(p),
    realpath: (p) => (fs.existsSync(p) ? canonicalPath(p) : p),
    getCurrentDirectory: () => env.paths.root,
  };
}
