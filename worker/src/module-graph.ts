import * as ts from 'typescript';
import type { ConfigInfo } from './config-discovery';
import type { AnalysisEnv } from './env';
import { scanImports, type ImportRef } from './import-scan';
import { ImportResolver, mustResolve } from './import-resolution';
import { nearestPackageJson } from './ownership';
import { baseName, dirOf, inNodeModules, isTsSource, ordinal } from './paths';
import {
  compareReasons,
  reason,
  ReasonCodes,
  type ContextGraph,
  type FileImport,
  type ModuleEdge,
  type ModuleGraphFacts,
  type Reason,
} from './protocol';

const DEFAULT_OPTIONS: ts.CompilerOptions = {
  allowJs: true,
  module: ts.ModuleKind.ESNext,
  moduleResolution: ts.ModuleResolutionKind.Bundler,
};

interface Edge {
  from: string;
  to: string;
  typeOnly: boolean;
}

function mergeEdge<T extends Edge>(edges: Map<string, T>, edge: T): void {
  const key = `${edge.from}\u0000${edge.to}`;
  const existing = edges.get(key);
  edges.set(key, existing ? { ...existing, typeOnly: existing.typeOnly && edge.typeOnly } : edge);
}

function sortedEdges<T extends Edge>(edges: Map<string, T>): T[] {
  return [...edges.values()].sort((a, b) => ordinal(a.from, b.from) || ordinal(a.to, b.to));
}

interface WorkspaceNames {
  names: Set<string>;
  failures: Reason[];
}

function packageName(text: string): string | undefined {
  const name = (JSON.parse(text) as { name?: unknown }).name;
  return typeof name === 'string' ? name : undefined;
}

function readWorkspaceNames(env: AnalysisEnv, packages: string[]): WorkspaceNames {
  const result: WorkspaceNames = { names: new Set(), failures: [] };
  for (const file of packages) {
    const text = env.ledger.readText(env.paths.toAbs(file));
    try {
      const name = text === undefined ? undefined : packageName(text);
      if (name !== undefined) {
        result.names.add(name);
      }
    } catch {
      result.failures.push(reason(ReasonCodes.ProjectLoadFailed, `'${file}' is not valid JSON.`, null, file));
    }
    if (text === undefined) {
      result.failures.push(reason(ReasonCodes.UnreadableSource, `'${file}' could not be read.`, null, file));
    }
  }
  return result;
}
class GraphCollector {
  readonly fileImports = new Map<string, FileImport>();
  readonly packageEdges = new Map<string, ModuleEdge>();
  readonly unresolved = new Map<string, Reason>();
  private readonly resolver: ImportResolver;

  constructor(
    private readonly env: AnalysisEnv,
    private readonly options: ts.CompilerOptions,
    private readonly workspaceNames: ReadonlySet<string>,
  ) {
    this.resolver = new ImportResolver(env);
  }

  addFile(file: string): void {
    const text = this.env.ledger.readText(this.env.paths.toAbs(file));
    if (text === undefined) {
      this.unresolved.set(`${file}\u0000read`, reason(ReasonCodes.UnreadableSource, `'${file}' could not be read.`, null, file));
      return;
    }
    const sourceFile = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, false);
    for (const ref of scanImports(sourceFile)) {
      this.addRef(file, ref);
    }
  }

  toContext(contextId: string): ContextGraph {
    return { contextId, edges: sortedEdges(this.packageEdges), fileImports: sortedEdges(this.fileImports), support: [] };
  }

  private addRef(file: string, ref: ImportRef): void {
    const resolution = this.resolver.resolve(ref.specifier, file, this.options);
    if (resolution.kind === 'repository' && resolution.file !== file) {
      this.addImport(file, resolution.file, ref.typeOnly);
    } else if (resolution.kind === 'unresolved' && mustResolve(ref.specifier, this.workspaceNames)) {
      const message = `Cannot resolve '${ref.specifier}' imported by ${file}.`;
      this.unresolved.set(`${file}\u0000${ref.specifier}`, reason(ReasonCodes.MissingDependency, message, null, file));
    }
  }

  private addImport(from: string, to: string, typeOnly: boolean): void {
    mergeEdge(this.fileImports, { from, to, typeOnly });
    const fromPackage = nearestPackageJson(dirOf(from), this.env.fileSet);
    const toPackage = nearestPackageJson(dirOf(to), this.env.fileSet);
    if (fromPackage && toPackage && fromPackage !== toPackage) {
      mergeEdge(this.packageEdges, { from: fromPackage, to: toPackage, typeOnly });
    }
  }
}

