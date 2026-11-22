import * as ts from 'typescript';
import type { ConfigInfo } from './config-discovery';
import type { AnalysisEnv } from './env';
import { SourceBundle } from './source-bundle';
import { createCompilerHost } from './ts-hosts';

export interface ContextProgram {
  program: ts.Program;
  bundles: Map<string, SourceBundle>;
  missing: string[];
}

/** Builds the TypeScript program for a config's owned files and wraps each parsed file with its names. */
export function createContextProgram(env: AnalysisEnv, config: ConfigInfo): ContextProgram {
  const options = { ...config.parsed.options, noEmit: true, incremental: false, composite: false };
  const rootNames = config.ownedFiles.map((file) => env.paths.toAbs(file));
  const program = ts.createProgram({
    rootNames,
    options,
    host: createCompilerHost(env, options),
    projectReferences: config.parsed.projectReferences,
  });
  const bundles = new Map<string, SourceBundle>();
  const missing: string[] = [];
  for (const file of config.ownedFiles) {
    const sourceFile = program.getSourceFile(env.paths.toAbs(file));
    if (sourceFile) {
      bundles.set(file, new SourceBundle(file, sourceFile));
    } else {
      missing.push(file);
    }
  }
  return { program, bundles, missing };
}
