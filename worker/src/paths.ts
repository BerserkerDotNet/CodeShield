import * as fs from 'node:fs';
import * as path from 'node:path';

export const toPosix = (p: string): string => p.replace(/\\/g, '/');

export function ordinal(a: string, b: string): number {
  if (a < b) {
    return -1;
  }
  return a > b ? 1 : 0;
}

export function dirOf(repoPath: string): string {
  const index = repoPath.lastIndexOf('/');
  return index < 0 ? '' : repoPath.slice(0, index);
}

export function baseName(repoPath: string): string {
  return repoPath.slice(repoPath.lastIndexOf('/') + 1);
}

export function joinRepo(dir: string, name: string): string {
  return dir === '' ? name : `${dir}/${name}`;
}

export function canonicalPath(p: string): string {
  let resolved: string;
  try {
    resolved = fs.realpathSync.native(p);
  } catch {
    resolved = path.resolve(p);
  }
  return toPosix(resolved).replace(/\/+$/, '');
}

export function isDeclarationFile(p: string): boolean {
  return /\.d\.(m|c)?ts$/i.test(p);
}

export function inNodeModules(p: string): boolean {
  return toPosix(p).split('/').includes('node_modules');
}

export function isTsSource(p: string): boolean {
  return /\.(ts|tsx|mts|cts)$/i.test(p) && !isDeclarationFile(p);
}

export function isTsConfigName(p: string): boolean {
  return /^tsconfig(\..+)?\.json$/i.test(baseName(p));
}

/** Maps between canonical absolute paths and repository-relative forward-slash paths. */
export class RepoPaths {
  readonly root: string;
  private readonly caseInsensitive = process.platform === 'win32';

  constructor(root: string) {
    this.root = canonicalPath(root);
  }

  toAbs(rel: string): string {
    return `${this.root}/${rel}`;
  }

  toRel(abs: string): string | undefined {
    const normalized = toPosix(abs);
    const prefix = `${this.root}/`;
    const head = normalized.slice(0, prefix.length);
    const same = this.caseInsensitive ? head.toLowerCase() === prefix.toLowerCase() : head === prefix;
    return same ? normalized.slice(prefix.length) : undefined;
  }
}
