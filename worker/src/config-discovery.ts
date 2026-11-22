import * as ts from 'typescript';
import type { AnalysisEnv } from './env';
import { inNodeModules, isTsConfigName, isTsSource, ordinal, toPosix } from './paths';
import { createParseHost } from './ts-hosts';

const NO_INPUTS_ERROR = 18003;

export interface ConfigInfo {
  path: string;
  parsed: ts.ParsedCommandLine;
  extended: string[];
  referenced: string[];
  ownedFiles: string[];
  errors: string[];
}

function repoRelative(env: AnalysisEnv, abs: string): string | undefined {
  return env.paths.toRel(toPosix(abs));
}

function referencedConfigs(env: AnalysisEnv, parsed: ts.ParsedCommandLine): string[] {
  const refs = (parsed.projectReferences ?? []).map((ref) => ts.resolveProjectReferencePath(ref));
  return refs.map((abs) => repoRelative(env, abs)).filter((rel): rel is string => rel !== undefined);
}

function isOwnableSource(env: AnalysisEnv, rel: string | undefined): rel is string {
  return rel !== undefined && isTsSource(rel) && !inNodeModules(rel) && env.fileSet.has(rel);
}

function ownedSourceFiles(env: AnalysisEnv, parsed: ts.ParsedCommandLine): string[] {
  const excludedDirs = [parsed.options.outDir, parsed.options.declarationDir]
    .filter((dir): dir is string => dir !== undefined)
    .map((dir) => `${toPosix(dir)}/`);
  const owned = new Set<string>();
  for (const abs of parsed.fileNames) {
    const rel = repoRelative(env, abs);
    const generated = excludedDirs.some((dir) => toPosix(abs).startsWith(dir));
    if (isOwnableSource(env, rel) && !generated) {
      owned.add(rel);
    }
  }
  return [...owned].sort(ordinal);
}

function loadConfig(env: AnalysisEnv, rel: string): ConfigInfo {
  const abs = env.paths.toAbs(rel);
  const source = ts.readJsonConfigFile(abs, (p) => env.ledger.readText(p));
  const baseDir = abs.slice(0, abs.lastIndexOf('/'));
  const parsed = ts.parseJsonSourceFileConfigFileContent(source, createParseHost(env), baseDir, undefined, abs);
  const extended = (source.extendedSourceFiles ?? [])
    .map((file) => repoRelative(env, file))
    .filter((file): file is string => file !== undefined);
  const errors = parsed.errors
    .filter((diagnostic) => diagnostic.code !== NO_INPUTS_ERROR)
    .map((diagnostic) => ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n'));
  return {
    path: rel,
    parsed,
    extended,
    referenced: referencedConfigs(env, parsed),
    ownedFiles: ownedSourceFiles(env, parsed),
    errors,
  };
}

export function loadConfigs(env: AnalysisEnv): ConfigInfo[] {
  return env.files.filter(isTsConfigName).map((rel) => loadConfig(env, rel));
}

/** A config that is only extended (and never referenced) is a settings source, not a project. */
export function standaloneConfigs(configs: ConfigInfo[]): ConfigInfo[] {
  const extended = new Set(configs.flatMap((config) => config.extended));
  const referenced = new Set(configs.flatMap((config) => config.referenced));
  return configs.filter((config) => !extended.has(config.path) || referenced.has(config.path));
}
