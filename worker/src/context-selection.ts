import type { ConfigInfo } from './config-discovery';
import type { AnalysisEnv } from './env';
import { nearestPackageJson } from './ownership';
import { dirOf, isTsConfigName, isTsSource } from './paths';

export function modulePathFor(config: ConfigInfo, env: AnalysisEnv): string | null {
  return nearestPackageJson(dirOf(config.path), env.fileSet) ?? null;
}

/** True when the config, a config it extends or references, or its package.json changed. */
function configChainChanged(config: ConfigInfo, modulePath: string | null, changed: ReadonlySet<string>): boolean {
  const chain = [config.path, ...config.extended, ...config.referenced.filter(isTsConfigName)];
  return chain.some((file) => changed.has(file)) || (modulePath !== null && changed.has(modulePath));
}

export interface ContextSelection {
  selected: ConfigInfo[];
  unowned: string[];
}

/** Selects standalone configs owning a focus file or whose config chain changed; lists unowned focus files. */
export function selectContexts(
  env: AnalysisEnv,
  standalone: ConfigInfo[],
  focusFiles: string[],
  changedConfigs: string[],
): ContextSelection {
  const focus = new Set(focusFiles.filter(isTsSource));
  const changed = new Set(changedConfigs);
  const owned = new Set<string>();
  const selected = standalone.filter((config) => {
    const ownsFocus = config.ownedFiles.filter((file) => focus.has(file));
    ownsFocus.forEach((file) => owned.add(file));
    return ownsFocus.length > 0 || configChainChanged(config, modulePathFor(config, env), changed);
  });
  return { selected, unowned: [...focus].filter((file) => !owned.has(file)).sort() };
}
