import * as ts from 'typescript';
import type { AnalysisEnv } from './env';
import { canonicalPath, inNodeModules } from './paths';
import { createResolutionHost } from './ts-hosts';

export type ImportResolution =
  | { kind: 'repository'; file: string }
  | { kind: 'external' }
  | { kind: 'unresolved' };

const ASSET_EXTENSION = /\.(css|scss|sass|less|svg|png|jpe?g|gif|webp|ico|json|html?|txt|md|woff2?|ttf|eot|mp4|wasm|ya?ml|csv|graphql|gql|vue|svelte|node)$/i;

function packageNameOf(specifier: string): string {
  const parts = specifier.split('/');
  return specifier.startsWith('@') ? parts.slice(0, 2).join('/') : (parts[0] ?? specifier);
}

/** Relative or workspace-internal specifiers that must resolve; assets and third-party packages are exempt. */
export function mustResolve(specifier: string, workspaceNames: ReadonlySet<string>): boolean {
  if (ASSET_EXTENSION.test(specifier)) {
    return false;
  }
  const relative = /^\.{1,2}(\/|$)/.test(specifier) || specifier.startsWith('/');
  return relative || workspaceNames.has(packageNameOf(specifier));
}

/** Resolves import specifiers with TypeScript's module resolution against the real file system. */
export class ImportResolver {
  private readonly host: ts.ModuleResolutionHost;

  constructor(private readonly env: AnalysisEnv) {
    this.host = createResolutionHost(env);
  }

  resolve(specifier: string, fromFile: string, options: ts.CompilerOptions): ImportResolution {
    const containing = this.env.paths.toAbs(fromFile);
    const resolved = ts.resolveModuleName(specifier, containing, options, this.host).resolvedModule;
    if (!resolved) {
      return { kind: 'unresolved' };
    }
    const file = this.env.paths.toRel(canonicalPath(resolved.resolvedFileName));
    if (file === undefined || inNodeModules(file) || !this.env.fileSet.has(file)) {
      return { kind: 'external' };
    }
    return { kind: 'repository', file };
  }
}