const REPOSITORY_CONTEXT = 'repository';

function mergeAll<T extends Edge>(lists: T[][]): T[] {
  const merged = new Map<string, T>();
  lists.flat().forEach((edge) => mergeEdge(merged, edge));
  return sortedEdges(merged);
}

function isGraphSource(file: string): boolean {
  return isTsSource(file) && !inNodeModules(file);
}

interface ContextPlan {
  id: string;
  options: ts.CompilerOptions;
  files: string[];
}

function planContexts(env: AnalysisEnv, standalone: ConfigInfo[]): ContextPlan[] {
  const plans = standalone.map((config) => ({ id: config.path, options: config.parsed.options, files: config.ownedFiles }));
  const owned = new Set(standalone.flatMap((config) => config.ownedFiles));
  const orphans = env.files.filter((file) => isGraphSource(file) && !owned.has(file));
  return orphans.length > 0 ? [...plans, { id: REPOSITORY_CONTEXT, options: DEFAULT_OPTIONS, files: orphans }] : plans;
}

/** Contexts owning a common file are alternative resolutions of it and never coexist; disjoint contexts do. */
function withSupport(contexts: ContextGraph[], filesById: Map<string, Set<string>>): ContextGraph[] {
  return contexts.map((context) => {
    const mine = filesById.get(context.contextId) ?? new Set<string>();
    const coexisting = contexts.filter(
      (other) => other !== context && ![...(filesById.get(other.contextId) ?? [])].some((file) => mine.has(file)),
    );
    const own = new Set(context.edges.map((edge) => `${edge.from}\u0000${edge.to}`));
    const support = coexisting.flatMap((other) =>
      other.edges
        .filter((edge) => !own.has(`${edge.from}\u0000${edge.to}`))
        .map((edge) => ({ edge, contextId: other.contextId })),
    );
    return { ...context, support };
  });
}

/**
 * Repository-wide package and file import graph, independent of the focus files. Each owning tsconfig is its own
 * context resolved with its own options (a file owned by several configs appears in each); files no config owns
 * use default options in the "repository" context. The top-level edges are the union over contexts.
 */
export function buildModuleGraph(env: AnalysisEnv, standalone: ConfigInfo[]): ModuleGraphFacts {
  const modules = env.files.filter((file) => baseName(file) === 'package.json').sort(ordinal);
  const workspace = readWorkspaceNames(env, modules);
  const collectors = planContexts(env, standalone).map((plan) => {
    const collector = new GraphCollector(env, plan.options, workspace.names);
    plan.files.filter(isGraphSource).forEach((file) => collector.addFile(file));
    return { plan, collector };
  });
  const built = collectors.map((item) => item.collector.toContext(item.plan.id)).sort((a, b) => ordinal(a.contextId, b.contextId));
  const filesById = new Map(collectors.map((item) => [item.plan.id, new Set(item.plan.files)]));
  const contexts = withSupport(built, filesById);
  const unresolved = new Map<string, Reason>(workspace.failures.map((failure) => [`${failure.contextId}\u0000${failure.code}`, failure]));
  collectors.forEach((item) => item.collector.unresolved.forEach((value, key) => unresolved.set(key, value)));
  return {
    modules,
    edges: mergeAll(contexts.map((context) => context.edges)),
    fileImports: mergeAll(contexts.map((context) => context.fileImports)),
    unresolved: [...unresolved.values()].sort(compareReasons),
    contexts,
  };
}