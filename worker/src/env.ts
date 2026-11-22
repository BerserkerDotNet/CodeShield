import { InputLedger } from './input-ledger';
import { RepoPaths } from './paths';

/** Shared per-request state: path mapping, repository inventory and the input ledger. */
export interface AnalysisEnv {
  paths: RepoPaths;
  files: string[];
  fileSet: ReadonlySet<string>;
  ledger: InputLedger;
}

export function createEnv(root: string, files: string[]): AnalysisEnv {
  const paths = new RepoPaths(root);
  const sorted = [...new Set(files)].sort();
  return { paths, files: sorted, fileSet: new Set(sorted), ledger: new InputLedger(paths) };
}
