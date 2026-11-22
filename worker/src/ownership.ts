import { dirOf, joinRepo } from './paths';

function nearestAncestorFile(dir: string, name: string, fileSet: ReadonlySet<string>): string | undefined {
  let current = dir;
  for (;;) {
    const candidate = joinRepo(current, name);
    if (fileSet.has(candidate)) {
      return candidate;
    }
    if (current === '') {
      return undefined;
    }
    current = dirOf(current);
  }
}

/** Nearest ancestor package.json (from `dir` upward) present in the repository inventory. */
export function nearestPackageJson(dir: string, fileSet: ReadonlySet<string>): string | undefined {
  return nearestAncestorFile(dir, 'package.json', fileSet);
}

/** Nearest ancestor tsconfig.json for a directory, if any. */
export function nearestTsConfig(dir: string, fileSet: ReadonlySet<string>): string | undefined {
  return nearestAncestorFile(dir, 'tsconfig.json', fileSet);
}
