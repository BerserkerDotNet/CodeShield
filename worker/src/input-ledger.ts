import * as crypto from 'node:crypto';
import * as fs from 'node:fs';
import { canonicalPath, ordinal, RepoPaths } from './paths';
import type { InputRecord } from './protocol';

/** Records every file the analysis reads, hashing the exact bytes that were used. */
export class InputLedger {
  private readonly entries = new Map<string, InputRecord>();

  constructor(private readonly paths: RepoPaths) {}

  private readonly failed = new Map<string, string>();

  /** Paths that exist but could not be read; ordinary absence (ENOENT/ENOTDIR probes) is not a failure. */
  readFailures(): { path: string; message: string }[] {
    return [...this.failed.entries()].map(([path, message]) => ({ path, message })).sort((a, b) => ordinal(a.path, b.path));
  }

  readText(absPath: string): string | undefined {
    let bytes: Buffer;
    try {
      bytes = fs.readFileSync(absPath);
    } catch (error) {
      this.noteFailure(absPath, error);
      return undefined;
    }
    this.record(absPath, bytes);
    return bytes.toString('utf8').replace(/^\uFEFF/, '');
  }

  private noteFailure(absPath: string, error: unknown): void {
    const code = (error as NodeJS.ErrnoException).code;
    if (code === 'ENOENT' || code === 'ENOTDIR') {
      return;
    }
    const real = canonicalPath(absPath);
    this.failed.set(this.paths.toRel(real) ?? real, error instanceof Error ? error.message : String(error));
  }

  record(absPath: string, bytes: Buffer): void {
    const real = canonicalPath(absPath);
    const rel = this.paths.toRel(real);
    const scope = rel === undefined ? 'external' : 'repository';
    const recorded = rel ?? real;
    const sha256 = crypto.createHash('sha256').update(bytes).digest('hex');
    this.entries.set(`${scope}|${recorded}`, { scope, path: recorded, sha256 });
  }

  snapshot(): InputRecord[] {
    return [...this.entries.values()].sort(
      (a, b) => ordinal(a.scope, b.scope) || ordinal(a.path, b.path),
    );
  }
}
